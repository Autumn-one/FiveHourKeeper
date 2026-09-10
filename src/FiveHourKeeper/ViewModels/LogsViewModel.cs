using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FiveHourKeeper.Models;
using FiveHourKeeper.Services;

namespace FiveHourKeeper.ViewModels;

public sealed partial class LogsViewModel : ObservableObject
{
    public ObservableCollection<RunLog> Entries { get; } = new();

    public LogsViewModel(RunLogStore store)
    {
        foreach (var r in store.Snapshot(500)) Entries.Add(r);
    }
}