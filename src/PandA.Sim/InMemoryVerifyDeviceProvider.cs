using PandA.Core.Plc;

namespace PandA.Sim;

/// <summary>
/// In-memory <see cref="IVerifyDeviceProvider"/> for the Sim/tests: a per-line verify-scanner device id.
/// Lines with no configured value report 0 (no verify scanner → no recovery).
/// </summary>
public sealed class InMemoryVerifyDeviceProvider : IVerifyDeviceProvider
{
    private readonly Dictionary<string, int> _byLine = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string lineId, int verifyDeviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        _byLine[lineId] = verifyDeviceId;
    }

    public ValueTask<int> GetVerifyDeviceIdAsync(string lineId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_byLine.GetValueOrDefault(lineId));
    }
}
