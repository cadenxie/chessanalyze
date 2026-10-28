using System.Formats.Tar;
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

    /// <summary>构造一个 tar.gz(Linux / macOS 官方包就是这种格式)。</summary>
    private static byte[] BuildTarGz(string entryName, bool executable = true)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionMode.Compress, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Ustar))
        {
            var entry = new UstarTarEntry(TarEntryType.RegularFile, entryName)
            {
                DataStream = new MemoryStream(Encoding.ASCII.GetBytes("fake engine binary")),
                Mode = executable
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite,
            };

            writer.WriteEntry(entry);
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

    private EngineInstaller CreateInstaller(
        TestDatabase db,
        Func<Uri, HttpResponseMessage> responder,
        EnginePlatform? platform = null,
        ChessOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("stockfish")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(responder));
        var provider = services.BuildServiceProvider();

        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
        var effectivePlatform = platform ?? EnginePlatform.Create(EngineOsKind.Windows, EngineArchKind.X64);
        var engineManager = new EngineManager(db.Settings, NullLogger<EngineManager>.Instance, effectivePlatform);

        var environment = new StubEnvironment { ContentRootPath = _root };

        return new EngineInstaller(
            httpClientFactory,
            db.Settings,
            engineManager,
            Options.Create(options ?? new ChessOptions()),
            environment,
            NullLogger<EngineInstaller>.Instance,
            effectivePlatform);
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
    public async Task Install_LinuxPlatform_ExtractsTarGzAndRegistersUnixBinary()
    {
        await using var db = new TestDatabase();
        var tarGz = BuildTarGz("stockfish/stockfish-linux-x86-64-universal");
        var hash = Sha256Of(tarGz);

        var installer = CreateInstaller(
            db,
            uri =>
            {
                if (uri.AbsolutePath.Contains("releases/latest"))
                {
                    return ReleasesJson(
                        "stockfish-linux-x86-64-universal.tar.gz",
                        "https://example.test/sf.tar.gz",
                        "sha256:" + hash);
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(tarGz) };
            },
            EnginePlatform.Create(EngineOsKind.Linux, EngineArchKind.X64));

        var result = await installer.InstallAsync(null);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.EnginePath);
        Assert.Equal("stockfish-linux-x86-64-universal", Path.GetFileName(result.EnginePath));
        Assert.True(File.Exists(result.EnginePath));

        var platformKey = await db.Settings.GetAsync(ChessGameD.Web.Data.SettingsStore.EnginePlatformKey);
        Assert.Equal("linux", platformKey);
    }

    [Fact]
    public async Task Install_LinuxPlatform_IgnoresWindowsAsset()
    {
        await using var db = new TestDatabase();
        var zip = BuildZip("stockfish.exe");
        var hash = Sha256Of(zip);

        var installer = CreateInstaller(
            db,
            uri =>
            {
                if (uri.AbsolutePath.Contains("releases/latest"))
                {
                    return ReleasesJson(
                        "stockfish-windows-x86-64-universal.zip",
                        "https://example.test/sf.zip",
                        "sha256:" + hash);
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
            },
            EnginePlatform.Create(EngineOsKind.Linux, EngineArchKind.X64));

        var result = await installer.InstallAsync(null);

        Assert.False(result.Success);
        Assert.Contains("没有找到适配", result.Error);
    }

    [Fact]
    public async Task Install_UnsupportedPlatform_FailsWithHint()
    {
        await using var db = new TestDatabase();

        var installer = CreateInstaller(
            db,
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            EnginePlatform.Create(EngineOsKind.Linux, EngineArchKind.X86));

        var result = await installer.InstallAsync(null);

        Assert.False(result.Success);
        Assert.Contains("没有官方 Stockfish 发行包", result.Error);
    }

    [Fact]
    public async Task Install_GitHubApiUnavailable_UsesPlatformFallback()
    {
        await using var db = new TestDatabase();
        var tarGz = BuildTarGz("stockfish/stockfish-linux-x86-64-universal");
        var hash = Sha256Of(tarGz);

        var options = new ChessOptions();
        options.Engine.Fallbacks["linux-x64"] = new EngineFallbackOptions
        {
            Url = "https://example.test/stockfish-linux-x86-64-universal.tar.gz",
            Sha256 = hash,
            Version = "sf_19",
        };

        var installer = CreateInstaller(
            db,
            uri =>
            {
                if (uri.AbsolutePath.Contains("releases/latest"))
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(tarGz) };
            },
            EnginePlatform.Create(EngineOsKind.Linux, EngineArchKind.X64),
            options);

        var result = await installer.InstallAsync(null);

        Assert.True(result.Success, result.Error);
        Assert.Equal("fallback", result.Source);
        Assert.Equal("stockfish-linux-x86-64-universal", Path.GetFileName(result.EnginePath));
    }

    [Fact]
    public async Task Install_LegacyFlatFallback_NotUsedOnOtherPlatform()
    {
        await using var db = new TestDatabase();

        var options = new ChessOptions();
        options.Engine.FallbackUrl = "https://example.test/stockfish-windows-x86-64-universal.zip";
        options.Engine.FallbackSha256 = new string('a', 64);
        options.Engine.FallbackVersion = "sf_19";

        var installer = CreateInstaller(
            db,
            uri => uri.AbsolutePath.Contains("releases/latest")
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(BuildZip("stockfish.exe")) },
            EnginePlatform.Create(EngineOsKind.Linux, EngineArchKind.X64),
            options);

        var result = await installer.InstallAsync(null);

        Assert.False(result.Success);
        Assert.Contains("无法获取 Stockfish 下载地址", result.Error);
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

    [Fact]
    public void FindEngineExecutable_LinuxPicksExtensionlessBinary()
    {
        var dir = Path.Combine(_root, "extract-unix");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "stockfish-windows-x86-64-universal.exe"), "x");
        File.WriteAllText(Path.Combine(dir, "stockfish-linux-x86-64-universal"), "x");

        var found = EngineInstaller.FindEngineExecutable(
            dir,
            EnginePlatform.Create(EngineOsKind.Linux, EngineArchKind.X64));

        Assert.NotNull(found);
        Assert.Equal("stockfish-linux-x86-64-universal", Path.GetFileName(found));
    }

    [Fact]
    public void DetectArchiveKindByContent_RecognizesZipAndTarGz()
    {
        var zipPath = Path.Combine(_root, "a.zip");
        File.WriteAllBytes(zipPath, BuildZip("stockfish.exe"));
        Assert.Equal(EngineArchiveKind.Zip, EngineInstaller.DetectArchiveKindByContent(zipPath));

        var tarPath = Path.Combine(_root, "a.tar.gz");
        File.WriteAllBytes(tarPath, BuildTarGz("stockfish/stockfish-linux-x86-64-universal"));
        Assert.Equal(EngineArchiveKind.TarGz, EngineInstaller.DetectArchiveKindByContent(tarPath));
    }
}
