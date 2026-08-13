namespace PandA.Core.Settings;

public interface ISettingsProvider
{
    ValueTask<T> GetAsync<T>(string name, T defaultValue, CancellationToken cancellationToken = default);

    ValueTask<T> GetAsync<T>(SettingsDescriptor<T> descriptor, CancellationToken cancellationToken = default) =>
        GetAsync(descriptor.Name, descriptor.DefaultValue, cancellationToken);

    ValueTask SetAsync<T>(string name, T value, CancellationToken cancellationToken = default);

    ValueTask SetAsync<T>(SettingsDescriptor<T> descriptor, T value, CancellationToken cancellationToken = default) =>
        SetAsync(descriptor.Name, value, cancellationToken);
}
