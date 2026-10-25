using ChessGameD.Web.Services.Engine;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>
/// 用一个真实的假 UCI 引擎进程驱动 <see cref="UciEngineClient"/>。
///
/// 假引擎是独立的控制台程序(见 ChessGameD.FakeEngine),不是 .cmd 批处理:
/// cmd.exe 从管道读 stdin 时连续写入的两行命令有几率被吞掉,会让这里的测试变成偶发失败。
/// </summary>
public class UciEngineClientTests
{
    private static string FakeEnginePath => Path.Combine(
        AppContext.BaseDirectory, "fakeengine", "ChessGameD.FakeEngine.exe");

    private static string EnginePath =>
        File.Exists(FakeEnginePath)
            ? FakeEnginePath
            : throw new FileNotFoundException("假引擎未构建到测试输出目录。", FakeEnginePath);

    [Fact]
    public async Task Handshake_ReadsEngineNameAndOptions()
    {
        await using var client = new UciEngineClient();

        await client.StartAsync(EnginePath);
        await client.HandshakeAsync();

        Assert.Equal("FakeEngine 1.0", client.EngineName);
        Assert.True(client.Options.ContainsKey("Threads"));
        Assert.True(client.Options.ContainsKey("Hash"));
        Assert.True(client.IsRunning);
    }

    [Fact]
    public async Task Search_ReturnsScoreDepthAndPv()
    {
        await using var client = new UciEngineClient();

        await client.StartAsync(EnginePath);
        await client.HandshakeAsync();

        var result = await client.SearchAsync(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            null,
            new UciSearchOptions { Depth = 10, TimeoutMs = 15000 });

        Assert.Equal(30, result.ScoreCp);
        Assert.Equal(2, result.Depth);
        Assert.Equal(200, result.Nodes);
        Assert.Equal(new[] { "e2e4", "e7e5" }, result.Pv);
        Assert.Equal("e2e4", result.BestMove);
    }

    [Fact]
    public async Task Search_CanBeCancelled()
    {
        await using var client = new UciEngineClient();

        await client.StartAsync(EnginePath, "--mode=nogo");
        await client.HandshakeAsync();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(600));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchAsync(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            null,
            new UciSearchOptions { Depth = 20, TimeoutMs = 30000 },
            cts.Token));
    }

    [Fact]
    public async Task Search_TimesOutWhenEngineNeverAnswers()
    {
        await using var client = new UciEngineClient();

        await client.StartAsync(EnginePath, "--mode=nogo");
        await client.HandshakeAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => client.SearchAsync(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            null,
            new UciSearchOptions { Depth = 20, TimeoutMs = 1200 }));
    }

    [Fact]
    public async Task Dispose_StopsTheEngineProcess()
    {
        var client = new UciEngineClient();

        await client.StartAsync(EnginePath);
        await client.HandshakeAsync();
        Assert.True(client.IsRunning);

        await client.DisposeAsync();
        Assert.False(client.IsRunning);
    }

    [Fact]
    public void ParseInfo_ReadsAllFields()
    {
        var info = UciEngineClient.ParseInfo(
            "info depth 18 seldepth 24 multipv 2 score cp -37 nodes 123456 nps 987654 time 2500 hashfull 12 pv e2e4 e7e5 g1f3");

        Assert.NotNull(info);
        Assert.Equal(18, info!.Depth);
        Assert.Equal(24, info.SelDepth);
        Assert.Equal(2, info.MultiPv);
        Assert.Equal(-37, info.ScoreCp);
        Assert.Equal(123456, info.Nodes);
        Assert.Equal(987654, info.Nps);
        Assert.Equal(2500, info.TimeMs);
        Assert.Equal(new[] { "e2e4", "e7e5", "g1f3" }, info.Pv);
    }

    [Fact]
    public void ParseInfo_ReadsMateScore()
    {
        var info = UciEngineClient.ParseInfo("info depth 12 score mate 3 pv d1h5");

        Assert.NotNull(info);
        Assert.Equal(3, info!.ScoreMate);
        Assert.Null(info.ScoreCp);
    }
}
