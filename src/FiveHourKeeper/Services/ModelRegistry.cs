using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FiveHourKeeper.Models;

namespace FiveHourKeeper.Services;

/// <summary>默认内存实现：维护当前模型列表并发出增删改事件。</summary>
public sealed class ModelRegistry : ObservableObject, IModelRegistry
{
    private readonly ObservableCollection<ModelProfile> _all = new();

    public ModelRegistry(IEnumerable<ModelProfile> seed)
    {
        foreach (var p in seed) _all.Add(p);
        _all.CollectionChanged += OnCollectionChanged;
        foreach (var p in _all) Hook(p);
    }

    public IReadOnlyList<ModelProfile> Snapshot() => _all.ToArray();

    public IEnumerable<ModelProfile> AllProfiles() => _all;

    public IEnumerable<ModelProfile> EnabledProfiles() => _all.Where(p => p.Enabled);

    public void Upsert(ModelProfile profile)
    {
        // 按 Id 匹配：编辑弹窗返回的是新构造的对象，引用比较会误判为新增。
        var idx = -1;
        for (var i = 0; i < _all.Count; i++)
        {
            if (_all[i].Id == profile.Id) { idx = i; break; }
        }

        if (idx >= 0)
        {
            Unhook(_all[idx]);
            _all[idx] = profile;
            Hook(profile);
        }
        else
        {
            _all.Add(profile);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Snapshot)));
    }

    public void Remove(string id)
    {
        var idx = -1;
        for (var i = 0; i < _all.Count; i++)
        {
            if (_all[i].Id == id) { idx = i; break; }
        }
        if (idx >= 0)
        {
            Unhook(_all[idx]);
            _all.RemoveAt(idx);
        }
    }

    public void DisableSchedule(ModelProfile profile, string scheduleId)
    {
        var s = profile.Schedules.FirstOrDefault(x => x.Id == scheduleId);
        if (s is not null)
        {
            s.Enabled = false;
            if (s.Kind == ScheduleKind.OneShot) profile.Schedules.Remove(s);
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Snapshot)));

    private void Hook(ModelProfile p)
    {
        ((INotifyPropertyChanged)p).PropertyChanged += OnProfileChanged;
    }

    private void Unhook(ModelProfile p)
    {
        ((INotifyPropertyChanged)p).PropertyChanged -= OnProfileChanged;
    }

    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e) =>
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Snapshot)));
}