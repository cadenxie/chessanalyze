using ChessGameD.Web.Data;
using ChessGameD.Web.Models;
using ChessGameD.Web.Services.Auth;
using ChessGameD.Web.Services.Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessGameD.Web.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;
    private readonly EngineManager _engine;
    private readonly CurrentUser _current;

    public HomeController(AppDbContext db, EngineManager engine, CurrentUser current)
    {
        _db = db;
        _engine = engine;
        _current = current;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var engineStatus = await _engine.GetStatusAsync(ct);

        // 只统计当前身份看得见的棋局
        var owned = _db.Games.Owned(_current).AsNoTracking();

        var analyzed = await _db.Analyses
            .AsNoTracking()
            .Where(a => owned.Select(g => g.Id).Contains(a.GameId))
            .Select(a => a.GameId)
            .Distinct()
            .ToListAsync(ct);

        var recent = await owned
            .OrderByDescending(g => g.ImportedAt)
            .Take(10)
            .ToListAsync(ct);

        var total = await owned.CountAsync(ct);

        var model = new HomeViewModel
        {
            Engine = engineStatus,
            TotalGames = total,
            IsAdmin = _current.IsAdmin,
            RecentGames = recent.Select(g => new GameListItem
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

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
}
