using PandA.Core;
using PandA.Sim;

namespace PandA.Tests;

public sealed class CartonAdviceServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTransportOrderStore _store = new();
    private readonly TestClock _clock = new(T0);
    private readonly CartonAdviceService _sut;

    public CartonAdviceServiceTests() => _sut = new CartonAdviceService(_store, _clock, new InMemorySettingsProvider());

    private static PandaLabelSet Labels(params string[] types) =>
        new(types.Select(t => new Label(t, $"LPN-{t}", $"^XA{t}^XZ")));

    [Fact]
    public async Task Advise_CreatesAdvisedShell_WithTuIdAndLabelSet()
    {
        var order = await _sut.AdviseAsync("L1", "BLIND1", Labels("Shipping", "Content"));

        Assert.Equal("BLIND1", order.TuId);
        Assert.Equal("L1", order.LineId);
        Assert.Equal(TransportOrderStatus.Advised, order.Status);
        Assert.Equal(T0, order.CreatedAt);
        Assert.Equal(["Shipping", "Content"], order.Labels.Labels.Select(l => l.LabelType));

        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Same(order, stored);
    }

    [Fact]
    public async Task DuplicateAdvice_OverwritesLabelSet_LastWins()
    {
        await _sut.AdviseAsync("L1", "BLIND1", Labels("Shipping"));
        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = await _sut.AdviseAsync("L1", "BLIND1", Labels("Content", "Return"));

        Assert.Single(_store.All); // still one TO for the blind label
        Assert.Equal(["Content", "Return"], second.Labels.Labels.Select(l => l.LabelType));
        Assert.Equal(T0.AddMinutes(5), second.CreatedAt);
        Assert.Equal(TransportOrderStatus.Advised, second.Status);
    }
}

