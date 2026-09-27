namespace FreeCamManager.Core.Services;

public interface IAppLogger
{
    void Event(string eventName, params (string Key, object? Value)[] fields);
}
