using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace ShakeToFindCursor;

/// <summary>
/// Detects fullscreen applications and games to disable shake-to-find when appropriate.
/// </summary>
public static class FullscreenDetector
{
    #region P/Invoke

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    // Window styles
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_BORDER = 0x00800000;
    private const uint WS_POPUP = 0x80000000;

    // Extended window styles
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    // MonitorFromWindow flags
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    #endregion

    // Common game/media process name patterns (lowercase)
    private static readonly string[] GamePatterns = 
    {
        "game", "unity", "unreal", "steam", "epic", "origin", "uplay",
        "valorant", "fortnite", "minecraft", "roblox", "league", "dota",
        "csgo", "cs2", "apex", "overwatch", "warzone", "pubg", "gta",
        "vlc", "mpv", "mpc-", "potplayer", "kmplayer", "plex"
    };

    // D3D/DirectX module names that indicate game usage
    private static readonly string[] GraphicsModules =
    {
        "d3d9", "d3d10", "d3d11", "d3d12", "dxgi", "nvapi", "amdxc"
    };

    /// <summary>
    /// Returns true if shake-to-find should be disabled based on current foreground window.
    /// </summary>
    /// <param name="excludedProcesses">List of process names to exclude (case-insensitive)</param>
    /// <param name="disableInFullscreen">Whether to disable when fullscreen apps are detected</param>
    public static bool ShouldDisable(List<string> excludedProcesses, bool disableInFullscreen)
    {
        try
        {
            var (processName, isFullscreen, _) = GetForegroundInfo();

            if (string.IsNullOrEmpty(processName))
                return false;

            // Check exclusion list (case-insensitive)
            if (excludedProcesses?.Any(excluded => 
                    string.Equals(excluded, processName, StringComparison.OrdinalIgnoreCase)) == true)
            {
                return true;
            }

            // Check fullscreen mode if enabled
            if (disableInFullscreen && isFullscreen)
            {
                return true;
            }

            return false;
        }
        catch
        {
            // On any error, don't disable (fail-safe)
            return false;
        }
    }

    /// <summary>
    /// Returns information about the current foreground window.
    /// </summary>
    public static (string ProcessName, bool IsFullscreen, string WindowTitle) GetForegroundInfo()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return (string.Empty, false, string.Empty);

        string processName = GetProcessName(hwnd);
        string windowTitle = GetWindowTitle(hwnd);
        bool isFullscreen = IsFullscreenWindow(hwnd);

        return (processName, isFullscreen, windowTitle);
    }

    /// <summary>
    /// Checks if a process appears to be a game based on heuristics.
    /// </summary>
    public static bool IsLikelyGame(string processName)
    {
        if (string.IsNullOrEmpty(processName))
            return false;

        string lowerName = processName.ToLowerInvariant();
        
        // Check name patterns
        if (GamePatterns.Any(pattern => lowerName.Contains(pattern)))
            return true;

        // Check for loaded graphics modules (more expensive check)
        try
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length > 0)
            {
                var process = processes[0];
                try
                {
                    foreach (ProcessModule module in process.Modules)
                    {
                        string moduleName = module.ModuleName?.ToLowerInvariant() ?? "";
                        if (GraphicsModules.Any(gfx => moduleName.Contains(gfx)))
                            return true;
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Access denied - can't enumerate modules
                }
                finally
                {
                    foreach (var p in processes)
                        p.Dispose();
                }
            }
        }
        catch
        {
            // Ignore errors in game detection
        }

        return false;
    }

    private static string GetProcessName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == 0)
                return string.Empty;

            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        try
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsFullscreenWindow(IntPtr hwnd)
    {
        // Get window rect
        if (!GetWindowRect(hwnd, out RECT windowRect))
            return false;

        // Get the monitor this window is on
        IntPtr hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
            return false;

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref monitorInfo))
            return false;

        // Check window style for fullscreen indicators
        uint style = (uint)GetWindowLong(hwnd, GWL_STYLE);
        uint exStyle = (uint)GetWindowLong(hwnd, GWL_EXSTYLE);

        // Fullscreen exclusive typically has no caption, no border
        bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;
        bool hasBorder = (style & WS_BORDER) != 0 || (style & WS_THICKFRAME) != 0;
        bool isPopup = (style & WS_POPUP) != 0;

        // Check if window covers entire monitor (full bounds, not just work area)
        RECT monitorBounds = monitorInfo.rcMonitor;
        bool coversFullMonitor = 
            windowRect.Left <= monitorBounds.Left &&
            windowRect.Top <= monitorBounds.Top &&
            windowRect.Right >= monitorBounds.Right &&
            windowRect.Bottom >= monitorBounds.Bottom;

        // Also check work area (maximized window without taskbar)
        RECT workArea = monitorInfo.rcWork;
        bool coversWorkArea =
            windowRect.Left <= workArea.Left &&
            windowRect.Top <= workArea.Top &&
            windowRect.Right >= workArea.Right &&
            windowRect.Bottom >= workArea.Bottom;

        // Fullscreen exclusive: covers full monitor, typically no caption/border, or popup style
        if (coversFullMonitor && (!hasCaption || isPopup))
            return true;

        // Borderless fullscreen: covers full monitor with no visible chrome
        if (coversFullMonitor && !hasBorder && !hasCaption)
            return true;

        // Maximized app covering work area is NOT considered fullscreen (user wants shake-to-find)
        // Only return true for true fullscreen (covering taskbar area)
        if (coversFullMonitor && !coversWorkArea)
        {
            // Window extends beyond work area into taskbar space = fullscreen
            return true;
        }

        return false;
    }
}
