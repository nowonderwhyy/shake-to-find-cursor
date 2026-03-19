using System;
using System.Diagnostics;
using System.Threading.Tasks;

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

    // --- Tuning ---
    // Expand: snappy with a tiny bounce overshoot
    private const double ExpandStiffness = 700.0;
    private const double ExpandDamping = 42.0;

    // Shrink: moderate pull-back, still responsive
    private const double ShrinkStiffness = 280.0;
    private const double ShrinkDamping = 38.0;

    // Final approach (below ~1.25x): critically damped, buttery glide to rest
    private const double FinalStiffness = 160.0;
    private const double FinalDamping = 26.0;

    // Release blend: how the target glides toward 1.0
    // Higher power = more front-loaded deceleration ("car braking")
    private const double ReleaseBlendMs = 200.0;
    private const double ReleaseCurvePower = 2.6;

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    public CursorAnimator(double maxScale, int holdDurationMs)
    {
        _maxScale = Math.Max(1.0, maxScale);
        _holdMs = Math.Clamp(holdDurationMs, 60, 1000);
    }

    public void UpdateSettings(double maxScale, int holdDurationMs)
    {
        lock (_gate)
        {
            _maxScale = Math.Max(1.0, maxScale);
            _holdMs = Math.Clamp(holdDurationMs, 60, 1000);
            _targetScale = Math.Min(_targetScale, _maxScale);
            _currentScale = Math.Min(_currentScale, _maxScale * 1.05);
        }
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
        long lastTicks = Stopwatch.GetTimestamp();

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

                lock (_gate)
                {
                    double msSinceExcite =
                        (Stopwatch.GetTimestamp() - _lastExciteTicks) * 1000.0 / Stopwatch.Frequency;

                    // --- Begin release blend when hold expires ---
                    if (!_releasing && msSinceExcite > _holdMs)
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

                        double p = Math.Clamp(releaseMs / ReleaseBlendMs, 0.0, 1.0);

                        // Power curve: front-loaded decel, long soft tail
                        double eased = 1.0 - Math.Pow(1.0 - p, ReleaseCurvePower);

                        _targetScale = Lerp(_releaseFromScale, 1.0, eased);
                    }

                    // --- Pick the right spring zone ---
                    bool expanding = _targetScale > _currentScale;

                    double stiffness;
                    double damping;

                    if (expanding)
                    {
                        stiffness = ExpandStiffness;
                        damping = ExpandDamping;
                    }
                    else if (_currentScale > 1.25)
                    {
                        // Main shrink: moderate spring
                        stiffness = ShrinkStiffness;
                        damping = ShrinkDamping;
                    }
                    else
                    {
                        // Final approach: critically damped glide to rest
                        stiffness = FinalStiffness;
                        damping = FinalDamping;
                    }

                    double accel = stiffness * (_targetScale - _currentScale) - damping * _velocity;
                    _velocity += accel * dt;
                    _currentScale += _velocity * dt;

                    if (_currentScale < 1.0) _currentScale = 1.0;
                    if (_currentScale > _maxScale * 1.05) _currentScale = _maxScale * 1.05;

                    scaleToApply = _currentScale;

                    done =
                        Math.Abs(_targetScale - _currentScale) < 0.01 &&
                        Math.Abs(_velocity) < 0.01 &&
                        _targetScale <= 1.001;
                }

                int frameIndex = CursorHelper.GetFrameIndexForScale(scaleToApply);
                if (frameIndex != _lastAppliedFrame)
                {
                    _lastAppliedFrame = frameIndex;
                    CursorHelper.ApplyScaleFrame(frameIndex);
                }

                if (done)
                {
                    // Sit on frame 0 for one beat before OS restore
                    CursorHelper.ApplyScaleFrame(0);
                    await Task.Delay(10);
                    CursorHelper.RestoreThemeCursors();
                    break;
                }

                await Task.Delay(8);
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
    }

    public void Dispose()
    {
        CursorHelper.RestoreThemeCursors();
    }
}
