using System.Net;
using System.Text.Json;
using ChessGameD.Web.Services.Engine;
using ChessGameD.Web.Services.Play;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>指导对弈的接口:开局 → 走子 → 悔棋 → 终局,以及归属隔离。</summary>
public sealed class PlayApiTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly AppFactory _factory;

    public PlayApiTests()
    {
        // 陪练引擎换成假的:测试不拉真实 Stockfish
        _factory = new AppFactory(services =>
        {
            services.RemoveAll<ICoachEngine>();
            services.AddSingleton<ICoachEngine>(new FakeCoachEngine());
        });
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task 开局_走一步_悔棋_终局_全流程()
    {
        using var session = WebSession.Create(_factory);

        var create = await session.PostJsonAsync("/api/play/sessions", new { level = 2 });
        var createText = await create.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = JsonSerializer.Deserialize<SessionResponse>(createText, JsonOpts);
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);
        Assert.Equal(3, created.HintsLeft);
        Assert.Equal(3, created.TakebacksLeft);
        Assert.Empty(created.Moves);

        var move = await session.PostJsonAsync($"/api/play/sessions/{created.Id}/move", new { uci = "e2e4" });
        var moveText = await move.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        var afterMove = JsonSerializer.Deserialize<MoveResponse>(moveText, JsonOpts);
        Assert.NotNull(afterMove);
        Assert.Equal(2, afterMove!.Session.Moves.Count);
        Assert.Equal("e4", afterMove.Session.Moves[0].San);

        var takeback = await session.PostJsonAsync($"/api/play/sessions/{created.Id}/takeback", new { });
        var takebackText = await takeback.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, takeback.StatusCode);
        var afterTakeback = JsonSerializer.Deserialize<MoveResponse>(takebackText, JsonOpts);
        Assert.NotNull(afterTakeback);
        Assert.Empty(afterTakeback!.Session.Moves);
        Assert.Equal(2, afterTakeback.Session.TakebacksLeft);

        var finish = await session.PostJsonAsync($"/api/play/sessions/{created.Id}/finish", new
        {
            result = "1-0",
            termination = "认输",
        });
        var finishText = await finish.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        var finished = JsonSerializer.Deserialize<FinishResponse>(finishText, JsonOpts);
        Assert.NotNull(finished);
        Assert.True(finished!.GameId > 0);

        // 终局之后能进复盘页(Razor 会把中文转成数字实体,断言前先解码)
        var review = await session.GetAsync($"/Games/Review/{finished.GameId}");
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var reviewHtml = WebUtility.HtmlDecode(await review.Content.ReadAsStringAsync());
        Assert.Contains("指导对弈", reviewHtml);
    }

    [Fact]
    public async Task 求提示_看评估不扣次数_看候选扣一次()
    {
        using var session = WebSession.Create(_factory);

        var create = await session.PostJsonAsync("/api/play/sessions", new { level = 2 });
        var created = JsonSerializer.Deserialize<SessionResponse>(await create.Content.ReadAsStringAsync(), JsonOpts);
        Assert.NotNull(created);

        var free = await session.PostJsonAsync($"/api/play/sessions/{created!.Id}/hint", new { strength = "eval" });
        var freeText = await free.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, free.StatusCode);
        var freeHint = JsonSerializer.Deserialize<HintResponse>(freeText, JsonOpts);
        Assert.NotNull(freeHint);
        Assert.False(freeHint!.Hint.Consumed);
        Assert.Equal(3, freeHint.Session.HintsLeft);

        var paid = await session.PostJsonAsync($"/api/play/sessions/{created.Id}/hint", new { strength = "candidates" });
        var paidText = await paid.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var paidHint = JsonSerializer.Deserialize<HintResponse>(paidText, JsonOpts);
        Assert.NotNull(paidHint);
        Assert.True(paidHint!.Hint.Consumed);
        Assert.Equal(2, paidHint.Session.HintsLeft);
    }

    [Fact]
    public async Task 非法走法_返回400()
    {
        using var session = WebSession.Create(_factory);

        var create = await session.PostJsonAsync("/api/play/sessions", new { level = 2 });
        var created = JsonSerializer.Deserialize<SessionResponse>(await create.Content.ReadAsStringAsync(), JsonOpts);

        var move = await session.PostJsonAsync($"/api/play/sessions/{created!.Id}/move", new { uci = "e2e5" });
        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);
    }

    [Fact]
    public async Task 跨用户_看不到别人的对局()
    {
        using var owner = WebSession.Create(_factory);
        using var other = WebSession.Create(_factory);

        var create = await owner.PostJsonAsync("/api/play/sessions", new { level = 2 });
        var created = JsonSerializer.Deserialize<SessionResponse>(await create.Content.ReadAsStringAsync(), JsonOpts);
        Assert.NotNull(created);

        var read = await other.GetAsync($"/api/play/sessions/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        var move = await other.PostJsonAsync($"/api/play/sessions/{created.Id}/move", new { uci = "e2e4" });
        Assert.Equal(HttpStatusCode.NotFound, move.StatusCode);
    }

    [Fact]
    public async Task 对弈页面_能打开()
    {
        using var session = WebSession.Create(_factory);

        var page = await session.GetAsync("/Play/Index");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Contains("id=\"board\"", html);
        Assert.Contains("指导对弈", html);
        // 「去复盘」按钮:没结束之前点它只会给提示
        Assert.Contains("id=\"btn-review\"", html);
    }

    /* ---------------- 响应模型 ---------------- */

    private sealed class SessionResponse
    {
        public int Id { get; set; }
        public int HintsLeft { get; set; }
        public int TakebacksLeft { get; set; }
        public List<MoveView> Moves { get; set; } = new();
    }

    private sealed class MoveView
    {
        public int Ply { get; set; }
        public string San { get; set; } = "";
    }

    private sealed class MoveResponse
    {
        public SessionResponse Session { get; set; } = new();
    }

    private sealed class HintResponse
    {
        public HintView Hint { get; set; } = new();
        public SessionResponse Session { get; set; } = new();
    }

    private sealed class HintView
    {
        public bool Consumed { get; set; }
    }

    private sealed class FinishResponse
    {
        public int GameId { get; set; }
    }
}
