using PandA.UI.Contracts.Common;

namespace PandA.UI.Contracts.Config;

/// <summary>Reads and saves the single global settings record.</summary>
public interface ISettingsEditor
{
    Task<SettingsDto> GetAsync(CancellationToken ct = default);

    Task<CommandResult> SaveAsync(SettingsDto settings, CancellationToken ct = default);
}

/// <summary>
/// Standard CRUD editor for a config entity. Implementations back it with Core (econtroller)
/// or the Sim (demo host). A null identifier on <paramref name="entity"/> means create.
/// </summary>
/// <typeparam name="TDto">Entity DTO type.</typeparam>
public interface IConfigEditor<TDto>
{
    Task<IReadOnlyList<TDto>> ListAsync(CancellationToken ct = default);

    Task<TDto?> GetAsync(string id, CancellationToken ct = default);

    Task<CommandResult> SaveAsync(TDto entity, CancellationToken ct = default);

    Task<CommandResult> DeleteAsync(string id, CancellationToken ct = default);
}

/// <summary>CRUD for label definitions.</summary>
public interface ILabelDefEditor : IConfigEditor<LabelDefDto>;

/// <summary>CRUD for MandA stations.</summary>
public interface IMandaStationEditor : IConfigEditor<MandaStationDto>;

/// <summary>CRUD for lines.</summary>
public interface ILineEditor : IConfigEditor<LineDto>;

/// <summary>CRUD for printers. <see cref="ListForLineAsync"/> scopes to one line.</summary>
public interface IPrinterEditor : IConfigEditor<PrinterDto>
{
    Task<IReadOnlyList<PrinterDto>> ListForLineAsync(string lineId, CancellationToken ct = default);
}

/// <summary>CRUD for fire points. <see cref="ListForPrinterAsync"/> scopes to one printer.</summary>
public interface IFirePointEditor : IConfigEditor<FirePointDto>
{
    Task<IReadOnlyList<FirePointDto>> ListForPrinterAsync(string printerId, CancellationToken ct = default);
}

/// <summary>CRUD for maps. <see cref="ListForLineAsync"/> scopes to one line.</summary>
public interface IMapEditor : IConfigEditor<MapDto>
{
    Task<IReadOnlyList<MapDto>> ListForLineAsync(string lineId, CancellationToken ct = default);
}
