using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Engine;

namespace ChessGameD.Web.Models;

public sealed class GameListItem
{
    public int Id { get; set; }
    public string White { get; set; } = "";
    public string Black { get; set; } = "";
    public string Result { get; set; } = "*";
    public string? Date { get; set; }
    public string? Event { get; set; }
    public string? Opening { get; set; }
    public string? Eco { get; set; }
    public int? WhiteElo { get; set; }
    public int? BlackElo { get; set; }
    public int PlyCount { get; set; }
    public bool HasAnalysis { get; set; }
    public DateTime ImportedAt { get; set; }
}

public sealed class HomeViewModel
{
    public EngineStatus Engine { get; set; } = new();
    public IReadOnlyList<GameListItem> RecentGames { get; set; } = Array.Empty<GameListItem>();
    public int TotalGames { get; set; }

    /// <summary>当前用户是不是管理员(决定能不能进设置页)。</summary>
    public bool IsAdmin { get; set; }
}

public sealed class GameListViewModel
{
    public IReadOnlyList<GameListItem> Games { get; set; } = Array.Empty<GameListItem>();
    public string? Query { get; set; }
    public int Total { get; set; }
}

public sealed record ReviewMove(int Ply, string San, string? Uci, string Fen, string? Comment);

public sealed class ReviewViewModel
{
    public int GameId { get; set; }
    public string White { get; set; } = "";
    public string Black { get; set; } = "";
    public string Result { get; set; } = "*";
    public string? Date { get; set; }
    public string? Event { get; set; }
    public string? Site { get; set; }
    public string? Opening { get; set; }
    public string? Eco { get; set; }
    public string? TimeControl { get; set; }
    public string? Termination { get; set; }
    public int? WhiteElo { get; set; }
    public int? BlackElo { get; set; }
    public int PlyCount { get; set; }
    public string PgnText { get; set; } = "";
    public IReadOnlyList<ReviewMove> Moves { get; set; } = Array.Empty<ReviewMove>();
    public bool EngineInstalled { get; set; }
    public string? EngineVersion { get; set; }
}

public sealed class SettingsViewModel
{
    public EngineStatus Engine { get; set; } = new();
    public string? EnginePath { get; set; }
    public int Threads { get; set; }
    public int HashMb { get; set; }
    public int MultiPv { get; set; }
    public int DefaultDepth { get; set; }
    public int AnalysisDepth { get; set; }
    public string? EngineInstalledAt { get; set; }

    /// <summary>下载引擎时使用的代理(空 = 直连)。</summary>
    public string? ProxyUrl { get; set; }

    /// <summary>当前运行平台,例如「Linux x64」。</summary>
    public string PlatformName { get; set; } = "";

    /// <summary>当前平台对应的官方发行包文件名(为空 = 没有官方包)。</summary>
    public string PlatformAssetName { get; set; } = "";

    /// <summary>当前平台是否有官方发行包可以一键安装。</summary>
    public bool PlatformSupported { get; set; }
}

public sealed record InstantAnalysisResponse(
    int? ScoreCp,
    int? ScoreMate,
    int Depth,
    long Nodes,
    int Nps,
    int TimeMs,
    IReadOnlyList<string> Pv,
    IReadOnlyList<MultiPvLine> Lines,
    string BestMove);
