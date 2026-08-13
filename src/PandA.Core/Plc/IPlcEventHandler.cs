namespace PandA.Core.Plc;

/// <summary>Ingests PLC tracking events and applies pre-verify carton recovery (F15).</summary>
public interface IPlcEventHandler
{
    ValueTask HandleAsync(PlcEvent evt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the configured verify-scanner device id for a line (source <c>VerifyScannerDeviceID</c> from
/// <c>sdivw_pandadefs</c>). The recovery position gate compares the event device against this value.
/// </summary>
public interface IVerifyDeviceProvider
{
    /// <summary>The verify scanner's device id for a line, or 0 when no verify scanner is configured.</summary>
    ValueTask<int> GetVerifyDeviceIdAsync(string lineId, CancellationToken cancellationToken = default);
}
