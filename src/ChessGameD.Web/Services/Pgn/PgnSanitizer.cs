using System.Text;
using System.Text.RegularExpressions;

namespace ChessGameD.Web.Services.Pgn;

/// <summary>
/// PGN 文本预处理(纯函数,无状态)。
///
/// 处理三件在实测中会出问题的事:
/// 1. 变着 <c>(...)</c> —— ChessDotNetCore 的词法器把 <c>(</c> 起始的变着当作注释吞掉,
///    不会报错, 但变着原文会被塞进上一手的 Comment 字段, 所以必须先剥掉。
/// 2. NAG <c>$n</c> 与 <c>e.p.</c> 记号 —— 词法器无法解析, 会导致整局被跳过。
/// 3. <c>1.e4</c> / <c>0-0</c> 这类非标准写法 —— 归一化成引擎能吃进去的格式。
/// </summary>
public static class PgnSanitizer
{
    static PgnSanitizer()
    {
        // GBK / GB2312 等代码页在 .NET Core 上默认不可用,需要显式注册
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
            // 忽略
        }
    }

    private static readonly Regex MoveNumberPattern = new(@"^(\d+)(\.{1,3})(.*)$", RegexOptions.Compiled);

    /// <summary>按 UTF-8 → GBK → Latin-1 的顺序尝试解码,返回第一个能严格解码的结果。</summary>
    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        // 严格 UTF-8(含 BOM,BOM 会在下一步被去掉)
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // 继续尝试
        }

        // GBK / GB2312(国内平台导出的 PGN 常见)
        try
        {
            var gbk = Encoding.GetEncoding(936);
            var decoded = gbk.GetString(bytes);
            // GBK 解码几乎不会失败, 用往返校验排除"其实是别的编码"的情况
            var roundTrip = gbk.GetBytes(decoded);
            if (roundTrip.Length == bytes.Length)
            {
                return decoded;
            }
        }
        catch (Exception)
        {
            // 忽略,继续回退
        }

        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>剥离 BOM。</summary>
    public static string StripBom(string text) => text.TrimStart('\uFEFF');

    /// <summary>清洗整份 PGN 文本:剥离变着、NAG、行注释与 e.p.,归一化走法记号。</summary>
    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        int i = 0;
        int n = text.Length;

        while (i < n)
        {
            char c = text[i];
            switch (c)
            {
                case '{':
                {
                    int end = text.IndexOf('}', i);
                    if (end < 0)
                    {
                        // 未闭合注释: 丢弃到结尾
                        i = n;
                        break;
                    }

                    sb.Append(text, i, end - i + 1);
                    i = end + 1;
                    break;
                }

                case '(':
                    i = SkipVariation(text, i);
                    break;

                case ')':
                case '}':
                    // 落单的右括号,直接丢弃
                    i++;
                    break;

                case '$':
                    // NAG
                    i++;
                    while (i < n && char.IsDigit(text[i]))
                    {
                        i++;
                    }

                    break;

                case ';':
                    // 行注释: 丢弃到行尾
                    while (i < n && text[i] != '\n')
                    {
                        i++;
                    }

                    break;

                case ',':
                    // 顿号/逗号: 部分导出工具用它分隔同一回合的着法。
                    // ChessDotNetCore 的词法器不认这个字符(会当成 UnknownToken → "Invalid Move ,"),
                    // 所以这里直接丢弃。
                    i++;
                    break;

                case '[':
                {
                    int end = text.IndexOf(']', i);
                    if (end < 0)
                    {
                        i = n;
                        break;
                    }

                    sb.Append(text, i, end - i + 1);
                    i = end + 1;
                    break;
                }

                default:
                {
                    if (char.IsWhiteSpace(c))
                    {
                        sb.Append(c);
                        i++;
                        break;
                    }

                    int start = i;
                    while (i < n && !IsDelimiter(text[i]))
                    {
                        i++;
                    }

                    sb.Append(NormalizeWord(text[start..i]));
                    break;
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>按表头段把多局 PGN 拆成单局文本。</summary>
    public static IReadOnlyList<string> SplitGames(string text)
    {
        var games = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return games;
        }

        var current = new StringBuilder();
        bool sawMovetext = false;

        foreach (var raw in text.Split('\n'))
        {
            var trimmed = raw.TrimStart();
            bool isTag = trimmed.StartsWith("[", StringComparison.Ordinal);
            bool isBlank = trimmed.Length == 0;

            if (isTag && sawMovetext)
            {
                AddGame(games, current);
                sawMovetext = false;
            }

            current.Append(raw).Append('\n');

            if (!isTag && !isBlank)
            {
                sawMovetext = true;
            }
        }

        AddGame(games, current);
        return games;
    }

    /// <summary>
    /// 保证单局文本以 <c>[Event "..."]</c> 开头。
    /// ChessDotNetCore 的 <c>ParsePgn</c> 要求以 '[' 开头,且错误恢复依赖字面量 <c>"[Event "</c>,
    /// 缺了这一行会让整批导入的恢复逻辑失效。
    /// </summary>
    public static string EnsureEventTag(string gameText)
    {
        if (gameText.Contains("[Event ", StringComparison.Ordinal))
        {
            return gameText;
        }

        return "[Event \"?\"]\n" + gameText;
    }

    private static void AddGame(List<string> games, StringBuilder current)
    {
        var text = current.ToString();
        current.Clear();
        if (!string.IsNullOrWhiteSpace(text))
        {
            games.Add(text);
        }
    }

    private static bool IsDelimiter(char c)
        => char.IsWhiteSpace(c) || c is '{' or '}' or '(' or ')' or '$' or '[' or ']' or ';' or ',';

    /// <summary>跳过一段变着(含嵌套);返回跳过后���位置。</summary>
    private static int SkipVariation(string text, int start)
    {
        int depth = 0;
        int i = start;
        int n = text.Length;

        while (i < n)
        {
            char c = text[i];
            if (c == '{')
            {
                int end = text.IndexOf('}', i);
                i = end < 0 ? n : end + 1;
                continue;
            }

            if (c == '(')
            {
                depth++;
                i++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                i++;
                if (depth == 0)
                {
                    return i;
                }

                continue;
            }

            i++;
        }

        // 未闭合: 丢弃剩余内容
        return n;
    }

    /// <summary>归一化单个词:走法序号、���王、e.p. 记号。</summary>
    private static string NormalizeWord(string word)
    {
        if (word.Length == 0)
        {
            return word;
        }

        var m = MoveNumberPattern.Match(word);
        if (m.Success)
        {
            var number = m.Groups[1].Value;
            var dots = m.Groups[2].Value;
            var rest = m.Groups[3].Value;
            return rest.Length == 0
                ? number + dots + " "
                : number + dots + " " + NormalizeWord(rest);
        }

        if (word == "0-0-0")
        {
            return "O-O-O";
        }

        if (word == "0-0")
        {
            return "O-O";
        }

        var trimmed = word.TrimEnd('.');
        if (trimmed.Equals("e.p", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("ep", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return word;
    }
}
