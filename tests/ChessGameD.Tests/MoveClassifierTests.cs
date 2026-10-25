using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Analysis;
using Xunit;

namespace ChessGameD.Tests;

public class MoveClassifierTests
{
    [Theory]
    [InlineData(0, MoveClass.Best)]
    [InlineData(10, MoveClass.Best)]
    [InlineData(11, MoveClass.Excellent)]
    [InlineData(20, MoveClass.Excellent)]
    [InlineData(21, MoveClass.Good)]
    [InlineData(50, MoveClass.Good)]
    [InlineData(51, MoveClass.Inaccuracy)]
    [InlineData(100, MoveClass.Inaccuracy)]
    [InlineData(101, MoveClass.Mistake)]
    [InlineData(300, MoveClass.Mistake)]
    [InlineData(301, MoveClass.Blunder)]
    [InlineData(900, MoveClass.Blunder)]
    public void FromLoss_UsesThresholds(int loss, MoveClass expected)
    {
        Assert.Equal(expected, MoveClassifier.FromLoss(loss));
    }

    [Fact]
    public void Classify_EngineTopMoveIsAlwaysBest()
    {
        Assert.Equal(MoveClass.Best, MoveClassifier.Classify(250, isEngineTopMove: true, inTopCandidates: true));
    }

    [Fact]
    public void Classify_NotInTopCandidatesDowngradesOneStep()
    {
        // 损失 30cp 属于"良好",不在候选里则降一档为"失误"
        Assert.Equal(MoveClass.Good, MoveClassifier.Classify(30, isEngineTopMove: false, inTopCandidates: true));
        Assert.Equal(MoveClass.Inaccuracy, MoveClassifier.Classify(30, isEngineTopMove: false, inTopCandidates: false));
    }

    [Fact]
    public void Classify_BlunderStaysBlunderAfterDowngrade()
    {
        Assert.Equal(MoveClass.Blunder, MoveClassifier.Classify(500, isEngineTopMove: false, inTopCandidates: false));
    }

    [Fact]
    public void Classify_InTopCandidateKeepsLossBasedClass()
    {
        Assert.Equal(MoveClass.Mistake, MoveClassifier.Classify(200, isEngineTopMove: false, inTopCandidates: true));
    }
}

public class MoveLossTests
{
    [Fact]
    public void ComputeLoss_WhiteMove_WhiteBlunder()
    {
        // 白方走之前 +0.5,走完之后变成 -2.0 → 白方损失 250cp
        var before = new Analysis { ScoreCp = 50 };
        var after = new Analysis { ScoreCp = -200 };

        Assert.Equal(250, GameAnalyzer.ComputeLoss(before, after, movePly: 1));
    }

    [Fact]
    public void ComputeLoss_BlackMove_BlackBlunder()
    {
        // 黑方走之前白方 +0.5,黑方走完之后白方 +2.0 → 黑方损失 150cp
        var before = new Analysis { ScoreCp = 50 };
        var after = new Analysis { ScoreCp = 200 };

        Assert.Equal(150, GameAnalyzer.ComputeLoss(before, after, movePly: 2));
    }

    [Fact]
    public void ComputeLoss_BlackMove_GoodMoveGivesNegativeLoss()
    {
        // 黑方走出好棋:白方 +2.0 → +0.5,黑方获益 → loss 为负
        var before = new Analysis { ScoreCp = 200 };
        var after = new Analysis { ScoreCp = 50 };

        Assert.Equal(-150, GameAnalyzer.ComputeLoss(before, after, movePly: 2));
    }

    [Fact]
    public void EvalToCp_ConvertsMateScores()
    {
        Assert.Equal(10000, GameAnalyzer.EvalToCp(null, 1));
        Assert.Equal(9900, GameAnalyzer.EvalToCp(null, 2));
        Assert.Equal(-10000, GameAnalyzer.EvalToCp(null, -1));
        Assert.Equal(120, GameAnalyzer.EvalToCp(120, null));
    }
}
