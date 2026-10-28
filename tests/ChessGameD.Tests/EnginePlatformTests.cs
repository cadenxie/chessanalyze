using ChessGameD.Web.Services.Engine;
using Xunit;

namespace ChessGameD.Tests;

public class EnginePlatformTests
{
    [Fact]
    public void Create_WindowsX64_UsesWindowsZip()
    {
        var platform = EnginePlatform.Create(EngineOsKind.Windows, EngineArchKind.X64);

        Assert.True(platform.IsSupported);
        Assert.Equal("stockfish-windows-x86-64-universal.zip", platform.AssetName);
        Assert.Equal(EngineArchiveKind.Zip, platform.Archive);
        Assert.Equal("windows", platform.Key);
        Assert.Equal("windows-x64", platform.FullKey);
    }

    [Fact]
    public void Create_LinuxX64_UsesTarGz()
    {
        var platform = EnginePlatform.Create(EngineOsKind.Linux, EngineArchKind.X64);

        Assert.True(platform.IsSupported);
        Assert.Equal("stockfish-linux-x86-64-universal.tar.gz", platform.AssetName);
        Assert.Equal(EngineArchiveKind.TarGz, platform.Archive);
        Assert.Equal("linux", platform.Key);
    }

    [Fact]
    public void Create_LinuxArm64_UsesArm64Package()
    {
        var platform = EnginePlatform.Create(EngineOsKind.Linux, EngineArchKind.Arm64);

        Assert.Equal("stockfish-linux-arm64-universal.tar.gz", platform.AssetName);
        Assert.Equal("linux-arm64", platform.FullKey);
    }

    [Fact]
    public void Create_MacOs_UsesUniversalPackage()
    {
        var platform = EnginePlatform.Create(EngineOsKind.MacOs, EngineArchKind.Arm64);

        Assert.True(platform.IsSupported);
        Assert.Equal("stockfish-macos-universal.tar.gz", platform.AssetName);
        Assert.Equal(EngineArchiveKind.TarGz, platform.Archive);
    }

    [Fact]
    public void Create_UnsupportedArchitecture_HasNoPackage()
    {
        var platform = EnginePlatform.Create(EngineOsKind.Linux, EngineArchKind.X86);

        Assert.False(platform.IsSupported);
        Assert.Equal(string.Empty, platform.AssetName);
    }

    [Fact]
    public void ScoreAssetName_LinuxX64_RejectsWindowsAndArm64Packages()
    {
        var platform = EnginePlatform.Create(EngineOsKind.Linux, EngineArchKind.X64);

        Assert.True(platform.ScoreAssetName("stockfish-linux-x86-64-universal.tar.gz") > 0);
        Assert.Equal(-1, platform.ScoreAssetName("stockfish-windows-x86-64-universal.zip"));
        Assert.Equal(-1, platform.ScoreAssetName("stockfish-linux-arm64-universal.tar.gz"));
        Assert.Equal(-1, platform.ScoreAssetName("stockfish-android-arm64-universal.tar.gz"));
        Assert.Equal(-1, platform.ScoreAssetName("stockfish-linux-riscv64-universal.tar.gz"));
    }

    [Fact]
    public void ScoreAssetName_WindowsX64_RejectsLinuxPackage()
    {
        var platform = EnginePlatform.Create(EngineOsKind.Windows, EngineArchKind.X64);

        Assert.True(platform.ScoreAssetName("stockfish-windows-x86-64-universal.zip") > 0);
        Assert.Equal(-1, platform.ScoreAssetName("stockfish-linux-x86-64-universal.tar.gz"));
        Assert.Equal(-1, platform.ScoreAssetName("stockfish-windows-arm64-universal.zip"));
    }

    [Fact]
    public void ScoreAssetName_ExactMatchBeatsOtherVariants()
    {
        var platform = EnginePlatform.Create(EngineOsKind.Linux, EngineArchKind.X64);

        var exact = platform.ScoreAssetName("stockfish-linux-x86-64-universal.tar.gz");
        var other = platform.ScoreAssetName("stockfish-linux-x86-64-avx2.tar.gz");

        Assert.True(exact > other);
    }

    [Fact]
    public void DetectArchiveKind_RecognizesExtensions()
    {
        Assert.Equal(EngineArchiveKind.Zip, EnginePlatform.DetectArchiveKind("a.zip"));
        Assert.Equal(EngineArchiveKind.TarGz, EnginePlatform.DetectArchiveKind("a.tar.gz"));
        Assert.Equal(EngineArchiveKind.TarGz, EnginePlatform.DetectArchiveKind("a.tgz"));
        Assert.Equal(EngineArchiveKind.Unknown, EnginePlatform.DetectArchiveKind("a.bin"));
    }

    [Fact]
    public void Detect_MatchesCurrentRuntime()
    {
        var platform = EnginePlatform.Detect();

        var expectedOs = OperatingSystem.IsWindows()
            ? EngineOsKind.Windows
            : OperatingSystem.IsLinux()
                ? EngineOsKind.Linux
                : OperatingSystem.IsMacOS()
                    ? EngineOsKind.MacOs
                    : EngineOsKind.Unknown;

        Assert.Equal(expectedOs, platform.Os);
        Assert.False(string.IsNullOrEmpty(platform.DisplayName));
    }
}
