using ChessGameD.Web.Data;
using ChessGameD.Web.Models;
using ChessGameD.Web.Services.Auth;
using ChessGameD.Web.Services.Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessGameD.Web.Controllers;

public class GamesController : Controller
{
    private readonly AppDbContext _db;
    private readonly EngineManager _engine;
    private readonly CurrentUser _current;

    public GamesController(AppDbContext db, EngineManager engine, CurrentUser current)
    {
        _db = db;
        _engine = engine;
        _current = current;
    }

    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        // 只列出当前身份看得见的棋局
        var owned = _db.Games.Owned(_current).AsNoTracking();
        var query = owned.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var keyword = q.Trim();
            query = query.Where(g =>
                g.White.Contains(keyword)
                || g.Black.Contains(keyword)
                || (g.Opening != null && g.Opening.Contains(keyword))
                || (g.Event != null && g.Event.Contains(keyword))
                || (g.Eco != null && g.Eco.Contains(keyword)));
        }

        var analyzed = await _db.Analyses
            .AsNoTracking()
            .Where(a => owned.Select(g => g.Id).Contains(a.GameId))
            .Select(a => a.GameId)
            .Distinct()
            .ToListAsync(ct);
        var total = await query.CountAsync(ct);
        var games = await query.OrderByDescending(g => g.ImportedAt).Take(500).ToListAsync(ct);

        var model = new GameListViewModel
        {
            Query = q,
            Total = total,
            Games = games.Select(g => new GameListItem
            {
                Id = g.Id,
                White = g.White,
                Black = g.Black,
                Result = g.Result,
                Date = g.Date,
                Event = g.Event,
                Opening = g.Opening,
                Eco = g.Eco,
                WhiteElo = g.WhiteElo,
                BlackElo = g.BlackElo,
                PlyCount = g.PlyCount,
                HasAnalysis = analyzed.Contains(g.Id),
                ImportedAt = g.ImportedAt,
            }).ToList(),
        };

        return View(model);
    }

    public async Task<IActionResult> Review(int id, CancellationToken ct)
    {
        var game = await _db.Games
            .Owned(_current)
            .AsNoTracking()
            .Include(g => g.Moves)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        if (game is null)
        {
            return NotFound();
        }

        var engineStatus = await _engine.GetStatusAsync(ct);
        var moves = game.Moves.OrderBy(m => m.Ply).ToList();

        var model = new ReviewViewModel
        {
            GameId = game.Id,
            White = game.White,
            Black = game.Black,
            Result = game.Result,
            Date = game.Date,
            Event = game.Event,
            Site = game.Site,
            Opening = game.Opening,
            Eco = game.Eco,
            TimeControl = game.TimeControl,
            Termination = game.Termination,
            WhiteElo = game.WhiteElo,
            BlackElo = game.BlackElo,
            PlyCount = game.PlyCount,
            PgnText = game.PgnText,
            Moves = moves.Select(m => new ReviewMove(m.Ply, m.San, m.Uci, m.Fen, m.Comment)).ToList(),
            EngineInstalled = engineStatus.IsInstalled,
            EngineVersion = engineStatus.Version,
        };

        return View(model);
    }
}
