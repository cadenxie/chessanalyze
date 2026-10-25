// 假 UCI 引擎:给 ChessGameD.Tests 用,模拟 Stockfish 的文本协议。
//
// 之所以做成独立的可执行程序而不是 .cmd 批处理:
// cmd.exe 从管道读 stdin 时,连续写入的两行命令有几率被吞掉一行
// (批处理的 set /p + goto 在重定向输入下不可靠),
// 那会让 UCI 客户端的测试变成"偶发失败",而不是真的在测协议实现。
//
// 用法:
//   ChessGameD.FakeEngine.exe            正常模式:go 之后回 bestmove
//   ChessGameD.FakeEngine.exe --mode=nogo 沉默模式:go 之后不吭声,直到收到 stop
var mode = args.FirstOrDefault(a => a.StartsWith("--mode=", StringComparison.Ordinal))?.Split('=', 2)[1]
           ?? "normal";

var answerGo = !string.Equals(mode, "nogo", StringComparison.OrdinalIgnoreCase);

while (true)
{
    var line = Console.In.ReadLine();
    if (line is null)
    {
        // stdin 关闭
        return 0;
    }

    var cmd = line.Trim();
    if (cmd.Length == 0)
    {
        continue;
    }

    var head = cmd.Split(' ', 2)[0];

    switch (head.ToLowerInvariant())
    {
        case "uci":
            Write("id name FakeEngine 1.0");
            Write("id author chessgamed-tests");
            Write("option name Threads type int default 1");
            Write("option name Hash type int default 16");
            Write("uciok");
            break;

        case "isready":
            Write("readyok");
            break;

        case "position":
        case "ucinewgame":
        case "setoption":
        case "ponderhit":
            // 无需处理
            break;

        case "go":
            if (answerGo)
            {
                Write("info depth 1 score cp 25 nodes 100 nps 1000 time 1");
                Write("info depth 2 score cp 30 seldepth 4 nodes 200 nps 1000 time 2 pv e2e4 e7e5");
                Write("bestmove e2e4");
            }

            break;

        case "stop":
            Write("bestmove e2e4");
            break;

        case "quit":
            return 0;

        default:
            break;
    }
}

static void Write(string line)
{
    Console.Out.WriteLine(line);
    Console.Out.Flush();
}
