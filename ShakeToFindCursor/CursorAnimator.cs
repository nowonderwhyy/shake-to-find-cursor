using System;
using System.Diagnostics;
using System.Threading;
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

    // --- Configurable Spring Parameters ---
    // Tuned for that satisfying macOS feel: snappy expand, smooth settle
    private double _expandStiffness = 800.0;   // Quick pop
    private double _expandDamping = 45.0;      // Slight overshoot allowed
    private double _shrinkStiffness = 320.0;   // Smooth retract
    private double _shrinkDamping = 40.0;
    private double _finalStiffness = 180.0;    // Buttery final settle
    private double _finalDamping = 28.0;
    private double _releaseBlendMs = 180.0;    // Quick release start
    private double _releaseCurvePower = 2.8;   // Smooth decel curve

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

            // Never yank target downward during active shaking
            _targetScale = Math.Max(_targetScale, desired);
            _lastExciteTicks = Stopwatch.GetTimestamp();

            if (_running) return;
            _running = true;
            _ = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        await Task.Run(async () =>
        {
            long lastTicks = Stopwatch.GetTimestamp();
            const double StuckAnimationTimeoutMs = 5000;

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
                    double msSinceLastInput;

                    lock (_gate)
                    {
                        msSinceLastInput = (Stopwatch.GetTimestamp() - _lastExciteTicks) * 1000.0 / Stopwatch.Frequency;

                        // Begin release blend when hold expires
                        if (!_releasing && msSinceLastInput > _holdMs)
                        {
                            _releasing = true;
                            _releaseFromScale = Math.Max(_currentScale, 1.0);
                            _releaseStartTicks = Stopwatch.GetTimestamp();
                        }

                        // Glide the target toward 1.0 on a deceleration curve
                        if (_releasing)
                        {
                            double releaseMs = (Stopwatch.GetTimestamp() - _releaseStartTicks) * 1000.0 / Stopwatch.Frequency;
                            double p = Math.Clamp(releaseMs / _releaseBlendMs, 0.0, 1.0);
                            double eased = 1.0 - Math.Pow(1.0 - p, _releaseCurvePower);
                            _targetScale = Lerp(_releaseFromScale, 1.0, eased);
                        }

                        // Pick the right spring parameters for current phase
                        bool expanding = _targetScale > _currentScale;
                        double stiffness, damping;

                        if (expanding)
                        {
                            stiffness = _expandStiffness;
                            damping = _expandDamping;
                        }
                        else if (_currentScale > 1.25)
                        {
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

                        _currentScale = Math.Clamp(_currentScale, 1.0, _maxScale * 1.05);
                        scaleToApply = _currentScale;

                        done = Math.Abs(_targetScale - _currentScale) < 0.005 &&
                               Math.Abs(_velocity) < 0.005 &&
                               _targetScale <= 1.001;

                        if (_releasing && msSinceLastInput > StuckAnimationTimeoutMs)
                            done = true;
                    }

                    // Apply cursor scale
                    int frameIndex = CursorHelper.GetFrameIndexForScale(scaleToApply);
                    if (frameIndex != _lastAppliedFrame)
                    {
                        _lastAppliedFrame = frameIndex;
                        try { CursorHelper.ApplyScaleFrame(frameIndex); } catch { }
                    }

                    if (done)
                    {
                        try { CursorHelper.ApplyScaleFrame(0); } catch { }
                        await Task.Delay(10);
                        try { CursorHelper.RestoreThemeCursors(); } catch { }
                        break;
                    }

                    // Frame pacing: ~7ms (143 FPS)
                    long targetTicks = Stopwatch.GetTimestamp() + (long)(0.007 * Stopwatch.Frequency);
                    while (Stopwatch.GetTimestamp() < targetTicks)
                    {
                        long ticksLeft = targetTicks - Stopwatch.GetTimestamp();
                        double msLeft = (double)ticksLeft / Stopwatch.Frequency * 1000.0;
                        if (msLeft > 2.0) Thread.Sleep(1);
                        else Thread.SpinWait(10);
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
        try { CursorHelper.RestoreThemeCursors(); } catch { }
    }
}
