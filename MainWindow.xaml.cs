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
        // 1. WIN32 API TANIMLAMALARI
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
        private const int  WM_SYSCOMMAND  = 0x0112;  // Sistem komutları (minimize, close vs.)
        private const int  SC_MINIMIZE    = 0xF020;  // Win+D bu komutu gönderir
        private const uint MOD_CONTROL    = 0x0002;
        private const uint MOD_SHIFT      = 0x0004;
        private const uint VK_M           = 0x4D;

        // ==========================================
        // 2. VİZÜELİZER SABİTLERİ (magic number'lar → const)
        // ==========================================
        private const int    BarCount              = 15;
        private const double BarSpacing            = 1.5;
        private const double BarMinHeight          = 2.0;
        private const double BarMaxHeight          = 20.0;
        private const double BarAmplitudeScale     = 500.0;  // ham PCM → piksel yüksekliği
        private const double BarCenterFalloff      = 1.5;    // orta bar vurgusu katsayısı
        private const double BarMinCenterMult      = 0.4;    // kenar barların minimum çarpanı
        private const double BarSmoothingFactor    = 0.3;    // yumuşak geçiş sönümlenmesi
        private const double BarColorHueShift      = 0.5;    // bar başına renk kayması (derece)
        private const double BarSaturation         = 0.8;
        private const double BarMinLightness       = 0.4;
        private const double BarLightnessRange     = 0.3;
        private const double BarLightnessPeakScale = 5.0;
        private const int    KeepAliveIntervalMs   = 200;    // Win+D koruma timer aralığı

        // ==========================================
        // 3. GLOBAL DEĞİŞKENLER
        // ==========================================
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession?        _session;
        private WasapiLoopbackCapture?                            _capture;

        private readonly Rectangle[] _bars = new Rectangle[BarCount];
        private bool             _isPinned       = true;
        private DispatcherTimer? _keepAliveTimer;
        private double           _currentBaseHue = 190.0;

        private static readonly string _appDataDir =
            IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicWidget");
        private readonly string _posFile  = IO.Path.Combine(_appDataDir, "pos.txt");
        private readonly string _langFile = IO.Path.Combine(_appDataDir, "lang.txt");

        // ==========================================
        // 4. DİL SÖZLÜKLERİ
        // ==========================================
        private bool _isEnglish = false;

        private readonly Dictionary<string, string> _textsTR = new()
        {
            { "Pin",           "Konumu Sabitle"      },
            { "Language",      "Language: English"   },
            { "Exit",          "Çıkış"               },
            { "UnknownTitle",  "Bilinmiyor"          },
            { "UnknownArtist", "Şarkı bekleniyor..." }
        };

        private readonly Dictionary<string, string> _textsEN = new()
        {
            { "Pin",           "Pin Position"        },
            { "Language",      "Dil: Türkçe"         },
            { "Exit",          "Exit"                 },
            { "UnknownTitle",  "Unknown"              },
            { "UnknownArtist", "Waiting for track..." }
        };

        // ==========================================
        // 5. BAŞLATICI
        // ==========================================
        public MainWindow()
        {
            InitializeComponent();
            EnsureAppDataDir();
            LoadSettings();
            SetupVisualizer();
            StartListening();
            SetupWindowBehavior();
            this.SourceInitialized += MainWindow_SourceInitialized;
        }

        /// <summary>Pencere koruma event'lerini ve keepAlive timer'ı kurar.</summary>
        private void SetupWindowBehavior()
        {
            this.Topmost = true;

            // SC_MINIMIZE HwndHook'ta engelleniyor; bu sadece beklenmedik durum için yedek
            this.StateChanged += (_, _) =>
            {
                if (this.WindowState == WindowState.Minimized)
                    this.WindowState = WindowState.Normal;
            };

            this.Deactivated += (_, _) => ForceTopmost();
            SetupKeepOnTop();
        }

        // ==========================================
        // 6. DİL YÖNETİMİ
        // ==========================================
        private Dictionary<string, string> CurrentTexts => _isEnglish ? _textsEN : _textsTR;

        private void ApplyLanguage()
        {
            var t = CurrentTexts;
            if (PinMenuItem  != null) PinMenuItem.Header  = t["Pin"];
            if (LangMenuItem != null) LangMenuItem.Header = t["Language"];
            if (ExitMenuItem != null) ExitMenuItem.Header = t["Exit"];
            UpdatePlaceholderTexts(t);
        }

        /// <summary>Sadece placeholder metinleri çevirir; gerçek şarkı adına dokunmaz.</summary>
        private void UpdatePlaceholderTexts(Dictionary<string, string> t)
        {
            bool titleIsPlaceholder  = TrackName.Text  == _textsTR["UnknownTitle"]
                                    || TrackName.Text  == _textsEN["UnknownTitle"];
            bool artistIsPlaceholder = ArtistName.Text == _textsTR["UnknownArtist"]
                                    || ArtistName.Text == _textsEN["UnknownArtist"];

            if (titleIsPlaceholder)  TrackName.Text  = t["UnknownTitle"];
            if (artistIsPlaceholder) ArtistName.Text = t["UnknownArtist"];
        }

        private void LangMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _isEnglish = !_isEnglish;
            ApplyLanguage();
            TrySave(() => IO.File.WriteAllText(_langFile, _isEnglish ? "EN" : "TR"));
        }

        // ==========================================
        // 7. PENCERE KORUMALARI & HOTKEY
        // ==========================================
        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd   = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(HwndHook);
            RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_M);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Ctrl+Shift+M hotkey → widget'ı göster/gizle
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleWidgetVisibility();
                handled = true;
            }

            // Win+D ve görev çubuğundan minimize → tamamen engelle, sıfır flicker
            if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_MINIMIZE)
            {
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void ToggleWidgetVisibility()
        {
            if (this.Visibility == Visibility.Visible) HideWidget();
            else                                       ShowWidget();
        }

        private void HideWidget()
        {
            this.Visibility = Visibility.Hidden;
            _keepAliveTimer?.Stop();
        }

        private void ShowWidget()
        {
            this.Visibility  = Visibility.Visible;
            this.WindowState = WindowState.Normal;
            this.Activate();
            _keepAliveTimer?.Start();
            ForceTopmost();
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
            _keepAliveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(KeepAliveIntervalMs) };
            _keepAliveTimer.Tick += (_, _) => ForceTopmost();
            _keepAliveTimer.Start();
        }

        // ==========================================
        // 8. AYARLAR — KONUM & DİL
        // ==========================================
        private static void EnsureAppDataDir() =>
            TrySave(() => IO.Directory.CreateDirectory(_appDataDir));

        private void LoadSettings()
        {
            LoadLanguageSetting();
            LoadPositionSetting();
        }

        private void LoadLanguageSetting()
        {
            try
            {
                if (IO.File.Exists(_langFile))
                    _isEnglish = IO.File.ReadAllText(_langFile).Trim() == "EN";
            }
            catch { }
        }

        private void LoadPositionSetting()
        {
            try
            {
                if (IO.File.Exists(_posFile))
                {
                    var pos   = IO.File.ReadAllText(_posFile).Split('|');
                    this.Left = double.Parse(pos[0]);
                    this.Top  = double.Parse(pos[1]);
                    _isPinned = true;
                    return;
                }
            }
            catch { }

            this.Top  = SystemParameters.PrimaryScreenHeight - this.Height;
            this.Left = SystemParameters.PrimaryScreenWidth  - 450;
            _isPinned = false;
        }

        private void SavePosition() =>
            TrySave(() => IO.File.WriteAllText(_posFile, $"{this.Left}|{this.Top}"));

        // ==========================================
        // 9. SÜRÜKLEME & SABITLEME
        // ==========================================
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isPinned && e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void TogglePin_Click(object sender, RoutedEventArgs e)
        {
            _isPinned   = PinMenuItem.IsChecked;
            this.Cursor = _isPinned ? Cursors.Arrow : Cursors.SizeAll;
            if (_isPinned) SavePosition();
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        // ==========================================
        // 10. WINDOWS MEDYA SESSİON (SMTC)
        // ==========================================
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PinMenuItem.IsChecked = _isPinned;
            this.Cursor           = _isPinned ? Cursors.Arrow : Cursors.SizeAll;
            ApplyLanguage();
            ForceTopmost();

            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_manager == null) return;

            _manager.CurrentSessionChanged += (s, _) =>
                Dispatcher.Invoke(() => UpdateSession(s.GetCurrentSession()));

            UpdateSession(_manager.GetCurrentSession());
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
            var session = _session;
            if (session == null) return;

            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null) return;

            var t      = CurrentTexts;
            var title  = string.IsNullOrWhiteSpace(props.Title)  ? t["UnknownTitle"]  : props.Title;
            var artist = string.IsNullOrWhiteSpace(props.Artist) ? t["UnknownArtist"] : props.Artist;

            TrackName.Text  = title;
            ArtistName.Text = artist;

            UpdateHashColor(title, artist);
            await TryLoadAlbumArtAsync(props);
        }

        /// <summary>Şarkı adı + sanatçıdan benzersiz bir hue üretir (Hash Color).</summary>
        private void UpdateHashColor(string title, string artist)
        {
            var trackId = title + artist;
            if (!string.IsNullOrEmpty(trackId))
                _currentBaseHue = Math.Abs(trackId.GetHashCode()) % 360;
        }

        /// <summary>Albüm kapağını SMTC thumbnail'inden çekip AlbumArt kontrolüne yükler.</summary>
        private async System.Threading.Tasks.Task TryLoadAlbumArtAsync(
            GlobalSystemMediaTransportControlsSessionMediaProperties props)
        {
            if (props.Thumbnail == null) return;
            try
            {
                var stream = await props.Thumbnail.OpenReadAsync();
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = stream.AsStreamForRead();
                bitmap.CacheOption  = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                AlbumArt.Source = bitmap;
            }
            catch { }
        }

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
        // 11. SES SPEKTRUMU (NAudio)
        // ==========================================
        private void SetupVisualizer()
        {
            VisualizerCanvas.Children.Clear();
            double barWidth = (VisualizerCanvas.Width / BarCount) - BarSpacing;

            for (int i = 0; i < BarCount; i++)
            {
                var rect = new Rectangle
                {
                    Width   = barWidth,
                    Height  = BarMinHeight,
                    Fill    = new SolidColorBrush(Colors.Cyan),
                    RadiusX = 1,
                    RadiusY = 1
                };
                Canvas.SetBottom(rect, 0);
                Canvas.SetLeft(rect, i * (barWidth + BarSpacing));
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
            var peaks = ComputePeaks(e);
            Dispatcher.InvokeAsync(() => UpdateBars(peaks));
        }

        /// <summary>Ham PCM buffer'ından her bar için tepe genliği hesaplar.</summary>
        private static float[] ComputePeaks(WaveInEventArgs e)
        {
            var peaks       = new float[BarCount];
            int bytesPerBar = e.BytesRecorded / BarCount;

            for (int i = 0; i < BarCount; i++)
            {
                float max   = 0;
                int   start = i * bytesPerBar;
                int   end   = Math.Min(start + bytesPerBar, e.BytesRecorded);

                for (int j = start; j < end - 3; j += 4)
                {
                    float sample = Math.Abs(BitConverter.ToSingle(e.Buffer, j));
                    if (sample > max) max = sample;
                }
                peaks[i] = max;
            }
            return peaks;
        }

        /// <summary>Peak değerlerine göre bar yüksekliklerini ve renklerini günceller.</summary>
        private void UpdateBars(float[] peaks)
        {
            for (int i = 0; i < BarCount; i++)
            {
                double centerMult = 1.0 - Math.Abs((BarCount / 2.0) - i) / (BarCount / BarCenterFalloff);
                double targetH    = Math.Max(BarMinHeight,
                                    Math.Min(BarMaxHeight,
                                        peaks[i] * BarAmplitudeScale * Math.Max(BarMinCenterMult, centerMult)));

                _bars[i].Height += (targetH - _bars[i].Height) * BarSmoothingFactor;

                double lightness = BarMinLightness + (Math.Min(1.0, peaks[i] * BarLightnessPeakScale) * BarLightnessRange);
                double hue       = (_currentBaseHue + (i * BarColorHueShift)) % 360;
                _bars[i].Fill    = new SolidColorBrush(HslToRgb(hue, BarSaturation, lightness));
            }
        }

        // ==========================================
        // 12. RENK DÖNÜŞÜMÜ (HSL → RGB)
        // ==========================================
        private static Color HslToRgb(double h, double s, double l)
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
        // 13. YARDIMCI METODLAR
        // ==========================================

        /// <summary>I/O işlemlerini sessizce dener; exception'ı yutar.</summary>
        private static void TrySave(Action action)
        {
            try { action(); } catch { }
        }

        // ==========================================
        // 14. KAYNAK TEMİZLİĞİ
        // ==========================================
        protected override void OnClosed(EventArgs e)
        {
            _keepAliveTimer?.Stop();

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