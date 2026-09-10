namespace FiveHourKeeper.Services;

public interface INotifier
{
    void ShowSuccess(string modelName, DateTime targetLocal);
    void ShowFailure(string modelName, string error);
    void ShowInfo(string title, string message);
}

public sealed class NoopNotifier : INotifier
{
    public void ShowSuccess(string modelName, DateTime targetLocal) { }
    public void ShowFailure(string modelName, string error) { }
    public void ShowInfo(string title, string message) { }
}