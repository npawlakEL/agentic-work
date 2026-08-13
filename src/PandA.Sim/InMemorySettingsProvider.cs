using System.Collections.Concurrent;
using System.Globalization;
using PandA.Core.Settings;

namespace PandA.Sim;

public sealed class InMemorySettingsProvider : ISettingsProvider
{
    private readonly ConcurrentDictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    public InMemorySettingsProvider(bool seedKnownSettings = true)
    {
        if (!seedKnownSettings)
        {
            return;
        }

        foreach (var setting in KnownSettings.All)
        {
            _values[setting.Name] = setting.DefaultObjectValue;
        }
    }

    public ValueTask<T> GetAsync<T>(string name, T defaultValue, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(_values.TryGetValue(name, out var value)
            ? ConvertValue(value, defaultValue)
            : defaultValue);
    }

    public ValueTask<T> GetAsync<T>(SettingsDescriptor<T> descriptor, CancellationToken cancellationToken = default) =>
        GetAsync(descriptor.Name, descriptor.DefaultValue, cancellationToken);

    public ValueTask SetAsync<T>(string name, T value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        _values[name] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetAsync<T>(SettingsDescriptor<T> descriptor, T value, CancellationToken cancellationToken = default) =>
        SetAsync(descriptor.Name, value, cancellationToken);

    private static T ConvertValue<T>(object? value, T defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        if (value is T typed)
        {
            return typed;
        }

        if (typeof(T) == typeof(bool))
        {
            if (value is string text)
            {
                if (bool.TryParse(text, out var parsedBool))
                {
                    return (T)(object)parsedBool;
                }

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                {
                    return (T)(object)(parsedInt != 0);
                }
            }

            if (value is int intValue)
            {
                return (T)(object)(intValue != 0);
            }
        }

        return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }
}
