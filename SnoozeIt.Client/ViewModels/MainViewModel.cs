namespace SnoozeIt.Client.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnoozeIt.Client.Services;
using SnoozeIt.Core.Engine;
using SnoozeIt.Core.Models;

public partial class MainViewModel : ObservableObject
{
    private readonly TelemetrySampler _sampler = new();
    private readonly ProcessServiceClient _serviceClient = new();
    public WindowFocusWatcher FocusWatcher { get; }

    [ObservableProperty]
    private string _savedRamText = "0 MB Reclaimed";

    [ObservableProperty]
    private int _snoozedCount = 0;

    [ObservableProperty]
    private string _searchFilter = "";

    [ObservableProperty]
    private bool _isServiceConnected = false;

    private long _totalReclaimedBytes = 0;

    public ObservableCollection<ProcessItemViewModel> Processes { get; } = new();

    public MainViewModel()
    {
        FocusWatcher = new WindowFocusWatcher(_serviceClient);
        FocusWatcher.ProcessAutoThawed += OnProcessAutoThawed;
        FocusWatcher.Start();

        _ = RefreshDataAsync();
    }

    private void OnProcessAutoThawed(int pid)
    {
        var item = Processes.FirstOrDefault(p => p.ProcessId == pid);
        if (item != null)
        {
            item.State = SnoozeState.Active;
            SnoozedCount = Math.Max(0, SnoozedCount - 1);
        }
    }

    [RelayCommand]
    public async Task RefreshDataAsync()
    {
        IsServiceConnected = await _serviceClient.IsServiceAvailableAsync();
        var samples = _sampler.SampleAllProcesses();

        var filtered = string.IsNullOrWhiteSpace(SearchFilter)
            ? samples
            : samples.Where(s => s.Process.ProcessName.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
                                 (s.Process.FriendlyName ?? "").Contains(SearchFilter, StringComparison.OrdinalIgnoreCase));

        Processes.Clear();
        foreach (var (proc, rec) in filtered.OrderByDescending(s => s.Recommendation.Score))
        {
            Processes.Add(new ProcessItemViewModel
            {
                ProcessId = proc.ProcessId,
                ProcessName = proc.ProcessName,
                FriendlyName = proc.FriendlyName ?? proc.ProcessName,
                WorkingSetBytes = proc.WorkingSetBytes,
                CpuPercent = proc.CpuPercent,
                Score = rec.Score,
                Tier = rec.Tier,
                IsPlayingAudio = proc.IsPlayingAudio,
                State = proc.State
            });
        }
    }

    [RelayCommand]
    public async Task SnoozeProcessAsync(ProcessItemViewModel item)
    {
        if (item == null || item.State == SnoozeState.Snoozed) return;

        var result = await _serviceClient.SuspendProcessAsync(item.ProcessId);
        if (result.Success)
        {
            item.State = SnoozeState.Snoozed;
            FocusWatcher.RegisterSnoozedProcess(item.ProcessId);
            _totalReclaimedBytes += result.Value;
            SavedRamText = $"{(_totalReclaimedBytes / (1024.0 * 1024.0)):F1} MB Reclaimed";
            SnoozedCount++;
        }
    }

    [RelayCommand]
    public async Task ResumeProcessAsync(ProcessItemViewModel item)
    {
        if (item == null || item.State != SnoozeState.Snoozed) return;

        item.State = SnoozeState.Thawing;
        var result = await _serviceClient.ResumeProcessAsync(item.ProcessId);
        if (result.Success)
        {
            item.State = SnoozeState.Active;
            FocusWatcher.UnregisterSnoozedProcess(item.ProcessId);
            SnoozedCount = Math.Max(0, SnoozedCount - 1);
        }
    }

    [RelayCommand]
    public async Task SnoozeAllRecommendedAsync()
    {
        var recommended = Processes.Where(p => p.Tier == RecommendationTier.SafeToSnooze && p.State != SnoozeState.Snoozed).ToList();
        foreach (var p in recommended)
        {
            await SnoozeProcessAsync(p);
        }
    }
}

