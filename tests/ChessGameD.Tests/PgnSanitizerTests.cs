using ChessGameD.Web.Services.Pgn;
using Xunit;

namespace ChessGameD.Tests;

public class PgnSanitizerTests
{
    [Fact]
    public void Sanitize_StripsNestedVariations()
    {
        var input = "1. e4 (1. d4 d5 (1... Nf6 2. c4)) 1... e5 2. Nf3";
        var result = PgnSanitizer.Sanitize(input);

        Assert.DoesNotContain("d4", result);
        Assert.DoesNotContain("Nf6", result);
        Assert.Contains("e4", result);
        Assert.Contains("e5", result);
        Assert.Contains("Nf3", result);
    }

    [Fact]
    public void Sanitize_KeepsBraceComments()
    {
        var input = "1. e4 {good move} e5";
        var result = PgnSanitizer.Sanitize(input);

        Assert.Contains("{good move}", result);
    }

    [Fact]
    public void Sanitize_RemovesNagTokens()
    {
        var result = PgnSanitizer.Sanitize("1. e4 $1 e5 $16");
        Assert.DoesNotContain("$1", result);
        Assert.DoesNotContain("$16", result);
        Assert.Contains("e4", result);
        Assert.Contains("e5", result);
    }

    [Fact]
    public void Sanitize_NormalizesMoveNumbers()
    {
        Assert.Equal("1. e4 e5", PgnSanitizer.Sanitize("1.e4 e5"));
        Assert.Equal("1... e5", PgnSanitizer.Sanitize("1...e5"));
        Assert.Equal("12. Nf3", PgnSanitizer.Sanitize("12.Nf3"));
    }

    [Fact]
    public void Sanitize_NormalizesCastlingNotation()
    {
        Assert.Contains("O-O-O", PgnSanitizer.Sanitize("1. O-O-O"));
        Assert.Contains("O-O", PgnSanitizer.Sanitize("1. O-O"));
        Assert.DoesNotContain("0-0-0", PgnSanitizer.Sanitize("1. 0-0-0"));
    }

    [Fact]
    public void Sanitize_KeepsResultTokensIntact()
    {
        Assert.Contains("1/2-1/2", PgnSanitizer.Sanitize("1. e4 e5 1/2-1/2"));
        Assert.Contains("1-0", PgnSanitizer.Sanitize("1. e4 e5 1-0"));
    }

    [Fact]
    public void Sanitize_RemovesEnPassantNotation()
    {
        var result = PgnSanitizer.Sanitize("1. e4 e6 2. exd6 e.p.");
        Assert.DoesNotContain("e.p.", result);
    }

    [Fact]
    public void Sanitize_RemovesCommaSeparators()
    {
        // 部分导出工具用顿号分隔着法,词法器不认,必须先剥掉
        var result = PgnSanitizer.Sanitize("1. e4, e5 2. Nf3, Nc6");
        Assert.DoesNotContain(",", result);
        Assert.Contains("e4", result);
        Assert.Contains("e5", result);
        Assert.Contains("Nf3", result);
        Assert.Contains("Nc6", result);
    }

    [Fact]
    public void Sanitize_RemovesCommaRightAfterMoveNumber()
    {
        var result = PgnSanitizer.Sanitize("1., e4 e5");
        Assert.DoesNotContain(",", result);
        Assert.Contains("e4", result);
    }

    [Fact]
    public void Sanitize_ToleratesUnbalancedParenthesis()
    {
        var result = PgnSanitizer.Sanitize("1. e4 (1. d4 d5 2. c4");
        Assert.DoesNotContain("d4", result);
    }

    [Fact]
    public void SplitGames_SplitsOnTagSections()
    {
        var games = PgnSanitizer.SplitGames(TestPgn.ThreeGames);

        Assert.Equal(3, games.Count);
        Assert.Contains("[Event \"G1\"]", games[0]);
        Assert.Contains("[Event \"G2\"]", games[1]);
        Assert.Contains("[Event \"G3\"]", games[2]);
    }

    [Fact]
    public void SplitGames_KeepsSingleGameIntact()
    {
        var games = PgnSanitizer.SplitGames(TestPgn.SimpleGame);
        Assert.Single(games);
    }

    [Fact]
    public void EnsureEventTag_AddsMissingTag()
    {
        var result = PgnSanitizer.EnsureEventTag("[White \"A\"]\n\n1. e4");
        Assert.StartsWith("[Event \"?\"]", result);
    }

    [Fact]
    public void EnsureEventTag_KeepsExistingTag()
    {
        var input = "[Event \"Keep\"]\n\n1. e4";
        Assert.Equal(input, PgnSanitizer.EnsureEventTag(input));
    }

    [Fact]
    public void Decode_FallsBackToGbk()
    {
        var gbk = System.Text.Encoding.GetEncoding(936);
        var bytes = gbk.GetBytes("[Event \"测试\"]\n\n1. e4 e5");

        var result = PgnSanitizer.Decode(bytes);

        Assert.Contains("测试", result);
        Assert.Contains("1. e4 e5", result);
    }

    [Fact]
    public void Decode_HandlesUtf8()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("[Event \"测试\"]");
        Assert.Contains("测试", PgnSanitizer.Decode(bytes));
    }
}
