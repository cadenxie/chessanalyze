using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Analysis;
using ChessGameD.Web.Services.Reports;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>服务端 UCI → SAN 转换。</summary>
public class SanConverterTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string CastlingFen = "r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1";

    /// <summary>1.e4 d5 之后的局面:白方可以走 exd5。</summary>
    private const string AfterE4D5Fen = "rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2";

    [Theory]
    [InlineData("e2e4", "e4")]
    [InlineData("g1f3", "Nf3")]
    [InlineData("b1c3", "Nc3")]
    [InlineData("d2d4", "d4")]
    public void ToSan_ConvertsSimpleMoves(string uci, string expected)
    {
        Assert.Equal(expected, SanConverter.ToSan(StartFen, uci));
    }

    [Fact]
    public void ToSan_ConvertsCapture()
    {
        Assert.Equal("exd5", SanConverter.ToSan(AfterE4D5Fen, "e4d5"));
    }

    [Theory]
    [InlineData("e1g1", "O-O")]
    [InlineData("e1c1", "O-O-O")]
    public void ToSan_ConvertsCastling(string uci, string expected)
    {
        Assert.Equal(expected, SanConverter.ToSan(CastlingFen, uci));
    }

    [Fact]
    public void ToSan_ConvertsPromotion()
    {
        var fen = "8/P6k/8/8/8/8/8/K7 w - - 0 1";
        Assert.Equal("a8=Q", SanConverter.ToSan(fen, "a7a8q"));
    }

    [Fact]
    public void ToSan_IllegalMove_ReturnsNull()
    {
        // 白兵不能从 e2 直接走到 e5
        Assert.Null(SanConverter.ToSan(StartFen, "e2e5"));
    }

    [Fact]
    public void ToSan_InvalidInput_ReturnsNull()
    {
        Assert.Null(SanConverter.ToSan(StartFen, null));
        Assert.Null(SanConverter.ToSan(StartFen, ""));
        Assert.Null(SanConverter.ToSan(null, "e2e4"));
        Assert.Null(SanConverter.ToSan("这不是 FEN", "e2e4"));
    }

    [Fact]
    public void ToSanList_ConvertsWholeLine()
    {
        var list = SanConverter.ToSanList(StartFen, "e2e4 e7e5 g1f3");
        Assert.Equal(new[] { "e4", "e5", "Nf3" }, list);
    }

    [Fact]
    public void ToSanList_StopsAtIllegalMove()
    {
        var list = SanConverter.ToSanList(StartFen, "e2e4 e2e5 g1f3");
        Assert.Equal(new[] { "e4" }, list);
    }
}

/// <summary>「漏着」判定。</summary>
public class MissedWinTests
{
    [Fact]
    public void IsMissedWin_ThrowingAwayAWin_IsTrue()
    {
        // 走之前 +2.00,走之后 +0.10 → 本来在赢,却把胜势送掉了
        Assert.True(MoveClassifier.IsMissedWin(200, 10));
    }

    [Fact]
    public void IsMissedWin_StillWinning_IsFalse()
    {
        Assert.False(MoveClassifier.IsMissedWin(200, 100));
    }

    [Fact]
    public void IsMissedWin_WasNotWinning_IsFalse()
    {
        Assert.False(MoveClassifier.IsMissedWin(100, 10));
    }

    [Fact]
    public void IsMissedWin_LostAMate_IsTrue()
    {
        // 杀棋折算后约等于 ±10000,这里表示"本来能杀却没杀"
        Assert.True(MoveClassifier.IsMissedWin(10000, 10));
        Assert.False(MoveClassifier.IsMissedWin(10000, 9800));
    }

    [Fact]
    public void Classify_MissedWinOverridesLossBasedClass()
    {
        Assert.Equal(MoveClass.MissedWin,
            MoveClassifier.Classify(190, isEngineTopMove: false, inTopCandidates: true, isMissedWin: true));
    }

    [Fact]
    public void Classify_EngineTopMoveStaysBest()
    {
        Assert.Equal(MoveClass.Best,
            MoveClassifier.Classify(190, isEngineTopMove: true, inTopCandidates: true, isMissedWin: true));
    }

    [Fact]
    public void ToLabel_AndCssClass_MissedWin()
    {
        Assert.Equal("漏着", MoveClassifier.ToLabel(MoveClass.MissedWin));
        Assert.Equal("mc-missed", MoveClassifier.ToCssClass(MoveClass.MissedWin));
    }
}

/// <summary>报告生成。</summary>
public class ReportBuilderTests
{
    private static Game MakeGame() => new()
    {
        Id = 7,
        White = "白棋手",
        Black = "黑棋手",
        Result = "1-0",
        Date = "2026.01.01",
        Event = "测试赛",
        Opening = "西班牙开局",
        Eco = "C60",
        PlyCount = 4,
        PgnText = "1. e4 e5 2. Nf3 Nc6",
    };

    private static List<GameMove> MakeMoves() => new()
    {
        new GameMove { Ply = 0, San = "", Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1" },
        new GameMove { Ply = 1, San = "e4", Uci = "e2e4", Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1" },
        new GameMove { Ply = 2, San = "e5", Uci = "e7e5", Fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2" },
        new GameMove { Ply = 3, San = "Nf3", Uci = "g1f3", Fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 3" },
        new GameMove { Ply = 4, San = "Nc6", Uci = "b8c6", Fen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 4" },
    };

    private static AnalysisSummary MakeSummary() => new(
        7,
        "Stockfish 19",
        new List<AnalysisPoint>
        {
            new(0, 20, null, 12),
            new(1, 30, null, 12),
            new(2, 25, null, 12),
            new(3, 40, null, 12),
            new(4, 35, null, 12),
        },
        new List<MoveClassification>
        {
            new(1, "e4", MoveClass.Best, 0, true, true, "e2e4", "e4", 20, 30),
            new(2, "e5", MoveClass.Good, 5, false, true, "c7c5", "c5", -25, -25),
            new(3, "Nf3", MoveClass.MissedWin, 190, false, false, "d2d4", "d4", 250, 60),
            new(4, "Nc6", MoveClass.Excellent, 15, false, true, "b8c6", "Nc6", -35, -35),
        });

    [Fact]
    public void BuildHtml_ContainsGameInfoMovesAndChart()
    {
        var html = ReportBuilder.BuildHtml(MakeGame(), MakeMoves(), MakeSummary());

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("白棋手", html);
        Assert.Contains("黑棋手", html);
        Assert.Contains("西班牙开局", html);
        Assert.Contains("Stockfish 19", html);
        Assert.Contains("Nf3", html);
        Assert.Contains("漏着", html);
        Assert.Contains("<svg", html);
        // 引擎推荐着法(逐步)
        Assert.Contains(">d4<", html);
    }

    [Fact]
    public void BuildHtml_EscapesUserContent()
    {
        var game = MakeGame();
        game.White = "<script>alert(1)</script>";

        var html = ReportBuilder.BuildHtml(game, MakeMoves(), MakeSummary());

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void BuildHtml_WithoutAnalysis_StillProducesReport()
    {
        var html = ReportBuilder.BuildHtml(MakeGame(), MakeMoves(), null);

        Assert.Contains("白棋手", html);
        Assert.Contains("尚未分析", html);
        Assert.Contains("Nf3", html);
    }

    [Fact]
    public void BuildText_ContainsSectionsAndMoves()
    {
        var text = ReportBuilder.BuildText(MakeGame(), MakeMoves(), MakeSummary());

        Assert.Contains("复盘报告", text);
        Assert.Contains("逐步分析", text);
        Assert.Contains("关键问题", text);
        Assert.Contains("漏着", text);
        Assert.Contains("Nf3", text);
    }

    [Fact]
    public void BuildText_WithoutAnalysis_StillProducesReport()
    {
        var text = ReportBuilder.BuildText(MakeGame(), MakeMoves(), null);

        Assert.Contains("复盘报告", text);
        Assert.Contains("尚未分析", text);
    }
}
