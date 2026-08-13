namespace PandA.Core.Settings;

public interface ISettingsDescriptor
{
    string Name { get; }

    object? DefaultObjectValue { get; }

    Type ValueType { get; }

    string Description { get; }

    bool Display { get; }
}

public sealed record SettingsDescriptor<T>(
    string Name,
    T DefaultValue,
    string Description,
    bool Display = true) : ISettingsDescriptor
{
    public object? DefaultObjectValue => DefaultValue;

    public Type ValueType => typeof(T);
}
