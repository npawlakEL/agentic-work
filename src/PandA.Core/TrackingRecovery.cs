namespace PandA.Core;

/// <summary>Outcome of a <see cref="TransportOrder.ResetForTrackingEvent"/> attempt (F15/decision-009).</summary>
public enum TrackingRecovery
{
    /// <summary>The carton was not eligible (not in <c>Printed</c> state); nothing changed. Idempotent.</summary>
    NotApplicable = 0,

    /// <summary>Reprint allowed: the carton was system-re-armed back to <c>Advised</c>.</summary>
    ReArmed = 1,

    /// <summary>Reprint disabled: the carton was held for operator intervention instead of re-armed.</summary>
    Held = 2,
}
