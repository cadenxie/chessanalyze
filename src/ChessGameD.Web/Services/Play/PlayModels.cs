using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChessGameD.Web.Services.Play;

/// <summary>对弈会话的状态。</summary>
public static class PlaySessionStates
{
    /// <summary>进行中。</summary>
    public const string Active = "Active";

    /// <summary>已终局(棋谱已写进 Games)。</summary>
    public const string Finished = "Finished";

    /// <summary>中途放弃。</summary>
    public const string Abandoned = "Abandoned";
}

/// <summary>提示的强度档位。</summary>
public enum HintStrength
{
    /// <summary>只看局面评估(不消耗次数)。</summary>
    Eval = 0,

    /// <summary>给引擎的前几个候选着法(消耗 1 次)。</summary>
    Candidates = 1,

    /// <summary>直接给出引擎的最佳走法(消耗 1 次)。</summary>
    Best = 2,
}

/// <summary>对局中的一步。</summary>
public sealed class PlayMove
{
    /// <summary>半回合序号(1 起);奇数是玩家的走法,偶数是引擎的走法。</summary>
    public int Ply { get; set; }

    /// <summary>标准代数记谱。</summary>
    public string San { get; set; } = "";

    /// <summary>UCI 走法。</summary>
    public string Uci { get; set; } = "";

    /// <summary>走完之后的局面 FEN。</summary>
    public string Fen { get; set; } = "";

    /// <summary>走子前的评估(相对白方,厘兵)。</summary>
    public int? EvalBeforeCp { get; set; }

    /// <summary>走完之后的评估(相对白方,厘兵)。</summary>
    public int? EvalAfterCp { get; set; }

    /// <summary>走子分类(枚举名)。</summary>
    public string? Class { get; set; }

    /// <summary>走子分类的中文标签。</summary>
    public string? Label { get; set; }

    /// <summary>是不是漏着(本来能赢却把胜势送掉了)。</summary>
    public bool IsMissedWin { get; set; }

    /// <summary>引擎这步是不是「放水」(故意没抓住玩家的漏着)。</summary>
    public bool EngineForgave { get; set; }

    /// <summary>是不是引擎走的。</summary>
    public bool IsEngineMove { get; set; }

    /// <summary>这一步的最佳走法(SAN),用于提示。</summary>
    public string? BestMoveSan { get; set; }

    /// <summary>教练批注(会写进复盘页的着法注释)。</summary>
    public string? Comment { get; set; }
}

/// <summary>候选着法。</summary>
public sealed class PlayCandidate
{
    public string Uci { get; set; } = "";
    public string? San { get; set; }

    /// <summary>相对白方的厘兵分值。</summary>
    public int? ScoreCp { get; set; }
}

/// <summary>某个局面的引擎评估(下标 = ply)。</summary>
public sealed class PlayEval
{
    public int Ply { get; set; }
    public string Fen { get; set; } = "";

    /// <summary>相对白方的厘兵分值。</summary>
    public int? ScoreCp { get; set; }

    /// <summary>杀棋步数(正 = 白胜)。</summary>
    public int? ScoreMate { get; set; }

    public int Depth { get; set; }

    /// <summary>引擎在这个局面的最佳走法(UCI)。</summary>
    public string BestUci { get; set; } = "";

    /// <summary>候选走法(UCI),第一个是最佳走法。</summary>
    public List<string> Candidates { get; set; } = new();

    /// <summary>候选走法的详细信息。</summary>
    public List<PlayCandidate> CandidateDetails { get; set; } = new();
}

/// <summary>一次悔棋的记录。</summary>
public sealed class PlayTakeback
{
    /// <summary>悔棋时棋局已经走了多少个半回合。</summary>
    public int Ply { get; set; }

    /// <summary>被撤掉的着法(SAN,逗号分隔)。</summary>
    public string San { get; set; } = "";

    public DateTime At { get; set; }
}

/// <summary>
/// 陪练难度档位。
///
/// 引擎不是靠「自带的弱等级」变弱的,而是每次搜索取前几个候选,按概率挑一个不那么好的走法——
/// 这样行为可控、可解释,复盘时也能说清「这步是引擎让的」。
/// </summary>
public sealed record CoachLevel(
    int Level,
    string Name,
    string Description,
    /// <summary>故意走次优着法的概率(百分比)。</summary>
    int ErrorRate,
    /// <summary>对手刚失误时「放水」的概率(百分比)。</summary>
    int ForgiveRate,
    /// <summary>抽签时最多往下取几个候选。</summary>
    int CandidatePool);

/// <summary>陪练难度档位表。</summary>
public static class CoachLevels
{
    /// <summary>全部档位。</summary>
    public static readonly IReadOnlyList<CoachLevel> All = new[]
    {
        new CoachLevel(1, "入门", "引擎经常走次优着法,适合刚熟悉规则", 35, 70, 3),
        new CoachLevel(2, "初级", "偶尔失误,但不会一直送子", 20, 45, 3),
        new CoachLevel(3, "中级", "基本下得最好,偶尔小漏", 10, 25, 2),
        new CoachLevel(4, "高级", "几乎全力,失误很少", 3, 10, 2),
    };

    /// <summary>默认档位。</summary>
    public const int Default = 2;

    /// <summary>取档位;越界就落到默认档。</summary>
    public static CoachLevel Get(int level)
        => All.FirstOrDefault(l => l.Level == level) ?? All.First(l => l.Level == Default);
}

/// <summary>对弈过程中的异常(状态不对、额度用完、走法不合法等)。</summary>
public sealed class PlayStateException : Exception
{
    public PlayStateException(string message) : base(message) { }
}

/// <summary>一次走子之后的结果。</summary>
public sealed class PlayMoveOutcome
{
    /// <summary>玩家的这一步是不是坏棋(失误 / 错误 / 严重失误 / 漏着)。</summary>
    public bool IsBadMove { get; set; }

    /// <summary>坏棋的标签(例如「严重失误」)。</summary>
    public string? AlertLabel { get; set; }

    /// <summary>坏棋的样式类(前端直接用)。</summary>
    public string? AlertCssClass { get; set; }

    /// <summary>教练批注。</summary>
    public string? AlertComment { get; set; }

    /// <summary>这一步的最佳走法(SAN)。</summary>
    public string? BestMoveSan { get; set; }

    /// <summary>对局是否已经结束。</summary>
    public bool GameOver { get; set; }
}

/// <summary>提示的结果。</summary>
public sealed class PlayHintOutcome
{
    public HintStrength Strength { get; set; }

    /// <summary>是否消耗了提示次数。</summary>
    public bool Consumed { get; set; }

    public int? ScoreCp { get; set; }
    public int? ScoreMate { get; set; }

    /// <summary>局面评价(中文一句话)。</summary>
    public string? Evaluation { get; set; }

    /// <summary>候选着法。</summary>
    public List<PlayCandidate> Candidates { get; set; } = new();

    /// <summary>最佳走法(SAN)。</summary>
    public string? BestMoveSan { get; set; }

    /// <summary>最佳走法(UCI),前端用来在棋盘上画箭头。</summary>
    public string? BestMoveUci { get; set; }
}

/// <summary>悔棋的结果。</summary>
public sealed class PlayTakebackOutcome
{
    /// <summary>撤掉了几个半回合。</summary>
    public int RemovedPlies { get; set; }

    /// <summary>撤掉的着法(SAN)。</summary>
    public string RemovedSan { get; set; } = "";

    /// <summary>悔棋之后引擎有没有重新走一步。</summary>
    public bool EngineReplied { get; set; }
}

/// <summary>终局的结果。</summary>
public sealed class PlayFinishOutcome
{
    /// <summary>写进 Games 的棋局 id。</summary>
    public int GameId { get; set; }

    public string Result { get; set; } = "*";

    public string? Termination { get; set; }
}

/// <summary>JSON 序列化的统一选项(驼峰命名)。</summary>
public static class PlayJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>走法(给前端的视图)。</summary>
public sealed class PlayMoveView
{
    public int Ply { get; set; }
    public string San { get; set; } = "";
    public string Uci { get; set; } = "";
    public string Fen { get; set; } = "";

    /// <summary>分类标签(中文)。</summary>
    public string? Label { get; set; }

    /// <summary>分类样式类(前端直接用)。</summary>
    public string? CssClass { get; set; }

    public bool IsMissedWin { get; set; }
    public bool EngineForgave { get; set; }
    public bool IsEngineMove { get; set; }
    public string? Comment { get; set; }
    public int? EvalBeforeCp { get; set; }
    public int? EvalAfterCp { get; set; }
    public string? BestMoveSan { get; set; }
}

/// <summary>当前局面的评估(给前端的视图)。</summary>
public sealed class PlayEvalView
{
    public int? ScoreCp { get; set; }
    public int? ScoreMate { get; set; }
    public int Depth { get; set; }

    /// <summary>局面评价(中文一句话)。</summary>
    public string? Evaluation { get; set; }

    public string? BestMoveSan { get; set; }
    public string? BestMoveUci { get; set; }

    public List<PlayCandidate> Candidates { get; set; } = new();
}

/// <summary>难度档位(给前端的视图)。</summary>
public sealed class CoachLevelView
{
    public int Level { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>对局状态(给前端的视图)。</summary>
public sealed class PlaySessionState
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
    public int Level { get; set; }
    public string LevelName { get; set; } = "";
    public string Fen { get; set; } = "";
    public int PlyCount { get; set; }

    /// <summary>现在是不是轮到玩家走。</summary>
    public bool YourTurn { get; set; }

    public int HintsLeft { get; set; }
    public int HintsUsed { get; set; }
    public int TakebacksLeft { get; set; }
    public int TakebacksUsed { get; set; }
    public int MistakeCount { get; set; }
    public int BlunderCount { get; set; }
    public int MissedWinCount { get; set; }
    public string? Result { get; set; }
    public string? Termination { get; set; }

    /// <summary>终局后写进棋局列表的 id。</summary>
    public int? GameId { get; set; }

    public List<PlayMoveView> Moves { get; set; } = new();
    public PlayEvalView? Eval { get; set; }
    public List<CoachLevelView> Levels { get; set; } = new();
}
