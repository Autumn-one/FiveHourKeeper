using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiveHourKeeper.Models;
using FiveHourKeeper.Services;

namespace FiveHourKeeper.ViewModels;

public sealed partial class ModelListViewModel : ObservableObject
{
    private readonly IModelRegistry _registry;
    private readonly ConfigRootHolder _config;
    private readonly JsonConfigStore _store;

    [ObservableProperty]
    private ModelProfile? _selected;

    public ObservableCollection<ModelProfile> Profiles { get; }

    /// <summary>弹窗打开回调（由 View 层注入，避免 VM 直接 new Window）。</summary>
    public Func<ModelProfile?, bool>? EditDialogLauncher { get; set; }

    public ModelListViewModel(IModelRegistry registry, ConfigRootHolder config, JsonConfigStore store)
    {
        _registry = registry;
        _config = config;
        _store = store;
        Profiles = new ObservableCollection<ModelProfile>(registry.AllProfiles());
    }

    [RelayCommand]
    private void Add() => OpenEdit(null);

    [RelayCommand]
    private void Edit(ModelProfile? profile) => OpenEdit(profile);

    private void OpenEdit(ModelProfile? source)
    {
        if (EditDialogLauncher is null) return;
        if (EditDialogLauncher(source)) RefreshFromRegistry();
    }

    [RelayCommand]
    private void Remove(ModelProfile? profile)
    {
        if (profile is null) return;
        var owner = System.Windows.Application.Current?.MainWindow;
        var dialog = System.Windows.MessageBox.Show(owner,
            $"确认删除模型 “{profile.Name}”？关联的所有重置时间将一并移除。",
            "删除模型",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning);
        if (dialog != System.Windows.MessageBoxResult.OK) return;

        _registry.Remove(profile.Id);
        PersistAndRefresh();
    }

    [RelayCommand]
    private void ToggleEnabled(ModelProfile? profile)
    {
        if (profile is null) return;
        profile.Enabled = !profile.Enabled;
        _registry.Upsert(profile);
        PersistAndRefresh();
    }

    private void RefreshFromRegistry()
    {
        Profiles.Clear();
        foreach (var p in _registry.AllProfiles()) Profiles.Add(p);
        OnPropertyChanged(nameof(Profiles));
    }

    private void PersistAndRefresh()
    {
        // registry 是唯一事实来源，落盘后回填列表。
        RefreshFromRegistry();
        _config.Current.Models.Clear();
        _config.Current.Models.AddRange(_registry.AllProfiles());
        _store.Save(_config.Current);
    }

    public void Persist()
    {
        _config.Current.Models.Clear();
        _config.Current.Models.AddRange(_registry.AllProfiles());
        _store.Save(_config.Current);
    }
}