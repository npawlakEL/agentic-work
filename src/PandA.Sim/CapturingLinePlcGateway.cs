using PandA.Core.Control;

namespace PandA.Sim;

public sealed class CapturingLinePlcGateway : ILinePlcGateway
{
    private readonly Lock _gate = new();
    private readonly List<LineControlCommand> _commands = [];

    public IReadOnlyList<LineControlCommand> Commands
    {
        get
        {
            lock (_gate)
            {
                return [.. _commands];
            }
        }
    }

    public ValueTask SendLineControlAsync(LineControlCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _commands.Add(command);
        }

        return ValueTask.CompletedTask;
    }
}
