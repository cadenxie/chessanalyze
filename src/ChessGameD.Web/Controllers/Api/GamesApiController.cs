using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Analysis;
using ChessGameD.Web.Services.Engine;
using ChessGameD.Web.Services.Pgn;
using ChessGameD.Web.Services.Reports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace ChessGameD.Web.Controllers.Api;

[ApiController]
[Route("api/games")]
public class GamesApiController : ControllerBase
{
    private const long MaxUploadBytes = 20L * 1024 * 1024;

    private readonly PgnImportService _importer;
    private readonly GameAnalyzer _analyzer;
    private readonly AppDbContext _db;

    public GamesApiController(PgnImportService importer, GameAnalyzer analyzer, AppDbContext db)
    {
        _importer = importer;
        _analyzer = analyzer;
        _db = db;
    }

    /// <summary>导入 PGN(文件或文本)。</summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile? file, [FromForm] string? text, CancellationToken ct)
    {
        string content;
        string fileName;

        if (file is not null && file.Length > 0)
        {
            if (file.Length > MaxUploadBytes)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    error = "文件超过 20MB 上限。",
                });
            }

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            content = PgnSanitizer.Decode(buffer.ToArray());
            fileName = file.FileName;
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            if (text.Length > MaxUploadBytes)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    error = "内容超过 20MB 上限。",
                });
            }

            content = text;
            fileName = "粘贴的 PGN";
        }
        else
        {
            return BadRequest(new { error = "请上传 PGN 文件,或粘贴 PGN 内容。" });
        }

        var result = await _importer.ImportAsync(content, fileName, ct);

        return Ok(new
        {
            batchId = result.BatchId,
            total = result.TotalGames,
            imported = result.ImportedGames,
            skipped = result.SkippedGames,
            errors = result.Errors,
        });
    }

    [HttpGet("{id:int}/moves")]
    public async Task<IActionResult> Moves(int id, CancellationToken ct)
    {
        var game = await _db.Games
            .AsNoTracking()
            .Include(g => g.Moves)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        if (game is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            id = game.Id,
            white = game.White,
            black = game.Black,
            result = game.Result,
            pgn = game.PgnText,
            moves = game.Moves
                .OrderBy(m => m.Ply)
                .Select(m => new { m.Ply, m.San, m.Uci, m.Fen, m.Comment })
                .ToList(),
        });
    }

    /// <summary>启动整局分析(后台执行)。</summary>
    [HttpPost("{id:int}/analyze")]
    public async Task<IActionResult> Analyze(int id, [FromQuery] bool force = false, CancellationToken ct = default)
    {
        var exists = await _db.Games.AnyAsync(g => g.Id == id, ct);
        if (!exists)
        {
            return NotFound();
        }

        var status = await _analyzer.StartAsync(id, force, ct);
        return Accepted(status);
    }

    [HttpPost("{id:int}/analyze/stop")]
    public IActionResult StopAnalyze(int id)
    {
        _analyzer.Stop(id);
        return Ok(new { ok = true });
    }

    [HttpGet("{id:int}/analysis/status")]
    public IActionResult AnalysisStatus(int id) => Ok(_analyzer.GetStatus(id));

    [HttpGet("{id:int}/analysis")]
    public async Task<IActionResult> Analysis(int id, CancellationToken ct)
    {
        var summary = await _analyzer.GetResultAsync(id, ct);
        if (summary is null)
        {
            return NotFound(new { error = "该棋局还没有分析结果。" });
        }

        return Ok(new
        {
            gameId = summary.GameId,
            engineVersion = summary.EngineVersion,
            points = summary.Points,
            classifications = summary.Classifications.Select(c => new
            {
                c.Ply,
                c.San,
                @class = c.Class.ToString(),
                label = MoveClassifier.ToLabel(c.Class),
                cssClass = MoveClassifier.ToCssClass(c.Class),
                c.LossCp,
                c.IsEngineTopMove,
                c.InTopCandidates,
                c.BestMoveUci,
                c.BestMoveSan,
                c.EvalBeforeCp,
                c.EvalAfterCp,
            }),
        });
    }

    /// <summary>导出复盘报告(HTML 或纯文本)。</summary>
    [HttpGet("{id:int}/report")]
    public async Task<IActionResult> Report(int id, [FromQuery] string format = "html", CancellationToken ct = default)
    {
        var game = await _db.Games
            .AsNoTracking()
            .Include(g => g.Moves)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        if (game is null)
        {
            return NotFound(new { error = "棋局不存在。" });
        }

        var moves = game.Moves.OrderBy(m => m.Ply).ToList();
        var summary = await _analyzer.GetResultAsync(id, ct);
        var fileName = $"review-{game.Id}-{DateTime.Now:yyyyMMdd}";

        if (format.Equals("text", StringComparison.OrdinalIgnoreCase)
            || format.Equals("txt", StringComparison.OrdinalIgnoreCase))
        {
            var text = ReportBuilder.BuildText(game, moves, summary);
            var bytes = new UTF8Encoding(false).GetBytes(text);
            return File(bytes, "text/plain; charset=utf-8", fileName + ".txt");
        }

        var html = ReportBuilder.BuildHtml(game, moves, summary);
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var game = await _db.Games.FindAsync(new object?[] { id }, ct);
        if (game is null)
        {
            return NotFound();
        }

        _db.Games.Remove(game);
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }
}
