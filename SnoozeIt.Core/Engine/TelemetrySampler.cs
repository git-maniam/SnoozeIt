namespace SnoozeIt.Core.Engine;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using SnoozeIt.Core.Models;
using SnoozeIt.Core.Native;

/// <summary>
/// Samples active processes periodically to collect memory, CPU delta,
/// audio playback status, and computes live snooze recommendations.
/// </summary>
public sealed class TelemetrySampler
{
    private readonly ConcurrentDictionary<int, ProcessCpuTracker> _cpuTrackers = new();

    private sealed class ProcessCpuTracker
    {
        public TimeSpan TotalProcessorTime { get; set; }
        public DateTime SampleUtc { get; set; }
    }

    /// <summary>
    /// Scans running processes on the system, applies audio and resource sampling,
    /// and returns evaluated ProcessInfoModels with live recommendations.
    /// </summary>
    public List<(ProcessInfoModel Process, SnoozeRecommendation Recommendation)> SampleAllProcesses()
    {
        var results = new List<(ProcessInfoModel, SnoozeRecommendation)>();
        var activeAudioPids = AudioDetector.GetActiveAudioProcessIds();
        var now = DateTime.UtcNow;

        IntPtr fgHwnd = NativeMethods.GetForegroundWindow();
        uint fgPid = 0;
        if (fgHwnd != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(fgHwnd, out fgPid);
        }

        Process[] runningProcesses;
        try
        {
            runningProcesses = Process.GetProcesses();
        }
        catch
        {
            return results;
        }

        var currentPids = new HashSet<int>(runningProcesses.Length);

        foreach (var proc in runningProcesses)
        {
            int pid = proc.Id;
            currentPids.Add(pid);

            try
            {
                // Skip kernel PIDs immediately
                if (pid <= 4)
                {
                    proc.Dispose();
                    continue;
                }

                string procName = proc.ProcessName + ".exe";
                long workingSet = proc.WorkingSet64;
                int sessionId = -1;
                try { sessionId = proc.SessionId; } catch { }

                // Calculate CPU percentage delta
                double cpuPercent = 0.0;
                try
                {
                    TimeSpan totalTime = proc.TotalProcessorTime;
                    if (_cpuTrackers.TryGetValue(pid, out var prev))
                    {
                        double elapsedSeconds = (now - prev.SampleUtc).TotalSeconds;
                        if (elapsedSeconds > 0)
                        {
                            double cpuUsedMs = (totalTime - prev.TotalProcessorTime).TotalMilliseconds;
                            double totalMsPassed = elapsedSeconds * 1000.0 * Environment.ProcessorCount;
                            cpuPercent = Math.Clamp((cpuUsedMs / totalMsPassed) * 100.0, 0.0, 100.0);
                        }
                        prev.TotalProcessorTime = totalTime;
                        prev.SampleUtc = now;
                    }
                    else
                    {
                        _cpuTrackers[pid] = new ProcessCpuTracker
                        {
                            TotalProcessorTime = totalTime,
                            SampleUtc = now
                        };
                    }
                }
                catch
                {
                    // Access denied for system processes or elevated handles
                }

                bool isPlayingAudio = activeAudioPids.Contains(pid);

                var info = new ProcessInfoModel
                {
                    ProcessId = pid,
                    ProcessName = procName,
                    FriendlyName = proc.ProcessName,
                    WorkingSetBytes = workingSet,
                    CpuPercent = cpuPercent,
                    SessionId = sessionId,
                    IsPlayingAudio = isPlayingAudio,
                    LastForegroundUtc = (pid == (int)fgPid) ? now : now.AddMinutes(-10)
                };

                var recommendation = HeuristicEngine.Evaluate(info);
                results.Add((info, recommendation));
            }
            catch
            {
                // Skip processes that exited during sampling
            }
            finally
            {
                proc.Dispose();
            }
        }

        // Clean up CPU trackers for processes that exited
        foreach (var key in _cpuTrackers.Keys)
        {
            if (!currentPids.Contains(key))
            {
                _cpuTrackers.TryRemove(key, out _);
            }
        }

        return results;
    }
}

