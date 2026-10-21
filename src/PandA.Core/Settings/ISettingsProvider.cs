namespace PandA.Core.Settings;

/// <summary>
/// Global PandA settings (source <c>Settings</c> table).
/// <para>
/// Multi-host (decision-010): settings live in the shared database and an operator may change them from any
/// host. Implementations are <b>read-through</b> — <see cref="GetAsync{T}(string, T, CancellationToken)"/>
/// reflects the shared value on every call. An adapter may cache only with a short bounded TTL (a few seconds)
/// or a version check against the row; an unbounded per-process cache is not a valid implementation. Core reads
/// a setting at the point of use on each operation and never holds it across operations.
/// </para>
/// </summary>
public interface ISettingsProvider
{
    ValueTask<T> GetAsync<T>(string name, T defaultValue, CancellationToken cancellationToken = default);

    ValueTask<T> GetAsync<T>(SettingsDescriptor<T> descriptor, CancellationToken cancellationToken = default) =>
        GetAsync(descriptor.Name, descriptor.DefaultValue, cancellationToken);

    ValueTask SetAsync<T>(string name, T value, CancellationToken cancellationToken = default);

    ValueTask SetAsync<T>(SettingsDescriptor<T> descriptor, T value, CancellationToken cancellationToken = default) =>
        SetAsync(descriptor.Name, value, cancellationToken);
}
