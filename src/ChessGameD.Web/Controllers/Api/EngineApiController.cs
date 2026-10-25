using ChessGameD.Web.Data;
using ChessGameD.Web.Models;
using ChessGameD.Web.Services.Engine;
using Microsoft.AspNetCore.Mvc;

namespace ChessGameD.Web.Controllers.Api;

[ApiController]
[Route("api/engine")]
public class EngineApiController : ControllerBase
{
    private readonly EngineManager _engine;
    private readonly EngineInstaller _installer;
    private readonly SettingsStore _settings;

    public EngineApiController(EngineManager engine, EngineInstaller installer, SettingsStore settings)
    {
        _engine = engine;
        _installer = installer;
        _settings = settings;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var status = await _engine.GetStatusAsync(ct);
        return Ok(new
        {
            state = status.State.ToString(),
            status.IsInstalled,
            status.IsReady,
            status.Path,
            status.Version,
            status.LastError,
            enginesRoot = _installer.EnginesRoot,
        });
    }

    /// <summary>一键安装/更新 Stockfish(后台执行,立即返回作业号)。</summary>
    [HttpPost("install")]
    public IActionResult Install()
    {
        var jobId = _installer.StartInstall();
        return Accepted(new { jobId });
    }

    [HttpGet("install/status")]
    public IActionResult InstallStatus([FromQuery] string jobId)
    {
        var (progress, result) = _installer.GetInstallStatus(jobId);
        return Ok(new
        {
            progress.Stage,
            progress.Percent,
            progress.Detail,
            success = result?.Success,
            error = result?.Error,
            version = result?.Version,
            enginePath = result?.EnginePath,
            sha256 = result?.Sha256,
        });
    }

    /// <summary>即时分析:对单个局面求评估。</summary>
    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] InstantAnalyzeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Fen))
        {
            return BadRequest(new { error = "缺少 FEN。" });
        }

        try
        {
            var depth = request.Depth > 0
                ? request.Depth
                : await _settings.GetIntAsync(SettingsStore.EngineDefaultDepth, 18, ct);

            var multiPv = request.MultiPv > 0
                ? request.MultiPv
                : await _settings.GetIntAsync(SettingsStore.EngineMultiPv, 3, ct);

            var analysis = await _engine.AnalyzeAsync(
                request.Fen,
                request.Moves,
                depth,
                request.MoveTimeMs,
                multiPv,
                ct);

            return Ok(new InstantAnalysisResponse(
                analysis.ScoreCp,
                analysis.ScoreMate,
                analysis.Depth,
                analysis.Nodes,
                analysis.Nps,
                analysis.TimeMs,
                analysis.Pv,
                analysis.Lines,
                analysis.BestMove));
        }
        catch (EngineNotInstalledException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (EngineBusyException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (TimeoutException ex)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { error = ex.Message });
        }
    }

    /// <summary>测试引擎连接。</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        try
        {
            var status = await _engine.GetStatusAsync(ct);
            if (!status.IsInstalled)
            {
                return Conflict(new { error = "尚未安装引擎。" });
            }

            await _engine.AnalyzeAsync(
                "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                null,
                8,
                null,
                1,
                ct);

            var refreshed = await _engine.GetStatusAsync(ct);
            return Ok(new { ok = true, version = refreshed.Version, state = refreshed.State.ToString() });
        }
        catch (EngineNotInstalledException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (EngineBusyException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }
}

public sealed class InstantAnalyzeRequest
{
    public string Fen { get; set; } = "";
    public string? Moves { get; set; }
    public int Depth { get; set; }
    public int MoveTimeMs { get; set; }
    public int MultiPv { get; set; }
}
