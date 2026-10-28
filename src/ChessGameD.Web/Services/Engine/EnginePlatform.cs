using System.Runtime.InteropServices;

namespace ChessGameD.Web.Services.Engine;

/// <summary>Stockfish 官方发行包对应的操作系统。</summary>
public enum EngineOsKind
{
    /// <summary>未识别的系统。</summary>
    Unknown = 0,

    /// <summary>Windows。</summary>
    Windows,

    /// <summary>Linux。</summary>
    Linux,

    /// <summary>macOS。</summary>
    MacOs,
}

/// <summary>CPU 架构。</summary>
public enum EngineArchKind
{
    /// <summary>未识别的架构。</summary>
    Unknown = 0,

    /// <summary>x86-64 / AMD64。</summary>
    X64,

    /// <summary>ARM64(AArch64)。</summary>
    Arm64,

    /// <summary>32 位 x86。</summary>
    X86,
}

/// <summary>官方发行包的压缩格式。</summary>
public enum EngineArchiveKind
{
    /// <summary>无法识别的格式。</summary>
    Unknown = 0,

    /// <summary>zip(Windows 官方包)。</summary>
    Zip,

    /// <summary>tar.gz(Linux / macOS 官方包)。</summary>
    TarGz,
}

/// <summary>
/// 当前运行环境对应的 Stockfish 发行包信息。
///
/// 官方 Release 的资产命名规则是 <c>stockfish-{系统}-{架构}-{变体}.{扩展名}</c>:
/// <list type="bullet">
///   <item><description><c>stockfish-windows-x86-64-universal.zip</c></description></item>
///   <item><description><c>stockfish-windows-arm64-universal.zip</c></description></item>
///   <item><description><c>stockfish-linux-x86-64-universal.tar.gz</c></description></item>
///   <item><description><c>stockfish-linux-arm64-universal.tar.gz</c></description></item>
///   <item><description><c>stockfish-macos-universal.tar.gz</c></description></item>
/// </list>
///
/// 注意 Windows 官方包是 <c>.zip</c>,而 Linux / macOS 官方包是 <c>.tar.gz</c>,
/// 解压时必须分开处理。
/// </summary>
public sealed class EnginePlatform
{
    private EnginePlatform(EngineOsKind os, EngineArchKind arch, string assetName, EngineArchiveKind archive)
    {
        Os = os;
        Arch = arch;
        AssetName = assetName;
        Archive = archive;
    }

    /// <summary>操作系统。</summary>
    public EngineOsKind Os { get; }

    /// <summary>CPU 架构。</summary>
    public EngineArchKind Arch { get; }

    /// <summary>该平台对应的官方资产名(没有官方包时为空)。</summary>
    public string AssetName { get; }

    /// <summary>该平台对应的官方压缩格式。</summary>
    public EngineArchiveKind Archive { get; }

    /// <summary>是否有官方发行包可以一键安装。</summary>
    public bool IsSupported => Archive != EngineArchiveKind.Unknown && AssetName.Length > 0;

    /// <summary>平台键名(用于配置查找,例如配置里的 <c>windows</c> / <c>linux</c> 兜底地址)。</summary>
    public string Key => Os switch
    {
        EngineOsKind.Windows => "windows",
        EngineOsKind.Linux => "linux",
        EngineOsKind.MacOs => "macos",
        _ => "unknown",
    };

    /// <summary>架构键名(x64 / arm64),与 <see cref="Key"/> 拼起来可以定位到具体平台的配置。</summary>
    public string ArchKey => Arch switch
    {
        EngineArchKind.X64 => "x64",
        EngineArchKind.Arm64 => "arm64",
        EngineArchKind.X86 => "x86",
        _ => "unknown",
    };

    /// <summary>带架构的平台键,例如 <c>linux-x64</c>。</summary>
    public string FullKey => $"{Key}-{ArchKey}";

    /// <summary>操作系统展示名。</summary>
    public string OsName => Os switch
    {
        EngineOsKind.Windows => "Windows",
        EngineOsKind.Linux => "Linux",
        EngineOsKind.MacOs => "macOS",
        _ => "未知系统",
    };

    /// <summary>架构展示名。</summary>
    public string ArchName => Arch switch
    {
        EngineArchKind.X64 => "x64",
        EngineArchKind.Arm64 => "ARM64",
        EngineArchKind.X86 => "x86",
        _ => "未知架构",
    };

    /// <summary>平台展示名,例如「Windows x64」。</summary>
    public string DisplayName => $"{OsName} {ArchName}";

    /// <summary>解压后引擎可执行文件的文件名提示(用于设置页提示)。</summary>
    public string ExecutableName => Os == EngineOsKind.Windows
        ? "stockfish-windows-x86-64-universal.exe"
        : AssetName.Length > 0
            ? Path.GetFileNameWithoutExtension(AssetName)
            : "stockfish";

    /// <summary>检测当前运行平台。</summary>
    public static EnginePlatform Detect() => Create(CurrentOs(), CurrentArch());

    /// <summary>按给定的系统与架构创建平台描述(测试用)。</summary>
    public static EnginePlatform Create(EngineOsKind os, EngineArchKind arch)
    {
        var (asset, archive) = (os, arch) switch
        {
            (EngineOsKind.Windows, EngineArchKind.X64) =>
                ("stockfish-windows-x86-64-universal.zip", EngineArchiveKind.Zip),
            (EngineOsKind.Windows, EngineArchKind.Arm64) =>
                ("stockfish-windows-arm64-universal.zip", EngineArchiveKind.Zip),
            (EngineOsKind.Linux, EngineArchKind.X64) =>
                ("stockfish-linux-x86-64-universal.tar.gz", EngineArchiveKind.TarGz),
            (EngineOsKind.Linux, EngineArchKind.Arm64) =>
                ("stockfish-linux-arm64-universal.tar.gz", EngineArchiveKind.TarGz),
            (EngineOsKind.MacOs, _) =>
                ("stockfish-macos-universal.tar.gz", EngineArchiveKind.TarGz),
            _ => ("", EngineArchiveKind.Unknown),
        };

        return new EnginePlatform(os, arch, asset, archive);
    }

    /// <summary>
    /// 给某个 Release 资产名打分:分数越高越贴合当前平台,-1 表示不适用。
    /// 用来在没有配置死资产名时,从 Release 列表里挑出最合适的一个。
    /// </summary>
    public int ScoreAssetName(string? assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return -1;
        }

        var name = assetName.ToLowerInvariant();
        if (!name.StartsWith("stockfish-", StringComparison.Ordinal))
        {
            return -1;
        }

        var osToken = Os switch
        {
            EngineOsKind.Windows => "windows",
            EngineOsKind.Linux => "linux",
            EngineOsKind.MacOs => "macos",
            _ => null,
        };

        if (osToken is null || !name.Contains(osToken, StringComparison.Ordinal))
        {
            return -1;
        }

        // Android 包不是桌面引擎,排除
        if (name.Contains("android", StringComparison.Ordinal))
        {
            return -1;
        }

        var archToken = Arch switch
        {
            EngineArchKind.X64 => "x86-64",
            EngineArchKind.Arm64 => "arm64",
            _ => null,
        };

        if (archToken is not null && !name.Contains(archToken, StringComparison.Ordinal))
        {
            return -1;
        }

        var archive = DetectArchiveKind(assetName);
        if (archive == EngineArchiveKind.Unknown)
        {
            return -1;
        }

        var score = 100;
        if (name.Contains("universal", StringComparison.Ordinal))
        {
            score += 50;
        }

        if (archive == Archive)
        {
            score += 20;
        }

        if (assetName.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }

        return score;
    }

    /// <summary>按文件名后缀判断压缩格式。</summary>
    public static EngineArchiveKind DetectArchiveKind(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        if (name.EndsWith(".zip", StringComparison.Ordinal))
        {
            return EngineArchiveKind.Zip;
        }

        if (name.EndsWith(".tar.gz", StringComparison.Ordinal)
            || name.EndsWith(".tgz", StringComparison.Ordinal)
            || name.EndsWith(".tar", StringComparison.Ordinal))
        {
            return EngineArchiveKind.TarGz;
        }

        return EngineArchiveKind.Unknown;
    }

    private static EngineOsKind CurrentOs()
    {
        if (OperatingSystem.IsWindows())
        {
            return EngineOsKind.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return EngineOsKind.Linux;
        }

        return OperatingSystem.IsMacOS() ? EngineOsKind.MacOs : EngineOsKind.Unknown;
    }

    private static EngineArchKind CurrentArch() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => EngineArchKind.X64,
        Architecture.Arm64 => EngineArchKind.Arm64,
        Architecture.X86 => EngineArchKind.X86,
        _ => EngineArchKind.Unknown,
    };
}
