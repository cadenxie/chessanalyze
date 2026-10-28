namespace ChessGameD.Web.Services.Engine;

public sealed class ChessOptions
{
    public EngineDownloadOptions Engine { get; set; } = new();
}

public sealed class EngineDownloadOptions
{
    /// <summary>
    /// 优先匹配的资产文件名。留空 = 按当前运行平台自动从 Release 里挑;
    /// 填了但与当前平台不匹配(例如在 Linux 上填了 Windows 包)时会被忽略。
    /// </summary>
    public string AssetName { get; set; } = "";

    /// <summary>
    /// 按平台的兜底下载地址,键为 <c>windows</c> / <c>linux</c> / <c>macos</c>;
    /// 还可以用 <c>default</c> 作为所有平台的通用兜底。
    /// </summary>
    public Dictionary<string, EngineFallbackOptions> Fallbacks { get; set; } = new();

    /// <summary>
    /// 旧版配置:不分平台的兜底地址。仅当地址里的资产名与当前平台匹配时才会被使用,
    /// 避免在 Linux 上误装 Windows 包。
    /// </summary>
    public string FallbackUrl { get; set; } = "";

    /// <summary>兜底地址对应的 SHA-256(十六进制,不带 "sha256:" 前缀)。</summary>
    public string FallbackSha256 { get; set; } = "";

    /// <summary>兜底版本标识,用于目录命名。</summary>
    public string FallbackVersion { get; set; } = "";

    /// <summary>
    /// 下载引擎时使用的代理(例如 <c>http://127.0.0.1:7897</c>)。
    /// 留空表示直连;设置页里的「下载代理」为空时用这里的值。
    /// </summary>
    public string ProxyUrl { get; set; } = "";

    /// <summary>下载停滞多少秒后判定为失败(直连 GitHub 在部分网络下会卡住不动)。</summary>
    public int StallTimeoutSeconds { get; set; } = 30;
}

/// <summary>某个平台的兜底下载配置。</summary>
public sealed class EngineFallbackOptions
{
    /// <summary>兜底下载地址(zip / tar.gz 均可)。</summary>
    public string Url { get; set; } = "";

    /// <summary>兜底地址对应的 SHA-256(十六进制,不带 "sha256:" 前缀)。</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>兜底版本标识,用于目录命名。</summary>
    public string Version { get; set; } = "";
}

public sealed class EngineInstallProgress
{
    public string Stage { get; set; } = "";
    public int Percent { get; set; }
    public string? Detail { get; set; }
}

public sealed class EngineInstallResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Version { get; set; }
    public string? EnginePath { get; set; }
    public string? Sha256 { get; set; }
    public long SizeBytes { get; set; }
    public string Source { get; set; } = "";

    /// <summary>安装时的运行平台(例如「Linux x64」)。</summary>
    public string Platform { get; set; } = "";
}
