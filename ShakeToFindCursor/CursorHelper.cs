using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ShakeToFindCursor;

public static class CursorHelper
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] public static extern IntPtr CopyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] public static extern bool SetSystemCursor(IntPtr hcur, uint id);
    [DllImport("user32.dll")] public static extern IntPtr CreateIconIndirect(ref ICONINFO icon);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    private const uint SPI_SETCURSORS = 0x0057;

    private const uint IMAGE_CURSOR = 2;
    private const uint LR_LOADFROMFILE = 0x00000010;

    public static readonly uint[] SYSTEM_CURSORS = new uint[] 
    {
        32512, 32513, 32514, 32515, 32516, 32640, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651
    };

    public static readonly Dictionary<uint, string> CURSOR_REG_NAMES = new()
    {
        { 32512, "Arrow" }, { 32513, "IBeam" }, { 32514, "Wait" }, { 32515, "Crosshair" },
        { 32516, "UpArrow" }, { 32640, "SizeAll" }, { 32642, "SizeNWSE" }, { 32643, "SizeNESW" },
        { 32644, "SizeWE" }, { 32645, "SizeNS" }, { 32646, "SizeAll" }, { 32648, "No" },
        { 32649, "Hand" }, { 32650, "AppStarting" }, { 32651, "Help" }
    };

    private static Dictionary<uint, IntPtr[]> _scaleFrames = new();
    private static double _cachedMaxScale = 6.0;
    public const int ScaleSteps = 48;
    public static bool IsCached { get; private set; } = false;
    private static double _lastFactor = -1;

    public static void InitCaches(double magnificationFactor)
    {
        if (IsCached && Math.Abs(_lastFactor - magnificationFactor) < 0.1) return;

        IsCached = false;
        _cachedMaxScale = magnificationFactor;

        foreach (var arr in _scaleFrames.Values)
            foreach (var ptr in arr)
                if (ptr != IntPtr.Zero) DestroyIcon(ptr);

        _scaleFrames.Clear();

        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");

        foreach (uint id in SYSTEM_CURSORS)
        {
            IntPtr fallbackSysHCursor = LoadCursor(IntPtr.Zero, (int)id);

            string? name = CURSOR_REG_NAMES.TryGetValue(id, out var n) ? n : null;
            string? path = null;

            if (name != null && key != null)
            {
                path = key.GetValue(name) as string;
                if (!string.IsNullOrEmpty(path))
                {
                    path = Environment.ExpandEnvironmentVariables(path);
                    if (path.Contains(",")) path = path.Split(',')[0];
                }
            }

            var frames = new IntPtr[ScaleSteps];
            for (int i = 0; i < ScaleSteps; i++)
            {
                double t = i / (double)(ScaleSteps - 1);
                // We cache up to 105% to handle the tiny physical overshoot from the Spring model securely!
                double scale = 1.0 + ((_cachedMaxScale * 1.05 - 1.0) * t);
                frames[i] = GenerateFrame(path, fallbackSysHCursor, scale);
            }

            _scaleFrames[id] = frames;
        }

        IsCached = true;
        _lastFactor = magnificationFactor;
    }

    private static IntPtr GenerateFrame(string? path, IntPtr fallback, double scale)
    {
        IntPtr frameCursor = IntPtr.Zero;
        if (!string.IsNullOrEmpty(path))
        {
            int ts = (int)(32 * scale);
            frameCursor = LoadImage(IntPtr.Zero, path, IMAGE_CURSOR, ts, ts, LR_LOADFROMFILE);
        }
        if (frameCursor == IntPtr.Zero && fallback != IntPtr.Zero)
        {
            frameCursor = MagnifyCursorFallback(fallback, scale);
        }
        return frameCursor;
    }

    private static IntPtr MagnifyCursorFallback(IntPtr hCursor, double scale)
    {
        IntPtr hSafeCopy = CopyIcon(hCursor);
        if (hSafeCopy == IntPtr.Zero) return IntPtr.Zero;

        using (Icon originalIcon = Icon.FromHandle(hSafeCopy))
        using (Bitmap originalBmp = originalIcon.ToBitmap())
        {
            int newW = (int)(originalBmp.Width * scale);
            int newH = (int)(originalBmp.Height * scale);
            if (newW <= 0) newW = 1; if (newH <= 0) newH = 1;

            using (Bitmap newBmp = new Bitmap(newW, newH, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(newBmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(originalBmp, 0, 0, newW, newH);
                }
                
                IntPtr hIconBig = newBmp.GetHicon();

                if (GetIconInfo(hCursor, out ICONINFO origInfo))
                {
                    if (GetIconInfo(hIconBig, out ICONINFO newInfo))
                    {
                        newInfo.fIcon = false;
                        newInfo.xHotspot = (int)(origInfo.xHotspot * scale);
                        newInfo.yHotspot = (int)(origInfo.yHotspot * scale);

                        IntPtr finalCursor = CreateIconIndirect(ref newInfo);

                        DeleteObject(origInfo.hbmColor); DeleteObject(origInfo.hbmMask);
                        DeleteObject(newInfo.hbmColor); DeleteObject(newInfo.hbmMask);
                        DestroyIcon(hIconBig);

                        return finalCursor;
                    }
                    DeleteObject(origInfo.hbmColor); DeleteObject(origInfo.hbmMask);
                }
                return hIconBig;
            }
        }
    }

    public static int GetFrameIndexForScale(double scale)
    {
        if (_cachedMaxScale <= 1.0) return 0;
        
        double peakScale = _cachedMaxScale * 1.05;
        scale = Math.Clamp(scale, 1.0, peakScale);
        double t = (scale - 1.0) / (peakScale - 1.0);
        return Math.Clamp((int)Math.Round(t * (ScaleSteps - 1)), 0, ScaleSteps - 1);
    }

    public static void ApplyScaleFrame(int frameIndex)
    {
        foreach (uint id in SYSTEM_CURSORS)
        {
            if (_scaleFrames.TryGetValue(id, out var frames))
            {
                IntPtr frame = frames[frameIndex];
                if (frame != IntPtr.Zero)
                    SetSystemCursor(CopyIcon(frame), id);
            }
        }
    }

    public static void RestoreThemeCursors()
    {
        SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
    }
}
