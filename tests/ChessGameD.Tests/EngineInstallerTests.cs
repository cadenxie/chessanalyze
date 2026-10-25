using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ChessGameD.Web.Services.Engine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChessGameD.Tests;

public class EngineInstallerTests : IDisposable
{
    private readonly string _root;

    public EngineInstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "chessgamed-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static byte[] BuildZip(string entryName)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var stream = entry.Open();
            var payload = Encoding.ASCII.GetBytes("fake engine binary");
            stream.Write(payload, 0, payload.Length);
        }

        return buffer.ToArray();
    }

    private static string Sha256Of(byte[] data)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<Uri, HttpResponseMessage> _responder;

        public StubHandler(Func<Uri, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request.RequestUri!));
    }

    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "ChessGameD.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
    }

    private EngineInstaller CreateInstaller(TestDatabase db, Func<Uri, HttpResponseMessage> responder)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("stockfish")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(responder));
        var provider = services.BuildServiceProvider();

        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
        var engineManager = new EngineManager(db.Settings, NullLogger<EngineManager>.Instance);

        var environment = new StubEnvironment { ContentRootPath = _root };

        return new EngineInstaller(
            httpClientFactory,
            db.Settings,
            engineManager,
            Options.Create(new ChessOptions()),
            environment,
            NullLogger<EngineInstaller>.Instance);
    }

    private static HttpResponseMessage ReleasesJson(string assetName, string url, string? digest)
    {
        var digestJson = digest is null ? "null" : $"\"{digest}\"";
        var json = $$"""
        {
          "tag_name": "sf_19",
          "assets": [
            {
              "name": "{{assetName}}",
              "browser_download_url": "{{url}}",
              "digest": {{digestJson}}
            }
          ]
        }
        """;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task Install_Sha256Mismatch_FailsAndLeavesNothingInstalled()
    {
        await using var db = new TestDatabase();
        var zip = BuildZip("stockfish.exe");

        var installer = CreateInstaller(db, uri =>
        {
            if (uri.AbsolutePath.Contains("releases/latest"))
            {
                return ReleasesJson("stockfish-windows-x86-64-universal.zip", "https://example.test/sf.zip", "sha256:" + new string('a', 64));
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        });

        var result = await installer.InstallAsync(null);

        Assert.False(result.Success);
        Assert.Contains("SHA-256", result.Error);

        var installed = await db.Settings.GetAsync(ChessGameD.Web.Data.SettingsStore.EnginePath);
        Assert.True(string.IsNullOrEmpty(installed));
    }

    [Fact]
    public async Task Install_ValidHash_ExtractsAndRegistersExecutable()
    {
        await using var db = new TestDatabase();
        var zip = BuildZip("stockfish.exe");
        var hash = Sha256Of(zip);

        var installer = CreateInstaller(db, uri =>
        {
            if (uri.AbsolutePath.Contains("releases/latest"))
            {
                return ReleasesJson(
                    "stockfish-windows-x86-64-universal.zip",
                    "https://example.test/sf.zip",
                    "sha256:" + hash);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        });

        var result = await installer.InstallAsync(null);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.EnginePath);
        Assert.True(File.Exists(result.EnginePath));
        Assert.Equal(hash, result.Sha256);
        Assert.Equal("sf_19", result.Version);

        var registered = await db.Settings.GetAsync(ChessGameD.Web.Data.SettingsStore.EnginePath);
        Assert.Equal(result.EnginePath, registered);
    }

    [Fact]
    public void FindEngineExecutable_PrefersStockfishExe()
    {
        var dir = Path.Combine(_root, "extract");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "stockfish.exe"), "x");
        File.WriteAllText(Path.Combine(dir, "stockfish-nnue.exe"), "x");

        var found = EngineInstaller.FindEngineExecutable(dir);

        Assert.NotNull(found);
        Assert.Equal("stockfish.exe", Path.GetFileName(found));
    }

    [Fact]
    public void FindEngineExecutable_ReturnsNullWhenMissing()
    {
        var dir = Path.Combine(_root, "empty");
        Directory.CreateDirectory(dir);

        Assert.Null(EngineInstaller.FindEngineExecutable(dir));
    }
}
