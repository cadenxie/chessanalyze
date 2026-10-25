# AGENTS.md

自用的国际象棋复盘分析工具:导入 PGN → 棋盘逐步复盘 → Stockfish 引擎评估 → 走子分类与评估曲线。
ASP.NET Core MVC / .NET 8 / SQLite,前端零 npm 构建。只做复盘分析主线,不含人机对弈与在线对战。

## Setup commands

需要 .NET SDK 8.0(开发机用的是 8.0.421)。

- Install deps: `dotnet restore ChessGameD.sln`
- Start dev:    `dotnet run --project src\ChessGameD.Web` → http://localhost:5055
- Build:        `dotnet build ChessGameD.sln -c Debug`
- Test:         `dotnet test ChessGameD.sln`
- Format:       `dotnet format ChessGameD.sln`(仓库无 .editorconfig,按默认规则,别顺手大改格式)
- 调试:         VS Code 直接 F5,配置见 `.vscode/launch.json`(需装 C# 或 C# Dev Kit 扩展)

## Project layout

- `src/ChessGameD.Web/` — ASP.NET Core MVC 站点,唯一可部署项目
  - `Controllers/` — 页面控制器(Home / Games / Settings)与 `Api/` 下的 JSON 接口
  - `Services/Engine/` — UCI 引擎客户端、引擎管理、一键安装(自研,不用第三方 UCI 包)
  - `Services/Analysis/` — 走子分类、UCI→SAN 转换、整局分析作业
  - `Services/Pgn/` — PGN 清洗与导入
  - `Services/Reports/` — HTML / 纯文本报告生成
  - `Data/` — EF Core 实体、DbContext、键值配置存储
  - `Models/` `Views/` `wwwroot/` — 视图模型、Razor 页面、前端资源
  - `App_Data/` — **运行时数据,已 gitignore**:SQLite 库 + 下载解压的 Stockfish
  - `wwwroot/lib/` — vendored 的 chess.js / cm-chessboard,**要进版本库**(自带 LICENSE)
- `tests/ChessGameD.Tests/` — xUnit 测试(91 个用例)
- `tests/ChessGameD.FakeEngine/` — 假 UCI 引擎,给测试用的独立控制台程序
- `samples/example.pgn` — 示例 PGN
- `.vscode/` — 团队共享的调试与任务配置,**不要忽略**

## Code style

- C#:file-scoped namespace、4 空格、Allman 大括号;`Nullable` 已开启
- 注释与文档一律中文;公开成员用 `/// <summary>` XML 文档
- 私有字段 `_camelCase`;async 方法带 `Async` 后缀,对外接口收 `CancellationToken ct`
- 命名空间按功能分层,新增类型放进对应 `Services.<功能>` 而不是新建顶层目录
- 前端 JS:4 空格、`var`、IIFE 挂到 `window.CG`、中文注释
- 没有 .editorconfig,保持周边文件既有风格,不要顺手重排格式

## Testing instructions

- 全部测试: `dotnet test ChessGameD.sln`(xUnit,91 个用例)
- 单个测试类: `dotnet test tests\ChessGameD.Tests\ChessGameD.Tests.csproj --filter "FullyQualifiedName~PgnImportTests"`
- 测试命名 `方法_场景_预期`,例如 `Import_SingleGame_StoresGameAndMoves`
- 改了 `Services/` 下的逻辑就要补对应用例;导入、分类、报告这三块最容易回归
- 提交前全部用例必须通过

## Key constraints

改这块代码前务必知道,违反了会静默出错:

- **导入全程串行**:`PgnParser` 内部有静态可变状态,并发解析会串味,必须走静态锁排队
- **分值统一存"相对白方"**,分类用的损失量要按走子方符号还原
- **UCI→SAN 在服务端做**(`Services/Analysis/SanConverter.cs`):引擎给的是 `e2e4`,人看的是 `e4`
- **数据库用 `EnsureCreated()`**,没有引入 EF Migrations 工具链;启动时发现表结构不匹配会自动重建数据库
- **引擎以独立进程通过 UCI 文本协议交互**,不链接、不分发 Stockfish;`UciEngineClient` 用 Channel 管道 + 单写锁,改它要保证能中断 `stop`
- 漏着判定阈值目前写死(走子前优势 ≥ +1.5、走完优势 ≤ +0.3),不是配置项
- 只支持 Windows 版 Stockfish;macOS / Linux 需自行指定引擎路径

## Commit conventions

- 本仓库目前只有本地库、没有远端;分支从 `main` 切,别直接往 `main` 上堆改动
- Commit message:一行中文摘要(可用 `feat:` / `fix:` / `docs:` / `refactor:` 前缀)+ 必要的正文
- 提交前跑一遍 `dotnet test ChessGameD.sln`,并在 README 的「已知限制」里同步你改动的行为

## Security

- 绝不提交 `App_Data/`(含 SQLite 库与引擎二进制)、`.env` 或任何密钥
- 一键安装只从 GitHub 官方发行版下载并校验 SHA-256,不要换成第三方镜像
- 不要把 Stockfish 源码或可执行文件塞进版本库
- 本地数据库里的棋谱是用户私有数据,别提交测试导出的真实数据
