using PandA.Core.Induct;

namespace PandA.Sim;

public sealed class SimMinGapProvider : IMinGapProvider
{
    public const int SourceDefaultMinGap = 20;

    private readonly int _minGap;

    public SimMinGapProvider(int minGap = SourceDefaultMinGap)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minGap);
        _minGap = minGap;
    }

    public int GetMinGap() => _minGap;
}
