namespace PandA.Core;

public enum TransportOrderStatus
{
    /// <summary>Advised by the host; label set stored, awaiting induct scan (source PandaData/ADVISED).</summary>
    Advised = 0,

    /// <summary>Labels have been dispatched to printers.</summary>
    Printed = 1,
}

/// <summary>
/// The carton, modelled as a Transport Order (decision-002): keyed by the blind label (<see cref="TuId"/>),
/// carrying the typed <see cref="PandaLabelSet"/> as an extension. Backend-agnostic; the econtroller adapter
/// maps this onto MfcTransportOrder + DynamicField at integration.
/// </summary>
public sealed class TransportOrder
{
    public TransportOrder(string tuId, string lineId, PandaLabelSet labels, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tuId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentNullException.ThrowIfNull(labels);

        TuId = tuId;
        LineId = lineId;
        Labels = labels;
        CreatedAt = createdAt;
        Status = TransportOrderStatus.Advised;
    }

    /// <summary>The blind label / transport-unit id used to match advice with the induct scan.</summary>
    public string TuId { get; }

    public string LineId { get; private set; }

    public PandaLabelSet Labels { get; private set; }

    public TransportOrderStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PrintedAt { get; private set; }

    /// <summary>Replace the advised label set (Phase-1 duplicate-advice = overwrite / last-wins; spec §6a).</summary>
    public void OverwriteAdvice(string lineId, PandaLabelSet labels, DateTimeOffset advisedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentNullException.ThrowIfNull(labels);

        LineId = lineId;
        Labels = labels;
        CreatedAt = advisedAt;
        Status = TransportOrderStatus.Advised;
        PrintedAt = null;
    }

    public void MarkPrinted(DateTimeOffset printedAt)
    {
        Status = TransportOrderStatus.Printed;
        PrintedAt = printedAt;
    }
}
