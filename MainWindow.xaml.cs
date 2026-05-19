using System;
using System.Collections.Generic;
using System.IO;                        // AsStreamForRead() extension metodu
using IO = System.IO;                   // System.Windows.Shapes.Path çakışmasını önler
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Diagnostics;          // Debug logging
using NAudio.Wave;
using Windows.Media.Control;

namespace MusicWidget
{
    public partial class MainWindow : Window
    {
        // ══════════════════════════════════════════
        // 1. WIN32 API — IMPORT & STRUCT
        // ══════════════════════════════════════════

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        // 32/64-bit güvenli WindowLong sarmalayıcıları
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]    private static extern int    GetWindowLong32   (IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]    private static extern int    SetWindowLong32   (IntPtr hWnd, int nIndex, int    newLong);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr newLong);

        private static IntPtr GetWindowLong(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

        private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr newLong) =>
            IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, newLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, (int)newLong));

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int    x, y, cx, cy;
            public uint   flags;
        }

        // ══════════════════════════════════════════
        // 2. WIN32 SABİTLERİ
        // ══════════════════════════════════════════

        private static readonly IntPtr HWND_TOPMOST = new(-1);

        private const uint SWP_NOSIZE       = 0x0001;
        private const uint SWP_NOMOVE       = 0x0002;
        private const uint SWP_NOZORDER     = 0x0004;
        private const uint SWP_NOACTIVATE   = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW   = 0x0040;
        private const uint SWP_HIDEWINDOW   = 0x0080;

        private const int GWL_EXSTYLE      = -20;
        private const int GWL_HWNDPARENT   = -8;   // Owner ilişkisi (SetParent değil!)
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const int  HOTKEY_ID            = 9000;
        private const int  WM_HOTKEY            = 0x0312;
        private const int  WM_SYSCOMMAND        = 0x0112;
        private const int  WM_WINDOWPOSCHANGING = 0x0046;
        private const int  SC_MINIMIZE          = 0xF020;
        private const uint MOD_CONTROL          = 0x0002;
        private const uint MOD_SHIFT            = 0x0004;
        private const uint VK_M                 = 0x4D;   // Ctrl+Shift+M

        // ══════════════════════════════════════════
        // 3. VİZÜELİZER SABİTLERİ
        // ══════════════════════════════════════════

        private const int    BarCount              = 15;
        private const double BarSpacing            = 1.5;
        private const double BarMinHeight          = 2.0;
        private const double BarMaxHeight          = 20.0;
        private const double BarAmplitudeScale     = 500.0;
        private const double BarCenterFalloff      = 1.5;
        private const double BarMinCenterMult      = 0.4;
        private const double BarSmoothingFactor    = 0.3;
        private const double BarColorHueShift      = 0.5;
        private const double BarSaturation         = 0.8;
        private const double BarMinLightness       = 0.4;
        private const double BarLightnessRange     = 0.3;
        private const double BarLightnessPeakScale = 5.0;
        private const int    KeepAliveIntervalMs   = 200;

        // ══════════════════════════════════════════
        // 4. GLOBAL DEĞİŞKENLER
        // ══════════════════════════════════════════

        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession?        _session;
        private WasapiLoopbackCapture?                            _capture;

        private readonly Rectangle[] _bars = new Rectangle[BarCount];
        private DispatcherTimer?     _keepAliveTimer;
        private double               _currentBaseHue = 190.0;

        private bool   _isPinned         = true;
        private bool   _isDocked         = false;
        private bool   _isInternalAction = false;  // Hook'un kendi hareketleriyle çakışmasını önler
        private bool   _isLoaded         = false;  // Başlangıçta hook'un erken tetiklenmesini önler
        private double _savedLeft        = 0;      // Undock'ta geri dönülecek konum
        private double _savedTop         = 0;

        // AppData dosya yolları
        private static readonly string _appDataDir =
            IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicWidget");
        private readonly string _posFile  = IO.Path.Combine(_appDataDir, "pos.txt");
        private readonly string _langFile = IO.Path.Combine(_appDataDir, "lang.txt");
        private readonly string _dockFile = IO.Path.Combine(_appDataDir, "dock.txt");

        // ══════════════════════════════════════════
        // 5. DİL SÖZLÜKLERİ
        // ══════════════════════════════════════════

        private bool _isEnglish = false;

        private readonly Dictionary<string, string> _textsTR = new()
        {
            { "Pin",           "Konumu Sabitle"         },
            { "Dock",          "Görev Çubuğuna Kenetle" },
            { "Language",      "Language: English"      },
            { "Exit",          "Çıkış"                  },
            { "UnknownTitle",  "Bilinmiyor"             },
            { "UnknownArtist", "Şarkı bekleniyor..."    }
        };

        private readonly Dictionary<string, string> _textsEN = new()
        {
            { "Pin",           "Pin Position"           },
            { "Dock",          "Dock to Taskbar"        },
            { "Language",      "Dil: Türkçe"            },
            { "Exit",          "Exit"                   },
            { "UnknownTitle",  "Unknown"                },
            { "UnknownArtist", "Waiting for track..."   }
        };

        private Dictionary<string, string> CurrentTexts => _isEnglish ? _textsEN : _textsTR;

        // ══════════════════════════════════════════
        // 6. BAŞLATICI
        // ══════════════════════════════════════════

        public MainWindow()
        {
            InitializeComponent();
            EnsureAppDataDir();
            LoadSettings();          // _isDocked, _isPinned, konum, dil
            SetupVisualizer();
            StartListening();
            SetupWindowBehavior();
            this.SourceInitialized += MainWindow_SourceInitialized;
        }

        private void SetupWindowBehavior()
        {
            this.Topmost = true;

            // Yedek: SC_MINIMIZE hook'tan kaçarsa yakalar
            this.StateChanged += (_, _) =>
            {
                if (this.WindowState == WindowState.Minimized)
                    this.WindowState = WindowState.Normal;
            };

            this.Deactivated += (_, _) => ForceTopmost();
            SetupKeepOnTop();
        }

        // ══════════════════════════════════════════
        // 7. WIN32 HOOK — MİNİMİZE & POZİSYON KORUMASI
        // ══════════════════════════════════════════

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd   = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(HwndHook);
            RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_M);

            // Tıklandığında odak çalmayan pencere stili
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_NOACTIVATE));
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // ── Ctrl+Shift+M: göster/gizle ──────────────────────────
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleWidgetVisibility();
                handled = true;
                return IntPtr.Zero;
            }

            // ── Katman 1: SC_MINIMIZE mesajını yut ──────────────────
            if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_MINIMIZE)
            {
                handled = true;
                return IntPtr.Zero;
            }

            // ── Katman 2: Pozisyon değişimlerini denetle ─────────────
            // _isLoaded: başlangıç tamamlanmadan çalışma
            // _isInternalAction: kendi SetWindowPos çağrılarıyla çakışma
            if (msg == WM_WINDOWPOSCHANGING && _isLoaded && !_isInternalAction)
            {
                var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);

                if (_isDocked)
                {
                    // Dock modunda: yalnızca gerçek hareket sırasında Y eksenini kilitle
                    if ((wp.flags & SWP_NOMOVE) == 0)
                    {
                        IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);
                        if (taskbarHwnd != IntPtr.Zero)
                        {
                            GetWindowRect(taskbarHwnd, out RECT tb);
                            wp.y = tb.Top;
                        }
                    }
                }
                else
                {
                    // Serbest modda: -32000 minimize koordinatını ve hide bayrağını engelle
                    bool isMinimizing = wp.x <= -30000 || wp.y <= -30000;
                    bool isHiding     = (wp.flags & SWP_HIDEWINDOW) != 0;

                    if (isMinimizing || isHiding)
                    {
                        wp.flags &= ~SWP_HIDEWINDOW;
                        wp.flags |=  SWP_SHOWWINDOW | SWP_NOMOVE | SWP_NOSIZE;
                    }
                }

                // FIX: false — blittable struct için doğru parametre
                Marshal.StructureToPtr(wp, lParam, false);
            }

            return IntPtr.Zero;
        }

        // ══════════════════════════════════════════
        // 8. TOPMOST & GÖRÜNÜRLÜK
        // ══════════════════════════════════════════

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

        private void ToggleWidgetVisibility()
        {
            if (this.Visibility == Visibility.Visible) HideWidget();
            else                                       ShowWidget();
        }

        private void HideWidget()
        {
            _isInternalAction = true;
            try   { this.Visibility = Visibility.Hidden; }
            finally { _isInternalAction = false; }
            _keepAliveTimer?.Stop();
        }

        private void ShowWidget()
        {
            _isInternalAction = true;
            try
            {
                this.Visibility  = Visibility.Visible;
                this.WindowState = WindowState.Normal;
                this.Activate();
            }
            finally { _isInternalAction = false; }
            _keepAliveTimer?.Start();
            ForceTopmost();
        }

        // ══════════════════════════════════════════
        // 9. GÖREV ÇUBUĞUNA KENETLEME (DOCK)
        // ══════════════════════════════════════════

        private void ToggleDock_Click(object sender, RoutedEventArgs e) =>
            DockToTaskbar(DockMenuItem.IsChecked);

        private void DockToTaskbar(bool dock)
        {
            var    hwnd        = new WindowInteropHelper(this).Handle;
            IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);

            _isInternalAction = true;
            try
            {
                if (dock)
                {
                    if (taskbarHwnd == IntPtr.Zero) return;

                    // Undock için mevcut konumu sakla (ilk dock'ta çağrılır)
                    if (!_isDocked)
                    {
                        _savedLeft = this.Left;
                        _savedTop  = this.Top;
                    }

                    GetWindowRect(taskbarHwnd, out RECT tb);
                    GetWindowRect(hwnd,        out RECT wr);

                    int w = wr.Right  - wr.Left;
                    int h = wr.Bottom - wr.Top;
                    int x = wr.Left;   // Yatay konum serbest kalır

                    // Görev çubuğunu owner yap (SetParent değil — koordinat kırılması olmaz)
                    SetWindowLong(hwnd, GWL_HWNDPARENT, taskbarHwnd);
                    SetWindowPos(hwnd, HWND_TOPMOST, x, tb.Top, w, h,
                                 SWP_NOACTIVATE | SWP_FRAMECHANGED);

                    _isDocked = true;
                    _isPinned = true;
                    if (PinMenuItem != null) { PinMenuItem.IsChecked = true; PinMenuItem.IsEnabled = false; }
                    this.Cursor = Cursors.Arrow;
                    TrySave(() => IO.File.WriteAllText(_dockFile, "1"));
                }
                else
                {
                    // Owner bağını kopar
                    SetWindowLong(hwnd, GWL_HWNDPARENT, IntPtr.Zero);
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                                 SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

                    _isDocked = false;
                    if (PinMenuItem != null) PinMenuItem.IsEnabled = true;

                    // FIX: _savedLeft/_savedTop her zaman geçerli bir değer taşır
                    this.Left = _savedLeft;
                    this.Top  = _savedTop;

                    TrySave(() => IO.File.WriteAllText(_dockFile, "0"));
                }
            }
            finally { _isInternalAction = false; }
        }

        /// <summary>
        /// Sürükleme bittiğinde widget görev çubuğuyla kesişiyorsa otomatik kenetler.
        /// </summary>
        private void CheckAndAutoDock()
        {
            if (_isDocked) return;

            var    hwnd        = new WindowInteropHelper(this).Handle;
            IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd == IntPtr.Zero) return;

            GetWindowRect(hwnd,        out RECT wr);
            GetWindowRect(taskbarHwnd, out RECT tb);

            bool intersects = wr.Left   < tb.Right  &&
                              wr.Right  > tb.Left   &&
                              wr.Top    < tb.Bottom &&
                              wr.Bottom > tb.Top;

            if (intersects)
            {
                if (DockMenuItem != null) DockMenuItem.IsChecked = true;
                DockToTaskbar(true);
            }
        }

        // ══════════════════════════════════════════
        // 10. AYARLAR (KONUM, DİL, DOCK)
        // ══════════════════════════════════════════

        private static void EnsureAppDataDir() =>
            TrySave(() => IO.Directory.CreateDirectory(_appDataDir));

        private void LoadSettings()
        {
            LoadLanguageSetting();
            LoadPositionSetting();   // _savedLeft/_savedTop da burada set edilir
            LoadDockSetting();
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
                    var parts = IO.File.ReadAllText(_posFile).Split('|');
                    this.Left  = double.Parse(parts[0]);
                    this.Top   = double.Parse(parts[1]);
                    _isPinned  = true;

                    // FIX: Dock'lu başlayıp undock yapılırsa doğru konuma döner
                    _savedLeft = this.Left;
                    _savedTop  = this.Top;
                    return;
                }
            }
            catch { }

            // Kayıtlı konum yoksa — ekranın sağ altı
            this.Top   = SystemParameters.PrimaryScreenHeight - this.Height;
            this.Left  = SystemParameters.PrimaryScreenWidth  - 450;
            _savedLeft = this.Left;
            _savedTop  = this.Top;
            _isPinned  = false;
        }

        private void LoadDockSetting()
        {
            try
            {
                if (IO.File.Exists(_dockFile))
                    _isDocked = IO.File.ReadAllText(_dockFile).Trim() == "1";
            }
            catch { }
        }

        private void SavePosition()
        {
            if (_isDocked) return;  // Dock'luyken konum kaydetme
            TrySave(() => IO.File.WriteAllText(_posFile, $"{this.Left}|{this.Top}"));
        }

        // ══════════════════════════════════════════
        // 11. SÜRÜKLEME & SABITLEME
        // ══════════════════════════════════════════

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isPinned && !_isDocked && e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    this.DragMove();
                    CheckAndAutoDock();   // Bırakınca görev çubuğu kontrolü
                }
                catch { }
            }
        }

        private void TogglePin_Click(object sender, RoutedEventArgs e)
        {
            _isPinned   = PinMenuItem.IsChecked;
            this.Cursor = _isPinned ? Cursors.Arrow : Cursors.SizeAll;
            if (_isPinned) SavePosition();
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        // ══════════════════════════════════════════
        // 12. DİL YÖNETİMİ
        // ══════════════════════════════════════════

        private void ApplyLanguage()
        {
            var t = CurrentTexts;
            if (PinMenuItem  != null) PinMenuItem.Header  = t["Pin"];
            if (DockMenuItem != null) DockMenuItem.Header = t["Dock"];
            if (LangMenuItem != null) LangMenuItem.Header = t["Language"];
            if (ExitMenuItem != null) ExitMenuItem.Header = t["Exit"];
            UpdatePlaceholderTexts(t);
        }

        private void UpdatePlaceholderTexts(Dictionary<string, string> t)
        {
            bool titleEmpty  = TrackName.Text  == _textsTR["UnknownTitle"]  || TrackName.Text  == _textsEN["UnknownTitle"];
            bool artistEmpty = ArtistName.Text == _textsTR["UnknownArtist"] || ArtistName.Text == _textsEN["UnknownArtist"];
            if (titleEmpty)  TrackName.Text  = t["UnknownTitle"];
            if (artistEmpty) ArtistName.Text = t["UnknownArtist"];
        }

        private void LangMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _isEnglish = !_isEnglish;
            ApplyLanguage();
            TrySave(() => IO.File.WriteAllText(_langFile, _isEnglish ? "EN" : "TR"));
        }

        // ══════════════════════════════════════════
        // 13. WINDOWS MEDYA SESSİON (SMTC)
        // ══════════════════════════════════════════

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (PinMenuItem != null) PinMenuItem.IsChecked = _isPinned;
            this.Cursor = _isPinned ? Cursors.Arrow : Cursors.SizeAll;
            ApplyLanguage();
            ForceTopmost();

            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            if (_manager != null)
            {
                _manager.CurrentSessionChanged += (s, _) =>
                    Dispatcher.Invoke(() => UpdateSession(s.GetCurrentSession()));
                UpdateSession(_manager.GetCurrentSession());
            }

            // Tüm başlangıç işlemleri tamamlandı, hook aktif olabilir
            _isLoaded = true;

            // Dock ayarı dosyadan geldiyse uygula
            if (_isDocked)
            {
                if (DockMenuItem != null) DockMenuItem.IsChecked = true;
                DockToTaskbar(true);
            }
        }

        private void UpdateSession(GlobalSystemMediaTransportControlsSession? session)
        {
            _session = session;
            if (session == null) return;
            session.MediaPropertiesChanged += (_, _) => Dispatcher.Invoke(UpdateMediaProperties);
            UpdateMediaProperties();
        }

        private async void UpdateMediaProperties()
        {
            var session = _session;   // Race condition için lokal kopya
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

        /// <summary>Şarkı adı + sanatçıdan tutarlı bir HSL hue üretir (Hash Color).</summary>
        private void UpdateHashColor(string title, string artist)
        {
            var id = title + artist;
            if (!string.IsNullOrEmpty(id))
                _currentBaseHue = Math.Abs(id.GetHashCode()) % 360;
        }

        /// <summary>SMTC thumbnail'ini albüm kapağı olarak yükler.</summary>
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
            catch (Exception ex) { Log(ex); }
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

        // ══════════════════════════════════════════
        // 14. SES SPEKTRUMU (NAudio)
        // ══════════════════════════════════════════

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
            catch (Exception ex) { Log(ex); }
        }

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_bars[0] == null || e.BytesRecorded == 0) return;
            var peaks = ComputePeaks(e);
            Dispatcher.InvokeAsync(() => UpdateBars(peaks));
        }

        /// <summary>Ham PCM buffer'dan her bar için tepe genliği hesaplar.</summary>
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
                    float s = Math.Abs(BitConverter.ToSingle(e.Buffer, j));
                    if (s > max) max = s;
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
                double hue       = (_currentBaseHue + i * BarColorHueShift) % 360;
                _bars[i].Fill    = new SolidColorBrush(HslToRgb(hue, BarSaturation, lightness));
            }
        }

        // ══════════════════════════════════════════
        // 15. RENK DÖNÜŞÜMÜ (HSL → RGB)
        // ══════════════════════════════════════════

        private static Color HslToRgb(double h, double s, double l)
        {
            double hue = h / 360.0;
            double v2  = l < 0.5 ? l * (1 + s) : (l + s) - (l * s);
            double v1  = 2 * l - v2;
            return Color.FromRgb(
                (byte)(255 * HueToRgb(v1, v2, hue + 1.0 / 3)),
                (byte)(255 * HueToRgb(v1, v2, hue)),
                (byte)(255 * HueToRgb(v1, v2, hue - 1.0 / 3)));
        }

        private static double HueToRgb(double v1, double v2, double vH)
        {
            if (vH < 0) vH += 1;
            if (vH > 1) vH -= 1;
            if (6 * vH < 1) return v1 + (v2 - v1) * 6 * vH;
            if (2 * vH < 1) return v2;
            if (3 * vH < 2) return v1 + (v2 - v1) * (2.0 / 3 - vH) * 6;
            return v1;
        }

        // ══════════════════════════════════════════
        // 16. YARDIMCI & TEMİZLİK
        // ══════════════════════════════════════════

        private static void TrySave(Action action)
        {
            try   { action(); }
            catch (Exception ex) { Log(ex); }
        }

        /// <summary>
        /// Hataları debug çıktısına yazar. Release build'de no-op.
        /// Production loglaması için buraya dosya/telemetri eklenebilir.
        /// </summary>
        [Conditional("DEBUG")]
        private static void Log(Exception ex) =>
            Debug.WriteLine($"[MusicWidget] {DateTime.Now:HH:mm:ss} — {ex.GetType().Name}: {ex.Message}");

        protected override void OnClosed(EventArgs e)
        {
            _keepAliveTimer?.Stop();
            try { _capture?.StopRecording(); _capture?.Dispose(); } catch (Exception ex) { Log(ex); }
            UnregisterHotKey(new WindowInteropHelper(this).Handle, HOTKEY_ID);
            base.OnClosed(e);
        }
    }
}