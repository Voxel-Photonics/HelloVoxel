using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Voxon
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Persist.Load();     // seed Bridge from hellovoxel.json before anything reads it
            Audio.Init();

            var gameThread = new Thread(GameHost.RunGame) { Name = "VLED-Game", IsBackground = false };
            gameThread.SetApartmentState(ApartmentState.STA);
            gameThread.Start();

            try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
            catch (Exception e) { Console.WriteLine("[ui] Avalonia failed: " + e.Message); }

            Bridge.Quit = true;
            gameThread.Join(4000);
            Audio.Dispose();
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }

    internal sealed class App : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Dark;
        }
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new GameWindow();
            base.OnFrameworkInitializationCompleted();
        }
    }

    // Hosts the LedWin native window inside Avalonia by reparenting its HWND.
    internal sealed class LedWinHost : NativeControlHost
    {
        private const int GWL_STYLE = -16;
        private const long WS_CHILD = 0x40000000, WS_POPUP = 0x80000000;
        private const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;

        [DllImport("user32")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32")] private static extern bool ShowWindow(IntPtr h, int cmd);

        public IntPtr Hwnd;

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (Hwnd != IntPtr.Zero)
            {
                try
                {
                    long style = (long)GetWindowLongPtr(Hwnd, GWL_STYLE);
                    style = (style | WS_CHILD) & ~WS_POPUP & ~WS_CAPTION & ~WS_THICKFRAME;
                    SetWindowLongPtr(Hwnd, GWL_STYLE, (IntPtr)style);
                    SetParent(Hwnd, parent.Handle);
                    ShowWindow(Hwnd, 5 /*SW_SHOW*/);
                    return new PlatformHandle(Hwnd, "HWND");
                }
                catch (Exception e) { Console.WriteLine("[ui] embed failed: " + e.Message); }
            }
            return base.CreateNativeControlCore(parent);
        }

        // Never destroy LedWin's window here - the game thread owns it.
        protected override void DestroyNativeControlCore(IPlatformHandle control) { }
    }

    internal sealed class GameWindow : Window
    {
        static readonly Color Bg = Color.FromRgb(0x0B, 0x0B, 0x12);
        static readonly Color Panel = Color.FromRgb(0x16, 0x16, 0x22);
        static readonly Color PanelHi = Color.FromRgb(0x1E, 0x1E, 0x2E);
        static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x20, 0xE0, 0xC0));
        static readonly IBrush Sky = new SolidColorBrush(Color.FromRgb(0x50, 0x80, 0xFF));
        static readonly IBrush Grey = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB0));
        static readonly IBrush White = Brushes.White;

        private readonly Slider _density, _figDen, _rotSpd, _size, _hue, _volume;
        private readonly TextBlock _densityV, _figDenV, _rotSpdV, _sizeV;
        private readonly CheckBox _sound, _demo;
        // lighting tab
        private readonly ComboBox _lightMode;
        private readonly Slider _ambBright, _exposure, _selfIllum, _spotInt, _spotRad, _shadowStr, _normStr, _normInt, _lightAng;
        private readonly TextBlock _ambBrightV, _exposureV, _selfIllumV, _spotIntV, _spotRadV, _shadowStrV, _normStrV, _normIntV, _lightAngV;
        private readonly CheckBox _shadows, _orbit, _showTitle, _gpu;
        private readonly MenuItem[] _modeItems = new MenuItem[3];   // Lighting menu radio items (Flat/Normals/Spotlight)
        private MenuItem? _miGpu;                                   // Lighting menu GPU checkbox
        private MenuItem? _miText;                                  // Lighting menu text checkbox
        private MenuItem? _miRec;                                   // Game menu "Record Depth Video" (Ctrl+R)
        // hardware tab
        private readonly Slider _rpm, _gamma, _dith;
        private readonly TextBlock _rpmV, _gammaV, _dithV;
        private readonly CheckBox _motor, _bilin, _ordered, _border;
        private readonly Button _sysBtn;
        private readonly Border _swatch;
        private bool _dragCam;
        private Point _lastCam;
        private readonly TextBlock _vox, _vps, _score, _level, _lives, _hint, _banner, _scores, _recStatus;
        private bool _wasRecording;
        private int _savedTicks;   // ticks remaining to show the "saved" message
        private readonly Border _centerBox;
        private bool _embedded;
        private readonly DispatcherTimer _timer;

        public GameWindow()
        {
            Title = "HELLO VOXEL";
            Width = 1180; Height = 780;
            MinWidth = 940; MinHeight = 640;
            Background = new SolidColorBrush(Bg);
            RequestedThemeVariant = ThemeVariant.Dark;

            _density = new Slider { Minimum = 0.5, Maximum = 8, Value = Bridge.Density, Width = 150 };
            _figDen = new Slider { Minimum = 0.4, Maximum = 4, Value = Bridge.FigureDensity, Width = 150 };
            _rotSpd = new Slider { Minimum = 0, Maximum = 2, Value = Bridge.RotSpeed, Width = 150 };
            _size = new Slider { Minimum = 0.5, Maximum = 1.4, Value = Bridge.TextSize, Width = 150 };
            _hue = new Slider { Minimum = 0, Maximum = 360, Value = 168, Width = 150 };
            _swatch = new Border { Width = 26, Height = 16, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(ColorFromInt(Bridge.Color)) };
            _volume = new Slider { Minimum = 0, Maximum = 1, Value = Bridge.Volume, Width = 150 };
            _densityV = Val(); _figDenV = Val(); _rotSpdV = Val(); _sizeV = Val();

            _sound = new CheckBox { Content = "Sound", IsChecked = Bridge.SoundEnabled, Foreground = White };
            _demo = new CheckBox { Content = "Demo mode (attract)", IsChecked = Bridge.DemoMode, Foreground = White };

            // lighting tab controls
            _lightMode = new ComboBox { Width = 200, ItemsSource = new[] { "Flat", "Normals", "Spotlight" }, SelectedIndex = Math.Clamp(Bridge.LightMode, 0, 2) };
            _ambBright = new Slider { Minimum = 0, Maximum = 1.5, Value = Bridge.AmbientBright, Width = 150 };
            _exposure = new Slider { Minimum = 0, Maximum = 2, Value = Bridge.Exposure, Width = 150 };
            _selfIllum = new Slider { Minimum = 0, Maximum = 1, Value = Bridge.SelfIllum, Width = 150 };
            _spotInt = new Slider { Minimum = 0, Maximum = 10, Value = Bridge.SpotIntensity, Width = 150 };
            _spotRad = new Slider { Minimum = 0.5, Maximum = 8, Value = Bridge.SpotRadius, Width = 150 };
            _shadowStr = new Slider { Minimum = 0.005, Maximum = 0.2, Value = Bridge.ShadowBias, Width = 150 };
            _normStr = new Slider { Minimum = 0, Maximum = 1, Value = Bridge.NormalStrength, Width = 150 };
            _normInt = new Slider { Minimum = 0, Maximum = 2, Value = Bridge.NormalIntensity, Width = 150 };
            _lightAng = new Slider { Minimum = 0, Maximum = 360, Value = Bridge.LightAngle, Width = 150 };
            _ambBrightV = Val(); _exposureV = Val(); _selfIllumV = Val(); _spotIntV = Val();
            _spotRadV = Val(); _shadowStrV = Val(); _normStrV = Val(); _normIntV = Val(); _lightAngV = Val();
            _shadows = new CheckBox { Content = "Cast shadows", IsChecked = Bridge.Shadows, Foreground = White };
            _orbit = new CheckBox { Content = "Orbit main light", IsChecked = Bridge.OrbitLights, Foreground = White };
            _gpu = new CheckBox { Content = "GPU acceleration (ComputeSharp/DX12)", IsChecked = Bridge.UseGpu, Foreground = White };
            _showTitle = new CheckBox { Content = "Show HELLO VOXEL text", IsChecked = Bridge.ShowTitle, Foreground = White };

            // hardware tab controls
            _rpm = new Slider { Minimum = 100, Maximum = 900, Value = Bridge.Rpm, Width = 150 };
            _gamma = new Slider { Minimum = 0.25, Maximum = 4.25, Value = Bridge.Gamma, Width = 150 };
            _dith = new Slider { Minimum = 0, Maximum = 255, Value = Bridge.DithThresh, Width = 150 };
            _rpmV = Val(); _gammaV = Val(); _dithV = Val();
            _motor = new CheckBox { Content = "Motor running", IsChecked = Bridge.MotorOn, Foreground = White };
            _bilin = new CheckBox { Content = "Bilinear (smooth)", IsChecked = Bridge.DrawBilin == 1, Foreground = White };
            _ordered = new CheckBox { Content = "Ordered dither", IsChecked = Bridge.DithMode == 1, Foreground = White };
            _border = new CheckBox { Content = "Draw border", IsChecked = Bridge.DrawBorder, Foreground = White };
            _sysBtn = Btn(Bridge.System == 0 ? "System: VX2" : "System: VX2-XL");

            _vox = Big(Accent); _vps = Mono(Grey);
            _score = Mono(White); _level = Mono(White); _lives = Mono(White);
            _hint = new TextBlock { Foreground = Grey, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            _banner = new TextBlock { Foreground = Accent, FontSize = 20, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2) };
            _recStatus = new TextBlock { FontSize = 15, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            _scores = new TextBlock { Foreground = Grey, FontFamily = new FontFamily("Consolas,monospace"), FontSize = 12 };

            _centerBox = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x05, 0x05, 0x09)),
                BorderBrush = new SolidColorBrush(PanelHi),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(10, 0),
                Child = ConnectingPanel()
            };

            WireEvents();
            Content = BuildLayout();
            UpdateSliderLabels();

            AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
            Focusable = true;
            Opened += (_, _) => Focus();
            Closing += (_, _) => Bridge.Quit = true;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }

        // ---- per-tick UI refresh + late embedding ---------------------
        private void Tick()
        {
            if (Bridge.Quit) { Close(); return; }

            if (!_embedded && Bridge.LedWinHwnd != 0)
            {
                _embedded = true;
                try { _centerBox.Child = new LedWinHost { Hwnd = new IntPtr(Bridge.LedWinHwnd) }; }
                catch (Exception ex)
                {
                    Console.WriteLine("[ui] embed failed, LedWin stays as its own window: " + ex.Message);
                    _centerBox.Child = new TextBlock
                    {
                        Text = "LedWin emulator is running in its own window\n(embedding unavailable on this system).",
                        Foreground = Grey, TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }

            UpdateSliderLabels();
            _vox.Text = Bridge.Voxels.ToString("N0");
            _vps.Text = $"{Bridge.Vps} VPS · {(Bridge.GpuActive ? "GPU" : "CPU")}{(Bridge.Recording ? "  ● REC" : "")}";

            // on-screen recording status: red "recording" banner, then a green
            // "saved" confirmation for a few seconds once the file is written.
            if (Bridge.Recording)
            {
                _recStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x40, 0x40));
                _recStatus.Text = "● RECORDING depth video  →  " + System.IO.Path.GetFileName(Bridge.RecordPath);
                _savedTicks = 0;
            }
            else if (_wasRecording)                       // just stopped -> file is written
            {
                _savedTicks = 40;                          // ~6 s at 150 ms/tick
            }
            if (!Bridge.Recording && _savedTicks > 0)
            {
                _savedTicks--;
                _recStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0xE0, 0x60));
                _recStatus.Text = "✔ Depth video saved:  " + Bridge.RecordPath;
            }
            else if (!Bridge.Recording && _savedTicks == 0)
            {
                _recStatus.Text = "";
            }
            _wasRecording = Bridge.Recording;

            // menu entry mirrors what's actually live (ticked + verb flips while recording)
            if (_miRec != null)
            {
                _miRec.IsChecked = Bridge.Recording;
                _miRec.Header = Bridge.Recording ? "Stop Recording Depth Video" : "Record Depth Video";
            }

            _banner.Text = Bridge.BannerText;
            _score.Text = "SCORE  " + Bridge.Score.ToString("N0");
            _level.Text = "LEVEL  " + Bridge.Level;
            _lives.Text = "LIVES  " + new string('#', Math.Max(0, Bridge.Lives));
            _hint.Text = Bridge.HintText;

            // reflect / lock the system selector to detected hardware
            _sysBtn.Content = "System: " + (Bridge.System == 0 ? "VX2" : "VX2-XL") + (Bridge.HardwareLive ? "  (detected)" : "");
            _sysBtn.IsEnabled = !Bridge.HardwareLive;

            // rpm ceiling follows the model the DLL actually reported
            if (Math.Abs(_rpm.Maximum - Bridge.MaxRpm) > 0.5)
            {
                _rpm.Maximum = Bridge.MaxRpm;
                if (_rpm.Value > _rpm.Maximum) _rpm.Value = _rpm.Maximum;
            }

            // reflect live lighting state in the Lighting menu (radio + checks)
            for (int i = 0; i < _modeItems.Length; i++)
                if (_modeItems[i] != null) _modeItems[i].IsChecked = Bridge.LightMode == i;
            if (_miText != null) _miText.IsChecked = Bridge.ShowTitle;
            if (_miGpu != null) _miGpu.IsChecked = Bridge.UseGpu;

            int[] hs = Bridge.HighScores;
            if (hs.Length == 0) _scores.Text = "(none yet)";
            else
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < hs.Length; i++) sb.AppendLine($"{i + 1,2}. {hs[i],8:N0}");
                _scores.Text = sb.ToString().TrimEnd();
            }
        }

        // ---- input -> Bridge ------------------------------------------
        private void OnKey(object? s, KeyEventArgs e)
        {
            bool g = true;
            switch (e.Key)
            {
                case Key.Left: case Key.A: Bridge.Want = (int)Dir6.AngCCW; break;
                case Key.Right: case Key.D: Bridge.Want = (int)Dir6.AngCW; break;
                case Key.Up: Bridge.Want = (int)Dir6.VertUp; break;
                case Key.Down: Bridge.Want = (int)Dir6.VertDown; break;
                case Key.W: Bridge.Want = (int)Dir6.RadOut; break;
                case Key.S: Bridge.Want = (int)Dir6.RadIn; break;
                case Key.P: Bridge.PauseToggleRequested = true; break;
                case Key.R:
                    if ((e.KeyModifiers & KeyModifiers.Control) != 0) _ = ToggleRecording();   // Ctrl+R record
                    else Bridge.NewGameRequested = true;
                    break;
                case Key.Escape: Close(); break;
                default: g = false; break;
            }
            if (g) e.Handled = true;
        }

        // Ctrl+R: start/stop depth-map recording. Writes to
        // C:\VLED\Media\DepthVideo if it exists, otherwise asks for a folder.
        // Runs on the UI thread (dialogs must); the game thread does the actual
        // start/stop + per-frame encode via the Bridge flags.
        private async System.Threading.Tasks.Task ToggleRecording()
        {
            if (Bridge.Recording) { Bridge.RecordStopRequested = true; return; }

            string dir = @"C:\VLED\Media\DepthVideo";
            if (!System.IO.Directory.Exists(dir))
            {
                var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Choose a folder for the depth-map video",
                    AllowMultiple = false
                });
                if (picked == null || picked.Count == 0) return;   // cancelled
                dir = picked[0].TryGetLocalPath() ?? "";
                if (string.IsNullOrEmpty(dir)) return;
            }
            Bridge.RecordDir = dir;
            Bridge.RecordStartRequested = true;
        }

        private void WireEvents()
        {
            _density.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.Density = (float)_density.Value; UpdateSliderLabels(); } };
            _figDen.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.FigureDensity = (float)_figDen.Value; UpdateSliderLabels(); } };
            _rotSpd.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.RotSpeed = (float)_rotSpd.Value; UpdateSliderLabels(); } };
            _size.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.TextSize = (float)_size.Value; UpdateSliderLabels(); } };
            _hue.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                int col = HsvToRgb(_hue.Value, 0.85, 1.0);
                Bridge.Color = col;
                _swatch.Background = new SolidColorBrush(ColorFromInt(col));
            };
            _volume.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) Bridge.Volume = (float)_volume.Value; };
            _sound.IsCheckedChanged += (_, _) => Bridge.SoundEnabled = _sound.IsChecked ?? true;
            _demo.IsCheckedChanged += (_, _) => Bridge.DemoMode = _demo.IsChecked ?? false;

            // lighting tab
            _lightMode.SelectionChanged += (_, _) => { Bridge.LightMode = _lightMode.SelectedIndex; Focus(); };
            _ambBright.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.AmbientBright = (float)_ambBright.Value; UpdateSliderLabels(); } };
            _exposure.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.Exposure = (float)_exposure.Value; UpdateSliderLabels(); } };
            _selfIllum.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.SelfIllum = (float)_selfIllum.Value; UpdateSliderLabels(); } };
            _spotInt.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.SpotIntensity = (float)_spotInt.Value; UpdateSliderLabels(); } };
            _spotRad.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.SpotRadius = (float)_spotRad.Value; UpdateSliderLabels(); } };
            _shadowStr.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.ShadowBias = (float)_shadowStr.Value; UpdateSliderLabels(); } };
            _normStr.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.NormalStrength = (float)_normStr.Value; UpdateSliderLabels(); } };
            _normInt.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.NormalIntensity = (float)_normInt.Value; UpdateSliderLabels(); } };
            _lightAng.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.LightAngle = (float)_lightAng.Value; UpdateSliderLabels(); } };
            _shadows.IsCheckedChanged += (_, _) => Bridge.Shadows = _shadows.IsChecked ?? true;
            _orbit.IsCheckedChanged += (_, _) => Bridge.OrbitLights = _orbit.IsChecked ?? true;
            _showTitle.IsCheckedChanged += (_, _) => Bridge.ShowTitle = _showTitle.IsChecked ?? false;
            _gpu.IsCheckedChanged += (_, _) => Bridge.UseGpu = _gpu.IsChecked ?? true;

            // hardware tab
            _rpm.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.Rpm = (int)Math.Round(_rpm.Value); UpdateSliderLabels(); } };
            _gamma.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.Gamma = (float)_gamma.Value; UpdateSliderLabels(); } };
            _dith.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { Bridge.DithThresh = (int)Math.Round(_dith.Value); UpdateSliderLabels(); } };
            _motor.IsCheckedChanged += (_, _) => Bridge.MotorOn = _motor.IsChecked ?? true;
            _bilin.IsCheckedChanged += (_, _) => Bridge.DrawBilin = (_bilin.IsChecked ?? false) ? 1 : 0;
            _ordered.IsCheckedChanged += (_, _) => Bridge.DithMode = (_ordered.IsChecked ?? false) ? 1 : 0;
            _border.IsCheckedChanged += (_, _) => Bridge.DrawBorder = _border.IsChecked ?? false;
            _sysBtn.Click += (_, _) =>
            {
                // request the other model; the game thread asks the DLL to switch
                // and publishes back the model + rpm ceiling it actually got, which
                // the status tick above reflects into the button and slider.
                Bridge.System = 1 - Bridge.System;
                Bridge.SystemDirty = true;
                Focus();
            };

            // mouse camera control over the emulator area (drag = rotate, wheel = zoom)
            _centerBox.PointerPressed += (_, e) => { _dragCam = true; _lastCam = e.GetPosition(_centerBox); };
            _centerBox.PointerReleased += (_, _) => _dragCam = false;
            _centerBox.PointerMoved += (_, e) =>
            {
                if (!_dragCam) return;
                Point p = e.GetPosition(_centerBox);
                Bridge.CamDH += (float)((p.X - _lastCam.X) * 0.01);
                Bridge.CamDV += (float)(-(p.Y - _lastCam.Y) * 0.01);
                _lastCam = p;
            };
            _centerBox.PointerWheelChanged += (_, e) => Bridge.CamDZoom += (float)e.Delta.Y;
        }

        // ---- layout ----------------------------------------------------
        private Control BuildLayout()
        {
            var dock = new DockPanel();
            var menu = BuildMenu(); DockPanel.SetDock(menu, Dock.Top); dock.Children.Add(menu);
            var header = HeaderBar(); DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
            var footer = FooterBar(); DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);

            var grid = new Grid { Margin = new Thickness(10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(290, GridUnitType.Pixel));
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(210, GridUnitType.Pixel));

            var left = LeftPanel(); Grid.SetColumn(left, 0); grid.Children.Add(left);
            // Simulator fills the column; the banner + hint dock to the bottom
            // (a StackPanel left a wasted grey gap under the sim view).
            var caption = new StackPanel { Children = { _recStatus, _banner, _hint } };
            DockPanel.SetDock(caption, Dock.Bottom);
            var center = new DockPanel { LastChildFill = true, Children = { caption, _centerBox } };
            Grid.SetColumn(center, 1); grid.Children.Add(center);
            var right = RightPanel(); Grid.SetColumn(right, 2); grid.Children.Add(right);
            _centerBox.MinHeight = 300;

            dock.Children.Add(grid);
            return dock;
        }

        private Control HeaderBar()
        {
            var title = new TextBlock { Text = "HELLO  VOXEL", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Accent, VerticalAlignment = VerticalAlignment.Center };
            var sub = new TextBlock { Text = "VLED template - LedWin emulator embedded, driving the volume", Foreground = Grey, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            return new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromRgb(0x10, 0x2A, 0x28), 0), new GradientStop(Bg, 1) }
                },
                Padding = new Thickness(16, 10),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { title, sub } }
            };
        }

        private Control FooterBar() => new Border
        {
            Background = new SolidColorBrush(Panel),
            Padding = new Thickness(16, 8),
            Child = new TextBlock { Foreground = Grey, Text = "<- / -> spin    Up / Down / W / S nudge    P pause    R reset    Ctrl+R record depth video    Esc quit" }
        };

        private Control ConnectingPanel() => new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Connecting to Voxon / LedWin...", Foreground = Sky, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "The emulator view will appear here once it starts.", Foreground = Grey, HorizontalAlignment = HorizontalAlignment.Center }
            }
        };

        private Control LeftPanel()
        {
            TabItem Tab(string h, Control body) => new()
            {
                Header = new TextBlock { Text = h, FontSize = 16, FontWeight = FontWeight.Bold, Padding = new Thickness(10, 4) },
                Foreground = White,
                Content = Card(body)
            };

            var tabs = new TabControl
            {
                Padding = new Thickness(0),
                Margin = new Thickness(0, 2, 0, 0),
                Background = new SolidColorBrush(PanelHi)
            };
            tabs.Items.Add(Tab("Game", GameBody()));
            tabs.Items.Add(Tab("💡 Lighting", LightingBody()));
            tabs.Items.Add(Tab("⚙ Hardware", HardwareBody()));
            return tabs;
        }

        private Control GameBody()
        {
            var body = new StackPanel { Spacing = 8 };

            // ---- placeholder game controls (replace per game) ----------
            body.Children.Add(Heading("Game  (placeholder)"));
            body.Children.Add(TwoBtn("New (R)", () => Bridge.NewGameRequested = true, "Pause (P)", () => Bridge.PauseToggleRequested = true));
            var rnd = Btn("Randomize"); rnd.Click += (_, _) => { Bridge.RandomizeRequested = true; Focus(); }; body.Children.Add(rnd);
            body.Children.Add(_demo);
            body.Children.Add(new TextBlock { Text = "Swap these for your own game controls in GameBody() + GameModel.cs.", Foreground = Grey, TextWrapping = TextWrapping.Wrap, FontSize = 11 });

            body.Children.Add(new Separator { Margin = new Thickness(0, 8) });
            body.Children.Add(Heading("Word  (demo)"));
            body.Children.Add(SliderRow("Size", _size, _sizeV));
            body.Children.Add(SliderRow("Spin", _rotSpd, _rotSpdV));

            // ---- universal visual controls -----------------------------
            body.Children.Add(new Separator { Margin = new Thickness(0, 8) });
            body.Children.Add(Heading("Visual  (universal)"));
            body.Children.Add(SliderRow("Density", _density, _densityV));
            body.Children.Add(SliderRow("Figures", _figDen, _figDenV));
            var colourRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            colourRow.Children.Add(new TextBlock { Text = "Colour", Foreground = White, Width = 60, VerticalAlignment = VerticalAlignment.Center });
            colourRow.Children.Add(_hue);
            colourRow.Children.Add(_swatch);
            body.Children.Add(colourRow);
            body.Children.Add(new TextBlock { Text = "Density is a DPI setting (voxels per unit volume) - universal to any game.", Foreground = Grey, TextWrapping = TextWrapping.Wrap, FontSize = 11 });

            body.Children.Add(new Separator { Margin = new Thickness(0, 8) });
            body.Children.Add(Heading("View  (emulator camera)"));
            body.Children.Add(TwoBtn("< Rotate", () => Bridge.CamDH -= 0.25f, "Rotate >", () => Bridge.CamDH += 0.25f));
            body.Children.Add(TwoBtn("^ Tilt", () => Bridge.CamDV -= 0.25f, "Tilt v", () => Bridge.CamDV += 0.25f));
            body.Children.Add(TwoBtn("Zoom +", () => Bridge.CamDZoom += 1f, "Zoom -", () => Bridge.CamDZoom -= 1f));
            var reset = Btn("Reset View"); reset.Click += (_, _) => { Bridge.CamReset = true; Focus(); };
            body.Children.Add(reset);

            body.Children.Add(new Separator { Margin = new Thickness(0, 8) });
            body.Children.Add(Heading("Audio  (universal)"));
            body.Children.Add(_sound);
            body.Children.Add(SliderRow("Volume", _volume, null));

            body.Children.Add(new Separator { Margin = new Thickness(0, 8) });
            var save = Btn("Save Settings + Scores");
            save.Click += (_, _) => { Bridge.SaveRequested = true; Focus(); };
            body.Children.Add(save);
            body.Children.Add(new TextBlock { Text = "Written to hellovoxel.json next to the app.", Foreground = Grey, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            return body;
        }

        private Control LightingBody()
        {
            var body = new StackPanel { Spacing = 8 };

            body.Children.Add(Heading("Mode"));
            body.Children.Add(_lightMode);
            body.Children.Add(new TextBlock { Text = "Flat: colours straight through. Normals: N·L, no shadow. Spotlight: GPU coloured point lights + orthographic shadows + interior-shell culling (VLEDStudio model).", Foreground = Grey, TextWrapping = TextWrapping.Wrap, FontSize = 11 });

            body.Children.Add(new Separator { Margin = new Thickness(0, 8) });
            body.Children.Add(Heading("Performance"));
            body.Children.Add(_gpu);
            body.Children.Add(new TextBlock { Text = "GPU (ComputeSharp/DX12) uploads the model once and does transform + lighting on-GPU each frame. Unticked (or no capable GPU) falls back to a flat CPU transform. Status shows under VPS.", Foreground = Grey, TextWrapping = TextWrapping.Wrap, FontSize = 11 });

            body.Children.Add(new Separator { Margin = new Thickness(0, 8) });
            body.Children.Add(Heading("Global"));
            body.Children.Add(SliderRow("Ambient", _ambBright, _ambBrightV));
            body.Children.Add(SliderRow("Exposure", _exposure, _exposureV));

            body.Children.Add(new Separator { Margin = new Thickness(0, 8) });
            body.Children.Add(Heading("Spotlight"));
            body.Children.Add(SliderRow("Intensity", _spotInt, _spotIntV));
            body.Children.Add(SliderRow("Radius", _spotRad, _spotRadV));
            body.Children.Add(SliderRow("Shadow bias", _shadowStr, _shadowStrV));
            body.Children.Add(new TextBlock { Text = "Raise the bias if shadows look noisy/speckly; lower it if shadows detach from contact points.", Foreground = Grey, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
            body.Children.Add(_orbit);

            body.Children.Add(new Separator { Margin = new Thickness(0, 8) });
            body.Children.Add(Heading("Normals"));
            body.Children.Add(SliderRow("Strength", _normStr, _normStrV));
            body.Children.Add(SliderRow("Intensity", _normInt, _normIntV));
            body.Children.Add(SliderRow("Angle", _lightAng, _lightAngV));

            body.Children.Add(new Separator { Margin = new Thickness(0, 8) });
            body.Children.Add(Heading("Scene"));
            body.Children.Add(_showTitle);
            body.Children.Add(new TextBlock { Text = "Off: cube / sphere / cone / cylinder test bench. On: classic HELLO VOXEL text.", Foreground = Grey, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
            return body;
        }

        private Control HardwareBody()
        {
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(Heading("System"));
            body.Children.Add(_sysBtn);
            body.Children.Add(new TextBlock { Text = "VX2  XY +/-2, Z +/-2  ·  VX2-XL  XY +/-4, Z +/-2.\nVX2 max 900 RPM · VX2-XL max 600 RPM.\nContent auto-fits the volume bounds.", Foreground = Grey, TextWrapping = TextWrapping.Wrap, FontSize = 11 });

            body.Children.Add(new Separator { Margin = new Thickness(0, 8) });
            body.Children.Add(Heading("Motor"));
            body.Children.Add(_motor);
            body.Children.Add(SliderRow("Speed", _rpm, _rpmV));

            body.Children.Add(new Separator { Margin = new Thickness(0, 8) });
            body.Children.Add(Heading("Image (VLED)"));
            body.Children.Add(_bilin);
            body.Children.Add(_border);
            body.Children.Add(_ordered);
            body.Children.Add(SliderRow("Dither", _dith, _dithV));
            body.Children.Add(SliderRow("Gamma", _gamma, _gammaV));
            body.Children.Add(new TextBlock { Text = "Voxel density is on the Game tab.", Foreground = Grey, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
            return body;
        }

        private Control RightPanel()
        {
            var body = new StackPanel { Spacing = 6 };
            body.Children.Add(Heading("Volume"));
            body.Children.Add(SmallLabel("VOXELS THIS FRAME"));
            body.Children.Add(_vox);
            body.Children.Add(_vps);
            body.Children.Add(new Separator { Margin = new Thickness(0, 6) });
            body.Children.Add(Heading("Game  (placeholder)"));
            body.Children.Add(_score);
            body.Children.Add(_level);
            body.Children.Add(_lives);
            body.Children.Add(new Separator { Margin = new Thickness(0, 6) });
            body.Children.Add(Heading("High Scores"));
            body.Children.Add(_scores);
            return Card(body);
        }

        private Menu BuildMenu()
        {
            MenuItem Item(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => a(); return mi; }
            MenuItem Sub(string h, params object[] kids) { var mi = new MenuItem { Header = h }; foreach (var k in kids) mi.Items.Add(k); return mi; }

            // Record depth video: menu entry + the Ctrl+R shortcut shown beside it.
            // InputGesture only DISPLAYS the accelerator - the key itself is handled
            // in OnKey, so don't also add a KeyBinding here or it toggles twice.
            _miRec = new MenuItem
            {
                Header = "Record Depth Video",
                ToggleType = MenuItemToggleType.CheckBox,
                InputGesture = new KeyGesture(Key.R, KeyModifiers.Control),
            };
            _miRec.Click += (_, _) => _ = ToggleRecording();

            var game = Sub("_Game",
                Item("New / Reset", () => Bridge.NewGameRequested = true),
                Item("Pause / Resume", () => Bridge.PauseToggleRequested = true),
                Item("Randomize", () => Bridge.RandomizeRequested = true),
                new Separator(),
                _miRec,
                new Separator(),
                Item("Save Settings + Scores", () => Bridge.SaveRequested = true),
                new Separator(),
                Item("Quit", Close));
            var view = Sub("_View",
                Item("Reset Camera", () => Bridge.CamReset = true),
                Item("Toggle Demo (attract)", () => _demo.IsChecked = !(_demo.IsChecked ?? false)));
            // Modes are a mutually-exclusive SELECTION (radio); Shadows / text
            // are on/off TOGGLES (checkmarks).  State is synced from the Bridge
            // in Tick() so the menu always reflects what's actually live.
            MenuItem ModeItem(string h, int idx)
            {
                var mi = new MenuItem { Header = h, ToggleType = MenuItemToggleType.Radio, GroupName = "lightmode" };
                mi.Click += (_, _) => _lightMode.SelectedIndex = idx;
                _modeItems[idx] = mi; return mi;
            }
            _miText = new MenuItem { Header = "HELLO VOXEL text", ToggleType = MenuItemToggleType.CheckBox };
            _miText.Click += (_, _) => _showTitle.IsChecked = !(_showTitle.IsChecked ?? false);
            _miGpu = new MenuItem { Header = "GPU acceleration", ToggleType = MenuItemToggleType.CheckBox };
            _miGpu.Click += (_, _) => _gpu.IsChecked = !(_gpu.IsChecked ?? true);
            var lighting = Sub("_Lighting",
                ModeItem("Flat", 0),
                ModeItem("Normals", 1),
                ModeItem("Spotlight (shadows)", 2),
                new Separator(), _miText, _miGpu);
            var audio = Sub("_Audio",
                Item("Sound On / Off", () => _sound.IsChecked = !(_sound.IsChecked ?? true)));
            var help = Sub("_Help", Item("About", ShowAbout));

            var m = new Menu();
            m.Items.Add(game); m.Items.Add(view); m.Items.Add(lighting); m.Items.Add(audio); m.Items.Add(help);
            return m;
        }

        private void ShowAbout()
        {
            new Window
            {
                Title = "About",
                Width = 400, Height = 240, CanResize = false,
                Background = new SolidColorBrush(Bg), RequestedThemeVariant = ThemeVariant.Dark,
                Content = new StackPanel
                {
                    Margin = new Thickness(18), Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "HELLO VOXEL", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Accent },
                        new TextBlock { Text = "A minimal Voxon VLED app template. Renders the words HELLO VOXEL in the volume as a single batch of voxels.", TextWrapping = TextWrapping.Wrap, Foreground = White },
                        new TextBlock { Text = "Boilerplate (UI, embedded emulator, hardware controls, audio, save, input) is reusable. Write your game in GameModel.cs.", TextWrapping = TextWrapping.Wrap, Foreground = Grey }
                    }
                }
            }.ShowDialog(this);
        }

        // ---- styled builders ------------------------------------------
        private Border Card(Control child) => new()
        {
            Background = new SolidColorBrush(Panel),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = new ScrollViewer { Content = child, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };

        private TextBlock Val() => new() { Foreground = Accent, Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas,monospace") };
        private TextBlock Heading(string t) => new() { Text = t, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Sky, Margin = new Thickness(0, 4, 0, 2) };
        private TextBlock SmallLabel(string t) => new() { Text = t, Foreground = Grey, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
        private TextBlock Big(IBrush c) => new() { Foreground = c, FontSize = 26, FontWeight = FontWeight.Bold, FontFamily = new FontFamily("Consolas,monospace") };
        private TextBlock Mono(IBrush c) => new() { Foreground = c, FontFamily = new FontFamily("Consolas,monospace") };

        private Button Btn(string t) => new()
        {
            Content = t, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0), Background = new SolidColorBrush(PanelHi), Foreground = White
        };

        private Control TwoBtn(string a, Action aa, string b, Action bb)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var ba = Btn(a); ba.Margin = new Thickness(0, 0, 3, 0); ba.Click += (_, _) => { aa(); Focus(); }; Grid.SetColumn(ba, 0); g.Children.Add(ba);
            var bc = Btn(b); bc.Margin = new Thickness(3, 0, 0, 0); bc.Click += (_, _) => { bb(); Focus(); }; Grid.SetColumn(bc, 1); g.Children.Add(bc);
            return g;
        }

        private Control SliderRow(string label, Slider s, TextBlock? val)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = label, Foreground = White, Width = 60, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(s);
            if (val != null) row.Children.Add(val);
            return row;
        }

        private static Color ColorFromInt(int c) => Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);

        private static int HsvToRgb(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60.0) % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            int R = (int)((r + m) * 255), G = (int)((g + m) * 255), B = (int)((b + m) * 255);
            return (R << 16) | (G << 8) | B;
        }

        private void UpdateSliderLabels()
        {
            _densityV.Text = _density.Value.ToString("0.0");
            _figDenV.Text = _figDen.Value.ToString("0.0");
            _rotSpdV.Text = _rotSpd.Value.ToString("0.0");
            _sizeV.Text = _size.Value.ToString("0.00");
            _rpmV.Text = ((int)Math.Round(_rpm.Value)).ToString();
            _gammaV.Text = _gamma.Value.ToString("0.0");
            _dithV.Text = ((int)Math.Round(_dith.Value)).ToString();
            _ambBrightV.Text = _ambBright.Value.ToString("0.0");
            _exposureV.Text = _exposure.Value.ToString("0.0");
            _selfIllumV.Text = _selfIllum.Value.ToString("0.0");
            _spotIntV.Text = _spotInt.Value.ToString("0.0");
            _spotRadV.Text = _spotRad.Value.ToString("0.0");
            _shadowStrV.Text = _shadowStr.Value.ToString("0.000");
            _normStrV.Text = _normStr.Value.ToString("0.0");
            _normIntV.Text = _normInt.Value.ToString("0.0");
            _lightAngV.Text = ((int)Math.Round(_lightAng.Value)).ToString();
        }
    }
}
