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

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    private const uint SPI_SETCURSORS = 0x0057;
    private const int SM_CXCURSOR = 13;
    private const int SM_CYCURSOR = 14;

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
    public const int ScaleSteps = 64;
    private static double[] _frameScales = Array.Empty<double>();
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

        // Build non-linear scale table: power bias packs more frames near 1.0
        double peakScale = _cachedMaxScale * 1.05;
        _frameScales = new double[ScaleSteps];
        for (int i = 0; i < ScaleSteps; i++)
        {
            double t = i / (double)(ScaleSteps - 1);
            double biased = Math.Pow(t, 1.8); // ~60% of frames below 1.25x
            _frameScales[i] = 1.0 + ((peakScale - 1.0) * biased);
        }

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
                frames[i] = GenerateFrame(path, fallbackSysHCursor, _frameScales[i]);
            }

            _scaleFrames[id] = frames;
        }

        IsCached = true;
        _lastFactor = magnificationFactor;
    }

    private static IntPtr GenerateFrame(string? path, IntPtr fallback, double scale)
    {
        try
        {
            IntPtr frameCursor = IntPtr.Zero;
            if (!string.IsNullOrEmpty(path))
            {
                // Dynamically fetch the system's actual base cursor size
                int baseSize = GetSystemMetrics(SM_CXCURSOR);
                if (baseSize == 0) baseSize = 32; // Fallback just in case

                int ts = Math.Max(1, (int)(baseSize * scale));
                frameCursor = LoadImage(IntPtr.Zero, path, IMAGE_CURSOR, ts, ts, LR_LOADFROMFILE);
            }

            if (frameCursor == IntPtr.Zero && fallback != IntPtr.Zero)
            {
                try
                {
                    frameCursor = MagnifyCursorFallback(fallback, scale);
                }
                catch
                {
                    // Frame generation failed - return zero
                    return IntPtr.Zero;
                }
            }

            return frameCursor;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static IntPtr MagnifyCursorFallback(IntPtr hCursor, double scale)
    {
        IntPtr hSafeCopy = CopyIcon(hCursor);
        if (hSafeCopy == IntPtr.Zero) return IntPtr.Zero;

        try
        {
            // Extract hotspot from the safe copy, not the shared system handle
            if (!GetIconInfo(hSafeCopy, out ICONINFO origInfo))
                return IntPtr.Zero;

            int hotX = origInfo.xHotspot;
            int hotY = origInfo.yHotspot;

            // Get bitmap data then immediately release the GDI objects
            Bitmap originalBmp;
            using (Icon tmpIcon = Icon.FromHandle(hSafeCopy))
                originalBmp = tmpIcon.ToBitmap();

            if (originalBmp == null)
                return IntPtr.Zero;

            DeleteObject(origInfo.hbmColor);
            DeleteObject(origInfo.hbmMask);

            int newW = Math.Max(1, (int)(originalBmp.Width * scale));
            int newH = Math.Max(1, (int)(originalBmp.Height * scale));

            using (originalBmp)
            using (Bitmap newBmp = new Bitmap(newW, newH, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(newBmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(originalBmp, 0, 0, newW, newH);
                }

                IntPtr hIconBig = IntPtr.Zero;
                try
                {
                    hIconBig = newBmp.GetHicon();
                    if (hIconBig == IntPtr.Zero)
                        return IntPtr.Zero;

                    if (!GetIconInfo(hIconBig, out ICONINFO newInfo))
                    {
                        DestroyIcon(hIconBig);
                        return IntPtr.Zero;
                    }

                    newInfo.fIcon = false;
                    newInfo.xHotspot = (int)(hotX * scale);
                    newInfo.yHotspot = (int)(hotY * scale);

                    IntPtr finalCursor = CreateIconIndirect(ref newInfo);

                    DeleteObject(newInfo.hbmColor);
                    DeleteObject(newInfo.hbmMask);
                    DestroyIcon(hIconBig);

                    return finalCursor;
                }
                catch
                {
                    if (hIconBig != IntPtr.Zero)
                        DestroyIcon(hIconBig);
                    return IntPtr.Zero;
                }
            }
        }
        catch
        {
            return IntPtr.Zero;
        }
        finally
        {
            DestroyIcon(hSafeCopy);
        }
    }

    public static int GetFrameIndexForScale(double scale)
    {
        if (_frameScales.Length == 0) return 0;

        scale = Math.Clamp(scale, _frameScales[0], _frameScales[^1]);

        int idx = Array.BinarySearch(_frameScales, scale);
        if (idx >= 0) return idx;

        // BinarySearch returns bitwise complement of next-larger element
        idx = ~idx;
        if (idx <= 0) return 0;
        if (idx >= _frameScales.Length) return _frameScales.Length - 1;

        // Return whichever neighbor is closest
        return Math.Abs(_frameScales[idx - 1] - scale) <= Math.Abs(_frameScales[idx] - scale)
            ? idx - 1 : idx;
    }

    public static void ApplyScaleFrame(int frameIndex)
    {
        try
        {
            // Bounds check
            if (frameIndex < 0 || frameIndex >= ScaleSteps)
                frameIndex = 0;

            // Safety: ensure we have cached frames
            if (_scaleFrames.Count == 0)
                return;

            try
            {
                foreach (uint id in SYSTEM_CURSORS)
                {
                    try
                    {
                        if (!_scaleFrames.TryGetValue(id, out var frames))
                            continue;

                        if (frameIndex >= frames.Length)
                            continue;

                        IntPtr frame = frames[frameIndex];

                        // Skip if no frame for this index
                        if (frame == IntPtr.Zero)
                            continue;

                        IntPtr copy = CopyIcon(frame);
                        if (copy == IntPtr.Zero)
                            continue;

                        // Try to set the cursor. If successful, Windows takes ownership.
                        // If it fails, we own the handle and must clean it up.
                        if (SetSystemCursor(copy, id))
                        {
                            // Success: Windows takes ownership of 'copy' and will destroy it automatically.
                        }
                        else
                        {
                            // SetSystemCursor failed - clean up the copy we made
                            DestroyIcon(copy);
                        }
                    }
                    catch
                    {
                        // Skip this cursor type and continue with others
                    }
                }
            }
            catch
            {
                // Complete failure - try to restore to avoid leaving system in bad state
                RestoreThemeCursors();
            }
        }
        catch
        {
            // Outermost safety net
        }
    }

    public static void RestoreThemeCursors()
    {
        try
        {
            SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
        }
        catch
        {
            // If restore fails, there's nothing more we can do
        }
    }
}
