using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace GlobalCpsTester;

internal readonly record struct ClickSnapshot(
    double InstantCps,
    double PeakCps,
    long TotalClicks,
    double SinceLastClickMs);

internal sealed class ClickMetrics
{
    private const double OneSecondWindowMs = 1000.0;

    private readonly ConcurrentQueue<PendingClick> _pendingClicks = new();
    private readonly Queue<double> _recentClickTimesMs = new();
    private readonly Queue<long> _recentClickStopwatchTicks = new();
    private readonly object _sync = new();

    private ClickSnapshot _cachedSnapshot = new(0, 0, 0, double.NaN);
    private double _peakCps;
    private long _totalClicks;
    private long _lastClickStopwatchTicks;

    public void RegisterClick(long timestampMs)
    {
        _pendingClicks.Enqueue(new PendingClick(timestampMs, Stopwatch.GetTimestamp()));
    }

    public void Advance(long nowTimestampMs)
    {
        long nowStopwatchTicks = Stopwatch.GetTimestamp();

        lock (_sync)
        {
            DrainPendingClicksLocked();
            TrimExpiredClicksLocked(nowTimestampMs);

            double instantCps = ComputeCurrentCpsLocked(nowStopwatchTicks);
            if (instantCps > _peakCps)
            {
                _peakCps = instantCps;
            }

            double sinceLastClickMs = _lastClickStopwatchTicks == 0
                ? double.NaN
                : Math.Max(0, StopwatchTicksToMilliseconds(nowStopwatchTicks - _lastClickStopwatchTicks));

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
            _recentClickTimesMs.Clear();
            _recentClickStopwatchTicks.Clear();
            _peakCps = 0;
            _totalClicks = 0;
            _lastClickStopwatchTicks = 0;
            _cachedSnapshot = new ClickSnapshot(0, 0, 0, double.NaN);
        }
    }

    private void DrainPendingClicksLocked()
    {
        while (_pendingClicks.TryDequeue(out PendingClick pendingClick))
        {
            _recentClickTimesMs.Enqueue(pendingClick.TimestampMs);
            _recentClickStopwatchTicks.Enqueue(pendingClick.StopwatchTicks);
            _totalClicks++;
            _lastClickStopwatchTicks = pendingClick.StopwatchTicks;
        }
    }

    private void TrimExpiredClicksLocked(double nowTimestampMs)
    {
        while (_recentClickTimesMs.Count > 0 && nowTimestampMs - _recentClickTimesMs.Peek() >= OneSecondWindowMs)
        {
            _recentClickTimesMs.Dequeue();
            _recentClickStopwatchTicks.Dequeue();
        }
    }

    private double ComputeCurrentCpsLocked(long nowStopwatchTicks)
    {
        int count = _recentClickStopwatchTicks.Count;
        if (count == 0)
        {
            return 0;
        }

        double sinceLastClickMs = _lastClickStopwatchTicks == 0
            ? OneSecondWindowMs
            : Math.Max(0, StopwatchTicksToMilliseconds(nowStopwatchTicks - _lastClickStopwatchTicks));

        if (count == 1)
        {
            double singleClickDecay = 1.0 - Math.Clamp(sinceLastClickMs / OneSecondWindowMs, 0.0, 1.0);
            return singleClickDecay;
        }

        long oldestStopwatchTicks = _recentClickStopwatchTicks.Peek();
        double spanMs = Math.Max(0.001, StopwatchTicksToMilliseconds(_lastClickStopwatchTicks - oldestStopwatchTicks));
        double averageIntervalMs = Math.Max(0.001, spanMs / (count - 1));

        double decayedCps = count - (sinceLastClickMs / averageIntervalMs);
        return Math.Max(0, decayedCps);
    }

    private static double StopwatchTicksToMilliseconds(long stopwatchTicks)
    {
        return stopwatchTicks * 1000.0 / Stopwatch.Frequency;
    }

    private readonly record struct PendingClick(double TimestampMs, long StopwatchTicks);
}
