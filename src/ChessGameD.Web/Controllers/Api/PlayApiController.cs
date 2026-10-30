using ChessGameD.Web.Services.Analysis;
using ChessGameD.Web.Services.Auth;
using ChessGameD.Web.Services.Engine;
using ChessGameD.Web.Services.Play;
using Microsoft.AspNetCore.Mvc;

namespace ChessGameD.Web.Controllers.Api;

/// <summary>指导对弈的接口。</summary>
[ApiController]
[Route("api/play")]
public class PlayApiController : ControllerBase
{
    private readonly PlayService _play;
    private readonly CurrentUser _current;
    private readonly GameAnalyzer _analyzer;

    public PlayApiController(PlayService play, CurrentUser current, GameAnalyzer analyzer)
    {
        _play = play;
        _current = current;
        _analyzer = analyzer;
    }

    /// <summary>开一局新的指导对弈。</summary>
    [HttpPost("sessions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateSessionRequest? request, CancellationToken ct)
    {
        try
        {
            var session = await _play.CreateAsync(_current, request?.Level ?? CoachLevels.Default, ct);
            return Ok(_play.BuildState(session));
        }
        catch (EngineNotInstalledException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>当前身份下还没结束的对局(用来续局)。</summary>
    [HttpGet("sessions/active")]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        var session = await _play.GetActiveAsync(_current, ct);
        if (session is null)
        {
            return Ok(new { active = false });
        }

        return Ok(new { active = true, session = _play.BuildState(session) });
    }

    /// <summary>取一局对局的状态。</summary>
    [HttpGet("sessions/{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var session = await _play.GetOwnedAsync(id, _current, ct);
        if (session is null)
        {
            return NotFound(new { error = "对局不存在。" });
        }

        return Ok(_play.BuildState(session));
    }

    /// <summary>玩家走一步。</summary>
    [HttpPost("sessions/{id:int}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(int id, [FromBody] MoveRequest request, CancellationToken ct)
    {
        var session = await _play.GetOwnedAsync(id, _current, ct);
        if (session is null)
        {
            return NotFound(new { error = "对局不存在。" });
        }

        try
        {
            var outcome = await _play.SubmitMoveAsync(session, request.Uci ?? "", ct);
            return Ok(new
            {
                session = _play.BuildState(session),
                outcome = new
                {
                    outcome.IsBadMove,
                    outcome.AlertLabel,
                    outcome.AlertCssClass,
                    outcome.AlertComment,
                    outcome.BestMoveSan,
                    outcome.GameOver,
                },
            });
        }
        catch (PlayStateException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (EngineNotInstalledException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (EngineCrashedException ex)
        {
            return BadRequest(new { error = "陪练引擎出问题了:" + ex.Message });
        }
    }

    /// <summary>求提示。</summary>
    [HttpPost("sessions/{id:int}/hint")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Hint(int id, [FromBody] HintRequest request, CancellationToken ct)
    {
        var session = await _play.GetOwnedAsync(id, _current, ct);
        if (session is null)
        {
            return NotFound(new { error = "对局不存在。" });
        }

        try
        {
            var strength = request.Strength switch
            {
                "candidates" => HintStrength.Candidates,
                "best" => HintStrength.Best,
                _ => HintStrength.Eval,
            };

            var hint = await _play.HintAsync(session, strength, ct);
            return Ok(new
            {
                hint = new
                {
                    hint.Strength,
                    hint.Consumed,
                    hint.ScoreCp,
                    hint.ScoreMate,
                    hint.Evaluation,
                    hint.Candidates,
                    hint.BestMoveSan,
                    hint.BestMoveUci,
                },
                session = _play.BuildState(session),
            });
        }
        catch (PlayStateException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (EngineNotInstalledException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (EngineCrashedException ex)
        {
            return BadRequest(new { error = "陪练引擎出问题了:" + ex.Message });
        }
    }

    /// <summary>悔棋。</summary>
    [HttpPost("sessions/{id:int}/takeback")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Takeback(int id, CancellationToken ct)
    {
        var session = await _play.GetOwnedAsync(id, _current, ct);
        if (session is null)
        {
            return NotFound(new { error = "对局不存在。" });
        }

        try
        {
            var outcome = await _play.TakebackAsync(session, ct);
            return Ok(new
            {
                takeback = new { outcome.RemovedPlies, outcome.RemovedSan, outcome.EngineReplied },
                session = _play.BuildState(session),
            });
        }
        catch (PlayStateException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>结束对局(认输 / 和棋),棋谱写进棋局列表。</summary>
    [HttpPost("sessions/{id:int}/finish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finish(int id, [FromBody] FinishRequest request, CancellationToken ct)
    {
        var session = await _play.GetOwnedAsync(id, _current, ct);
        if (session is null)
        {
            return NotFound(new { error = "对局不存在。" });
        }

        var result = request.Result switch
        {
            "0-1" => "0-1",
            "1/2-1/2" => "1/2-1/2",
            _ => "1-0",
        };

        var termination = string.IsNullOrWhiteSpace(request.Termination) ? "认输" : request.Termination.Trim();

        try
        {
            var finish = await _play.FinishAsync(session, result, termination, ct);

            // 顺手把整局分析排上队(跟导入棋局一样走分析队列)
            if (finish.GameId > 0)
            {
                await _analyzer.StartAsync(finish.GameId, force: false, ct);
            }

            return Ok(new
            {
                gameId = finish.GameId,
                result = finish.Result,
                termination = finish.Termination,
                session = _play.BuildState(session),
            });
        }
        catch (PlayStateException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>开局的请求体。</summary>
    public sealed class CreateSessionRequest
    {
        public int? Level { get; set; }
    }

    /// <summary>走子的请求体。</summary>
    public sealed class MoveRequest
    {
        public string? Uci { get; set; }
    }

    /// <summary>求提示的请求体。</summary>
    public sealed class HintRequest
    {
        /// <summary>eval / candidates / best。</summary>
        public string? Strength { get; set; }
    }

    /// <summary>终局的请求体。</summary>
    public sealed class FinishRequest
    {
        /// <summary>1-0 / 0-1 / 1/2-1/2。</summary>
        public string? Result { get; set; }

        public string? Termination { get; set; }
    }
}
