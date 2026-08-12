using Axon.Utility.Service;
using PandA.UI;
using PandA.UI.DemoHost.Components;
using PandA.UI.DemoHost.Sim;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server + the Axon design system (registers MudBlazor services under the hood).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.UseAxonDesignSystem();

// Sim-backed implementations of the UI view-model contracts (no real backend).
builder.Services.AddPandaDemoBackend();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAntiforgery();

app.MapStaticAssets();

// Register the PandA.UI RCL assembly so its routable pages are discovered.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(UiAssemblyMarker).Assembly);

app.Run();

/// <summary>Exposed so the E2E test host (WebApplicationFactory) can boot this app.</summary>
public partial class Program;
