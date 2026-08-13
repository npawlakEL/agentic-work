namespace PandA.Core.Advice;

public static class WaveIdParser
{
    public static string? ParseFromFilename(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return null;
        }

        var marker = filename.IndexOf("LD", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var start = marker + 2;
        if (start >= filename.Length)
        {
            return null;
        }

        var end = filename.IndexOf('_', start);
        if (end < 0 || end == start)
        {
            return null;
        }

        return filename[start..end];
    }
}
