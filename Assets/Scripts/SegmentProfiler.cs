using System.Collections.Generic;
using System.Diagnostics;

public static class SegmentProfiler
{
    private class SegmentData
    {
        public long TotalTicks = 0;
        public int CallCount = 0;
    }

    private static readonly Dictionary<string, SegmentData> segments = new();
    private static readonly Stopwatch stopwatch = Stopwatch.StartNew();

    private static string currentSegment = null;
    private static long segmentStartTicks = 0;

    public static void Start(string name)
    {
        if (currentSegment != null)
            End();

        currentSegment = name;
        segmentStartTicks = stopwatch.ElapsedTicks;
    }

    public static void End()
    {
        if (currentSegment == null)
            return;

        long elapsedTicks = stopwatch.ElapsedTicks - segmentStartTicks;

        if (!segments.TryGetValue(currentSegment, out var data))
            segments[currentSegment] = data = new SegmentData();

        data.TotalTicks += elapsedTicks;
        data.CallCount++;

        currentSegment = null;
    }

    public static void Reset()
    {
        segments.Clear();
        currentSegment = null;
        segmentStartTicks = 0;
    }

    public static void PrintReport()
    {
        UnityEngine.Debug.Log("==== Segment Profiler Report ====");
        foreach (var (name, data) in segments)
        {
            double ms = data.TotalTicks * 1000.0 / Stopwatch.Frequency;
            UnityEngine.Debug.Log($"{name}: {ms:F2} ms over {data.CallCount} calls");
        }
    }
}