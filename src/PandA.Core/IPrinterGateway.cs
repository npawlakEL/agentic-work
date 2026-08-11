namespace PandA.Core;

/// <summary>A single ZPL payload destined for one printer.</summary>
/// <param name="PrinterId">Chosen printer.</param>
/// <param name="Ip">Printer host.</param>
/// <param name="Port">Printer TCP port.</param>
/// <param name="LabelType">The label type being printed.</param>
/// <param name="Lpn">The label's barcode / license-plate number.</param>
/// <param name="Zpl">Ready-to-print ZPL payload.</param>
public sealed record PrintJob(string PrinterId, string Ip, int Port, string LabelType, string Lpn, string Zpl);

/// <summary>
/// Egress port to a physical printer. The Sim captures jobs; the econtroller adapter maps to the outbound
/// telegram / TCP connector at integration (decision-002).
/// </summary>
public interface IPrinterGateway
{
    ValueTask SendAsync(PrintJob job, CancellationToken cancellationToken = default);
}
