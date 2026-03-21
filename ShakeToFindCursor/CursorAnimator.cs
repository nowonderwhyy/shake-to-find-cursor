using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WpfApplication = System.Windows.Application;

namespace ShakeToFindCursor;

public sealed class CursorAnimator : IDisposable
{
    private readonly object _gate = new();

    private double _currentScale = 1.0;
    private double _targetScale = 1.0;
    private double _velocity = 0.0;
    private double _maxScale;
    private double _holdMs;

    private long _lastExciteTicks = Stopwatch.GetTimestamp();
    private bool _running;
    private int _lastAppliedFrame = -1;

    // --- Release blend state ---
    private bool _releasing;
    private double _releaseFromScale = 1.0;
    private long _releaseStartTicks;

    // --- Configurable Spring Parameters (from settings) ---
    private double _expandStiffness = 700.0;
    private double _expandDamping = 42.0;
    private double _shrinkStiffness = 280.0;
    private double _shrinkDamping = 38.0;
    private double _finalStiffness = 160.0;
    private double _finalDamping = 26.0;
    private double _releaseBlendMs = 200.0;
    private double _releaseCurvePower = 2.6;

    // --- Overlay renderer ---
    private OverlayWindow? _overlayWindow;
    private bool _useOverlay = true;

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    public CursorAnimator(double maxScale, int holdDurationMs)
    {
        _maxScale = Math.Max(1.0, maxScale);
        _holdMs = Math.Clamp(holdDurationMs, 60, 1000);
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_gate)
        {
            _maxScale = Math.Max(1.0, settings.MagnificationFactor);
            _holdMs = Math.Clamp(settings.HoldDurationMs, 60, 1000);
            _targetScale = Math.Min(_targetScale, _maxScale);
            _currentScale = Math.Min(_currentScale, _maxScale * 1.05);

            // Update spring parameters
            _expandStiffness = settings.ExpandStiffness;
            _expandDamping = settings.ExpandDamping;
            _shrinkStiffness = settings.ShrinkStiffness;
            _shrinkDamping = settings.ShrinkDamping;
            _finalStiffness = settings.FinalStiffness;
            _finalDamping = settings.FinalDamping;
            _releaseBlendMs = settings.ReleaseBlendMs;
            _releaseCurvePower = settings.ReleaseCurvePower;
            _useOverlay = settings.UseOverlayRenderer;

            // Update overlay settings if it exists
            if (_overlayWindow != null)
            {
                WpfApplication.Current?.Dispatcher?.Invoke(() =>
                {
                    _overlayWindow.RingOpacity = settings.OverlayRingOpacity;
                    _overlayWindow.RingThickness = settings.OverlayRingThickness;
                    _overlayWindow.EnableSpotlight = settings.ShowSpotlight;
                    try
                    {
                        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.OverlayColor);
                        _overlayWindow.RingColor = color;
                    }
                    catch { }
                });
            }
        }
    }

    public void InitializeOverlay()
    {
        WpfApplication.Current?.Dispatcher?.Invoke(() =>
        {
            if (_overlayWindow == null)
            {
                _overlayWindow = new OverlayWindow();
                var settings = App.CurrentSettings;
                _overlayWindow.RingOpacity = settings.OverlayRingOpacity;
                _overlayWindow.RingThickness = settings.OverlayRingThickness;
                _overlayWindow.EnableSpotlight = settings.ShowSpotlight;
                try
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.OverlayColor);
                    _overlayWindow.RingColor = color;
                }
                catch { }
            }
        });
    }

    public void Excite(double intensity)
    {
        intensity = Math.Clamp(intensity, 0.0, 1.0);

        lock (_gate)
        {
            double desired = 1.0 + ((_maxScale - 1.0) * intensity);

            // Cancel any in-flight release — we're shaking again
            _releasing = false;

            // Never yank target downward during active shaking.
            _targetScale = Math.Max(_targetScale, desired);
            _lastExciteTicks = Stopwatch.GetTimestamp();

            if (_running) return;
            _running = true;
            _ = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        // Offload the entire animation loop to a thread pool thread.
        // This is necessary because the spin-yield pacing loop below would otherwise run
        // synchronously on the caller's thread (the mouse hook), causing input freezing and crashes.
        await Task.Run(async () =>
        {
            long lastTicks = Stopwatch.GetTimestamp();
            const double StuckAnimationTimeoutMs = 5000; // Force exit if no new input for 5 seconds

            try
            {
                while (true)
                {
                    long nowTicks = Stopwatch.GetTimestamp();
                    double dt = (nowTicks - lastTicks) / (double)Stopwatch.Frequency;
                    lastTicks = nowTicks;

                    if (dt <= 0) dt = 1.0 / 120.0;
                    if (dt > 0.05) dt = 0.05;

                    bool done;
                    double scaleToApply;
                    double msSinceLastInput = 0;

                    lock (_gate)
                    {
                        msSinceLastInput =
                            (Stopwatch.GetTimestamp() - _lastExciteTicks) * 1000.0 / Stopwatch.Frequency;

                        // --- Begin release blend when hold expires ---
                        if (!_releasing && msSinceLastInput > _holdMs)
                        {
                            _releasing = true;
                            _releaseFromScale = Math.Max(_currentScale, 1.0);
                            _releaseStartTicks = Stopwatch.GetTimestamp();
                        }

                        // --- Glide the target toward 1.0 on a deceleration curve ---
                        if (_releasing)
                        {
                            double releaseMs =
                                (Stopwatch.GetTimestamp() - _releaseStartTicks) * 1000.0 / Stopwatch.Frequency;

                            double p = Math.Clamp(releaseMs / _releaseBlendMs, 0.0, 1.0);

                            // Power curve: front-loaded decel, long soft tail
                            double eased = 1.0 - Math.Pow(1.0 - p, _releaseCurvePower);

                            _targetScale = Lerp(_releaseFromScale, 1.0, eased);
                        }

                        // --- Pick the right spring zone ---
                        bool expanding = _targetScale > _currentScale;

                        double stiffness;
                        double damping;

                        if (expanding)
                        {
                            stiffness = _expandStiffness;
                            damping = _expandDamping;
                        }
                        else if (_currentScale > 1.25)
                        {
                            // Main shrink: moderate spring
                            stiffness = _shrinkStiffness;
                            damping = _shrinkDamping;
                        }
                        else
                        {
                            // Final approach: critically damped glide to rest
                            stiffness = _finalStiffness;
                            damping = _finalDamping;
                        }

                        double accel = stiffness * (_targetScale - _currentScale) - damping * _velocity;
                        _velocity += accel * dt;
                        _currentScale += _velocity * dt;

                        if (_currentScale < 1.0) _currentScale = 1.0;
                        if (_currentScale > _maxScale * 1.05) _currentScale = _maxScale * 1.05;

                        scaleToApply = _currentScale;

                        done =
                            Math.Abs(_targetScale - _currentScale) < 0.005 &&
                            Math.Abs(_velocity) < 0.005 &&
                            _targetScale <= 1.001;

                        // Safety timeout: if animation is still running but no new input for way too long,
                        // force exit to prevent stuck loops. But only after release has started.
                        if (_releasing && msSinceLastInput > StuckAnimationTimeoutMs)
                            done = true;
                    }

                    // Apply visual effect - use overlay or cursor replacement
                    bool useOverlay;
                    lock (_gate)
                    {
                        useOverlay = _useOverlay;
                    }

                    if (useOverlay && _overlayWindow != null)
                    {
                        // Use overlay window (works in all apps)
                        WpfApplication.Current?.Dispatcher?.Invoke(() =>
                        {
                            if (scaleToApply > 1.01)
                            {
                                _overlayWindow.UpdateScale(scaleToApply);
                                if (!_overlayWindow.IsVisible)
                                    _overlayWindow.Show(scaleToApply);
                            }
                            else if (done)
                            {
                                _overlayWindow.Hide();
                            }
                        });
                    }
                    else
                    {
                        // Use cursor replacement (legacy mode)
                        int frameIndex = CursorHelper.GetFrameIndexForScale(scaleToApply);
                        if (frameIndex != _lastAppliedFrame)
                        {
                            _lastAppliedFrame = frameIndex;
                            try
                            {
                                CursorHelper.ApplyScaleFrame(frameIndex);
                            }
                            catch
                            {
                                // Cursor operation failed — just continue, we'll restore on cleanup
                            }
                        }
                    }

                    if (done)
                    {
                        if (!useOverlay)
                        {
                            // Always restore to frame 0 before final restore (legacy mode)
                            try
                            {
                                CursorHelper.ApplyScaleFrame(0);
                            }
                            catch
                            {
                                // If even this fails, we'll restore below
                            }

                            await Task.Delay(10);

                            try
                            {
                                CursorHelper.RestoreThemeCursors();
                            }
                            catch
                            {
                                // Final restore attempt failed, but at least we tried
                            }
                        }
                        break;
                    }

                    // Precise frame pacing: ~7ms (approx 143 FPS) with CPU-friendly hybrid wait
                    long targetTicks = Stopwatch.GetTimestamp() + (long)(0.007 * Stopwatch.Frequency);
                    while (Stopwatch.GetTimestamp() < targetTicks)
                    {
                        long ticksLeft = targetTicks - Stopwatch.GetTimestamp();
                        double msLeft = (double)ticksLeft / Stopwatch.Frequency * 1000.0;

                        if (msLeft > 2.0)
                        {
                            // Yield to OS to save CPU/Battery
                            Thread.Sleep(1);
                        }
                        else
                        {
                            // Lighter busy-wait for sub-millisecond precision
                            Thread.SpinWait(10);
                        }
                    }
                }
            }
            finally
            {
                lock (_gate)
                {
                    _running = false;
                    _releasing = false;
                    _velocity = 0.0;
                    _targetScale = 1.0;
                    _currentScale = 1.0;
                    _lastAppliedFrame = -1;
                }
            }
        });
    }

    public void Dispose()
    {
        try
        {
            CursorHelper.RestoreThemeCursors();
        }
        catch
        {
            // Best effort — if restore fails, there's not much we can do
        }

        // Close overlay window on UI thread
        WpfApplication.Current?.Dispatcher?.Invoke(() =>
        {
            _overlayWindow?.Close();
            _overlayWindow = null;
        });
    }
}
