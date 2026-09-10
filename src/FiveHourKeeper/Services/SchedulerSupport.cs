using FiveHourKeeper.Models;

namespace FiveHourKeeper.Services;

/// <summary>当前配置的轻量级容器，便于 SchedulerService 始终读到最新的 <see cref="AppConfig"/>。</summary>
public sealed class ConfigRootHolder
{
    public AppConfig Current { get; set; } = new();
}

/// <summary>可注入的时钟，便于测试。</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>UI 与调度器共享的模型注册表，对外暴露读写。</summary>
public interface IModelRegistry
{
    IEnumerable<ModelProfile> AllProfiles();
    IEnumerable<ModelProfile> EnabledProfiles();
    void Upsert(ModelProfile profile);
    void Remove(string id);
    void DisableSchedule(ModelProfile profile, string scheduleId);
}

/// <summary>AI 客户端工厂：根据协议路由到 OpenAI / Anthropic 实现。</summary>
public interface IAiClientFactory
{
    IAiClient Create(ModelProfile profile);
}