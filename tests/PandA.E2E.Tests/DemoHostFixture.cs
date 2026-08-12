using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace PandA.E2E.Tests;

/// <summary>
/// Boots the real <c>PandA.UI.DemoHost</c> Kestrel server on a free port and starts a
/// Playwright browser, so end-to-end tests exercise the app exactly as a browser would
/// (real HTTP, real Blazor Server circuit, real JS). Shared across the E2E collection.
/// </summary>
public sealed class DemoHostFixture : IAsyncLifetime
{
    private Process? _host;
    private IPlaywright? _playwright;

    public string BaseUrl { get; private set; } = "";
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}";

        var hostDll = LocateDemoHostDll();

        var psi = new ProcessStartInfo("dotnet", $"\"{hostDll}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(hostDll)!,
        };
        psi.Environment["ASPNETCORE_URLS"] = BaseUrl;
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        _host = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start DemoHost.");

        await WaitForReadyAsync(BaseUrl).ConfigureAwait(false);

        _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync().ConfigureAwait(false);
        }

        _playwright?.Dispose();

        if (_host is { HasExited: false })
        {
            _host.Kill(entireProcessTree: true);
            _host.WaitForExit(5000);
        }

        _host?.Dispose();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string LocateDemoHostDll()
    {
        // Walk up from the test output dir to the repo root, then to the DemoHost build output.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PandA.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate repository root (PandA.slnx).");
        }

        var candidates = Directory.GetFiles(
            Path.Combine(dir.FullName, "src", "PandA.UI.DemoHost", "bin"),
            "PandA.UI.DemoHost.dll",
            SearchOption.AllDirectories);

        return candidates.Length > 0
            ? candidates.OrderByDescending(File.GetLastWriteTimeUtc).First()
            : throw new InvalidOperationException("PandA.UI.DemoHost.dll not found; build the solution first.");
    }

    private static async Task WaitForReadyAsync(string baseUrl)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync(baseUrl).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // server not up yet
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        throw new TimeoutException($"DemoHost did not become ready at {baseUrl} within 60s.");
    }
}
