using System.Globalization;
using System.Net;
using System.Text;
using ChessGameD.Web.Data;
using ChessGameD.Web.Services.Analysis;

namespace ChessGameD.Web.Services.Reports;

/// <summary>
/// 复盘报告生成器:把「棋局 + 引擎分析结果」导出成自包含的 HTML 页面或纯文本。
///
/// 两种格式都不依赖任何外部资源(HTML 的样式与评估曲线全部内联),
/// 所以报告可以直接发给别人,离线打开也不会丢样式。
/// </summary>
public static class ReportBuilder
{
    private const int ChartWidth = 920;
    private const int ChartHeight = 240;
    private const int ChartPadX = 34;
    private const int ChartPadY = 24;

    /// <summary>问题手:值得单独列出来的分类。</summary>
    private static readonly MoveClass[] ProblemClasses =
    {
        MoveClass.MissedWin,
        MoveClass.Inaccuracy,
        MoveClass.Mistake,
        MoveClass.Blunder,
    };

    /* ------------------------------------------------------------------ HTML */

    public static string BuildHtml(Game game, IReadOnlyList<GameMove> moves, AnalysisSummary? summary)
    {
        var byPly = moves.ToDictionary(m => m.Ply);
        var classByPly = summary?.Classifications.ToDictionary(c => c.Ply) ?? new Dictionary<int, MoveClassification>();
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\" />");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine($"<title>复盘报告 · {H(game.White)} vs {H(game.Black)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(HtmlStyle);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body><main>");

        sb.AppendLine("<header>");
        sb.AppendLine($"<h1>{H(game.White)} <span class=\"vs\">vs</span> {H(game.Black)}</h1>");
        sb.AppendLine($"<p class=\"result\">{H(game.Result)}</p>");
        sb.AppendLine("</header>");

        sb.AppendLine("<section class=\"meta\"><h2>基本信息</h2><table class=\"kv\">");
        AppendKv(sb, "白方", game.White);
        AppendKv(sb, "黑方", game.Black);
        AppendKv(sb, "结果", game.Result);
        if (!string.IsNullOrWhiteSpace(game.Date)) { AppendKv(sb, "日期", game.Date!); }
        if (!string.IsNullOrWhiteSpace(game.Event)) { AppendKv(sb, "赛事", game.Event!); }
        if (!string.IsNullOrWhiteSpace(game.Site)) { AppendKv(sb, "站点", game.Site!); }
        if (!string.IsNullOrWhiteSpace(game.Opening)) { AppendKv(sb, "开局", game.Opening!); }
        if (!string.IsNullOrWhiteSpace(game.Eco)) { AppendKv(sb, "ECO", game.Eco!); }
        if (!string.IsNullOrWhiteSpace(game.TimeControl)) { AppendKv(sb, "时限", game.TimeControl!); }
        if (game.WhiteElo.HasValue || game.BlackElo.HasValue)
        {
            AppendKv(sb, "等级分", $"{game.WhiteElo?.ToString() ?? "-"} / {game.BlackElo?.ToString() ?? "-"}");
        }
        AppendKv(sb, "引擎", summary?.EngineVersion ?? "尚未分析");
        AppendKv(sb, "总手数", $"{game.PlyCount} ply");
        AppendKv(sb, "生成时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.AppendLine("</table></section>");

        sb.AppendLine("<section class=\"chart\"><h2>评估曲线</h2>");
        if (summary is null)
        {
            sb.AppendLine("<p class=\"hint\">该棋局还没有分析结果,分析后即可显示评估曲线。</p>");
        }
        else
        {
            sb.AppendLine(BuildChartSvg(summary, classByPly));
            sb.AppendLine("<p class=\"hint\">纵轴为相对白方的评估(正数白方优),横轴为步数。✕ 标记问题手。</p>");
        }
        sb.AppendLine("</section>");

        if (summary is not null)
        {
            sb.AppendLine("<section class=\"stats\"><h2>走子统计</h2><div class=\"chips\">");
            foreach (var cls in new[] { MoveClass.Best, MoveClass.Excellent, MoveClass.Good, MoveClass.Inaccuracy, MoveClass.Mistake, MoveClass.Blunder, MoveClass.MissedWin })
            {
                var count = summary.Classifications.Count(c => c.Class == cls);
                sb.AppendLine($"<span class=\"chip\"><i class=\"{MoveClassifier.ToCssClass(cls)}\"></i>{MoveClassifier.ToLabel(cls)} {count}</span>");
            }
            sb.AppendLine("</div></section>");
        }

        sb.AppendLine("<section class=\"moves\"><h2>逐步分析</h2>");
        sb.AppendLine("<table class=\"movetable\"><thead><tr><th>回合</th><th>白方</th><th>评估</th><th>分类</th><th>引擎建议</th><th>黑方</th><th>评估</th><th>分类</th><th>引擎建议</th></tr></thead><tbody>");
        for (int ply = 1; byPly.ContainsKey(ply); ply += 2)
        {
            byPly.TryGetValue(ply, out var white);
            byPly.TryGetValue(ply + 1, out var black);

            sb.Append("<tr>");
            sb.Append($"<td class=\"no\">{(ply + 1) / 2}.</td>");
            AppendMoveCells(sb, white, classByPly);
            AppendMoveCells(sb, black, classByPly);
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table></section>");

        var problems = summary?.Classifications.Where(c => ProblemClasses.Contains(c.Class)).ToList() ?? new List<MoveClassification>();
        sb.AppendLine("<section class=\"issues\"><h2>关键问题</h2>");
        if (summary is null)
        {
            sb.AppendLine("<p class=\"hint\">该棋局还没有分析结果,分析后即可列出关键问题。</p>");
        }
        else if (problems.Count == 0)
        {
            sb.AppendLine("<p class=\"hint\">本局没有明显问题手。</p>");
        }
        else
        {
            sb.AppendLine("<table class=\"movetable\"><thead><tr><th>回合</th><th>走子方</th><th>着法</th><th>分类</th><th>损失</th><th>引擎建议</th></tr></thead><tbody>");
            foreach (var p in problems)
            {
                var ply = p.Ply;
                var side = ply % 2 == 1 ? "白方" : "黑方";
                sb.Append("<tr>");
                sb.Append($"<td class=\"no\">{(ply + 1) / 2}.</td>");
                sb.Append($"<td class=\"score\">{side}</td>");
                sb.Append($"<td class=\"san\">{H(p.San)}</td>");
                sb.Append($"<td class=\"cls\"><span class=\"badge {MoveClassifier.ToCssClass(p.Class)}\">{MoveClassifier.ToLabel(p.Class)}</span></td>");
                sb.Append($"<td class=\"score\">{H(FormatCp(p.LossCp))}</td>");
                sb.Append(p.BestMoveSan is null ? "<td class=\"rec\">—</td>" : $"<td class=\"rec\">{H(p.BestMoveSan)}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
        }
        sb.AppendLine("</section>");

        sb.AppendLine("</main></body></html>");
        return sb.ToString();
    }

    private static void AppendMoveCells(StringBuilder sb, GameMove? move, Dictionary<int, MoveClassification> classByPly)
    {
        if (move is null)
        {
            sb.Append("<td class=\"san\">…</td><td class=\"score\">—</td><td class=\"cls\">—</td><td class=\"rec\">—</td>");
            return;
        }

        classByPly.TryGetValue(move.Ply, out var cls);

        sb.Append($"<td class=\"san\">{H(move.San)}</td>");
        sb.Append($"<td class=\"score\">{H(FormatCp(cls?.EvalAfterCp))}</td>");
        sb.Append(cls is null
            ? "<td class=\"cls\">—</td>"
            : $"<td class=\"cls\"><span class=\"badge {MoveClassifier.ToCssClass(cls.Class)}\">{MoveClassifier.ToLabel(cls.Class)}</span></td>");
        sb.Append(cls is null || cls.BestMoveSan is null
            ? "<td class=\"rec\">—</td>"
            : $"<td class=\"rec\">{H(cls.BestMoveSan)}</td>");
    }

    /* ------------------------------------------------------------------ 文本 */

    public static string BuildText(Game game, IReadOnlyList<GameMove> moves, AnalysisSummary? summary)
    {
        var byPly = moves.ToDictionary(m => m.Ply);
        var classByPly = summary?.Classifications.ToDictionary(c => c.Ply) ?? new Dictionary<int, MoveClassification>();
        var sb = new StringBuilder();

        sb.AppendLine("复盘报告");
        sb.AppendLine("========");
        sb.AppendLine($"棋局     : {game.White} vs {game.Black}");
        sb.AppendLine($"结果     : {game.Result}");
        if (!string.IsNullOrWhiteSpace(game.Date)) { sb.AppendLine($"日期     : {game.Date}"); }
        if (!string.IsNullOrWhiteSpace(game.Event)) { sb.AppendLine($"赛事     : {game.Event}"); }
        if (!string.IsNullOrWhiteSpace(game.Opening)) { sb.AppendLine($"开局     : {game.Opening}"); }
        if (!string.IsNullOrWhiteSpace(game.Eco)) { sb.AppendLine($"ECO      : {game.Eco}"); }
        sb.AppendLine($"引擎     : {summary?.EngineVersion ?? "尚未分析"}");
        sb.AppendLine($"总手数   : {game.PlyCount} ply");
        sb.AppendLine($"生成时间 : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("逐步分析");
        sb.AppendLine("--------");
        for (int ply = 1; byPly.ContainsKey(ply); ply += 2)
        {
            byPly.TryGetValue(ply, out var white);
            byPly.TryGetValue(ply + 1, out var black);

            var no = ((ply + 1) / 2).ToString(CultureInfo.InvariantCulture) + ".";
            sb.Append(PadDisplay(no, 6));
            sb.Append(PadDisplay(FormatMoveCell(white, classByPly), 38));
            sb.Append(FormatMoveCell(black, classByPly));
            sb.AppendLine();
        }
        sb.AppendLine();

        var problems = summary?.Classifications.Where(c => ProblemClasses.Contains(c.Class)).ToList() ?? new List<MoveClassification>();
        sb.AppendLine("关键问题");
        sb.AppendLine("--------");
        if (summary is null)
        {
            sb.AppendLine("(该棋局还没有分析结果)");
        }
        else if (problems.Count == 0)
        {
            sb.AppendLine("(本局没有明显问题手)");
        }
        else
        {
            foreach (var p in problems)
            {
                var side = p.Ply % 2 == 1 ? "白方" : "黑方";
                sb.AppendLine(PadDisplay($"{((p.Ply + 1) / 2).ToString(CultureInfo.InvariantCulture),3}.", 6)
                    + PadDisplay(side, 8)
                    + PadDisplay(p.San, 10)
                    + PadDisplay(MoveClassifier.ToLabel(p.Class), 12)
                    + PadDisplay("损失 " + FormatCp(p.LossCp), 18)
                    + "引擎建议: " + (p.BestMoveSan ?? "-"));
            }
        }

        return sb.ToString();
    }

    private static string FormatMoveCell(GameMove? move, Dictionary<int, MoveClassification> classByPly)
    {
        if (move is null)
        {
            return "…";
        }

        classByPly.TryGetValue(move.Ply, out var cls);
        var label = cls is null ? "-" : MoveClassifier.ToLabel(cls.Class);
        var score = cls is null ? "-" : FormatCp(cls.EvalAfterCp);
        var rec = cls?.BestMoveSan ?? "-";
        return PadDisplay(move.San, 9) + PadDisplay(label, 10) + PadDisplay(score, 8) + "建议 " + rec;
    }

    /* ------------------------------------------------------------------ 图表 */

    private static string BuildChartSvg(AnalysisSummary summary, Dictionary<int, MoveClassification> classByPly)
    {
        var points = summary.Points
            .Select(p => (p.Ply, Cp: GameAnalyzer.EvalToCp(p.ScoreCp, p.ScoreMate)))
            .Where(p => p.Cp != 0 || p.Ply == 0)
            .ToList();

        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {ChartWidth} {ChartHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");

        var maxPly = Math.Max(1, points.Count == 0 ? 1 : points.Max(p => p.Ply));
        double X(int ply) => ChartPadX + (double)ply / maxPly * (ChartWidth - ChartPadX - 12);
        double Y(int cp)
        {
            var clamped = Math.Clamp(cp, -1000, 1000);
            return ChartPadY + (1000 - clamped) / 2000.0 * (ChartHeight - ChartPadY * 2);
        }

        // 背景与 0 线
        sb.Append($"<rect x=\"0\" y=\"0\" width=\"{ChartWidth}\" height=\"{ChartHeight}\" fill=\"#faf8f4\" stroke=\"#e7e1d6\" rx=\"8\"/>");
        sb.Append($"<line x1=\"{ChartPadX}\" y1=\"{Y(0):F1}\" x2=\"{ChartWidth - 12}\" y2=\"{Y(0):F1}\" stroke=\"#ded7ca\" stroke-width=\"1\"/>");
        sb.Append($"<text x=\"4\" y=\"{Y(1000) + 4:F1}\" fill=\"#857d72\" font-size=\"10\">+10</text>");
        sb.Append($"<text x=\"4\" y=\"{Y(0) + 4:F1}\" fill=\"#857d72\" font-size=\"10\">0</text>");
        sb.Append($"<text x=\"4\" y=\"{Y(-1000) + 4:F1}\" fill=\"#857d72\" font-size=\"10\">-10</text>");

        if (points.Count < 2)
        {
            sb.Append($"<text x=\"{ChartWidth / 2}\" y=\"{ChartHeight / 2}\" fill=\"#857d72\" font-size=\"13\" text-anchor=\"middle\">暂无评估数据</text>");
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        // 折线
        sb.Append("<polyline fill=\"none\" stroke=\"#4f46e5\" stroke-width=\"1.8\" points=\"");
        foreach (var p in points.OrderBy(p => p.Ply))
        {
            sb.Append($"{X(p.Ply):F1},{Y(p.Cp):F1} ");
        }
        sb.AppendLine("\"/>");

        // 问题手标记
        foreach (var c in classByPly.Values.Where(c => ProblemClasses.Contains(c.Class)))
        {
            var point = points.FirstOrDefault(p => p.Ply == c.Ply);
            if (point.Ply == 0 && c.Ply != 0) { continue; }
            var x = X(c.Ply);
            var y = Y(point.Cp);
            var color = c.Class == MoveClass.MissedWin ? "#7c3aed" : c.Class == MoveClass.Inaccuracy ? "#c07c12" : "#c02b2b";
            sb.Append($"<line x1=\"{x - 4:F1}\" y1=\"{y - 4:F1}\" x2=\"{x + 4:F1}\" y2=\"{y + 4:F1}\" stroke=\"{color}\" stroke-width=\"2\"/>");
            sb.Append($"<line x1=\"{x - 4:F1}\" y1=\"{y + 4:F1}\" x2=\"{x + 4:F1}\" y2=\"{y - 4:F1}\" stroke=\"{color}\" stroke-width=\"2\"/>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    /* ------------------------------------------------------------------ 工具 */

    private static void AppendKv(StringBuilder sb, string key, string value)
        => sb.AppendLine($"<tr><th>{H(key)}</th><td>{H(value)}</td></tr>");

    private static string H(string? value)
        => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string FormatCp(int? cp)
    {
        if (!cp.HasValue)
        {
            return "—";
        }

        var pawns = cp.Value / 100.0;
        return (pawns > 0 ? "+" : "") + pawns.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>按显示宽度补齐(中文字符占两列)。</summary>
    private static string PadDisplay(string text, int width)
    {
        var pad = width - DisplayWidth(text);
        return pad > 0 ? text + new string(' ', pad) : text;
    }

    private static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var ch in text)
        {
            width += IsWide(ch) ? 2 : 1;
        }

        return width;
    }

    private static bool IsWide(char ch)
        => (ch >= 0x1100 && ch <= 0x115F)     // 韩文字母
        || (ch >= 0x2E80 && ch <= 0xA4CF)     // CJK 部首、汉字
        || (ch >= 0xAC00 && ch <= 0xD7A3)     // 韩文音节
        || (ch >= 0xF900 && ch <= 0xFAFF)     // CJK 兼容汉字
        || (ch >= 0xFE30 && ch <= 0xFE6F)     // CJK 兼容形式
        || (ch >= 0xFF00 && ch <= 0xFF60)     // 全角字符
        || (ch >= 0xFFE0 && ch <= 0xFFE6);

    private const string HtmlStyle = """
body{font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","Microsoft YaHei",sans-serif;background:#f4f2ed;color:#26221c;margin:0;padding:28px 18px}
main{max-width:980px;margin:0 auto}
h1{font-size:24px;margin:0 0 4px}
h2{font-size:15px;margin:0 0 12px;color:#857d72}
h1 .vs{color:#857d72;font-size:16px;font-weight:400;margin:0 6px}
header{border-bottom:1px solid #e7e1d6;padding-bottom:14px;margin-bottom:22px}
header .result{color:#4f46e5;margin:6px 0 0;font-size:15px}
section{background:#fff;border:1px solid #e7e1d6;border-radius:14px;padding:16px 18px;margin-bottom:18px;box-shadow:0 1px 2px rgba(38,34,29,.05),0 12px 28px rgba(38,34,29,.06)}
table{border-collapse:collapse;width:100%}
table.kv th{text-align:left;color:#857d72;font-weight:400;width:110px;padding:5px 10px 5px 0;vertical-align:top}
table.kv td{padding:5px 0}
.chips{display:flex;flex-wrap:wrap;gap:10px}
.chip{display:inline-flex;align-items:center;gap:6px;background:#faf8f4;border:1px solid #e7e1d6;border-radius:999px;padding:4px 12px;font-size:13px}
.chip i{width:9px;height:9px;border-radius:50%;display:inline-block}
.mc-best{background:#2f7d57}.mc-excellent{background:#3f7fbf}.mc-good{background:#7b8794}
.mc-inaccuracy{background:#c07c12}.mc-mistake{background:#d0563f}.mc-blunder{background:#c02b2b}.mc-missed{background:#7c3aed}
table.movetable{font-size:13px}
table.movetable th{text-align:left;color:#857d72;font-weight:400;padding:6px 8px;border-bottom:1px solid #e7e1d6}
table.movetable td{padding:5px 8px;border-bottom:1px solid #f0ece4;vertical-align:top}
table.movetable td.no{color:#857d72;width:46px}
table.movetable td.san{font-family:Consolas,monospace;color:#26221c;white-space:nowrap}
table.movetable td.score{font-family:Consolas,monospace;color:#857d72;white-space:nowrap}
table.movetable td.rec{font-family:Consolas,monospace;color:#2f7d57;white-space:nowrap}
.badge{display:inline-block;border-radius:999px;padding:1px 9px;font-size:12px;color:#26221c;white-space:nowrap}
.badge.mc-best{background:#eaf5ef;color:#2f7d57}.badge.mc-excellent{background:#eaf2fb;color:#3f7fbf}.badge.mc-good{background:#f0f2f4;color:#7b8794}
.badge.mc-inaccuracy{background:#fdf3e2;color:#c07c12}.badge.mc-mistake{background:#fdeee9;color:#d0563f}.badge.mc-blunder{background:#fdeaea;color:#c02b2b}.badge.mc-missed{background:#f2ecfd;color:#7c3aed}
.hint{color:#857d72;font-size:12px;margin:10px 0 0}
svg{width:100%;height:auto;display:block}
""";
}
