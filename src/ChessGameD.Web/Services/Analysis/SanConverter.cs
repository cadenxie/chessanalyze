using ChessDotNetCore;

namespace ChessGameD.Web.Services.Analysis;

/// <summary>
/// UCI 走法 → SAN 的转换(服务端)。
///
/// 引擎给出的是 UCI(如 <c>e2e4</c>),但人看的、棋谱里写的是 SAN(如 <c>e4</c>)。
/// 浏览器端有 chess.js 可以转,但导出报告、邮件分享这些都要在服务端完成,
/// 所以这里用 ChessDotNetCore 复现一步走法,直接读它算好的 SAN。
///
/// 注意:转换会真实走一步棋,所以必须传入<b>走子前</b>的局面 FEN。
/// </summary>
public static class SanConverter
{
    /// <summary>把 UCI 走法转成 SAN;无法转换时返回 null。</summary>
    public static string? ToSan(string? fen, string? uci)
    {
        if (string.IsNullOrWhiteSpace(fen) || string.IsNullOrWhiteSpace(uci) || uci.Length < 4)
        {
            return null;
        }

        try
        {
            var game = new ChessGame();
            game.Initialize(fen);

            var from = uci[..2];
            var to = uci.Substring(2, 2);
            // 升变字符:引擎给的是小写("e7e8q"),但 ChessDotNetCore 只认大写
            char? promotion = uci.Length > 4 ? char.ToUpperInvariant(uci[4]) : null;

            var move = new Move(from, to, game.StartPlayer, promotion);
            if (game.MakeMove(move, alreadyValidated: false) == MoveType.Invalid)
            {
                return null;
            }

            return game.LastMove?.SAN;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把一整条 PV(UCI 空格分隔)转成 SAN 序列;中途断掉就返回已转换的部分。</summary>
    public static IReadOnlyList<string> ToSanList(string? fen, string? pv)
    {
        if (string.IsNullOrWhiteSpace(fen) || string.IsNullOrWhiteSpace(pv))
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var currentFen = fen;

        foreach (var uci in pv.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var san = ToSan(currentFen, uci);
            if (san is null)
            {
                break;
            }

            result.Add(san);
            currentFen = ApplyFen(currentFen, uci);
        }

        return result;
    }

    /// <summary>走完一步之后的局面 FEN;失败时退回原局面。</summary>
    private static string ApplyFen(string fen, string uci)
    {
        try
        {
            var game = new ChessGame();
            game.Initialize(fen);
            var promotion = uci.Length > 4 ? char.ToUpperInvariant(uci[4]) : (char?)null;
            var move = new Move(uci[..2], uci.Substring(2, 2), game.StartPlayer, promotion);
            if (game.MakeMove(move, alreadyValidated: false) == MoveType.Invalid)
            {
                return fen;
            }

            return game.GetFen();
        }
        catch
        {
            return fen;
        }
    }
}
