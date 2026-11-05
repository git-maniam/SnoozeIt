namespace SnoozeIt_Client;

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SnoozeIt.Client.ViewModels;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        ProcessGroupListView.ItemsSource = ViewModel.Groups;

        ViewModel.PropertyChanged += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (e.PropertyName == nameof(ViewModel.SavedRamText))
                    SavedRamTextBlock.Text = ViewModel.SavedRamText;
                else if (e.PropertyName == nameof(ViewModel.SnoozedCount))
                    SnoozedCountTextBlock.Text = $"{ViewModel.SnoozedCount} Apps";
                else if (e.PropertyName == nameof(ViewModel.IsServiceConnected))
                    ServiceStatusTextBlock.Text = ViewModel.IsServiceConnected ? "Privileged Service Mode" : "Direct User Mode";
                else if (e.PropertyName == nameof(ViewModel.FeedbackMessage) && !string.IsNullOrEmpty(ViewModel.FeedbackMessage))
                {
                    ActionInfoBar.Message = ViewModel.FeedbackMessage;
                    ActionInfoBar.IsOpen = true;
                }
            });
        };
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.SearchFilter = SearchBox.Text;
        _ = ViewModel.RefreshDataAsync();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshDataAsync();
    }

    private async void OnItemToggleToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.Tag is ProcessItemViewModel item)
        {
            if (toggle.IsOn != item.IsSnoozed)
            {
                if (toggle.IsOn)
                {
                    await ViewModel.SnoozeProcessAsync(item);
                }
                else
                {
                    await ViewModel.ResumeProcessAsync(item);
                }
            }
        }
    }

    private async void OnGroupToggleToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.Tag is ProcessGroupViewModel group)
        {
            if (toggle.IsOn != group.IsSnoozed)
            {
                if (toggle.IsOn)
                {
                    await ViewModel.SnoozeGroupAsync(group);
                }
                else
                {
                    await ViewModel.ResumeGroupAsync(group);
                }
            }
        }
    }

    private async void OnSnoozeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProcessItemViewModel item)
        {
            await ViewModel.SnoozeProcessAsync(item);
        }
    }

    private async void OnThawClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProcessItemViewModel item)
        {
            await ViewModel.ResumeProcessAsync(item);
        }
    }

    private async void OnSnoozeGroupClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProcessGroupViewModel group)
        {
            await ViewModel.SnoozeGroupAsync(group);
        }
    }

    private async void OnThawGroupClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProcessGroupViewModel group)
        {
            await ViewModel.ResumeGroupAsync(group);
        }
    }

    private async void OnSnoozeAllClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.SnoozeAllRecommendedAsync();
    }
}
