namespace ChessGameD.Web.Services.Engine;

public sealed class ChessOptions
{
    public EngineDownloadOptions Engine { get; set; } = new();
}

public sealed class EngineDownloadOptions
{
    /// <summary>优先匹配的资产文件名。</summary>
    public string AssetName { get; set; } = "stockfish-windows-x86-64-universal.zip";

    /// <summary>GitHub API 不可用(限流/断网)时的兜底下载地址。</summary>
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
}
