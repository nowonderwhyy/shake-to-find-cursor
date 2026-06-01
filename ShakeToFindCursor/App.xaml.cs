using System;
using System.IO;
using System.Windows;
using System.Threading.Tasks;
using WinForms = System.Windows.Forms;

namespace ShakeToFindCursor;

public partial class App : System.Windows.Application
{
    private WinForms.NotifyIcon? _notifyIcon;
    private bool _isEnabled = true;
    private SettingsWindow? _settingsWindow;

    // Throttle the (expensive) foreground/fullscreen check so it doesn't run on every
    // mouse-move inside the low-level hook. Touched only on the hook/UI thread.
    private long _lastFsCheckTicks;
    private bool _cachedShouldDisable;
    private const long FsCheckIntervalMs = 300;

    private string _crashDir = "";

    public static AppSettings CurrentSettings { get; private set; } = new AppSettings();
    public static ShakeDetector? Detector { get; private set; }
    public static CursorAnimator? Animator { get; private set; }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        _crashDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShakeToFindCursor");
        Directory.CreateDirectory(_crashDir);

        // Route WinForms message-loop exceptions to our handler instead of letting the
        // ThreadExceptionDialog appear — it can itself crash loading stock icons.
        WinForms.Application.SetUnhandledExceptionMode(WinForms.UnhandledExceptionMode.CatchException);
        WinForms.Application.ThreadException += (s, args) => LogAndRestore("ThreadException", args.Exception);

        // Always restore the system cursors on any crash path so the user is never left
        // with a permanently enlarged cursor.
        AppDomain.CurrentDomain.UnhandledException += (s, args) => LogAndRestore("AppDomain", args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogAndRestore("UnobservedTask", args.Exception);
            args.SetObserved();
        };
        DispatcherUnhandledException += (s, args) =>
        {
            LogAndRestore("Dispatcher", args.Exception);
            args.Handled = true; // background utility: log + restore, then stay alive
        };

        CurrentSettings = AppSettings.Load();

        Task.Run(() => CursorHelper.InitCaches(CurrentSettings.MagnificationFactor));

        Detector = new ShakeDetector(CurrentSettings);
        Animator = new CursorAnimator(Detector, CurrentSettings);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Visible = true,
            Text = "Shake to Find Cursor"
        };
        
        var contextMenu = new WinForms.ContextMenuStrip();
        var toggleItem = new WinForms.ToolStripMenuItem("Disable");
        toggleItem.Click += (s, ev) => 
        {
            _isEnabled = !_isEnabled;
            toggleItem.Text = _isEnabled ? "Disable" : "Enable";
            _notifyIcon.Text = _isEnabled ? "Shake to Find Cursor" : "Find Cursor (Disabled)";
        };
        
        var settingsItem = new WinForms.ToolStripMenuItem("Settings...");
        settingsItem.Click += (s, ev) => 
        {
            if (_settingsWindow == null || !_settingsWindow.IsLoaded)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                _settingsWindow.Show();
                _settingsWindow.Activate();
            }
            else
            {
                _settingsWindow.Activate();
            }
        };

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (s, ev) => Shutdown();
        
        contextMenu.Items.Add(toggleItem);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(exitItem);
        _notifyIcon.ContextMenuStrip = contextMenu;

        if (!MouseHook.Start())
        {
            WriteCrashLog("MouseHook", "SetWindowsHookEx failed — shake detection is inactive.");
            _notifyIcon.Text = "Shake to Find Cursor (hook failed)";
            _notifyIcon.ShowBalloonTip(5000, "Shake to Find Cursor",
                "Could not install the mouse hook; shake detection is inactive.",
                WinForms.ToolTipIcon.Warning);
        }
        MouseHook.MouseMoved += OnMouseMoved;

        // Warm up WPF and the settings window now, while idle, so the first real "open
        // Settings" doesn't trigger a one-time JIT/allocation burst whose GC pause would
        // briefly suspend the mouse-hook thread and stutter system-wide input.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(PrewarmSettingsWindow));
    }

    private void PrewarmSettingsWindow()
    {
        try
        {
            var warm = new SettingsWindow
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                Opacity = 0
            };
            warm.Show();
            // Let one render pass complete (warms the render/JIT path), then discard it.
            warm.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => { try { warm.Close(); } catch { } }));
        }
        catch { }
    }

    private void OnMouseMoved(object? sender, MouseHook.NativePoint point)
    {
        if (!_isEnabled || Detector == null) return;

        // This runs inside the low-level mouse hook for every move, so the expensive
        // foreground/fullscreen check (which opens a process handle) is throttled to a
        // few times a second and its result reused in between.
        long now = Environment.TickCount64;
        if (now - _lastFsCheckTicks >= FsCheckIntervalMs)
        {
            _lastFsCheckTicks = now;
            _cachedShouldDisable = FullscreenDetector.ShouldDisable(
                CurrentSettings.ExcludedProcesses,
                CurrentSettings.DisableInFullscreen);
        }

        // Don't grow the cursor while dragging/selecting (a button is held), or while an
        // excluded/fullscreen app is in front.
        Detector.SetSuppressed(_cachedShouldDisable || MouseHook.IsButtonDown);

        Detector.AddSample(point, now);
        if (Detector.Energy > 0 && CursorHelper.IsCached)
            Animator?.Wake();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        MouseHook.Stop();
        Animator?.Dispose();
        
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
    
    public static void ReloadSettings()
    {
        Detector?.UpdateSettings(CurrentSettings);
        Animator?.UpdateSettings(CurrentSettings);
    }

    private void WriteCrashLog(string source, object? info)
    {
        try
        {
            string path = Path.Combine(_crashDir, "crash.log");
            // Roll the log if it grows past ~512 KB so it can't accumulate forever.
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                File.WriteAllText(path, "");
            File.AppendAllText(path, $"{DateTime.Now} [{source}]\n{info}\n\n");
        }
        catch { }
    }

    private void LogAndRestore(string source, object? info)
    {
        WriteCrashLog(source, info);
        try { CursorHelper.RestoreThemeCursors(); } catch { }
    }
}
