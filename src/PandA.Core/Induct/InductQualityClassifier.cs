namespace PandA.Core.Induct;

public static class InductQualityClassifier
{
    public static CartonStatus Classify(string? blindLabel, int gap, int minGap)
    {
        var label = blindLabel ?? string.Empty;

        // Source: sdisp_PA_LookupCarton.sql lines 208-235; ordered IF/ELSE chain, first match wins.
        if (label.Contains('?', StringComparison.Ordinal) || label == "-" || label == "<")
        {
            return CartonStatus.NoRead;
        }

        if (label.Contains('!', StringComparison.Ordinal) || label == "0")
        {
            return CartonStatus.NoData;
        }

        if (label.Contains('#', StringComparison.Ordinal))
        {
            return CartonStatus.LabelConflict;
        }

        return gap < minGap ? CartonStatus.GapError : CartonStatus.PrintReady;
    }
}
