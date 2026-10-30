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

    /// <summary>开局局面 FEN;棋盘从 ply 0 的局面开始摆。</summary>
    public string InitialFen { get; set; } = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
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

    /// <summary>每局指导对弈的提示次数。</summary>
    public int PlayHintQuota { get; set; } = 3;

    /// <summary>每局指导对弈的悔棋次数。</summary>
    public int PlayTakebackQuota { get; set; } = 3;
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

/// <summary>一个陪练难度档位。</summary>
public sealed class CoachLevelOption
{
    public int Level { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>指导对弈页面的视图模型。</summary>
public sealed class PlayViewModel
{
    /// <summary>引擎是否已经装好(没装就没法下)。</summary>
    public bool EngineInstalled { get; set; }

    public string? EngineVersion { get; set; }

    /// <summary>默认难度档位。</summary>
    public int DefaultLevel { get; set; } = 2;

    /// <summary>每局默认的提示次数。</summary>
    public int HintQuota { get; set; } = 3;

    /// <summary>每局默认的悔棋次数。</summary>
    public int TakebackQuota { get; set; } = 3;

    public IReadOnlyList<CoachLevelOption> Levels { get; set; } = Array.Empty<CoachLevelOption>();
}
