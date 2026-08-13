using PandA.Core;

namespace PandA.Sim;

/// <summary>
/// In-memory <see cref="IProfileStore"/> for the Sim/tests. Holds, per line, the known profiles and a
/// per-profile active flag (source <c>LabelProfileHeader.Active</c>). Thread-safe; profile names are
/// matched case-insensitively.
/// </summary>
public sealed class InMemoryProfileStore : IProfileStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Dictionary<string, (FirePointProfile Profile, bool Active)>> _byLine =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register a profile on a line. Newly registered profiles are active by default.</summary>
    public void Register(string lineId, FirePointProfile profile, bool active = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentNullException.ThrowIfNull(profile);

        lock (_gate)
        {
            var profiles = ProfilesFor(lineId);
            profiles[profile.Name] = (profile, active);
        }
    }

    public ValueTask<IReadOnlyList<FirePointProfile>> GetActiveProfilesAsync(
        string lineId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            IReadOnlyList<FirePointProfile> active = _byLine.TryGetValue(lineId, out var profiles)
                ? [.. profiles.Values.Where(entry => entry.Active).Select(entry => entry.Profile)]
                : [];
            return ValueTask.FromResult(active);
        }
    }

    public ValueTask ActivateAsync(string lineId, string profileName, CancellationToken cancellationToken = default) =>
        SetActive(lineId, profileName, active: true, cancellationToken);

    public ValueTask DeactivateAsync(string lineId, string profileName, CancellationToken cancellationToken = default) =>
        SetActive(lineId, profileName, active: false, cancellationToken);

    private ValueTask SetActive(string lineId, string profileName, bool active, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_byLine.TryGetValue(lineId, out var profiles) &&
                profiles.TryGetValue(profileName, out var entry))
            {
                profiles[profileName] = (entry.Profile, active);
            }
        }

        return ValueTask.CompletedTask;
    }

    private Dictionary<string, (FirePointProfile Profile, bool Active)> ProfilesFor(string lineId)
    {
        if (!_byLine.TryGetValue(lineId, out var profiles))
        {
            profiles = new Dictionary<string, (FirePointProfile, bool)>(StringComparer.OrdinalIgnoreCase);
            _byLine[lineId] = profiles;
        }

        return profiles;
    }
}
