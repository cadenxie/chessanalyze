using ChessGameD.Web.Data;
using ChessGameD.Web.Models;
using ChessGameD.Web.Services.Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessGameD.Web.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;
    private readonly EngineManager _engine;

    public HomeController(AppDbContext db, EngineManager engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var engineStatus = await _engine.GetStatusAsync(ct);

        var analyzed = await _db.Analyses
            .AsNoTracking()
            .Select(a => a.GameId)
            .Distinct()
            .ToListAsync(ct);

        var recent = await _db.Games
            .AsNoTracking()
            .OrderByDescending(g => g.ImportedAt)
            .Take(10)
            .ToListAsync(ct);

        var total = await _db.Games.CountAsync(ct);

        var model = new HomeViewModel
        {
            Engine = engineStatus,
            TotalGames = total,
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
