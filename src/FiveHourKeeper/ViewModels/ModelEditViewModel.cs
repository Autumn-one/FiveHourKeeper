using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiveHourKeeper.Models;
using FiveHourKeeper.Services;

namespace FiveHourKeeper.ViewModels;

public sealed partial class ModelEditViewModel : ObservableObject
{
    private readonly IModelRegistry _registry;
    private readonly IAiClientFactory _clientFactory;
    private readonly ConfigRootHolder _config;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private AiProtocol _protocol = AiProtocol.OpenAiCompatible;
    [ObservableProperty] private string _baseUrl = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _modelName = "";
    [ObservableProperty] private string? _promptOverride;
    [ObservableProperty] private int? _maxTokens = 64;
    [ObservableProperty] private int _maxRetries = 3;
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _nameError = "";
    [ObservableProperty] private string _baseUrlError = "";
    [ObservableProperty] private string _apiKeyError = "";
    [ObservableProperty] private string _modelNameError = "";
    [ObservableProperty] private string _conflictWarning = "";

    public ObservableCollection<string> FetchedModels { get; } = new();
    public ObservableCollection<ScheduleRowViewModel> Schedules { get; } = new();

    public ModelProfile? Target { get; }

    public ModelEditViewModel(
        ModelProfile? source,
        IModelRegistry registry,
        IAiClientFactory clientFactory,
        ConfigRootHolder config)
    {
        _registry = registry;
        _clientFactory = clientFactory;
        _config = config;
        Schedules.CollectionChanged += (_, _) => RebuildConflictWarning();

        if (source is not null)
        {
            Target = source;
            Name = source.Name;
            Protocol = source.Protocol;
            BaseUrl = source.BaseUrl;
            ApiKey = DpapiSecretProtector.Unprotect(source.ApiKeyCipherBase64);
            ModelName = source.ModelName;
            PromptOverride = source.PromptOverride;
            MaxTokens = source.MaxTokens;
            MaxRetries = source.MaxRetries;
            Enabled = source.Enabled;
            foreach (var m in source.FetchedModels) FetchedModels.Add(m);
            foreach (var s in source.Schedules) Schedules.Add(new ScheduleRowViewModel(s));
        }
        else
        {
            // 新模型默认给一条每天 09:00 的占位，便于理解编辑交互。
            Schedules.Add(new ScheduleRowViewModel());
        }
        Validate();
        RebuildConflictWarning();
    }

    partial void OnBaseUrlChanged(string value) { FetchedModels.Clear(); Validate(); }
    partial void OnApiKeyChanged(string value) { Validate(); }
    partial void OnNameChanged(string value) => Validate();
    partial void OnModelNameChanged(string value) => Validate();
    partial void OnProtocolChanged(AiProtocol value)
    {
        FetchedModels.Clear();
        Validate();
    }

    private void Validate()
    {
        NameError = string.IsNullOrWhiteSpace(Name) ? "请填写模型别名" : "";
        BaseUrlError = string.IsNullOrWhiteSpace(BaseUrl) ? "请填写 Base URL" : "";
        ApiKeyError = string.IsNullOrWhiteSpace(ApiKey) ? "请填写 API Key" : "";
        ModelNameError = string.IsNullOrWhiteSpace(ModelName) ? "请填写模型名称" : "";
        RebuildConflictWarning();
    }

    private void RebuildConflictWarning()
    {
        if (Schedules.Count == 0) { ConflictWarning = ""; return; }

        var now = DateTimeOffset.Now;
        var targets = new List<DateTimeOffset>();
        foreach (var row in Schedules)
        {
            if (row.Kind == ScheduleKind.Daily && TimeOnly.TryParse(row.TimeText, out var t))
                targets.Add(now.Date.Add(t.ToTimeSpan()));
            else if (row.Kind == ScheduleKind.OneShot &&
                     DateOnly.TryParse(row.DateText, out var d) &&
                     TimeOnly.TryParse(row.TimeText, out var tt))
            {
                var local = d.ToDateTime(tt);
                targets.Add(new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)));
            }
        }

        var conflicts = ScheduleCalculator.DetectConflicts(targets);
        ConflictWarning = conflicts.Count == 0
            ? ""
            : string.Join("\n", conflicts.Select(c => $"· {c.Message}"));
    }

    [RelayCommand]
    private async Task FetchModelsAsync()
    {
        Validate();
        if (!string.IsNullOrEmpty(BaseUrlError) || !string.IsNullOrEmpty(ApiKeyError))
        {
            Status = "请先补全 Base URL 与 API Key";
            return;
        }
        IsBusy = true;
        Status = "拉取中…";
        try
        {
            var working = BuildDraft();
            WithApiKey(working, ApiKey);
            var client = _clientFactory.Create(working);
            var models = await client.ListModelsAsync(working);
            FetchedModels.Clear();
            foreach (var m in models) FetchedModels.Add(m);
            if (models.Count == 0) Status = "拉取成功但返回为空，可手动填写模型名称";
            else Status = $"已拉取 {models.Count} 个模型，可在下拉中选择";
        }
        catch (AiClientException ex)
        {
            Status = $"拉取失败：HTTP {ex.HttpStatus} {ex.Message}";
        }
        catch (Exception ex)
        {
            Status = $"拉取失败：{ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        Validate();
        if (!string.IsNullOrEmpty(BaseUrlError) || !string.IsNullOrEmpty(ApiKeyError) || !string.IsNullOrEmpty(ModelNameError))
        {
            Status = "请先补全必填字段再测试";
            return;
        }
        IsBusy = true;
        Status = "测试连通中…（注意：请求成功即建立新窗口，可能影响既有计划）";
        try
        {
            var prompt = ScheduleCalculator.EffectivePrompt(_config.Current.Global.DefaultPrompt, PromptOverride);
            var working = BuildDraft();
            WithApiKey(working, ApiKey);
            var client = _clientFactory.Create(working);
            var result = await client.SendPingAsync(working, prompt);
            if (result.Success)
                Status = $"连通成功（{result.LatencyMs} ms，响应：{Truncate(result.ResponseSnippet, 60)}）。已建立新窗口锚点";
            else
                Status = $"连通失败：HTTP {result.HttpStatus} {result.Error}";
        }
        catch (Exception ex)
        {
            Status = $"测试失败：{ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "(空)" : s.Length <= max ? s : s[..max] + "…";

    public ModelProfile BuildDraft()
    {
        var profile = new ModelProfile
        {
            Id = Target?.Id ?? Guid.NewGuid().ToString("N"),
            Name = Name.Trim(),
            Protocol = Protocol,
            BaseUrl = BaseUrl.Trim(),
            ModelName = ModelName.Trim(),
            PromptOverride = string.IsNullOrWhiteSpace(PromptOverride) ? null : PromptOverride.Trim(),
            MaxTokens = MaxTokens,
            MaxRetries = MaxRetries,
            Enabled = Enabled,
            FetchedModels = FetchedModels.ToList(),
            ModelsFetchedAt = FetchedModels.Count > 0 ? DateTimeOffset.UtcNow : Target?.ModelsFetchedAt,
            Schedules = new List<ResetSchedule>(),
        };

        // 密钥：仅当用户改动过 API Key 时才重新加密；未改动沿用原密文。
        profile.ApiKeyCipherBase64 = string.IsNullOrEmpty(ApiKey)
            ? Target?.ApiKeyCipherBase64 ?? ""
            : DpapiSecretProtector.Protect(ApiKey);

        foreach (var row in Schedules)
        {
            if (row.TryToModel(out var schedule, out _)) profile.Schedules.Add(schedule);
        }
        return profile;
    }

    /// <summary>把 API Key 明文直接塞进待发副本（仅内存中，不落盘）。</summary>
    public static void WithApiKey(ModelProfile target, string plainApiKey) =>
        target.ApiKeyCipherBase64 = plainApiKey;

    /// <summary>收集所有行的时间格式错误，供弹窗保存前拦截。</summary>
    public string? CollectScheduleErrors()
    {
        var errors = new List<string>();
        foreach (var row in Schedules)
        {
            if (!row.TryToModel(out _, out var error)) errors.Add($"· {error}");
        }
        return errors.Count == 0 ? null : string.Join("\n", errors);
    }

    public bool IsValid() =>
        string.IsNullOrEmpty(NameError) && string.IsNullOrEmpty(BaseUrlError)
        && string.IsNullOrEmpty(ApiKeyError) && string.IsNullOrEmpty(ModelNameError);

    [RelayCommand]
    private void AddSchedule()
    {
        Schedules.Add(new ScheduleRowViewModel());
        RebuildConflictWarning();
    }

    [RelayCommand]
    private void RemoveSchedule(ScheduleRowViewModel? row)
    {
        if (row is null) return;
        Schedules.Remove(row);
        RebuildConflictWarning();
    }
}