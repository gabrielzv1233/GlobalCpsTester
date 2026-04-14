using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LowLevelCpsTester;

internal readonly record struct ClickSnapshot(
    double InstantCps,
    double PeakCps,
    long TotalClicks,
    double SinceLastClickMs);

internal sealed class ClickMetrics
{
    private const long OneSecondWindowMs = 1000;

    private readonly ConcurrentQueue<long> _pendingClicks = new();
    private readonly Queue<long> _recentClicks = new();
    private readonly object _sync = new();

    private ClickSnapshot _cachedSnapshot = new(0, 0, 0, double.NaN);
    private double _peakCps;
    private long _totalClicks;
    private long _lastClickTimestampMs;

    public void RegisterClick(long timestampMs)
    {
        _pendingClicks.Enqueue(timestampMs);
    }

    public void Advance(long nowTimestampMs)
    {
        lock (_sync)
        {
            DrainPendingClicksLocked();
            TrimExpiredClicksLocked(nowTimestampMs);

            double instantCps = ComputeCurrentCpsLocked(nowTimestampMs);
            if (instantCps > _peakCps)
            {
                _peakCps = instantCps;
            }

            double sinceLastClickMs = _lastClickTimestampMs == 0
                ? double.NaN
                : Math.Max(0, nowTimestampMs - _lastClickTimestampMs);

            _cachedSnapshot = new ClickSnapshot(
                InstantCps: instantCps,
                PeakCps: _peakCps,
                TotalClicks: _totalClicks,
                SinceLastClickMs: sinceLastClickMs);
        }
    }

    public ClickSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return _cachedSnapshot;
        }
    }

    public ClickSnapshot GetCachedSnapshot()
    {
        lock (_sync)
        {
            return _cachedSnapshot;
        }
    }

    public void Reset()
    {
        while (_pendingClicks.TryDequeue(out _))
        {
        }

        lock (_sync)
        {
            _recentClicks.Clear();
            _peakCps = 0;
            _totalClicks = 0;
            _lastClickTimestampMs = 0;
            _cachedSnapshot = new ClickSnapshot(0, 0, 0, double.NaN);
        }
    }

    private void DrainPendingClicksLocked()
    {
        while (_pendingClicks.TryDequeue(out long timestampMs))
        {
            _recentClicks.Enqueue(timestampMs);
            _totalClicks++;
            _lastClickTimestampMs = timestampMs;
            TrimExpiredClicksLocked(timestampMs);

            double instantCps = ComputeCurrentCpsLocked(timestampMs);
            if (instantCps > _peakCps)
            {
                _peakCps = instantCps;
            }
        }
    }

    private void TrimExpiredClicksLocked(long nowTimestampMs)
    {
        while (_recentClicks.Count > 0 && nowTimestampMs - _recentClicks.Peek() >= OneSecondWindowMs)
        {
            _recentClicks.Dequeue();
        }
    }

    private double ComputeCurrentCpsLocked(long nowTimestampMs)
    {
        if (_recentClicks.Count == 0)
        {
            return 0;
        }

        if (_lastClickTimestampMs == 0 || nowTimestampMs - _lastClickTimestampMs >= OneSecondWindowMs)
        {
            return 0;
        }

        return _recentClicks.Count;
    }
}
