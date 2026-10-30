using ChessGameD.Web.Data;
using ChessGameD.Web.Models;
using ChessGameD.Web.Services.Engine;
using ChessGameD.Web.Services.Play;
using Microsoft.AspNetCore.Mvc;

namespace ChessGameD.Web.Controllers;

/// <summary>指导对弈页面。</summary>
public class PlayController : Controller
{
    private readonly EngineManager _engine;
    private readonly SettingsStore _settings;

    public PlayController(EngineManager engine, SettingsStore settings)
    {
        _engine = engine;
        _settings = settings;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var engineStatus = await _engine.GetStatusAsync(ct);

        var model = new PlayViewModel
        {
            EngineInstalled = engineStatus.IsInstalled,
            EngineVersion = engineStatus.Version,
            DefaultLevel = CoachLevels.Default,
            HintQuota = Math.Clamp(await _settings.GetIntAsync(SettingsStore.PlayHintQuota, 3, ct), 0, 10),
            TakebackQuota = Math.Clamp(await _settings.GetIntAsync(SettingsStore.PlayTakebackQuota, 3, ct), 0, 10),
            Levels = CoachLevels.All
                .Select(l => new CoachLevelOption
                {
                    Level = l.Level,
                    Name = l.Name,
                    Description = l.Description,
                })
                .ToList(),
        };

        return View(model);
    }
}
