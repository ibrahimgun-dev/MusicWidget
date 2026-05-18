using System;
using System.IO;
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

        // Pencere Sabitleri
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        // Global Hotkey Sabitleri (Ctrl + Shift + M)
        private const int HOTKEY_ID = 9000;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_M = 0x4D;

        // ==========================================
        // 2. GLOBAL DEĞİŞKENLER
        // ==========================================
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private WasapiLoopbackCapture? _capture;
        
        private const int BarCount = 15; 
        private Rectangle[] _bars = new Rectangle[BarCount];
        private bool _isPinned = true; 
        private DispatcherTimer? _keepAliveTimer; 
        private double _currentBaseHue = 190.0; 
        private string _posFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pos.txt");

        public MainWindow()
        {
            InitializeComponent();
            
            LoadPosition(); 
            SetupVisualizer();
            StartListening();
            this.Topmost = true; 

            // İnatçı Anti-Minimize Koruması
            this.StateChanged += (s, e) => { if (this.WindowState == WindowState.Minimized) { this.WindowState = WindowState.Normal; ForceTopmost(); } };
            this.Deactivated += (s, e) => ForceTopmost();

            SetupKeepOnTop();

            // Hotkey için mesaj dinleme kancasını (Hook) başlat
            this.SourceInitialized += MainWindow_SourceInitialized;
        }

        // ==========================================
        // 3. GLOBAL HOTKEY (YENİ EKLENEN KISIM)
        // ==========================================
        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(HwndHook);

            // Ctrl + Shift + M kısayolunu kaydet
            RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_M);
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
                // Gizlendiğinde gereksiz API çağrılarını durdur
                _keepAliveTimer?.Stop(); 
            }
            else
            {
                this.Visibility = Visibility.Visible;
                this.WindowState = WindowState.Normal;
                this.Activate();
                // Ekrana dönünce inatçı korumayı tekrar başlat
                _keepAliveTimer?.Start(); 
                ForceTopmost();
            }
        }

        // ==========================================
        // 4. ARAYÜZ VE GÜÇLÜ KORUMA METOTLARI
        // ==========================================
        private void ForceTopmost()
        {
            try {
                // Görünmez durumdaysa API'yi yormamak için atla
                if (this.Visibility != Visibility.Visible) return;

                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            } catch { }
        }

        private void SetupKeepOnTop()
        {
            _keepAliveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _keepAliveTimer.Tick += (sender, args) => ForceTopmost();
            _keepAliveTimer.Start();
        }

        private void LoadPosition()
        {
            try {
                if (System.IO.File.Exists(_posFile)) {
                    string[] pos = System.IO.File.ReadAllText(_posFile).Split('|');
                    this.Left = double.Parse(pos[0]);
                    this.Top = double.Parse(pos[1]);
                    _isPinned = true; 
                    return;
                }
            } catch { }
            
            this.Top = SystemParameters.PrimaryScreenHeight - this.Height; 
            this.Left = SystemParameters.PrimaryScreenWidth - 450;
            _isPinned = false; 
        }

        private void SavePosition()
        {
            try {
                System.IO.File.WriteAllText(_posFile, $"{this.Left}|{this.Top}");
            } catch { }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isPinned && e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void TogglePin_Click(object sender, RoutedEventArgs e)
        {
            _isPinned = PinMenuItem.IsChecked;
            this.Cursor = _isPinned ? Cursors.Arrow : Cursors.SizeAll;
            if (_isPinned) SavePosition(); 
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        // ==========================================
        // 5. MEDYA BİLGİSİ VE KONTROLLER
        // ==========================================
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PinMenuItem.IsChecked = _isPinned;
            this.Cursor = _isPinned ? Cursors.Arrow : Cursors.SizeAll;

            ForceTopmost(); 
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_manager != null) {
                _manager.CurrentSessionChanged += (s, arg) => Dispatcher.Invoke(() => UpdateSession(s.GetCurrentSession()));
                UpdateSession(_manager.GetCurrentSession());
            }
        }

        private void UpdateSession(GlobalSystemMediaTransportControlsSession? session)
        {
            _session = session;
            if (session == null) return;
            session.MediaPropertiesChanged += (s, a) => Dispatcher.Invoke(UpdateMediaProperties);
            UpdateMediaProperties();
        }

        private async void UpdateMediaProperties()
        {
            if (_session == null) return;
            var props = await _session.TryGetMediaPropertiesAsync();
            string title = props.Title ?? "Bilinmiyor";
            string artist = props.Artist ?? "";
            TrackName.Text = title; ArtistName.Text = artist;

            string trackId = title + artist;
            if (!string.IsNullOrEmpty(trackId)) {
                _currentBaseHue = Math.Abs(trackId.GetHashCode()) % 360;
            }

            if (props.Thumbnail != null) {
                var stream = await props.Thumbnail.OpenReadAsync();
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.StreamSource = stream.AsStreamForRead();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit();
                AlbumArt.Source = bitmap;
            }
        }

        private async void PrevBtn_Click(object sender, RoutedEventArgs e) => await _session?.TrySkipPreviousAsync()!;
        private async void PlayPauseBtn_Click(object sender, RoutedEventArgs e) => await _session?.TryTogglePlayPauseAsync()!;
        private async void NextBtn_Click(object sender, RoutedEventArgs e) => await _session?.TrySkipNextAsync()!;

        // ==========================================
        // 6. GÖRSELLEŞTİRİCİ (VISUALIZER) VE NAUDIO
        // ==========================================
        private void SetupVisualizer()
        {
            VisualizerCanvas.Children.Clear();
            double barWidth = (VisualizerCanvas.Width / BarCount) - 1.5;
            for (int i = 0; i < BarCount; i++) {
                var rect = new Rectangle { Width = barWidth, Height = 2, Fill = new SolidColorBrush(Colors.Cyan), RadiusX = 1, RadiusY = 1 };
                Canvas.SetBottom(rect, 0); Canvas.SetLeft(rect, i * (barWidth + 1.5));
                VisualizerCanvas.Children.Add(rect); _bars[i] = rect;
            }
        }

        private void StartListening()
        {
            try {
                _capture = new WasapiLoopbackCapture();
                _capture.DataAvailable += OnAudioDataAvailable;
                _capture.StartRecording();
            } catch { }
        }

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_bars[0] == null) return;
            float[] peaks = new float[BarCount];
            int bytesPerBar = e.BytesRecorded / BarCount;
            for (int i = 0; i < BarCount; i++) {
                float max = 0;
                int start = i * bytesPerBar;
                int end = Math.Min(start + bytesPerBar, e.BytesRecorded);
                for (int j = start; j < end; j += 4) {
                    float sample = Math.Abs(BitConverter.ToSingle(e.Buffer, j));
                    if (sample > max) max = sample;
                }
                peaks[i] = max;
            }

            Dispatcher.InvokeAsync(() => {
                for (int i = 0; i < BarCount; i++) {
                    double centerMultiplier = 1.0 - Math.Abs((BarCount / 2.0) - i) / (BarCount / 1.5);
                    double newHeight = Math.Max(2, Math.Min(20, peaks[i] * 500 * Math.Max(0.4, centerMultiplier)));
                    _bars[i].Height += (newHeight - _bars[i].Height) * 0.3;
                    double lightness = 0.4 + (Math.Min(1.0, peaks[i] * 5) * 0.3);
                    _bars[i].Fill = new SolidColorBrush(HslToRgb((_currentBaseHue + (i * 0.5)) % 360, 0.8, lightness));
                }
            });
        }

        private Color HslToRgb(double h, double s, double l) {
            double hue = h / 360.0;
            double v2 = (l < 0.5) ? (l * (1 + s)) : ((l + s) - (l * s));
            double v1 = 2 * l - v2;
            return Color.FromRgb((byte)(255 * HueToRgb(v1, v2, hue + (1.0 / 3))), (byte)(255 * HueToRgb(v1, v2, hue)), (byte)(255 * HueToRgb(v1, v2, hue - (1.0 / 3))));
        }

        private double HueToRgb(double v1, double v2, double vH) {
            if (vH < 0) vH += 1; if (vH > 1) vH -= 1;
            if ((6 * vH) < 1) return (v1 + (v2 - v1) * 6 * vH);
            if ((2 * vH) < 1) return v2;
            if ((3 * vH) < 2) return (v1 + (v2 - v1) * ((2.0 / 3) - vH) * 6);
            return v1;
        }

        // ==========================================
        // 7. TEMİZLİK
        // ==========================================
        protected override void OnClosed(EventArgs e) 
        { 
            _keepAliveTimer?.Stop(); 
            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_ID);
            base.OnClosed(e); 
        }
    }
}