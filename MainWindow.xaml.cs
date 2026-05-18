using System;
using System.Collections.Generic;
using System.IO;                  // AsStreamForRead() extension metodu için gerekli
using IO = System.IO;             // Path/File/Directory — System.Windows.Shapes.Path çakışmasını önler
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using NAudio.Wave;
using Windows.Media.Control;

namespace MusicWidget
{
    public partial class MainWindow : Window
    {
        // ==========================================
        // 1. WIN32 API TANIMLAMALARI (Korumalar & Hotkey)
        // ==========================================
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE     = 0x0001;
        private const uint SWP_NOMOVE     = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int  HOTKEY_ID      = 9000;
        private const int  WM_HOTKEY      = 0x0312;
        private const uint MOD_CONTROL    = 0x0002;
        private const uint MOD_SHIFT      = 0x0004;
        private const uint VK_M           = 0x4D; // M Tuşu

        // ==========================================
        // 2. GLOBAL DEĞİŞKENLER & ALTYAPI
        // ==========================================
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession?        _session;
        private WasapiLoopbackCapture?                            _capture;

        private const int    BarCount = 15;
        private Rectangle[]  _bars   = new Rectangle[BarCount];
        private bool         _isPinned = true;
        private DispatcherTimer? _keepAliveTimer;
        private double       _currentBaseHue = 190.0;

        // FIX #4: %AppData% kullan — Program Files yazma izni sorununu önler
        private static readonly string _appDataDir =
            IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicWidget");
        private readonly string _posFile  = IO.Path.Combine(_appDataDir, "pos.txt");
        private readonly string _langFile = IO.Path.Combine(_appDataDir, "lang.txt");

        // ==========================================
        // 3. CANLI DİL DESTEĞİ SÖZLÜKLERİ
        // ==========================================
        private bool _isEnglish = false;

        private readonly Dictionary<string, string> _textsTR = new()
        {
            { "Pin",           "Konumu Sabitle"    },
            { "Language",      "Language: English" },
            { "Exit",          "Çıkış"             },
            { "UnknownTitle",  "Bilinmiyor"        },
            { "UnknownArtist", "Şarkı bekleniyor..." }
        };

        private readonly Dictionary<string, string> _textsEN = new()
        {
            { "Pin",           "Pin Position"      },
            { "Language",      "Dil: Türkçe"       },
            { "Exit",          "Exit"               },
            { "UnknownTitle",  "Unknown"            },
            { "UnknownArtist", "Waiting for track..." }
        };

        // ==========================================
        // 4. BAŞLATICI (CONSTRUCTOR)
        // ==========================================
        public MainWindow()
        {
            InitializeComponent();
            EnsureAppDataDir();     // FIX #4: klasör garantisi
            LoadSettings();
            SetupVisualizer();
            StartListening();

            this.Topmost = true;

            this.StateChanged += (s, e) =>
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    this.WindowState = WindowState.Normal;
                    ForceTopmost();
                }
            };
            this.Deactivated += (s, e) => ForceTopmost();

            SetupKeepOnTop();
            this.SourceInitialized += MainWindow_SourceInitialized;
        }

        // ==========================================
        // 5. DİL YÖNETİMİ METOTLARI
        // ==========================================
        private void ApplyLanguage()
        {
            var t = _isEnglish ? _textsEN : _textsTR;

            if (PinMenuItem  != null) PinMenuItem.Header  = t["Pin"];
            if (LangMenuItem != null) LangMenuItem.Header = t["Language"];
            if (ExitMenuItem != null) ExitMenuItem.Header = t["Exit"];

            // Sadece placeholder metinleri çevir, gerçek şarkı adını dokunma
            if (TrackName.Text  == _textsTR["UnknownTitle"]  || TrackName.Text  == _textsEN["UnknownTitle"])
                TrackName.Text  = t["UnknownTitle"];
            if (ArtistName.Text == _textsTR["UnknownArtist"] || ArtistName.Text == _textsEN["UnknownArtist"])
                ArtistName.Text = t["UnknownArtist"];
        }

        private void LangMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _isEnglish = !_isEnglish;
            ApplyLanguage();
            // FIX #4: artık %AppData%'ya yazıyor
            try { IO.File.WriteAllText(_langFile, _isEnglish ? "EN" : "TR"); } catch { }
        }

        // ==========================================
        // 6. GLOBAL HOTKEY & PENCERE KORUMALARI
        // ==========================================
        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd   = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(HwndHook);
            RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_M); // Ctrl+Shift+M
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleWidgetVisibility();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ToggleWidgetVisibility()
        {
            if (this.Visibility == Visibility.Visible)
            {
                this.Visibility = Visibility.Hidden;
                _keepAliveTimer?.Stop();
            }
            else
            {
                this.Visibility  = Visibility.Visible;
                this.WindowState = WindowState.Normal;
                this.Activate();
                _keepAliveTimer?.Start();
                ForceTopmost();
            }
        }

        private void ForceTopmost()
        {
            try
            {
                if (this.Visibility != Visibility.Visible) return;
                var hwnd = new WindowInteropHelper(this).Handle;
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { }
        }

        private void SetupKeepOnTop()
        {
            // FIX #1: 50ms → 200ms — Win+D'ye karşı hâlâ etkili, CPU daha az yoruluyor
            _keepAliveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _keepAliveTimer.Tick += (s, a) => ForceTopmost();
            _keepAliveTimer.Start();
        }

        // ==========================================
        // 7. AYARLARIN YÜKLENMESİ VE SÜRÜKLEME
        // ==========================================

        // FIX #4: AppData klasörünü garantiye al
        private static void EnsureAppDataDir()
        {
            try { IO.Directory.CreateDirectory(_appDataDir); } catch { }
        }

        private void LoadSettings()
        {
            // Kayıtlı Dil Ayarını Oku
            try
            {
                if (IO.File.Exists(_langFile))
                    _isEnglish = IO.File.ReadAllText(_langFile).Trim() == "EN";
            }
            catch { }

            // Kayıtlı Konum Ayarını Oku
            try
            {
                if (IO.File.Exists(_posFile))
                {
                    var pos    = IO.File.ReadAllText(_posFile).Split('|');
                    this.Left  = double.Parse(pos[0]);
                    this.Top   = double.Parse(pos[1]);
                    _isPinned  = true;
                    return;
                }
            }
            catch { }

            // Kayıtlı konum yoksa ekranın sağ altına hizala
            this.Top  = SystemParameters.PrimaryScreenHeight - this.Height;
            this.Left = SystemParameters.PrimaryScreenWidth  - 450;
            _isPinned = false;
        }

        private void SavePosition()
        {
            try { IO.File.WriteAllText(_posFile, $"{this.Left}|{this.Top}"); } catch { }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isPinned && e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void TogglePin_Click(object sender, RoutedEventArgs e)
        {
            _isPinned    = PinMenuItem.IsChecked;
            this.Cursor  = _isPinned ? Cursors.Arrow : Cursors.SizeAll;
            if (_isPinned) SavePosition();
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        // ==========================================
        // 8. WINDOWS MEDYA SESSİON BAZLI MÜZİK TAKİBİ
        // ==========================================
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PinMenuItem.IsChecked = _isPinned;
            this.Cursor           = _isPinned ? Cursors.Arrow : Cursors.SizeAll;
            ApplyLanguage();
            ForceTopmost();

            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_manager != null)
            {
                _manager.CurrentSessionChanged += (s, _) =>
                    Dispatcher.Invoke(() => UpdateSession(s.GetCurrentSession()));
                UpdateSession(_manager.GetCurrentSession());
            }
        }

        private void UpdateSession(GlobalSystemMediaTransportControlsSession? session)
        {
            _session = session;
            if (session == null) return;

            session.MediaPropertiesChanged += (_, _) =>
                Dispatcher.Invoke(UpdateMediaProperties);

            UpdateMediaProperties();
        }

        private async void UpdateMediaProperties()
        {
            // FIX #2: _session null guard — race condition önlenir
            var session = _session;
            if (session == null) return;

            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null) return;

            var t      = _isEnglish ? _textsEN : _textsTR;
            var title  = string.IsNullOrWhiteSpace(props.Title)  ? t["UnknownTitle"]  : props.Title;
            var artist = string.IsNullOrWhiteSpace(props.Artist) ? t["UnknownArtist"] : props.Artist;

            TrackName.Text  = title;
            ArtistName.Text = artist;

            // Hash Color: şarkıya özgü renk tonu
            var trackId = title + artist;
            if (!string.IsNullOrEmpty(trackId))
                _currentBaseHue = Math.Abs(trackId.GetHashCode()) % 360;

            // Albüm Kapağı
            if (props.Thumbnail != null)
            {
                try
                {
                    var stream = await props.Thumbnail.OpenReadAsync();
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource  = stream.AsStreamForRead();
                    bitmap.CacheOption   = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    AlbumArt.Source = bitmap;
                }
                catch { /* thumbnail okunamazsa sessizce geç */ }
            }
        }

        // FIX #5: null-forgiving (!) operatörü kaldırıldı, güvenli null-conditional kullanıldı
        private async void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_session != null) await _session.TrySkipPreviousAsync();
        }

        private async void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_session != null) await _session.TryTogglePlayPauseAsync();
        }

        private async void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_session != null) await _session.TrySkipNextAsync();
        }

        // ==========================================
        // 9. NAUDIO DONANIMSAL SES SPEKTRUMU
        // ==========================================
        private void SetupVisualizer()
        {
            VisualizerCanvas.Children.Clear();
            double barWidth = (VisualizerCanvas.Width / BarCount) - 1.5;
            for (int i = 0; i < BarCount; i++)
            {
                var rect = new Rectangle
                {
                    Width    = barWidth,
                    Height   = 2,
                    Fill     = new SolidColorBrush(Colors.Cyan),
                    RadiusX  = 1,
                    RadiusY  = 1
                };
                Canvas.SetBottom(rect, 0);
                Canvas.SetLeft(rect, i * (barWidth + 1.5));
                VisualizerCanvas.Children.Add(rect);
                _bars[i] = rect;
            }
        }

        private void StartListening()
        {
            try
            {
                _capture = new WasapiLoopbackCapture();
                _capture.DataAvailable += OnAudioDataAvailable;
                _capture.StartRecording();
            }
            catch { }
        }

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_bars[0] == null || e.BytesRecorded == 0) return;

            float[] peaks       = new float[BarCount];
            int     bytesPerBar = e.BytesRecorded / BarCount;

            for (int i = 0; i < BarCount; i++)
            {
                float max   = 0;
                int   start = i * bytesPerBar;
                int   end   = Math.Min(start + bytesPerBar, e.BytesRecorded);

                for (int j = start; j < end - 3; j += 4)  // -3: sınır taşması önlenir
                {
                    float sample = Math.Abs(BitConverter.ToSingle(e.Buffer, j));
                    if (sample > max) max = sample;
                }
                peaks[i] = max;
            }

            Dispatcher.InvokeAsync(() =>
            {
                for (int i = 0; i < BarCount; i++)
                {
                    double centerMultiplier = 1.0 - Math.Abs((BarCount / 2.0) - i) / (BarCount / 1.5);
                    double newHeight        = Math.Max(2, Math.Min(20, peaks[i] * 500 * Math.Max(0.4, centerMultiplier)));
                    _bars[i].Height        += (newHeight - _bars[i].Height) * 0.3;

                    double lightness = 0.4 + (Math.Min(1.0, peaks[i] * 5) * 0.3);
                    _bars[i].Fill    = new SolidColorBrush(HslToRgb((_currentBaseHue + (i * 0.5)) % 360, 0.8, lightness));
                }
            });
        }

        private Color HslToRgb(double h, double s, double l)
        {
            double hue = h / 360.0;
            double v2  = (l < 0.5) ? (l * (1 + s)) : ((l + s) - (l * s));
            double v1  = 2 * l - v2;
            return Color.FromRgb(
                (byte)(255 * HueToRgb(v1, v2, hue + (1.0 / 3))),
                (byte)(255 * HueToRgb(v1, v2, hue)),
                (byte)(255 * HueToRgb(v1, v2, hue - (1.0 / 3)))
            );
        }

        private static double HueToRgb(double v1, double v2, double vH)
        {
            if (vH < 0) vH += 1;
            if (vH > 1) vH -= 1;
            if ((6 * vH) < 1) return v1 + (v2 - v1) * 6 * vH;
            if ((2 * vH) < 1) return v2;
            if ((3 * vH) < 2) return v1 + (v2 - v1) * ((2.0 / 3) - vH) * 6;
            return v1;
        }

        // ==========================================
        // 10. KAYNAK TEMİZLİĞİ (Kapanış)
        // ==========================================
        protected override void OnClosed(EventArgs e)
        {
            _keepAliveTimer?.Stop();

            // FIX #3: ses aygıtı düzgün kapatılıyor — kaynak sızıntısı önlenir
            try
            {
                _capture?.StopRecording();
                _capture?.Dispose();
            }
            catch { }

            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_ID);

            base.OnClosed(e);
        }
    }
}