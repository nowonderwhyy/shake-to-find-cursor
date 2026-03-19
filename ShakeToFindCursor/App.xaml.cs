using System;
using System.IO;
using System.Windows;
using System.Threading.Tasks;
using WinForms = System.Windows.Forms;

namespace ShakeToFindCursor;

public partial class App : System.Windows.Application
{
    private ShakeDetector _detector = new ShakeDetector();
    private WinForms.NotifyIcon? _notifyIcon;
    private bool _isEnabled = true;
    private SettingsWindow? _settingsWindow;
    
    public static AppSettings CurrentSettings { get; private set; } = new AppSettings();
    public static CursorAnimator? Animator { get; private set; }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // Capture the REAL exception before .NET's dialog tries to load corrupted system icons
        string crashDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShakeToFindCursor");
        Directory.CreateDirectory(crashDir);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            File.AppendAllText(Path.Combine(crashDir, "crash.log"), $"{DateTime.Now}\n{args.ExceptionObject}\n\n");
        };
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            File.AppendAllText(Path.Combine(crashDir, "crash_task.log"), $"{DateTime.Now}\n{args.Exception}\n\n");
            args.SetObserved();
        };

        CurrentSettings = AppSettings.Load();
        
        Task.Run(() => {
            CursorHelper.InitCaches(CurrentSettings.MagnificationFactor);
        });
        
        Animator = new CursorAnimator(CurrentSettings.MagnificationFactor, CurrentSettings.HoldDurationMs);

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

        MouseHook.Start();
        MouseHook.MouseMoved += OnMouseMoved;
        _detector.ShakeDetected += OnShakeDetected;
    }

    private void OnMouseMoved(object? sender, MouseHook.NativePoint point)
    {
        if (!_isEnabled) return;
        _detector.AddPoint(point);
    }

    private void OnShakeDetected(object? sender, ShakeEventArgs e)
    {
        if (!CursorHelper.IsCached || Animator == null) return;
        Animator.Excite(e.Intensity);
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
}
