using ChessGameD.Web.Data;
using ChessGameD.Web.Models;
using ChessGameD.Web.Services.Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ChessGameD.Web.Controllers;

public sealed class SettingsForm
{
    public string? EnginePath { get; set; }
    public int Threads { get; set; }
    public int HashMb { get; set; }
    public int MultiPv { get; set; }
    public int DefaultDepth { get; set; }
    public int AnalysisDepth { get; set; }
    public string? ProxyUrl { get; set; }
}

public class SettingsController : Controller
{
    private readonly SettingsStore _settings;
    private readonly EngineManager _engine;
    private readonly EngineInstaller _installer;
    private readonly ChessOptions _options;

    public SettingsController(
        SettingsStore settings,
        EngineManager engine,
        EngineInstaller installer,
        IOptions<ChessOptions> options)
    {
        _settings = settings;
        _engine = engine;
        _installer = installer;
        _options = options.Value;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var proxyUrl = await _settings.GetAsync(SettingsStore.ProxyUrl, ct);
        if (string.IsNullOrWhiteSpace(proxyUrl))
        {
            proxyUrl = _options.Engine.ProxyUrl;
        }

        var model = new SettingsViewModel
        {
            Engine = await _engine.GetStatusAsync(ct),
            EnginePath = await _settings.GetAsync(SettingsStore.EnginePath, ct),
            Threads = await _settings.GetIntAsync(SettingsStore.EngineThreads, 0, ct),
            HashMb = await _settings.GetIntAsync(SettingsStore.EngineHashMb, 256, ct),
            MultiPv = await _settings.GetIntAsync(SettingsStore.EngineMultiPv, 3, ct),
            DefaultDepth = await _settings.GetIntAsync(SettingsStore.EngineDefaultDepth, 18, ct),
            AnalysisDepth = await _settings.GetIntAsync(SettingsStore.EngineAnalysisDepth, 16, ct),
            EngineInstalledAt = await _settings.GetAsync(SettingsStore.EngineInstalledAt, ct),
            ProxyUrl = proxyUrl,
        };

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SettingsForm form, CancellationToken ct)
    {
        var path = form.EnginePath?.Trim();
        if (!string.IsNullOrEmpty(path))
        {
            if (!System.IO.File.Exists(path))
            {
                return BadRequest(new { error = $"引擎文件不存在: {path}" });
            }

            await _settings.SetAsync(SettingsStore.EnginePath, Path.GetFullPath(path), ct);

            try
            {
                await _engine.RestartAsync(ct);
            }
            catch (Exception ex)
            {
                // 路径已经记下了,只是引擎没起来;告诉用户原因,不要整次保存都失败
                return Json(new
                {
                    ok = false,
                    error = $"引擎路径已保存,但引擎启动失败: {ex.Message}",
                });
            }
        }

        await _settings.SetIntAsync(SettingsStore.EngineThreads, Math.Clamp(form.Threads, 0, 512), ct);
        await _settings.SetIntAsync(SettingsStore.EngineHashMb, Math.Clamp(form.HashMb, 16, 65536), ct);
        await _settings.SetIntAsync(SettingsStore.EngineMultiPv, Math.Clamp(form.MultiPv, 1, 8), ct);
        await _settings.SetIntAsync(SettingsStore.EngineDefaultDepth, Math.Clamp(form.DefaultDepth, 1, 60), ct);
        await _settings.SetIntAsync(SettingsStore.EngineAnalysisDepth, Math.Clamp(form.AnalysisDepth, 1, 60), ct);

        // 代理留空 = 直连;填了就走代理(设置页优先于 appsettings)
        var proxyUrl = form.ProxyUrl?.Trim();
        await _settings.SetAsync(SettingsStore.ProxyUrl, string.IsNullOrWhiteSpace(proxyUrl) ? null : proxyUrl, ct);

        return Json(new { ok = true });
    }
}
