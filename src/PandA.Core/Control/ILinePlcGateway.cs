namespace PandA.Core.Control;

public interface ILinePlcGateway
{
    ValueTask SendLineControlAsync(LineControlCommand command, CancellationToken cancellationToken = default);
}
