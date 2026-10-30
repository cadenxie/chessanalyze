using System.Text;
using System.Text.Json;
using ChessDotNetCore;
using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Analysis;
using ChessGameD.Web.Services.Auth;
using ChessGameD.Web.Services.Engine;
using Microsoft.EntityFrameworkCore;

namespace ChessGameD.Web.Services.Play;

/// <summary>
/// 指导对弈:人和陪练引擎下棋,引擎同时兼任教练。
///
/// 规则(和用户对齐过的口径):
/// - 走法合法性由服务端裁决(ChessDotNetCore),浏览器只负责动画;
/// - 每走一步引擎评估一次「走前 / 走后」,损失量按走子方(玩家永远执白)还原,
///   分类直接复用 <see cref="MoveClassifier"/> 的阈值;
/// - 事后警报(严重失误 / 漏着)不消耗额度;事前求提示才消耗;
/// - 悔棋一次退回到玩家的决策点(引擎的应手一起退),额度用完为止;
/// - 终局后棋谱写进 <see cref="Game"/> / <see cref="GameMove"/>,复盘、分析、报告走原有主线。
/// </summary>
public sealed class PlayService
{
    /// <summary>标准开局局面。</summary>
    public const string StandardFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private readonly AppDbContext _db;
    private readonly ICoachEngine _coach;
    private readonly SettingsStore _settings;
    private readonly ILogger<PlayService> _logger;
    private readonly Random _random = new();

    public PlayService(
        AppDbContext db,
        ICoachEngine coach,
        SettingsStore settings,
        ILogger<PlayService> logger)
    {
        _db = db;
        _coach = coach;
        _settings = settings;
        _logger = logger;
    }

    /* ---------------- 会话 ---------------- */

    /// <summary>开一局新的指导对弈。</summary>
    public async Task<PlaySession> CreateAsync(CurrentUser user, int level, CancellationToken ct)
    {
        var owner = GameOwner.For(user);
        var profile = CoachLevels.Get(level);
        var hintQuota = Math.Clamp(await _settings.GetIntAsync(SettingsStore.PlayHintQuota, 3, ct), 0, 10);
        var takebackQuota = Math.Clamp(await _settings.GetIntAsync(SettingsStore.PlayTakebackQuota, 3, ct), 0, 10);

        var session = new PlaySession
        {
            Status = PlaySessionStates.Active,
            Level = profile.Level,
            InitialFen = null,
            CurrentFen = StandardFen,
            PlyCount = 0,
            HintsLeft = hintQuota,
            TakebacksLeft = takebackQuota,
            OwnerUserId = owner.UserId,
            OwnerAnonKey = owner.AnonKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        // 开局局面先评估一次:提示、分类都要用
        var analysis = await _coach.AnalyzeAsync(StandardFen, ct);
        var eval = BuildEval(0, StandardFen, analysis);
        session.EvalsJson = JsonSerializer.Serialize(new List<PlayEval> { eval }, PlayJson.Options);

        _db.PlaySessions.Add(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("开了一局指导对弈(档位 {Level},用户 {User})", profile.Name, DescribeOwner(owner));
        return session;
    }

    /// <summary>按 id 取对局(必须过归属过滤)。</summary>
    public Task<PlaySession?> GetOwnedAsync(int id, CurrentUser user, CancellationToken ct)
        => _db.PlaySessions.Owned(user).FirstOrDefaultAsync(s => s.Id == id, ct);

    /// <summary>当前身份下还没结束的对局(用来续局)。</summary>
    public Task<PlaySession?> GetActiveAsync(CurrentUser user, CancellationToken ct)
        => _db.PlaySessions
            .Owned(user)
            .Where(s => s.Status == PlaySessionStates.Active)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(ct);

    /* ---------------- 走子 ---------------- */

    /// <summary>玩家走一步:校验 → 评估 → 教练批注 → 引擎应手。</summary>
    public async Task<PlayMoveOutcome> SubmitMoveAsync(PlaySession session, string uci, CancellationToken ct)
    {
        EnsureActive(session);

        var moves = LoadMoves(session);
        var evals = LoadEvals(session);

        if (moves.Count % 2 != 0)
        {
            throw new PlayStateException("引擎正在思考,请稍候。");
        }

        var fen = CurrentFen(session, moves, evals);
        var applied = ApplyMove(fen, uci);
        if (!applied.Ok)
        {
            throw new PlayStateException("这一步不合法。");
        }

        // 走完之后的局面评估(引擎此刻是黑方 → 换算成相对白方)
        var afterAnalysis = await _coach.AnalyzeAsync(applied.Fen, ct);
        var afterEval = BuildEval(moves.Count + 1, applied.Fen, afterAnalysis);
        evals.Add(afterEval);

        var beforeEval = evals[moves.Count];
        var loss = EvalCp(beforeEval) - EvalCp(afterEval);
        var isTop = !string.IsNullOrEmpty(beforeEval.BestUci)
            && string.Equals(uci, beforeEval.BestUci, StringComparison.OrdinalIgnoreCase);
        var inTop = beforeEval.Candidates.Contains(uci, StringComparer.OrdinalIgnoreCase);
        var missed = MoveClassifier.IsMissedWin(EvalCp(beforeEval), EvalCp(afterEval));
        var cls = MoveClassifier.Classify(loss, isTop, inTop, missed);

        var bestSan = SanConverter.ToSan(beforeEval.Fen, beforeEval.BestUci);
        var playerMove = new PlayMove
        {
            Ply = moves.Count + 1,
            San = applied.San,
            Uci = uci.ToLowerInvariant(),
            Fen = applied.Fen,
            EvalBeforeCp = EvalCp(beforeEval),
            EvalAfterCp = EvalCp(afterEval),
            Class = cls.ToString(),
            Label = MoveClassifier.ToLabel(cls),
            IsMissedWin = missed,
            IsEngineMove = false,
            BestMoveSan = bestSan,
            Comment = BuildCoachComment(cls, loss, missed, bestSan),
        };
        moves.Add(playerMove);

        var outcome = new PlayMoveOutcome { BestMoveSan = bestSan };
        if (IsBadMove(cls))
        {
            outcome.IsBadMove = true;
            outcome.AlertLabel = MoveClassifier.ToLabel(cls);
            outcome.AlertCssClass = MoveClassifier.ToCssClass(cls);
            outcome.AlertComment = playerMove.Comment;
        }

        // 玩家把引擎将死 / 逼和 → 直接终局
        var end = DetectEnd(applied.Fen);
        if (end.IsOver)
        {
            SaveState(session, moves, evals);
            await FinishInternalAsync(session, end.Result!, end.Termination!, ct);
            outcome.GameOver = true;
            return outcome;
        }

        await PlayEngineMoveAsync(session, moves, evals, playerMove, ct);
        SaveState(session, moves, evals);
        await _db.SaveChangesAsync(ct);

        // 引擎走完可能正好把玩家将死
        var endAfterEngine = DetectEnd(CurrentFen(session, moves, evals));
        if (endAfterEngine.IsOver)
        {
            await FinishInternalAsync(session, endAfterEngine.Result!, endAfterEngine.Termination!, ct);
            outcome.GameOver = true;
        }

        return outcome;
    }

    /// <summary>引擎应手:按难度档位在候选里抽签,必要时「放水」。</summary>
    private async Task PlayEngineMoveAsync(
        PlaySession session,
        List<PlayMove> moves,
        List<PlayEval> evals,
        PlayMove playerMove,
        CancellationToken ct)
    {
        var profile = CoachLevels.Get(session.Level);
        var positionEval = evals[^1];

        // 玩家刚失误的时候,引擎不急着抓——这就是「陪练」
        var forgive = playerMove.IsMissedWin
            || playerMove.Class == nameof(MoveClass.Blunder)
            || playerMove.Class == nameof(MoveClass.Mistake);

        var choice = ChooseEngineMove(positionEval, profile, forgive);
        if (string.IsNullOrEmpty(choice))
        {
            return;
        }

        var applied = ApplyMove(positionEval.Fen, choice);
        if (!applied.Ok)
        {
            // 引擎给的走法在这个局面不合法(例如换了引擎版本):换一个合法走法,别让对局卡住
            var fallback = PickAnyLegalMove(positionEval.Fen);
            if (fallback is null)
            {
                return;
            }

            applied = ApplyMove(positionEval.Fen, fallback);
            if (!applied.Ok)
            {
                return;
            }

            choice = fallback;
        }

        var analysis = await _coach.AnalyzeAsync(applied.Fen, ct);
        var afterEval = BuildEval(moves.Count + 1, applied.Fen, analysis);
        evals.Add(afterEval);

        var isBest = string.Equals(choice, positionEval.BestUci, StringComparison.OrdinalIgnoreCase);
        moves.Add(new PlayMove
        {
            Ply = moves.Count + 1,
            San = applied.San,
            Uci = choice.ToLowerInvariant(),
            Fen = applied.Fen,
            EvalBeforeCp = EvalCp(positionEval),
            EvalAfterCp = EvalCp(afterEval),
            IsEngineMove = true,
            EngineForgave = !isBest,
            Comment = isBest ? null : "引擎这步是陪练放水(不是最佳走法)。",
        });
    }

    /// <summary>按难度挑引擎的走法。</summary>
    private string ChooseEngineMove(PlayEval positionEval, CoachLevel profile, bool forgive)
    {
        var candidates = positionEval.Candidates;
        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        var best = candidates[0];
        var pool = candidates.Skip(1).Take(Math.Max(1, profile.CandidatePool - 1)).ToList();
        if (pool.Count == 0)
        {
            return best;
        }

        if (forgive && _random.Next(100) < profile.ForgiveRate)
        {
            return pool[_random.Next(pool.Count)];
        }

        if (_random.Next(100) < profile.ErrorRate)
        {
            return pool[_random.Next(pool.Count)];
        }

        return best;
    }

    /* ---------------- 提示 ---------------- */

    /// <summary>求提示:只有「候选着法 / 最佳走法」消耗次数,只看评估是免费的。</summary>
    public async Task<PlayHintOutcome> HintAsync(PlaySession session, HintStrength strength, CancellationToken ct)
    {
        EnsureActive(session);

        var evals = LoadEvals(session);
        if (evals.Count == 0)
        {
            throw new PlayStateException("当前局面还没有评估,稍等一下再试。");
        }

        var current = evals[Math.Min(session.PlyCount, evals.Count - 1)];

        var outcome = new PlayHintOutcome
        {
            Strength = strength,
            ScoreCp = current.ScoreCp,
            ScoreMate = current.ScoreMate,
            Evaluation = DescribeEval(current),
        };

        if (strength != HintStrength.Eval)
        {
            if (session.HintsLeft <= 0)
            {
                throw new PlayStateException("提示次数已经用完了。");
            }

            session.HintsLeft--;
            session.HintsUsed++;
            outcome.Consumed = true;
        }

        if (strength >= HintStrength.Candidates)
        {
            outcome.Candidates = current.CandidateDetails.Take(3).ToList();
        }

        if (strength == HintStrength.Best)
        {
            outcome.BestMoveSan = SanConverter.ToSan(current.Fen, current.BestUci);
            outcome.BestMoveUci = current.BestUci;
        }

        await _db.SaveChangesAsync(ct);
        return outcome;
    }

    /* ---------------- 悔棋 ---------------- */

    /// <summary>
    /// 悔棋:退回到玩家上一次的决策点(引擎的应手一起退),额度用完为止。
    /// 被撤掉的着法不写进最终棋谱,但会记一条悔棋日志。
    /// </summary>
    public async Task<PlayTakebackOutcome> TakebackAsync(PlaySession session, CancellationToken ct)
    {
        EnsureActive(session);

        if (session.TakebacksLeft <= 0)
        {
            throw new PlayStateException("悔棋次数已经用完了。");
        }

        var moves = LoadMoves(session);
        var evals = LoadEvals(session);
        if (moves.Count == 0)
        {
            throw new PlayStateException("还没有棋可以悔。");
        }

        // 轮到玩家(偶数个半回合)→ 退两步;引擎刚走完还没轮到自己(奇数)→ 退一步
        var remove = moves.Count % 2 == 0 ? 2 : 1;
        remove = Math.Min(remove, moves.Count);

        var removed = moves.Skip(moves.Count - remove).ToList();
        moves.RemoveRange(moves.Count - remove, remove);

        // 评估表截断到当前局面
        while (evals.Count > moves.Count + 1)
        {
            evals.RemoveAt(evals.Count - 1);
        }

        session.TakebacksLeft--;
        session.TakebacksUsed++;

        var takebacks = LoadTakebacks(session);
        takebacks.Add(new PlayTakeback
        {
            Ply = moves.Count,
            San = string.Join(", ", removed.Select(m => m.San)),
            At = DateTime.UtcNow,
        });

        SaveState(session, moves, evals);
        session.TakebacksJson = JsonSerializer.Serialize(takebacks, PlayJson.Options);
        await _db.SaveChangesAsync(ct);

        return new PlayTakebackOutcome
        {
            RemovedPlies = remove,
            RemovedSan = string.Join(", ", removed.Select(m => m.San)),
            EngineReplied = false,
        };
    }

    /* ---------------- 终局 ---------------- */

    /// <summary>结束对局(认输 / 和棋),棋谱写进 Games。</summary>
    public Task<PlayFinishOutcome> FinishAsync(PlaySession session, string result, string termination, CancellationToken ct)
    {
        EnsureActive(session);
        return FinishInternalAsync(session, result, termination, ct);
    }

    private async Task<PlayFinishOutcome> FinishInternalAsync(
        PlaySession session,
        string result,
        string termination,
        CancellationToken ct)
    {
        var moves = LoadMoves(session);
        var profile = CoachLevels.Get(session.Level);
        var white = "我";
        var black = $"陪练引擎({profile.Name})";
        var now = DateTime.Now;

        var game = new Game
        {
            Event = "指导对弈",
            Site = "ChessGameD",
            Round = "-",
            Date = now.ToString("yyyy.MM.dd"),
            Termination = termination,
            White = white,
            Black = black,
            Result = result,
            InitialFen = session.InitialFen,
            PlyCount = moves.Count,
            ImportedAt = DateTime.UtcNow,
            OwnerUserId = session.OwnerUserId,
            OwnerAnonKey = session.OwnerAnonKey,
            PgnText = BuildPgn(white, black, result, moves, now),
        };

        _db.Games.Add(game);
        foreach (var m in moves)
        {
            game.Moves.Add(new GameMove
            {
                Ply = m.Ply,
                San = m.San,
                Uci = m.Uci,
                Fen = m.Fen,
                Comment = m.Comment,
            });
        }

        await _db.SaveChangesAsync(ct);

        session.GameId = game.Id;
        session.Result = result;
        session.Termination = termination;
        session.Status = PlaySessionStates.Finished;
        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new PlayFinishOutcome { GameId = game.Id, Result = result, Termination = termination };
    }

    /* ---------------- 状态视图 ---------------- */

    /// <summary>把会话拼成前端要的状态。</summary>
    public PlaySessionState BuildState(PlaySession session)
    {
        var moves = LoadMoves(session);
        var evals = LoadEvals(session);
        var profile = CoachLevels.Get(session.Level);
        var current = evals.Count > 0 ? evals[Math.Min(session.PlyCount, evals.Count - 1)] : null;

        return new PlaySessionState
        {
            Id = session.Id,
            Status = session.Status,
            Level = session.Level,
            LevelName = profile.Name,
            Fen = CurrentFen(session, moves, evals),
            PlyCount = session.PlyCount,
            YourTurn = session.Status == PlaySessionStates.Active && moves.Count % 2 == 0,
            HintsLeft = session.HintsLeft,
            HintsUsed = session.HintsUsed,
            TakebacksLeft = session.TakebacksLeft,
            TakebacksUsed = session.TakebacksUsed,
            MistakeCount = session.MistakeCount,
            BlunderCount = session.BlunderCount,
            MissedWinCount = session.MissedWinCount,
            Result = session.Result,
            Termination = session.Termination,
            GameId = session.GameId,
            Moves = moves.Select(m => new PlayMoveView
            {
                Ply = m.Ply,
                San = m.San,
                Uci = m.Uci,
                Fen = m.Fen,
                Label = m.Label,
                CssClass = m.Class is null ? null : MoveClassifier.ToCssClass(Enum.Parse<MoveClass>(m.Class)),
                IsMissedWin = m.IsMissedWin,
                EngineForgave = m.EngineForgave,
                IsEngineMove = m.IsEngineMove,
                Comment = m.Comment,
                EvalBeforeCp = m.EvalBeforeCp,
                EvalAfterCp = m.EvalAfterCp,
                BestMoveSan = m.BestMoveSan,
            }).ToList(),
            Eval = current is null ? null : new PlayEvalView
            {
                ScoreCp = current.ScoreCp,
                ScoreMate = current.ScoreMate,
                Depth = current.Depth,
                Evaluation = DescribeEval(current),
                BestMoveUci = current.BestUci,
                BestMoveSan = SanConverter.ToSan(current.Fen, current.BestUci),
                Candidates = current.CandidateDetails.Take(3).ToList(),
            },
            Levels = CoachLevels.All.Select(l => new CoachLevelView
            {
                Level = l.Level,
                Name = l.Name,
                Description = l.Description,
            }).ToList(),
        };
    }

    /* ---------------- 内部工具 ---------------- */

    private static void EnsureActive(PlaySession session)
    {
        if (session.Status != PlaySessionStates.Active)
        {
            throw new PlayStateException("这局已经结束了。");
        }
    }

    private static bool IsBadMove(MoveClass cls)
        => cls is MoveClass.Inaccuracy or MoveClass.Mistake or MoveClass.Blunder or MoveClass.MissedWin;

    /// <summary>教练批注:坏棋才写,好棋不啰嗦。</summary>
    private static string? BuildCoachComment(MoveClass cls, int loss, bool missed, string? bestSan)
    {
        if (missed)
        {
            return bestSan is null
                ? "教练:本来能赢,这一步把胜势送掉了。"
                : $"教练:本来能赢,这一步把胜势送掉了。更好的走法是 {bestSan}。";
        }

        if (!IsBadMove(cls))
        {
            return null;
        }

        var pawns = (loss / 100.0).ToString("0.0");
        var label = MoveClassifier.ToLabel(cls);
        return bestSan is null
            ? $"教练:这一步是{label},大约损失 {pawns} 兵。"
            : $"教练:这一步是{label},大约损失 {pawns} 兵。更好的走法是 {bestSan}。";
    }

    /// <summary>归属的日志描述。</summary>
    private static string DescribeOwner(GameOwner owner)
        => owner.UserId is int userId ? $"用户 {userId}" : $"匿名 {owner.AnonKey}";

    /// <summary>局面评估的一句话说明。</summary>
    private static string DescribeEval(PlayEval eval)
    {
        if (eval.ScoreMate is int mate)
        {
            return mate > 0 ? $"白方 {mate} 步杀" : $"黑方 {Math.Abs(mate)} 步杀";
        }

        var cp = EvalCp(eval);
        return cp switch
        {
            >= 300 => "白方明显占优",
            >= 80 => "白方略优",
            > -80 => "局面均衡",
            > -300 => "黑方略优",
            _ => "黑方明显占优",
        };
    }

    /// <summary>分值统一折成相对白方的厘兵;杀棋取极大值。</summary>
    private static int EvalCp(PlayEval eval)
    {
        if (eval.ScoreCp is int cp)
        {
            return cp;
        }

        if (eval.ScoreMate is int mate)
        {
            return mate >= 0 ? 10000 : -10000;
        }

        return 0;
    }

    /// <summary>把引擎结果转成局面评估(候选带 SAN,方便前端直接显示)。</summary>
    private static PlayEval BuildEval(int ply, string fen, EngineAnalysis analysis)
    {
        var eval = new PlayEval
        {
            Ply = ply,
            Fen = fen,
            ScoreCp = analysis.ScoreCp,
            ScoreMate = analysis.ScoreMate,
            Depth = analysis.Depth,
        };

        var candidates = new List<PlayCandidate>();
        foreach (var line in analysis.Lines)
        {
            var uci = line.Pv.FirstOrDefault();
            if (string.IsNullOrEmpty(uci))
            {
                continue;
            }

            candidates.Add(new PlayCandidate
            {
                Uci = uci,
                San = SanConverter.ToSan(fen, uci),
                ScoreCp = line.ScoreCp,
            });
        }

        if (candidates.Count == 0 && !string.IsNullOrEmpty(analysis.BestMove))
        {
            candidates.Add(new PlayCandidate
            {
                Uci = analysis.BestMove,
                San = SanConverter.ToSan(fen, analysis.BestMove),
                ScoreCp = analysis.ScoreCp,
            });
        }

        eval.CandidateDetails = candidates;
        eval.Candidates = candidates.Select(c => c.Uci).ToList();
        eval.BestUci = candidates.Count > 0 ? candidates[0].Uci : analysis.BestMove;
        return eval;
    }

    /// <summary>走一步棋的结果。</summary>
    private readonly record struct AppliedMove(bool Ok, string Fen, string San);

    /// <summary>在服务端校验并执行一步棋。</summary>
    private static AppliedMove ApplyMove(string fen, string uci)
    {
        if (string.IsNullOrWhiteSpace(uci) || uci.Length < 4)
        {
            return new AppliedMove(false, fen, string.Empty);
        }

        var game = new ChessGame();
        game.Initialize(fen);

        var move = ParseMove(uci, game.StartPlayer);
        if (move is null)
        {
            return new AppliedMove(false, fen, string.Empty);
        }

        var type = game.MakeMove(move, alreadyValidated: false);
        if (type == MoveType.Invalid)
        {
            return new AppliedMove(false, fen, string.Empty);
        }

        return new AppliedMove(true, game.GetFen(), game.LastMove?.SAN ?? string.Empty);
    }

    /// <summary>解析 UCI 走法(e2e4 / e7e8q)。</summary>
    private static Move? ParseMove(string uci, Player player)
    {
        var from = uci[..2].ToLowerInvariant();
        var to = uci.Substring(2, 2).ToLowerInvariant();
        char? promotion = null;

        if (uci.Length > 4)
        {
            var raw = char.ToUpperInvariant(uci[4]);
            if (raw is 'Q' or 'R' or 'B' or 'N')
            {
                promotion = raw;
            }
        }

        try
        {
            return new Move(from, to, player, promotion);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>随便挑一个合法走法(引擎给的走法不合法时兜底)。</summary>
    private static string? PickAnyLegalMove(string fen)
    {
        var game = new ChessGame();
        game.Initialize(fen);

        foreach (var move in game.GetValidMoves(game.CurrentPlayer))
        {
            return ToUci(move);
        }

        return null;
    }

    /// <summary>Move → UCI。</summary>
    private static string ToUci(Move move)
    {
        var uci = $"{move.OriginalPosition}{move.NewPosition}";
        if (move.Promotion is not null)
        {
            uci += char.ToLowerInvariant(move.Promotion.Value);
        }

        return uci.ToLowerInvariant();
    }

    /// <summary>判断局面是不是已经终局。</summary>
    private static (bool IsOver, string? Result, string? Termination) DetectEnd(string fen)
    {
        var game = new ChessGame();
        game.Initialize(fen);
        var current = game.CurrentPlayer;

        if (game.IsCheckmated(current))
        {
            return (true, current == Player.White ? "0-1" : "1-0", "将死");
        }

        if (game.IsStalemated(current))
        {
            return (true, "1/2-1/2", "逼和");
        }

        if (game.IsInsufficientMaterial())
        {
            return (true, "1/2-1/2", "子力不足");
        }

        return (false, null, null);
    }

    /// <summary>当前局面 FEN。</summary>
    private static string CurrentFen(PlaySession session, List<PlayMove> moves, List<PlayEval> evals)
    {
        if (moves.Count > 0)
        {
            return moves[^1].Fen;
        }

        if (evals.Count > 0)
        {
            return evals[^1].Fen;
        }

        return session.InitialFen ?? StandardFen;
    }

    /// <summary>把会话状态写回实体(顺带重算统计)。</summary>
    private static void SaveState(PlaySession session, List<PlayMove> moves, List<PlayEval> evals)
    {
        session.MovesJson = JsonSerializer.Serialize(moves, PlayJson.Options);
        session.EvalsJson = JsonSerializer.Serialize(evals, PlayJson.Options);
        session.PlyCount = moves.Count;
        session.CurrentFen = moves.Count > 0 ? moves[^1].Fen : (evals.Count > 0 ? evals[^1].Fen : StandardFen);

        session.MistakeCount = moves.Count(m => m.Class == nameof(MoveClass.Mistake)
            || m.Class == nameof(MoveClass.Blunder)
            || m.Class == nameof(MoveClass.Inaccuracy));
        session.BlunderCount = moves.Count(m => m.Class == nameof(MoveClass.Blunder));
        session.MissedWinCount = moves.Count(m => m.IsMissedWin);
        session.UpdatedAt = DateTime.UtcNow;
    }

    private static List<PlayMove> LoadMoves(PlaySession session)
        => JsonSerializer.Deserialize<List<PlayMove>>(session.MovesJson, PlayJson.Options) ?? new List<PlayMove>();

    private static List<PlayEval> LoadEvals(PlaySession session)
        => JsonSerializer.Deserialize<List<PlayEval>>(session.EvalsJson, PlayJson.Options) ?? new List<PlayEval>();

    private static List<PlayTakeback> LoadTakebacks(PlaySession session)
        => JsonSerializer.Deserialize<List<PlayTakeback>>(session.TakebacksJson, PlayJson.Options) ?? new List<PlayTakeback>();

    /// <summary>生成 PGN 文本(带教练批注)。</summary>
    private static string BuildPgn(
        string white,
        string black,
        string result,
        IReadOnlyList<PlayMove> moves,
        DateTime date)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Event \"指导对弈\"]");
        sb.AppendLine("[Site \"ChessGameD\"]");
        sb.AppendLine($"[Date \"{date:yyyy.MM.dd}\"]");
        sb.AppendLine("[Round \"-\"]");
        sb.AppendLine($"[White \"{white}\"]");
        sb.AppendLine($"[Black \"{black}\"]");
        sb.AppendLine($"[Result \"{result}\"]");
        sb.AppendLine();

        for (var i = 0; i < moves.Count; i++)
        {
            var move = moves[i];
            if (i % 2 == 0)
            {
                sb.Append($"{i / 2 + 1}. ");
            }

            sb.Append(move.San);

            if (!string.IsNullOrEmpty(move.Comment))
            {
                sb.Append(" {").Append(move.Comment.Replace("{", "(").Replace("}", ")")).Append('}');
            }

            sb.Append(' ');
        }

        sb.Append(result);
        sb.AppendLine();
        return sb.ToString();
    }
}
