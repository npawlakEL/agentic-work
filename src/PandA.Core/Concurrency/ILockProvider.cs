namespace PandA.Core.Concurrency;

/// <summary>
/// Cross-host mutual exclusion over a named resource, implemented as a <b>lease</b> in the shared database
/// (decision-010). PandA runs as several hosts/processes over one eController database and any host may process
/// any line or carton at any time, so every implementation MUST be shared by all hosts — a process-local
/// implementation is never correct in production.
/// <para>
/// Semantics every adapter must honor:
/// <list type="bullet">
/// <item>At most one <b>unexpired</b> lease exists per resource name across all hosts.</item>
/// <item>Each acquisition gets a unique owner token; leases are <b>not reentrant</b> (a second acquire of the
/// same resource from the same host waits like any other contender).</item>
/// <item>A lease expires <see cref="ILease.ExpiresUtc"/> after acquisition/renewal so a crashed holder cannot
/// block the resource forever; an expired lease may be taken over by any contender.</item>
/// <item>Release (dispose) only removes the lease when it is still owned by the releasing token.</item>
/// <item>When the lease cannot be obtained within the timeout, <see cref="LockTimeoutException"/> is thrown;
/// each call site decides its fail-safe behavior (decision-010 §timeouts).</item>
/// </list>
/// </para>
/// </summary>
public interface ILockProvider
{
    /// <summary>
    /// Acquire the lease on <paramref name="resourceName"/>, waiting up to <paramref name="timeout"/>
    /// (null → <see cref="LockDefaults.AcquireTimeout"/>, the source <c>sdisp_PA_Lock</c> 60 s wait).
    /// </summary>
    /// <exception cref="LockTimeoutException">The lease was not obtained within the timeout.</exception>
    ValueTask<ILease> AcquireAsync(
        string resourceName,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

/// <summary>A held lease. Disposing releases it (only if still owned by this token).</summary>
public interface ILease : IAsyncDisposable
{
    /// <summary>The leased resource name.</summary>
    string Resource { get; }

    /// <summary>The unique owner token of this acquisition (host id + per-acquisition nonce).</summary>
    string OwnerId { get; }

    /// <summary>When the lease lapses unless renewed.</summary>
    DateTimeOffset ExpiresUtc { get; }

    /// <summary>
    /// Extend the lease by the provider's lease duration. Returns <c>false</c> when the lease was lost (it
    /// expired and another owner took it over) — the caller must then stop and fail safe. Callers use this as
    /// a fencing check immediately before an irreversible side effect (e.g. dispatching a print).
    /// </summary>
    ValueTask<bool> RenewAsync(CancellationToken cancellationToken = default);
}

/// <summary>Defaults shared by lease providers.</summary>
public static class LockDefaults
{
    /// <summary>Source <c>sdisp_PA_Lock</c> default wait.</summary>
    public static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Default lease lifetime before an un-renewed lease may be taken over (crashed-holder recovery).</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
}

/// <summary>
/// The canonical lease names (decision-010). Every writer of a resource takes the same name so writers
/// serialize across hosts.
/// </summary>
public static class LockNames
{
    /// <summary>
    /// <c>Carton_{TUID}</c> — taken by EVERY <see cref="TransportOrder"/> writer (advice, induct, verify,
    /// PLC tracking events, reprint authorization, exception-carton creation). Case-insensitive on the TuId.
    /// </summary>
    public static string Carton(string tuId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tuId);
        return "Carton_" + tuId.ToUpperInvariant();
    }

    /// <summary>
    /// <c>PrinterState_{LINEID}</c> — taken by lane evaluation and every printer status / spare mutation of
    /// the line, so promote/demote decisions over the printer group serialize across hosts.
    /// </summary>
    public static string PrinterState(string lineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        return "PrinterState_" + lineId.ToUpperInvariant();
    }
}
