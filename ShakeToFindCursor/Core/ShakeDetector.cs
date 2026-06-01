using System;
using System.Collections.Generic;
using static ShakeToFindCursor.MouseHook;

namespace ShakeToFindCursor;

/// <summary>
/// Tracks how vigorously the pointer is being shaken and exposes a continuous
/// "energy" value in [0, 1]. Energy rises while the pointer oscillates rapidly and
/// decays smoothly once shaking stops — mirroring macOS, where the enlarged cursor is
/// a live readout of the shaking rather than a one-shot trigger.
/// </summary>
public class ShakeDetector
{
    private readonly object _lock = new();
    private readonly Queue<(NativePoint Point, long TimeMs)> _history = new();

    private double _energy;
    private long _lastUpdateMs;
    private bool _suppressed;

    // Derived from Sensitivity in UpdateSettings.
    private double _triggerPath;   // window path length where energy starts rising
    private double _pathForFull;   // window path length that yields full energy

    private const long WindowMs = 350;          // how far back we look for oscillation
    private const double WiggleGate = 0.55;     // 1 - net/total must exceed this (oscillation, not a straight drag)
    private const int MinReversals = 2;         // sharp direction changes required within the window
    private const double ReleaseTauMs = 140.0;  // how fast energy decays once shaking stops

    public ShakeDetector(AppSettings settings) => UpdateSettings(settings);

    public void UpdateSettings(AppSettings settings)
    {
        lock (_lock)
        {
            // Higher sensitivity → less pointer travel needed to trigger and reach full size.
            double t = (Math.Clamp(settings.Sensitivity, 1.0, 10.0) - 1.0) / 9.0; // 0 (hard) .. 1 (easy)
            _triggerPath = 1700.0 - (1250.0 * t);  // 1700 .. 450 px within the window
            _pathForFull = _triggerPath * 2.2;
        }
    }

    /// <summary>While suppressed (e.g. a mouse button is held for a drag) energy is forced to zero.</summary>
    public void SetSuppressed(bool suppressed)
    {
        lock (_lock)
        {
            _suppressed = suppressed;
            if (suppressed)
            {
                _energy = 0;
                _history.Clear();
            }
        }
    }

    public double Energy
    {
        get { lock (_lock) { return _energy; } }
    }

    public void AddSample(NativePoint point, long nowMs)
    {
        lock (_lock)
        {
            if (_suppressed) return;

            _history.Enqueue((point, nowMs));
            while (_history.Count > 0 && nowMs - _history.Peek().TimeMs > WindowMs)
                _history.Dequeue();

            Decay(nowMs);

            double instant = ComputeInstant();
            if (instant > _energy) _energy = instant; // attack instantly; the animator eases the visual
        }
    }

    /// <summary>Advances the decay envelope on the animator's steady clock and returns current energy.</summary>
    public double Tick(long nowMs)
    {
        lock (_lock)
        {
            Decay(nowMs);
            return _energy;
        }
    }

    private void Decay(long nowMs)
    {
        long dt = nowMs - _lastUpdateMs;
        _lastUpdateMs = nowMs;
        if (dt <= 0) return;
        _energy *= Math.Exp(-dt / ReleaseTauMs);
        if (_energy < 1e-3) _energy = 0;
    }

    private double ComputeInstant()
    {
        if (_history.Count < 5) return 0;

        // Single pass over the window (runs on the hook thread for every move, so no
        // allocation — iterate the queue's struct enumerator directly).
        double totalPath = 0;
        int reversals = 0;
        long prevDx = 0, prevDy = 0;
        bool havePrevSeg = false;
        bool haveFirst = false;
        NativePoint first = default, prev = default, last = default;

        foreach (var (point, _) in _history)
        {
            if (!haveFirst)
            {
                first = prev = last = point;
                haveFirst = true;
                continue;
            }

            long dx = point.X - prev.X;
            long dy = point.Y - prev.Y;
            totalPath += Math.Sqrt((double)((dx * dx) + (dy * dy)));

            if (havePrevSeg)
            {
                // A negative dot product means this segment reversed direction (>90°) vs the
                // previous one — the signature of a back-and-forth shake rather than a curve.
                if ((dx * prevDx) + (dy * prevDy) < 0) reversals++;
            }

            if (dx != 0 || dy != 0) { prevDx = dx; prevDy = dy; havePrevSeg = true; }
            prev = last = point;
        }

        if (totalPath < _triggerPath) return 0;
        if (reversals < MinReversals) return 0;

        double net = Distance(first, last);
        double wiggle = totalPath > 0 ? 1.0 - (net / totalPath) : 0;
        if (wiggle < WiggleGate) return 0;

        return Math.Clamp((totalPath - _triggerPath) / (_pathForFull - _triggerPath), 0.0, 1.0);
    }

    private static double Distance(NativePoint a, NativePoint b)
    {
        long dx = a.X - b.X;
        long dy = a.Y - b.Y;
        return Math.Sqrt((double)((dx * dx) + (dy * dy)));
    }
}
