using PandA.UI.Contracts.Common;

namespace PandA.UI.DemoHost.Sim;

/// <summary>
/// Demo-backed operator identity: a simple settable value the operator picks from the
/// app-bar selector. A real host would back <see cref="IOperatorContext"/> with its auth
/// session instead. Scoped per Blazor circuit so each connected operator has their own.
/// </summary>
public sealed class DemoOperatorContext : IOperatorContext
{
    private string _current = "unassigned";

    public string CurrentOperator => _current;

    /// <summary>A few sample operators the demo selector offers.</summary>
    public static IReadOnlyList<string> SampleOperators { get; } =
        ["A. Rivera", "J. Chen", "M. Okoro", "S. Patel", "unassigned"];

    public void SetOperator(string operatorName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorName);
        var next = operatorName.Trim();
        if (string.Equals(next, _current, StringComparison.Ordinal))
        {
            return;
        }

        _current = next;
        OperatorChanged?.Invoke();
    }

    public event Action? OperatorChanged;
}
