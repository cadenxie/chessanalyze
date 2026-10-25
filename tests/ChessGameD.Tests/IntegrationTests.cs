using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChessGameD.Tests;

public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly string _dir;

    public AppFactory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "chessgamed-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={Path.Combine(_dir, "test.db")}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}

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

    private static MultipartFormDataContent BuildImportRequest(string pgn)
    {
        var content = new MultipartFormDataContent();
        // 不传 fileName,这样才是普通表单字段而不是文件
        content.Add(new StringContent(pgn, Encoding.UTF8), "text");
        return content;
    }

    [Fact]
    public async Task Import_ThenList_ThenReview_Works()
    {
        var client = _factory.CreateClient();

        var import = await client.PostAsync("/api/games/import", BuildImportRequest(TestPgn.SimpleGame));
        var importBodyText = await import.Content.ReadAsStringAsync();
        Assert.True(import.StatusCode == HttpStatusCode.OK, $"HTTP {(int)import.StatusCode}: {importBodyText}");

        var importBody = JsonSerializer.Deserialize<ImportResponse>(importBodyText, JsonOpts);
        Assert.NotNull(importBody);
        Assert.True(importBody!.Total == 1, $"应识别出 1 局,实际 {importBody.Total}: {importBodyText}");
        Assert.True(importBody.Imported == 1, $"应导入 1 局,实际 {importBody.Imported}: {importBodyText}");
        Assert.Equal(0, importBody.Skipped);

        var list = await client.GetFromJsonAsync<ListResponse>("/api/games/1/moves");
        Assert.NotNull(list);
        Assert.Equal("Alice", list!.White);
        Assert.Equal(7, list.Moves.Count);
        Assert.Equal("e4", list.Moves[1].San);

        var home = await client.GetAsync("/Home/Index");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var homeHtml = await home.Content.ReadAsStringAsync();
        Assert.Contains("导入 PGN", homeHtml);

        var review = await client.GetAsync("/Games/Review/1");
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
        var client = _factory.CreateClient();
        var import = await client.PostAsync("/api/games/import", BuildImportRequest(TestPgn.SimpleGame));
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);

        var review = await client.GetAsync("/Games/Review/1");
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
        var client = _factory.CreateClient();

        var import = await client.PostAsync("/api/games/import", BuildImportRequest(TestPgn.ThreeGames));
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);

        var response = await client.GetAsync("/api/games/1/analysis");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EngineStatus_ReportsNotInstalled()
    {
        var client = _factory.CreateClient();

        var status = await client.GetFromJsonAsync<EngineStatusResponse>("/api/engine/status");
        Assert.NotNull(status);
        Assert.Equal("NotInstalled", status!.State);
        Assert.False(status.IsInstalled);
    }

    [Fact]
    public async Task InstantAnalyze_WithoutEngine_ReturnsConflict()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/engine/analyze", new
        {
            fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GamesIndex_RendersList()
    {
        var client = _factory.CreateClient();
        await client.PostAsync("/api/games/import", BuildImportRequest(TestPgn.ThreeGames));

        var page = await client.GetAsync("/Games/Index");
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
        var client = _factory.CreateClient();
        await client.PostAsync("/api/games/import", BuildImportRequest(TestPgn.SimpleGame));

        var response = await client.GetAsync("/api/games/1/report?format=html");
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
        var client = _factory.CreateClient();
        await client.PostAsync("/api/games/import", BuildImportRequest(TestPgn.SimpleGame));

        var response = await client.GetAsync("/api/games/1/report?format=text");
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
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/games/999/report?format=html");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
