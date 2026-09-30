using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.Utils;
using Unity.Profiling;
using UnityEngine;

[assembly: MelonInfo(typeof(FrameProbe.FrameProbeMod), "Frame Probe", "0.1.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace FrameProbe;

public sealed class FrameProbeMod : MelonMod
{
    const string Version = "0.1.0";
    readonly Il2CppStructArray<FrameTiming> timings = new(1);
    readonly List<double> frame = new(4096), main = new(4096), render = new(4096), gpu = new(4096), draws = new(4096);
    ProfilerRecorder drawRecorder;
    MelonPreferences_Entry<bool> enabled;
    MelonPreferences_Entry<int> windowSeconds;
    StreamWriter output;
    string scene = "startup";
    double windowStart;
    int observed, gpuUnavailable, timingFailures;
    bool warnedTiming;
    ulong lastTimingStamp;

    public override void OnInitializeMelon()
    {
        var category = MelonPreferences.CreateCategory("FrameProbe", "Frame Probe");
        enabled = category.CreateEntry("Enabled", true, description: "Record frame timing summaries for controlled performance comparisons.");
        windowSeconds = category.CreateEntry("WindowSeconds", 30, description: "Summary interval in seconds (10 to 300). Scene changes also end the current interval.");
        try
        {
            string dir = Path.Combine(MelonEnvironment.UserDataDirectory, "FrameProbe");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"session-{DateTime.Now:yyyyMMdd-HHmmss-fff}.csv");
            output = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            output.WriteLine("utc,scene,seconds,samples,frame_median_ms,frame_p95_ms,frame_p99_ms,main_median_ms,main_p95_ms,render_median_ms,render_p95_ms,gpu_median_ms,gpu_p95_ms,draw_calls_median,draw_calls_p95,gpu_missing,timing_failures");
            output.Flush();
            LoggerInstance.Msg($"Logging to {path}");
            LoggerInstance.Msg("Loaded mods: " + string.Join(", ", MelonMod.RegisteredMelons.Select(m => m.Info.Name + " " + m.Info.Version)));
        }
        catch (Exception e) { LoggerInstance.Error($"Cannot open performance log: {e.Message}"); }
        try { drawRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"); }
        catch (Exception e) { LoggerInstance.Warning($"Draw call counter unavailable: {e.Message}"); }
        windowStart = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        Report();
        scene = sceneName;
    }

    public override void OnUpdate()
    {
        if (output == null || !enabled.Value) return;
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (now - windowStart >= Math.Clamp(windowSeconds.Value, 10, 300)) Report();
        observed++;
        frame.Add(Time.unscaledDeltaTime * 1000.0);
        if (drawRecorder.Valid && drawRecorder.LastValue > 0) draws.Add(drawRecorder.LastValue);
        try
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) == 0) { gpuUnavailable++; return; }
            var sample = timings[0];
            if (sample.frameStartTimestamp == lastTimingStamp) return;
            lastTimingStamp = sample.frameStartTimestamp;
            if (sample.cpuMainThreadFrameTime > 0) main.Add(sample.cpuMainThreadFrameTime);
            if (sample.cpuRenderThreadFrameTime > 0) render.Add(sample.cpuRenderThreadFrameTime);
            if (sample.gpuFrameTime > 0) gpu.Add(sample.gpuFrameTime);
            else gpuUnavailable++;
        }
        catch (Exception e)
        {
            timingFailures++;
            if (!warnedTiming) { warnedTiming = true; LoggerInstance.Warning($"FrameTimingManager unavailable: {e.Message}"); }
        }
    }

    public override void OnApplicationQuit() { Report(); output?.Dispose(); output = null; if (drawRecorder.Valid) drawRecorder.Dispose(); }

    void Report()
    {
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (output != null && observed > 0)
        {
            string row = string.Join(",", DateTime.UtcNow.ToString("O"), Csv(scene), (now - windowStart).ToString("F1", System.Globalization.CultureInfo.InvariantCulture), observed,
                Metric(frame, .5), Metric(frame, .95), Metric(frame, .99), Metric(main, .5), Metric(main, .95),
                Metric(render, .5), Metric(render, .95), Metric(gpu, .5), Metric(gpu, .95), Metric(draws, .5), Metric(draws, .95), gpuUnavailable, timingFailures);
            output.WriteLine(row);
            output.Flush();
            LoggerInstance.Msg($"{scene}: {observed} frames, frame p95 {Metric(frame, .95)} ms, main p95 {Metric(main, .95)} ms, GPU p95 {Metric(gpu, .95)} ms");
        }
        frame.Clear(); main.Clear(); render.Clear(); gpu.Clear(); draws.Clear();
        observed = gpuUnavailable = timingFailures = 0;
        windowStart = now;
    }

    static string Metric(List<double> values, double quantile)
    {
        if (values.Count == 0) return "";
        values.Sort();
        return values[(int)Math.Ceiling(quantile * values.Count) - 1].ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
    }

    static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
