namespace PandA.UI.Contracts.Config;

/// <summary>The kind of entity a config tree node represents.</summary>
public enum ConfigNodeKind
{
    Root,
    SettingsGroup,
    LabelDefinitions,
    LabelDefinition,
    MandaStations,
    MandaStation,
    Lines,
    Line,
    Printers,
    Printer,
    FirePoints,
    FirePoint,
    Maps,
    Map,
}

/// <summary>
/// A node in the Config Explorer's left tree. Children are pre-expanded lazily by the host;
/// <see cref="EntityId"/> keys the detail-edit panel to the right entity.
/// </summary>
/// <param name="Kind">Entity kind.</param>
/// <param name="Label">Display label.</param>
/// <param name="EntityId">Identifier of the underlying entity (null for grouping nodes).</param>
/// <param name="Children">Child nodes.</param>
public sealed record ConfigTreeNode(
    ConfigNodeKind Kind,
    string Label,
    string? EntityId,
    IReadOnlyList<ConfigTreeNode> Children)
{
    public static ConfigTreeNode Group(ConfigNodeKind kind, string label, IReadOnlyList<ConfigTreeNode> children) =>
        new(kind, label, null, children);
}

/// <summary>Builds the Config Explorer entity tree (System → Settings/Labels/MandA/Lines…).</summary>
public interface IConfigTreeQuery
{
    Task<ConfigTreeNode> GetTreeAsync(CancellationToken ct = default);
}
