# ChessGameD

一个自用的**国际象棋复盘分析**工具:导入 PGN → 棋盘逐步复盘 → Stockfish 引擎评估 → 走子分类与评估曲线。

ASP.NET Core MVC / .NET 8 / SQLite,前端零 npm 构建。

> 只做复盘分析主线。**不包含**人机对弈、双人对弈、在线对战。

---

## 环境要求

| 项目 | 要求 |
| --- | --- |
| .NET SDK | 8.0(开发时用的是 8.0.421) |
| 操作系统 | Windows 10/11(x64 / ARM64)。一键安装下载的是官方 Windows 通用版引擎 |
| 磁盘 | 引擎约 80MB,数据库与引擎都放在项目目录内 |
| 网络 | 仅"一键安装引擎"需要联网;GitHub 直连慢时可以填本地代理 |

## 快速开始

```powershell
git clone <本仓库>
cd ChessGameD
dotnet run --project src\ChessGameD.Web
```

浏览器打开 **http://localhost:5055**。

首次使用:

1. 打开 **设置** 页 → 点「下载并安装 / 更新」,等进度条走完(下载 → 校验 SHA-256 → 解压 → 启动验证)。
2. 回到首页,上传 `.pgn` 文件或直接粘贴 PGN 文本。项目里带了一个示例:`samples/example.pgn`(三局经典名局)。
3. 在棋局列表点进去,用棋盘/走法列表复盘;点「分析本局」跑引擎评估。

### 在 VS Code 里调试

仓库自带 `.vscode/` 配置(`launch.json` + `tasks.json`)。装上 [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharpdevkit)(推荐)或 [C#](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp)扩展后就能直接调试;扩展缺失时 VS Code 会自动提示安装。

| 想做的事 | 操作 |
| --- | --- |
| 调试 Web 应用 | 「运行和调试」面板选 `Web: 调试 (http://localhost:5055)`,按 **F5** |
| 下断点 | 直接在 `.cs` 文件行号左边点一下,命中即停 |
| 只跑起来(不挂调试器) | 终端 → 运行任务 → `run` |
| 热重载(改代码自动重建重启) | 终端 → 运行任务 → `watch` |
| 跑测试 | 终端 → 运行任务 → `test`;要在用例里下断点,用扩展自带的测试资源管理器 |
| 构建整个解决方案 | `Ctrl+Shift+B` |
| 附加到已经在跑的进程 | 「运行和调试」面板选 `Web: 附加到已运行的进程` |

几点说明:

- 调试配置会先执行 `build:web` 任务再启动,**浏览器等服务真正监听后才打开**,不会弹出「无法连接」的白页。
- 调试时的工作目录固定为 `src/ChessGameD.Web`,所以 `App_Data/` 里的数据库和已安装的引擎会被直接复用,不会另起一份空数据。
- HTTPS 配置(`Web: 调试 (https://localhost:7007)`)需要可信的开发证书,没装过就先跑一次 `dotnet dev-certs https --trust`。
- 断点默认只落在自己的代码上(`justMyCode`),想在框架内部下断点时把这个选项关掉即可。

## 功能

### PGN 导入

- 支持多局 PGN(按表头段自动拆分)、GBK / UTF-8 编码自动识别。
- 导入前会自动清洗:剥离变着 `(...)`、NAG `$n`、`e.p.` 记号与顿号 `,`,归一化 `1.e4` / `0-0` 这类写法,补齐缺失的 `[Event]` 标签。
- 解析不出来的棋局会被**单独跳过**并给出原因(指明第几局、第几回合、哪个着法不合法),不会影响同一批里的其他棋局。
- 走法序列一律由服务端 `ChessDotNetCore` 解析并落库,浏览器端只负责显示与试走。

### 复盘

- 棋盘(cm-chessboard)+ 走法列表双向联动,键盘 ←/→ / Home / End 翻步。
- 每半回合都有服务端算好的 FEN 与 UCI 走法,回放不会出现前后不一致。
- 可以试走一步(合法着法会高亮),试走不影响已保存的棋局。

### 引擎分析

- 引擎:Stockfish(单一引擎,不做多引擎切换),以独立进程通过 UCI 文本协议交互,不链接、不分发。
- 即时分析:对当前局面求评估(分值、深度、节点数、主变)。
- 整局分析:后台逐半回合搜索,带进度条与增量缓存(中途可停,已算过的结果会保留)。
- 评估曲线:横轴为半回合数,纵轴为评估分;问题手会在曲线上标出来,点曲线可以跳到对应着法。
- 走子分类:按损失分值分为 **最佳 / 优秀 / 良好 / 失误 / 错误 / 严重失误**,另外单列 **漏着**(本来在赢却把胜势送掉了),并标记是否为引擎首选着法。
- **逐步显示引擎推荐着法**:每一手都能看到引擎当时推荐的那步(走法列表里绿色显示,悬浮提示里给出评价与损失分)。
- 当前局面的推荐着法会单独列在「引擎分析」面板里,并在棋盘上用箭头标出。

### 导出报告

- 复盘页右下方「PGN 与导出」区可以导出两种格式:
  - **HTML 报告**:自包含单页(样式与评估曲线全部内联,可直接转发给别人离线打开),含基本信息、评估曲线、走子统计、逐步分析表与关键问题清单。
  - **文本报告**:纯文本,便于贴进聊天或笔记。
- 导出走的是 `GET /api/games/{id}/report?format=html|text`;棋局还没分析也能导出(会标注「尚未分析」)。

## 数据放在哪

全部在项目目录内,不写用户目录、不写注册表:

```
src/ChessGameD.Web/App_Data/
├── chessgame.db          # SQLite 数据库(棋局、走法、分析结果、设置)
└── engines/
    ├── .tmp/             # 下载临时目录
    └── stockfish-sf_19/  # 解压后的引擎
        └── stockfish.exe
```

想彻底清空重来,删掉整个 `App_Data` 目录即可(引擎要重新下载)。

## 设置项

设置页可以改:

| 项 | 说明 |
| --- | --- |
| 引擎路径 | 自动安装失败时,可手动指定 `stockfish.exe` 的完整路径 |
| 下载代理 | 直连 GitHub 慢/连不上时填本地代理,例如 `http://127.0.0.1:7897`;留空 = 直连 |
| 线程数 | 0 = 自动(按 CPU 核数 - 1) |
| Hash 内存 | 引擎搜索缓存(MB) |
| MultiPV | 候选着法数量,影响走子分类的"是否在前 N 候选"判定 |
| 即时分析深度 | 单个局面的搜索深度 |
| 整局分析深度 | 整局逐步搜索的深度,越深越慢 |

下载代理也可以写进 `src/ChessGameD.Web/appsettings.json`:

```json
"Chess": { "Engine": { "ProxyUrl": "http://127.0.0.1:7897", "StallTimeoutSeconds": 30 } }
```

设置页的值优先于配置文件。

## 项目结构

```
ChessGameD.sln
├── src/ChessGameD.Web/            # Web 应用
│   ├── Controllers/                # 页面控制器(Home / Games / Settings)
│   │   └── Api/                    # JSON API(导入、分析、引擎安装、报告导出)
│   ├── Data/                       # 实体、DbContext、键值配置
│   ├── Services/
│   │   ├── Pgn/                    # PGN 清洗与导入
│   │   ├── Engine/                 # UCI 客户端、引擎管理、一键安装
│   │   ├── Analysis/               # 走子分类、整局分析作业、UCI→SAN 转换
│   │   └── Reports/                # 复盘报告生成(HTML / 纯文本)
│   ├── Models/                     # 视图模型
│   ├── Views/                      # Razor 页面
│   └── wwwroot/
│       ├── css/ js/                # 自有样式与前端脚本
│       └── lib/                    # vendored 的 chess.js / cm-chessboard
├── samples/example.pgn             # 示例 PGN(三局经典名局)
└── tests/
    ├── ChessGameD.Tests/           # xUnit 测试(91 个)
    └── ChessGameD.FakeEngine/      # 假的 UCI 引擎,给测试用
```

### 关键设计

- **引擎层自己写**,没用第三方 UCI 包:常见实现要么阻塞到收到响应(没法发 `stop` 中断搜索),要么用共享可变字段(非线程安全)。`Services/Engine/UciEngineClient.cs` 用 Channel 管道 + 单写锁实现,支持取消、超时、崩溃重启后重试一次。
- **导入全程串行**:`PgnParser` 内部有静态可变状态,并发解析会串味,所以用静态锁排队。
- **分值统一存"相对白方"**,分类用的损失量按走子方符号还原。
- **UCI→SAN 在服务端做**(`Services/Analysis/SanConverter.cs`):引擎给的是 `e2e4`,人看的是 `e4`,导出报告与"逐步推荐着法"都要用后者。
- **数据库用 `EnsureCreated()`**,没有引入 EF Migrations 工具链;启动时发现表结构不匹配会自动重建。

## 测试

```powershell
dotnet test ChessGameD.sln
```

91 个用例,覆盖 PGN 清洗、导入、走子分类(含漏着判定)、UCI→SAN 转换、报告生成、UCI 协议解析、一键安装(含 HTTP 打桩)、以及走完整 HTTP 链路的集成测试。
测试用的假引擎是真实的独立控制台程序(`tests/ChessGameD.FakeEngine`),不用 `.cmd` 批处理——cmd.exe 从管道读 stdin 时会吞掉缓冲中的后续行,会让协议测试变成偶发失败。

## 许可与第三方组件

本项目代码由使用者自行决定如何分发。内置/下载的第三方组件各自遵循其许可:

| 组件 | 许可 | 说明 |
| --- | --- | --- |
| [Stockfish](https://github.com/official-stockfish/Stockfish) | GPL-3.0 | **独立进程**,通过标准输入输出交互;本项目不链接、不修改、不再分发它,由用户在本机自行下载 |
| [ChessDotNetCore](https://www.nuget.org/packages/ChessDotNetCore) | MIT | 服务端 PGN 解析与走法验证 |
| [chess.js](https://github.com/jhlywa/chess.js) | BSD-2-Clause | 浏览器端棋盘交互与试走校验 |
| [cm-chessboard](https://github.com/lichess-org/cm-chessboard) | MIT | 棋盘 UI |
| cm-chessboard 棋子图 `standard.svg` | CC BY-SA 3.0(Wikimedia) | 默认使用的棋子图 |
| cm-chessboard 棋子图 `staunty.svg` | CC BY-NC-SA 4.0(Wikimedia) | 备选棋子图(含 NC 条款,商用需注意) |

第三方组件的完整声明见 `src/ChessGameD.Web/wwwroot/lib/THIRD-PARTY-NOTICES.md`。

Stockfish 是 GPL 软件:以独立进程方式调用、通过标准输入输出通信,在绝大多数司法辖区属于" mere aggregation ",不会让本项目变成 GPL 作品。但如果你打算**分发**这个工具(含引擎二进制),请自行确认合规性。

## 已知限制

- 只支持 Windows 的 Stockfish 官方发行包;macOS / Linux 需要自己准备引擎可执行文件并在设置页指定路径。
- 整局分析是**单次搜索**口径。严格的"双方各搜一次再比对"的双搜索口径留作后续版本。
- 「漏着」的判定阈值是写死的(走子前优势 ≥ +1.5、走完优势 ≤ +0.3),没做成可配置项。
- 不支持在线对战、人机对弈、双人对弈。
