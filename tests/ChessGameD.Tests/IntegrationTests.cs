using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ChessGameD.Tests;

public class IntegrationTests : IDisposable
{
    /// <summary>API 输出为驼峰 JSON,反序列化时必须忽略大小写。</summary>
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly AppFactory _factory;

    public IntegrationTests()
    {
        _factory = new AppFactory();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Import_ThenList_ThenReview_Works()
    {
        using var session = WebSession.Create(_factory);

        var import = await session.PostTextFieldAsync("/api/games/import", "text", TestPgn.SimpleGame);
        var importBodyText = await import.Content.ReadAsStringAsync();
        Assert.True(import.StatusCode == HttpStatusCode.OK, $"HTTP {(int)import.StatusCode}: {importBodyText}");

        var importBody = JsonSerializer.Deserialize<ImportResponse>(importBodyText, JsonOpts);
        Assert.NotNull(importBody);
        Assert.True(importBody!.Total == 1, $"应识别出 1 局,实际 {importBody.Total}: {importBodyText}");
        Assert.True(importBody.Imported == 1, $"应导入 1 局,实际 {importBody.Imported}: {importBodyText}");
        Assert.Equal(0, importBody.Skipped);

        var list = await session.GetAsync("/api/games/1/moves");
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listData = JsonSerializer.Deserialize<ListResponse>(listBody, JsonOpts);
        Assert.NotNull(listData);
        Assert.Equal("Alice", listData!.White);
        Assert.Equal(7, listData.Moves.Count);
        Assert.Equal("e4", listData.Moves[1].San);

        var home = await session.GetAsync("/Home/Index");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var homeHtml = await home.Content.ReadAsStringAsync();
        Assert.Contains("导入 PGN", homeHtml);

        var review = await session.GetAsync("/Games/Review/1");
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var reviewHtml = await review.Content.ReadAsStringAsync();
        Assert.Contains("Alice", reviewHtml);
        Assert.Contains("Bob", reviewHtml);
        Assert.Contains("id=\"board\"", reviewHtml);
    }

    [Fact]
    public async Task ReviewPage_EmbedsCamelCaseJson()
    {
        // 复盘页把走法数据内嵌成 JSON 给前端用。前端读的是 move.fen / move.san,
        // 如果这里序列化成 PascalCase,棋盘会没有棋子、走法列表也会是空的。
        using var session = WebSession.Create(_factory);
        var import = await session.PostTextFieldAsync("/api/games/import", "text", TestPgn.SimpleGame);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);

        var review = await session.GetAsync("/Games/Review/1");
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var html = await review.Content.ReadAsStringAsync();

        var start = html.IndexOf("id=\"game-data\"", StringComparison.Ordinal);
        Assert.True(start > 0, "复盘页缺少 game-data 数据块。");
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        Assert.True(end > start, "game-data 数据块没有正确闭合。");
        var payload = html[start..end];

        Assert.Contains("\"fen\":", payload);
        Assert.Contains("\"san\":", payload);
        Assert.Contains("\"uci\":", payload);
        Assert.DoesNotContain("\"Fen\":", payload);
        Assert.DoesNotContain("\"San\":", payload);
    }

    [Fact]
    public async Task Analysis_WithoutEngine_ReturnsNotFound()
    {
        using var session = WebSession.Create(_factory);

        var import = await session.PostTextFieldAsync("/api/games/import", "text", TestPgn.ThreeGames);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);

        var response = await session.GetAsync("/api/games/1/analysis");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EngineStatus_ReportsNotInstalled()
    {
        using var session = WebSession.Create(_factory);

        var status = await session.GetAsync("/api/engine/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var body = await status.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<EngineStatusResponse>(body, JsonOpts);
        Assert.NotNull(data);
        Assert.Equal("NotInstalled", data!.State);
        Assert.False(data.IsInstalled);
    }

    [Fact]
    public async Task InstantAnalyze_WithoutEngine_ReturnsConflict()
    {
        using var session = WebSession.Create(_factory);

        var response = await session.PostJsonAsync("/api/engine/analyze", new
        {
            fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GamesIndex_RendersList()
    {
        using var session = WebSession.Create(_factory);
        await session.PostTextFieldAsync("/api/games/import", "text", TestPgn.ThreeGames);

        var page = await session.GetAsync("/Games/Index");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("A1", html);
        Assert.Contains("棋局列表", html);

        // 徽章是 HTML 片段,必须原样输出而不是被转义成文本
        Assert.Contains("<span class=\"badge\">未分析</span>", html);
        Assert.DoesNotContain("&lt;span class=", html);
    }

    [Fact]
    public async Task Report_Html_ReturnsSelfContainedReport()
    {
        using var session = WebSession.Create(_factory);
        await session.PostTextFieldAsync("/api/games/import", "text", TestPgn.SimpleGame);

        var response = await session.GetAsync("/api/games/1/report?format=html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("复盘报告", html);
        Assert.Contains("Alice", html);
        Assert.Contains("Bb5", html);

        // 自包含:报告要能直接发给别人,不能依赖外部资源
        Assert.DoesNotContain("<link rel=\"stylesheet\"", html);
        Assert.DoesNotContain("<script src=", html);
    }

    [Fact]
    public async Task Report_Text_ReturnsDownloadableAttachment()
    {
        using var session = WebSession.Create(_factory);
        await session.PostTextFieldAsync("/api/games/import", "text", TestPgn.SimpleGame);

        var response = await session.GetAsync("/api/games/1/report?format=text");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("复盘报告", text);
        Assert.Contains("逐步分析", text);
        Assert.Contains("Bb5", text);
    }

    [Fact]
    public async Task Report_UnknownGame_ReturnsNotFound()
    {
        using var session = WebSession.Create(_factory);
        var response = await session.GetAsync("/api/games/999/report?format=html");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Import_WithoutCsrfToken_ReturnsBadRequest()
    {
        // 写操作必须带防伪 token,前端统一在 CG.fetchJson 里加
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

        var multi = new MultipartFormDataContent();
        multi.Add(new StringContent(TestPgn.SimpleGame), "text");
        var response = await client.PostAsync("/api/games/import", multi);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        client.Dispose();
    }

    private sealed class ImportResponse
    {
        public int BatchId { get; set; }
        public int Total { get; set; }
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    private sealed class MoveDto
    {
        public int Ply { get; set; }
        public string San { get; set; } = "";
        public string? Uci { get; set; }
        public string Fen { get; set; } = "";
        public string? Comment { get; set; }
    }

    private sealed class ListResponse
    {
        public string White { get; set; } = "";
        public List<MoveDto> Moves { get; set; } = new();
    }

    private sealed class EngineStatusResponse
    {
        public string State { get; set; } = "";
        public bool IsInstalled { get; set; }
        public bool IsReady { get; set; }
    }
}
