using PandA.UI.Contracts.Common;
using PandA.UI.Contracts.Config;
using PandA.UI.Contracts.Lookup;
using PandA.UI.Contracts.Manda;
using PandA.UI.Contracts.Rejects;
using PandA.UI.Contracts.Reprint;
using PandA.UI.Contracts.Status;
using PandA.Sim.Line;
using PandA.UI.Shell;

namespace PandA.UI.DemoHost.Sim;

/// <summary>Registers the Sim-backed implementations of every UI view-model contract.</summary>
public static class DemoServiceCollectionExtensions
{
    public static IServiceCollection AddPandaDemoBackend(this IServiceCollection services)
    {
        services.AddSingleton<DemoDataStore>();
        services.AddSingleton<SimLineCatalog>();
        services.AddSingleton<MessageConsoleService>();
        services.AddSingleton<LineSimulation>(sp => LineSimulationFactory.Create(sp.GetRequiredService<DemoDataStore>()));

        // Operator identity — scoped per Blazor circuit so each connected operator is distinct.
        services.AddScoped<IOperatorContext, DemoOperatorContext>();

        // Host-specific nav entry for the DemoHost-owned Line Simulator page.
        services.AddSingleton<INavExtras, DemoNavExtras>();

        // Status streams (one instance implements both feeds).
        services.AddSingleton<DemoStatusStreams>();
        services.AddSingleton<ILineStatusStream>(sp => sp.GetRequiredService<DemoStatusStreams>());
        services.AddSingleton<IPrinterStatusStream>(sp => sp.GetRequiredService<DemoStatusStreams>());

        // Lookup (one instance implements both queries).
        services.AddSingleton<DemoLookupQueries>();
        services.AddSingleton<ITransportOrderQuery>(sp => sp.GetRequiredService<DemoLookupQueries>());
        services.AddSingleton<ICartonLabelDetailQuery>(sp => sp.GetRequiredService<DemoLookupQueries>());

        services.AddSingleton<IReprintAuthorizationCommand, DemoReprintAuthorization>();
        services.AddSingleton<IRejectCartonQuery, DemoRejectCartonQuery>();

        // MandA (one instance implements the four commands + stream).
        services.AddSingleton<DemoMandaServices>();
        services.AddSingleton<IMandaStationQuery>(sp => sp.GetRequiredService<DemoMandaServices>());
        services.AddSingleton<IMandaScanCommand>(sp => sp.GetRequiredService<DemoMandaServices>());
        services.AddSingleton<IMandaPrintCommand>(sp => sp.GetRequiredService<DemoMandaServices>());
        services.AddSingleton<IMandaVerifyCommand>(sp => sp.GetRequiredService<DemoMandaServices>());
        services.AddSingleton<IMandaStationStream>(sp => sp.GetRequiredService<DemoMandaServices>());

        // Config Explorer.
        services.AddSingleton<IConfigTreeQuery, DemoConfigTreeQuery>();
        services.AddSingleton<ISettingsEditor, DemoSettingsEditor>();
        services.AddSingleton<ILabelDefEditor, DemoLabelDefEditor>();
        services.AddSingleton<IOrientationEditor, DemoOrientationEditor>();
        services.AddSingleton<IMandaStationEditor, DemoMandaStationEditor>();
        services.AddSingleton<ILineEditor, DemoLineEditor>();
        services.AddSingleton<IPrinterEditor, DemoPrinterEditor>();
        services.AddSingleton<IFirePointEditor, DemoFirePointEditor>();
        services.AddSingleton<IMapEditor, DemoMapEditor>();

        return services;
    }
}
