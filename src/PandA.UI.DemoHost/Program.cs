using Axon.Utility.Service;
using PandA.UI;
using PandA.UI.DemoHost.Components;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server + the Axon design system (registers MudBlazor services under the hood).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.UseAxonDesignSystem();

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
