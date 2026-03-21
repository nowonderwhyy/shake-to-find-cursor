using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using WpfPen = System.Windows.Media.Pen;
using WpfBrush = System.Windows.Media.Brush;

namespace ShakeToFindCursor;

/// <summary>
/// A transparent overlay window that renders a macOS-style spotlight/ring effect around the cursor.
/// </summary>
public partial class OverlayWindow : Window
{
    #region P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    #endregion

    #region Configuration

    /// <summary>Ring color (default: gray).</summary>
    public WpfColor RingColor { get; set; } = WpfColor.FromRgb(0x80, 0x80, 0x80);

    /// <summary>Ring thickness in pixels.</summary>
    public double RingThickness { get; set; } = 3.5;

    /// <summary>Ring opacity (0.0 - 1.0).</summary>
    public double RingOpacity { get; set; } = 0.5;

    /// <summary>Base ring radius at scale 1.0.</summary>
    public double BaseRadius { get; set; } = 40.0;

    /// <summary>Enable spotlight/vignette effect outside ring.</summary>
    public bool EnableSpotlight { get; set; } = true;

    /// <summary>Spotlight darkness (0.0 - 1.0).</summary>
    public double SpotlightOpacity { get; set; } = 0.15;

    /// <summary>Fade duration in milliseconds.</summary>
    public double FadeDurationMs { get; set; } = 150.0;

    #endregion

    #region State

    private double _currentScale = 1.0;
    private double _targetOpacity = 0.0;
    private double _currentOpacity = 0.0;
    private bool _isVisible;
    private bool _isRendering;

    private WpfPoint _cursorPosition;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    // Cached brushes/pens for performance
    private WpfPen? _ringPen;
    private WpfBrush? _spotlightBrush;
    private WpfColor _lastRingColor;
    private double _lastRingThickness;
    private double _lastRingOpacity;

    #endregion

    public OverlayWindow()
    {
        InitializeComponent();

        // Cover entire virtual screen
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Get DPI scaling
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        // Make window click-through
        MakeClickThrough();

        // Initially hidden
        Opacity = 0;
        _isVisible = false;
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE,
            extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    #region Public API

    /// <summary>
    /// Show the overlay at current cursor position with the given scale.
    /// </summary>
    public void Show(double scale)
    {
        _currentScale = Math.Max(1.0, scale);
        _targetOpacity = 1.0;

        UpdatePosition();

        if (!_isVisible)
        {
            _isVisible = true;
            base.Show();
            StartRendering();
        }
    }

    /// <summary>
    /// Update the ring size smoothly.
    /// </summary>
    public void UpdateScale(double scale)
    {
        _currentScale = Math.Max(1.0, scale);
    }

    /// <summary>
    /// Fade out and hide the overlay.
    /// </summary>
    public new void Hide()
    {
        _targetOpacity = 0.0;
        // Rendering loop will handle the fade and actual hide
    }

    /// <summary>
    /// Update the cursor position for the effect.
    /// </summary>
    public void UpdatePosition()
    {
        if (GetCursorPos(out POINT pt))
        {
            // Convert screen coordinates to window coordinates
            _cursorPosition = new WpfPoint(
                pt.X - SystemParameters.VirtualScreenLeft,
                pt.Y - SystemParameters.VirtualScreenTop
            );
        }
    }

    #endregion

    #region Rendering

    private void StartRendering()
    {
        if (_isRendering) return;
        _isRendering = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_isRendering) return;
        _isRendering = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Animate opacity
        double fadeStep = 1000.0 / 60.0 / FadeDurationMs; // Per frame at 60fps
        if (_currentOpacity < _targetOpacity)
        {
            _currentOpacity = Math.Min(_currentOpacity + fadeStep, _targetOpacity);
        }
        else if (_currentOpacity > _targetOpacity)
        {
            _currentOpacity = Math.Max(_currentOpacity - fadeStep, _targetOpacity);
        }

        Opacity = _currentOpacity;

        // Update cursor position
        UpdatePosition();

        // Check if we should hide completely
        if (_currentOpacity <= 0 && _targetOpacity <= 0)
        {
            _isVisible = false;
            StopRendering();
            base.Hide();
            return;
        }

        // Trigger visual update
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (!_isVisible || _currentOpacity <= 0) return;

        double radius = BaseRadius * _currentScale;
        double cx = _cursorPosition.X;
        double cy = _cursorPosition.Y;

        // Update cached resources if settings changed
        UpdateCachedResources();

        // Draw spotlight effect (semi-transparent darkening outside ring)
        if (EnableSpotlight && SpotlightOpacity > 0 && _spotlightBrush != null)
        {
            // Create a geometry with a hole (ring cutout)
            var outerRect = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
            var innerCircle = new EllipseGeometry(new WpfPoint(cx, cy), radius + 10, radius + 10);

            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, outerRect, innerCircle);
            dc.DrawGeometry(_spotlightBrush, null, combined);
        }

        // Draw the ring
        if (_ringPen != null)
        {
            dc.DrawEllipse(null, _ringPen, new WpfPoint(cx, cy), radius, radius);

            // Optional: Draw a subtle inner glow ring
            var glowPen = new WpfPen(
                new SolidColorBrush(WpfColor.FromArgb((byte)(40 * RingOpacity), RingColor.R, RingColor.G, RingColor.B)),
                RingThickness * 2);
            dc.DrawEllipse(null, glowPen, new WpfPoint(cx, cy), radius - RingThickness, radius - RingThickness);
        }
    }

    private void UpdateCachedResources()
    {
        // Recreate pen if settings changed
        if (_ringPen == null ||
            _lastRingColor != RingColor ||
            _lastRingThickness != RingThickness ||
            _lastRingOpacity != RingOpacity)
        {
            _lastRingColor = RingColor;
            _lastRingThickness = RingThickness;
            _lastRingOpacity = RingOpacity;

            var ringBrush = new SolidColorBrush(
                WpfColor.FromArgb((byte)(255 * RingOpacity), RingColor.R, RingColor.G, RingColor.B));
            ringBrush.Freeze();

            _ringPen = new WpfPen(ringBrush, RingThickness);
            _ringPen.Freeze();

            // Spotlight brush
            var spotlightColor = WpfColor.FromArgb((byte)(255 * SpotlightOpacity), 0, 0, 0);
            _spotlightBrush = new SolidColorBrush(spotlightColor);
            _spotlightBrush.Freeze();
        }
    }

    #endregion

    #region Cleanup

    protected override void OnClosed(EventArgs e)
    {
        StopRendering();
        base.OnClosed(e);
    }

    #endregion
}
