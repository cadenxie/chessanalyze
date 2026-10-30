using System.ComponentModel.DataAnnotations;

namespace ChessGameD.Web.Data;

/// <summary>一局棋。表名 Games。</summary>
public class Game
{
    public int Id { get; set; }

    [MaxLength(200)] public string? Event { get; set; }
    [MaxLength(200)] public string? Site { get; set; }
    [MaxLength(50)] public string? Round { get; set; }
    [MaxLength(50)] public string? Date { get; set; }
    [MaxLength(100)] public string? Termination { get; set; }
    [MaxLength(50)] public string? TimeControl { get; set; }
    [MaxLength(20)] public string? Eco { get; set; }
    [MaxLength(300)] public string? Opening { get; set; }
    [MaxLength(300)] public string? Variation { get; set; }

    [MaxLength(200)] public string White { get; set; } = "";
    [MaxLength(200)] public string Black { get; set; } = "";
    public int? WhiteElo { get; set; }
    public int? BlackElo { get; set; }

    /// <summary>1-0 / 0-1 / 1/2-1/2 / *</summary>
    [MaxLength(10)] public string Result { get; set; } = "*";

    /// <summary>表头 [FEN](从局面开局);标准开局为 null。</summary>
    [MaxLength(120)] public string? InitialFen { get; set; }

    /// <summary>规范化后的单局 PGN(已剥离变着)。</summary>
    public string PgnText { get; set; } = "";

    /// <summary>总半回合数(不含初始局面)。</summary>
    public int PlyCount { get; set; }

    /// <summary>导入时间(UTC)。用 <see cref="DateTime"/> 而非 DateTimeOffset,否则 SQLite 无法对该列排序。</summary>
    public DateTime ImportedAt { get; set; }

    /// <summary>导入批次 id;指导对弈产生的棋局没有批次,为 null。</summary>
    public int? ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }

    /// <summary>归属的登录用户;与 <see cref="OwnerAnonKey"/> 二选一。</summary>
    public int? OwnerUserId { get; set; }
    public User? OwnerUser { get; set; }

    /// <summary>归属的匿名会话(会话级 GUID);与 <see cref="OwnerUserId"/> 二选一。</summary>
    [MaxLength(64)] public string? OwnerAnonKey { get; set; }

    public ICollection<GameMove> Moves { get; set; } = new List<GameMove>();
    public ICollection<Analysis> Analyses { get; set; } = new List<Analysis>();
}

/// <summary>棋局中的一步(ply 0 = 初始局面)。表名 Moves。</summary>
public class GameMove
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>0 = 初始局面;1 起为实际走子。</summary>
    public int Ply { get; set; }

    /// <summary>标准代数记谱;ply 0 为空。</summary>
    [MaxLength(20)] public string San { get; set; } = "";

    /// <summary>UCI 走法,如 e2e4;ply 0 为空。</summary>
    [MaxLength(10)] public string? Uci { get; set; }

    /// <summary>该步走完后的局面 FEN。</summary>
    [MaxLength(120)] public string Fen { get; set; } = "";

    public string? Comment { get; set; }
    public int Nag { get; set; }
}

/// <summary>某个局面的引擎评估结果(缓存)。</summary>
public class Analysis
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>被评估的局面 ply。</summary>
    public int Ply { get; set; }

    public int Depth { get; set; }

    /// <summary>相对白方的厘兵分值;与 <see cref="ScoreMate"/> 互斥。</summary>
    public int? ScoreCp { get; set; }

    /// <summary>杀棋步数(正 = 白胜);与 <see cref="ScoreCp"/> 互斥。</summary>
    public int? ScoreMate { get; set; }

    /// <summary>主变,UCI 走法空格分隔。</summary>
    public string Pv { get; set; } = "";

    /// <summary>前 N 候选的 JSON。</summary>
    public string MultiPvJson { get; set; } = "[]";

    public long Nodes { get; set; }
    public int Nps { get; set; }
    public int TimeMs { get; set; }

    /// <summary>产生该结果的引擎版本,用于失效判定。</summary>
    [MaxLength(100)] public string EngineVersion { get; set; } = "";

    public DateTime CreatedAt { get; set; }
}

/// <summary>一次 PGN 导入的批次记录。</summary>
public class ImportBatch
{
    public int Id { get; set; }

    [MaxLength(260)] public string FileName { get; set; } = "";

    public DateTime ImportedAt { get; set; }

    public int TotalGames { get; set; }
    public int ImportedGames { get; set; }
    public int SkippedGames { get; set; }

    /// <summary>前若干条错误摘要。</summary>
    public string? ErrorSummary { get; set; }

    public ICollection<Game> Games { get; set; } = new List<Game>();
}

/// <summary>键值型运行时配置。</summary>
public class AppSetting
{
    [MaxLength(100)] public string Key { get; set; } = "";
    public string? Value { get; set; }
}

/// <summary>
/// 一局「人机指导对弈」会话。表名 PlaySessions。
///
/// 对局过程中的走法、局面评估、额度都存在这里;终局之后会把棋谱写进
/// <see cref="Game"/> / <see cref="GameMove"/>,复盘、分析、报告走原有那条主线。
/// </summary>
public class PlaySession
{
    public int Id { get; set; }

    /// <summary>归属的登录用户;与 <see cref="OwnerAnonKey"/> 二选一。</summary>
    public int? OwnerUserId { get; set; }
    public User? OwnerUser { get; set; }

    /// <summary>归属的匿名会话(会话级 GUID);与 <see cref="OwnerUserId"/> 二选一。</summary>
    [MaxLength(64)] public string? OwnerAnonKey { get; set; }

    /// <summary>会话状态:Active / Finished / Abandoned。</summary>
    [MaxLength(20)] public string Status { get; set; } = "Active";

    /// <summary>对手强度档位(1 入门 ~ 4 高级)。</summary>
    public int Level { get; set; } = 2;

    /// <summary>开局局面 FEN(标准开局为 null)。</summary>
    [MaxLength(120)] public string? InitialFen { get; set; }

    /// <summary>当前局面 FEN。</summary>
    [MaxLength(120)] public string CurrentFen { get; set; } = "";

    /// <summary>已经走了多少个半回合。</summary>
    public int PlyCount { get; set; }

    /// <summary>走法记录(JSON 数组)。</summary>
    public string MovesJson { get; set; } = "[]";

    /// <summary>局面评估记录(JSON 数组,下标 = ply)。</summary>
    public string EvalsJson { get; set; } = "[]";

    /// <summary>悔棋记录(JSON 数组)。</summary>
    public string TakebacksJson { get; set; } = "[]";

    /// <summary>剩余提示次数。</summary>
    public int HintsLeft { get; set; } = 3;

    /// <summary>已用提示次数。</summary>
    public int HintsUsed { get; set; }

    /// <summary>剩余悔棋次数。</summary>
    public int TakebacksLeft { get; set; } = 3;

    /// <summary>已用悔棋次数。</summary>
    public int TakebacksUsed { get; set; }

    /// <summary>失误(含错误 / 严重失误)次数。</summary>
    public int MistakeCount { get; set; }

    /// <summary>严重失误次数。</summary>
    public int BlunderCount { get; set; }

    /// <summary>漏着次数。</summary>
    public int MissedWinCount { get; set; }

    /// <summary>终局后写入的棋局 id。</summary>
    public int? GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>1-0 / 0-1 / 1/2-1/2 / *</summary>
    [MaxLength(10)] public string? Result { get; set; }

    /// <summary>终局原因(将死 / 认输 / 和棋 …)。</summary>
    [MaxLength(100)] public string? Termination { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
