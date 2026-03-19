using System;
using System.Collections.Generic;
using System.Linq;
using static ShakeToFindCursor.MouseHook;

namespace ShakeToFindCursor;

public class ShakeEventArgs : EventArgs 
{
    public double Intensity { get; set; }
}

public class ShakeDetector
{
    private readonly Queue<(NativePoint Point, DateTime Time)> _history = new Queue<(NativePoint, DateTime)>();
    private TimeSpan WindowSize => TimeSpan.FromMilliseconds(App.CurrentSettings.TimeWindowMs);
    private double TotalDistanceThreshold => App.CurrentSettings.DistanceThreshold;
    private readonly double _netToTotalRatioThreshold = 0.35;

    public event EventHandler<ShakeEventArgs>? ShakeDetected;

    public void AddPoint(NativePoint point)
    {
        var now = DateTime.UtcNow;
        _history.Enqueue((point, now));

        // Remove old points
        while (_history.Count > 0 && now - _history.Peek().Time > WindowSize)
        {
            _history.Dequeue();
        }

        CheckForShake();
    }

    private void CheckForShake()
    {
        if (_history.Count < 5) return;

        var points = _history.Select(h => h.Point).ToList();
        
        double totalDistance = 0;
        for (int i = 1; i < points.Count; i++)
        {
            totalDistance += Distance(points[i - 1], points[i]);
        }

        var oldest = points.First();
        var newest = points.Last();
        double netDistance = Distance(oldest, newest);

        if (totalDistance > TotalDistanceThreshold)
        {
            if (netDistance / totalDistance < _netToTotalRatioThreshold)
            {
                double intensity = Math.Clamp((totalDistance - TotalDistanceThreshold) / (TotalDistanceThreshold * 1.5), 0.3, 1.0);
                ShakeDetected?.Invoke(this, new ShakeEventArgs { Intensity = intensity });
                _history.Clear(); // prevent re-triggering immediately
            }
        }
    }

    private double Distance(NativePoint p1, NativePoint p2)
    {
        long dx = p1.X - p2.X;
        long dy = p1.Y - p2.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
