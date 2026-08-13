using PandA.UI.Contracts.Common;

namespace PandA.UI.Tests;

/// <summary>Test double for the operator identity port, with a settable current operator.</summary>
public sealed class FakeOperatorContext(string initial = "test-op") : IOperatorContext
{
    public string CurrentOperator { get; private set; } = initial;

    public void SetOperator(string operatorName)
    {
        CurrentOperator = operatorName;
        OperatorChanged?.Invoke();
    }

    public event Action? OperatorChanged;
}
