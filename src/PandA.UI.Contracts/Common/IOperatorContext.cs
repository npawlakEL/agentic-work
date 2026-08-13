namespace PandA.UI.Contracts.Common;

/// <summary>
/// Supplies the identity of the operator currently driving the UI. Audited operator
/// actions (reprint authorization, overrides) attribute themselves to this name so the
/// audit trail reflects a real person rather than a hardcoded literal.
/// <para>
/// Host applications back this with their own auth/session context (SSO, badge login,
/// shift sign-in). The standalone demo backs it with a simple settable value the operator
/// picks from the app-bar selector. There is no RBAC yet (decision-019); this is identity
/// for attribution, not authorization.
/// </para>
/// </summary>
public interface IOperatorContext
{
    /// <summary>The current operator's display name / id, used for audit attribution.</summary>
    string CurrentOperator { get; }

    /// <summary>
    /// Sets the active operator. Host integrations may back this with a login/selector or
    /// restrict it; the standalone demo lets the operator switch freely (e.g. shift change).
    /// </summary>
    /// <param name="operatorName">Non-empty display name / id of the operator taking over.</param>
    void SetOperator(string operatorName);

    /// <summary>Raised after <see cref="CurrentOperator"/> changes, so UI chrome can refresh.</summary>
    event Action? OperatorChanged;
}
