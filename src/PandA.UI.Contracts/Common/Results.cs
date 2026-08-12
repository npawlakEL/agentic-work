namespace PandA.UI.Contracts.Common;

/// <summary>A page of results from a filtered/paged query.</summary>
/// <typeparam name="T">Row type.</typeparam>
/// <param name="Items">The rows on this page.</param>
/// <param name="TotalCount">Total rows matching the filter across all pages.</param>
/// <param name="Page">Zero-based page index.</param>
/// <param name="PageSize">Requested page size.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public static PagedResult<T> Empty(int pageSize) => new([], 0, 0, pageSize);
}

/// <summary>Outcome of a command (Save/Delete/Authorize/etc.).</summary>
/// <param name="Success">True when the command succeeded.</param>
/// <param name="Message">Human-readable detail (error text on failure, confirmation on success).</param>
public sealed record CommandResult(bool Success, string? Message = null)
{
    public static CommandResult Ok(string? message = null) => new(true, message);

    public static CommandResult Fail(string message) => new(false, message);
}
