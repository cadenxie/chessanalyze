namespace ChessGameD.Web.Services.Analysis;

public enum MoveClass
{
    Best = 0,
    Excellent = 1,
    Good = 2,
    Inaccuracy = 3,
    Mistake = 4,
    Blunder = 5,

    /// <summary>漏着:本来在赢,却把胜势送掉了。</summary>
    MissedWin = 6,
}

/// <summary>
/// 走子分类。
///
/// v1 采用"单次搜索"口径:用走子前后的评估差(loss)衡量这一步的损失,
/// 再用引擎候选做修正。精确口径需要对每个局面的 PV 首着再搜一次(耗时翻倍),留作后续。
/// </summary>
public static class MoveClassifier
{
    public const int BestThreshold = 10;
    public const int ExcellentThreshold = 20;
    public const int GoodThreshold = 50;
    public const int InaccuracyThreshold = 100;
    public const int MistakeThreshold = 300;

    /// <summary>「漏着」判定:走子前优势达到这个值(厘兵)算"本来在赢"。</summary>
    public const int MissedWinThreshold = 150;

    /// <summary>「漏着」判定:走完之后优势跌到这个值(厘兵)以下算"胜势没了"。</summary>
    public const int MissedWinDropThreshold = 30;

    /// <summary>
    /// 是否属于「漏着」。
    ///
    /// 两个分值都必须是<b>走子方视角</b>(正数表示对走子方有利)。
    /// 杀棋局面会被折成 ±10000 左右的分值,所以"本来能杀却没杀"也走同一条判定。
    /// </summary>
    public static bool IsMissedWin(int evalBeforeCp, int evalAfterCp)
        => evalBeforeCp >= MissedWinThreshold && evalAfterCp <= MissedWinDropThreshold;

    public static MoveClass Classify(int lossCp, bool isEngineTopMove, bool inTopCandidates, bool isMissedWin = false)
    {
        // 走了引擎的首选着法 → 判为最佳
        if (isEngineTopMove)
        {
            return MoveClass.Best;
        }

        // 赢了却没赢住 → 漏着(比单纯的"损失多少厘兵"更能说明问题)
        if (isMissedWin)
        {
            return MoveClass.MissedWin;
        }

        var cls = FromLoss(lossCp);

        // 不在引擎前 N 候选里 → 下调一档
        if (!inTopCandidates)
        {
            cls = Downgrade(cls);
        }

        return cls;
    }

    public static MoveClass FromLoss(int lossCp) => lossCp switch
    {
        <= BestThreshold => MoveClass.Best,
        <= ExcellentThreshold => MoveClass.Excellent,
        <= GoodThreshold => MoveClass.Good,
        <= InaccuracyThreshold => MoveClass.Inaccuracy,
        <= MistakeThreshold => MoveClass.Mistake,
        _ => MoveClass.Blunder,
    };

    public static MoveClass Downgrade(MoveClass cls) => cls switch
    {
        MoveClass.Best => MoveClass.Excellent,
        MoveClass.Excellent => MoveClass.Good,
        MoveClass.Good => MoveClass.Inaccuracy,
        MoveClass.Inaccuracy => MoveClass.Mistake,
        MoveClass.Mistake => MoveClass.Blunder,
        MoveClass.MissedWin => MoveClass.MissedWin,
        _ => MoveClass.Blunder,
    };

    public static string ToLabel(MoveClass cls) => cls switch
    {
        MoveClass.Best => "最佳",
        MoveClass.Excellent => "优秀",
        MoveClass.Good => "良好",
        MoveClass.Inaccuracy => "失误",
        MoveClass.Mistake => "错误",
        MoveClass.Blunder => "严重失误",
        MoveClass.MissedWin => "漏着",
        _ => "未知",
    };

    public static string ToCssClass(MoveClass cls) => cls switch
    {
        MoveClass.Best => "mc-best",
        MoveClass.Excellent => "mc-excellent",
        MoveClass.Good => "mc-good",
        MoveClass.Inaccuracy => "mc-inaccuracy",
        MoveClass.Mistake => "mc-mistake",
        MoveClass.Blunder => "mc-blunder",
        MoveClass.MissedWin => "mc-missed",
        _ => "mc-good",
    };
}
