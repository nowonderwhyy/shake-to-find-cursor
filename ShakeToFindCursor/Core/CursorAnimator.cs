using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ShakeToFindCursor;

/// <summary>
/// Drives the system cursor size so it continuously tracks the shake energy. Every
/// frame the cursor eases toward 1 + (maxScale - 1) * energy with no overshoot and no
/// hold timer: it grows while you shake and shrinks smoothly the instant you stop —
/// the macOS "shake to locate" behavior.
/// </summary>
public sealed class CursorAnimator : IDisposable
{
    private readonly object _gate = new();
    private readonly ShakeDetector _detector;

    private double _maxScale;
    private double _currentScale = 1.0;
    private bool _running;
    private int _lastAppliedFrame = -1;

    // Time constant for easing the cursor scale toward its energy-driven target.
    private const double FollowTauMs = 55.0;

    public CursorAnimator(ShakeDetector detector, AppSettings settings)
    {
        _detector = detector;
        UpdateSettings(settings);
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_gate)
        {
            _maxScale = Math.Max(1.0, settings.MagnificationFactor);
        }
    }

    /// <summary>Ensures the animation loop is running. Safe to call on every mouse move.</summary>
    public void Wake()
    {
        lock (_gate)
        {
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

            try
            {
                while (true)
                {
                    long nowTicks = Stopwatch.GetTimestamp();
                    double dt = (nowTicks - lastTicks) / (double)Stopwatch.Frequency;
                    lastTicks = nowTicks;
                    if (dt <= 0) dt = 1.0 / 144.0;
                    if (dt > 0.05) dt = 0.05;

                    double energy = _detector.Tick(Environment.TickCount64);

                    double max;
                    lock (_gate) { max = _maxScale; }

                    double target = 1.0 + ((max - 1.0) * energy);
                    double alpha = 1.0 - Math.Exp(-dt / (FollowTauMs / 1000.0));

                    _currentScale += (target - _currentScale) * alpha;
                    if (_currentScale < 1.0) _currentScale = 1.0;

                    double scale = _currentScale;
                    bool done = energy <= 0.0 && Math.Abs(_currentScale - 1.0) < 0.01;

                    int frameIndex = CursorHelper.GetFrameIndexForScale(scale);
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

                    // Frame pacing: ~7 ms (≈143 FPS). Deliberate high-precision pacer —
                    // Thread.Sleep(1) for the bulk of the wait, busy-spin only for the
                    // sub-2ms tail Sleep can't resolve. Runs only during an active shake.
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
