# ChessGameD

一个自用的**国际象棋复盘分析**工具:导入 PGN → 棋盘逐步复盘 → Stockfish 引擎评估 → 走子分类与评估曲线。

ASP.NET Core MVC / .NET 8 / SQLite,前端零 npm 构建。

> 只做复盘分析主线。**不包含**人机对弈、双人对弈、在线对战。

---

## 环境要求

| 项目 | 要求 |
| --- | --- |
| .NET SDK | 8.0(开发时用的是 8.0.421) |
| 操作系统 | Windows 10/11(x64 / ARM64)或 Linux(x64 / ARM64)。一键安装会自动按当前系统下载对应发行包 |
| 磁盘 | 引擎约 100MB(解压后),数据库与引擎都放在项目目录内 |
| 网络 | 仅"一键安装引擎"需要联网;GitHub 直连慢时可以填本地代理 |

## 快速开始

```powershell
git clone <本仓库>
cd ChessGameD
dotnet run --project src\ChessGameD.Web
```

浏览器打开 **http://localhost:5055**。

首次使用:

1. 先把管理员账号配好(见下面的「管理员账号」),否则进不了设置页。
2. 打开 **设置** 页 → 点「下载并安装 / 更新」,等进度条走完(下载 → 校验 SHA-256 → 解压 → 启动验证)。
3. 回到首页,上传 `.pgn` 文件或直接粘贴 PGN 文本。项目里带了一个示例:`samples/example.pgn`(三局经典名局)。
4. 在棋局列表点进去,用棋盘/走法列表复盘;点「分析本局」跑引擎评估。

### 管理员账号

设置页(引擎安装、路径、线程数等)只对管理员开放。管理员账号由部署者注入,仓库里不存明文口令:

```jsonc
// src/ChessGameD.Web/appsettings.Local.json(已 gitignore,只留在本机)
{
  "Chess": {
    "Admin": { "Username": "admin", "Password": "换成你自己的口令" }
  }
}
```

也可以用环境变量:`Chess__Admin__Username=admin`、`Chess__Admin__Password=...`。

- 启动时如果 `Users` 表是空的(首次启动),就按配置建一个管理员,**首次登录会强制改密**。
- 已经存在同名账号时不会覆盖它的口令;想补建管理员可以执行 `dotnet ChessGameD.Web.dll --seed-admin`(同样只从配置 / 环境变量读,不走命令行明文参数)。
- 部署完记得把 `appsettings.Local.json` 收好:它里面有明文口令。

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

- 界面是**两栏**:左边棋盘(含竖向评估条)+ 当前局面 + 评估曲线,右边引擎分析 + 走法与最佳行 + PGN。
- 棋盘(cm-chessboard)+ 走法列表双向联动,键盘 ←/→ / Home / End 翻步,F 翻转棋盘。
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
- **主变(PV)可以点击演示**:点主变里的任意一步,棋盘会从当前局面按主变逐步走到那一步(带动画,箭头跟着当前步走);演示中随时按 ← 或点「返回原局面」回到棋局当前局面。
- **最佳步骤行**:失误 / 错误 / 严重失误 / 漏着这四类坏棋的正下方会紧跟一行引擎的最佳续着(参考 lichess 的计算机分支做法),
  由服务端从该局面的引擎 PV 里取前 4 手并转成 SAN;点这一行,棋盘会从出错前的局面开始带箭头逐步演示。
  「下一个问题手」按钮可以依次跳到每一处需要复盘的着法。

### 导出报告

- 复盘页右下方「PGN 与导出」区可以导出两种格式:
  - **HTML 报告**:自包含单页(样式与评估曲线全部内联,可直接转发给别人离线打开),含基本信息、评估曲线、走子统计、逐步分析表与关键问题清单。
  - **文本报告**:纯文本,便于贴进聊天或笔记。
- 导出走的是 `GET /api/games/{id}/report?format=html|text`;棋局还没分析也能导出(会标注「尚未分析」)。

### 账号与数据隔离

- **注册**:账号名(2–32 位,字母 / 数字 / 下划线)+ 可选邮箱 + 密码;开放注册,同一 IP 每小时最多注册 5 个(可配)。
- **登录**:用账号名登录(邮箱不参与登录);Cookie 有效期 7 天、滑动过期,登出立即失效。
- **密码**:只存哈希(`Microsoft.AspNetCore.Identity` 的 PBKDF2 实现),库里没有明文;最少 6 位,且不能包含账号名。
- **权限**:管理员能看设置页、用户管理,并能看到所有用户的棋局;普通用户只看到自己的棋局。跨用户访问一律按「不存在」处理(404),不泄露 id 是否存在。
- **匿名访客**:不登录也能上传、分析、导出,但只能看到**本次浏览器会话**上传的棋局;换个浏览器 / 清掉 Cookie 就看不到了,这些数据会在 24 小时后被定时任务清掉。
- **登录认领**:匿名会话登录后,本次会话上传的棋局会自动转到该账号名下。
- **停用即时生效**:管理员停用某个账号后,对方已登录的会话在下一次请求时就会被踢出。
- **忘记密码**:没有邮件找回(邮箱本来就不是必填),由管理员在「用户管理」页重置并线下告知;重置时会生成一个一次性临时密码。

### 引擎信息的可见范围

- 首页的「引擎状态」卡片只显示状态、版本、运行平台;**引擎可执行文件的绝对路径只对管理员显示**,普通用户与匿名访客看不到。
- 状态接口 `/api/engine/status` 也会按角色裁剪:非管理员拿到的 `path` / `enginesRoot` / `lastError` 都是 null。
- 安装类的接口(`/api/engine/install`、`/api/engine/install/status`、`/api/engine/test`)只对管理员开放。

### 顶部菜单的 Git 信息

- 顶部导航里有个 **GitHub** 按钮,点进去就是项目仓库:<https://github.com/cadenxie/chessanalyze>。
- 引擎状态右边的 Git 角标(分支 + 提交号)已按使用习惯**去掉**,页面不再显示;构建时从 `.git` 读到的分支/提交号仍写进程序集元数据备用。

## 数据放在哪

全部在项目目录内,不写用户目录、不写注册表:

```
src/ChessGameD.Web/App_Data/
├── chessgame.db          # SQLite 数据库(棋局、走法、分析结果、设置)
└── engines/
    ├── .tmp/             # 下载临时目录
    └── stockfish-sf_19/  # 解压后的引擎
        └── stockfish/    # Windows 下是 stockfish-windows-x86-64-universal.exe
                         # Linux 下是 stockfish-linux-x86-64-universal
```

想彻底清空重来,删掉整个 `App_Data` 目录即可(引擎要重新下载)。

## 设置项

设置页可以改:

| 项 | 说明 |
| --- | --- |
| 引擎路径 | 自动安装失败时,可手动指定引擎可执行文件的完整路径(Windows 是 `.exe`,Linux 是无扩展名的那个) |
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

### 账号相关的配置

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `Chess:Admin:Username` | 空 | 种子管理员登录名;留空则不创建 |
| `Chess:Admin:Password` | 空 | 种子管理员初始口令(首次登录强制改密) |
| `Chess:AnonymousRetentionHours` | 24 | 匿名棋局保留多少小时后被定时任务清掉 |
| `Chess:RegistrationRateLimitPerHour` | 5 | 同一 IP 每小时最多注册几个账号 |

前两项建议写在 `appsettings.Local.json` 或环境变量里(`Chess__Admin__Username` / `Chess__Admin__Password`),
不要提交到版本库。

### 一键安装怎么认平台

设置页会显示当前**运行平台**(例如「Linux x64」),一键安装时程序会:

1. 调 GitHub Releases API 拿最新版本的资产列表;
2. 按「系统 + 架构 + 通用版」挑出匹配当前平台的那个资产
   (Windows 是 `stockfish-windows-x86-64-universal.zip`,Linux 是 `stockfish-linux-x86-64-universal.tar.gz`,macOS 是 `stockfish-macos-universal.tar.gz`);
3. 校验 SHA-256(取 Release 里公布的 `digest`),再解压到 `App_Data/engines` 并注册路径。

Windows 官方包是 zip,Linux / macOS 官方包是 tar.gz,解压时按**文件头**判断格式,不靠扩展名;
Linux / macOS 下还会自动给引擎补上执行权限(`chmod +x`)。

GitHub API 拿不到时(限流/断网),会退回 `appsettings.json` 里按平台配置的兜底地址:

```json
"Chess": {
  "Engine": {
    "Fallbacks": {
      "linux-x64": {
        "Url": "https://github.com/official-stockfish/Stockfish/releases/download/sf_19/stockfish-linux-x86-64-universal.tar.gz",
        "Sha256": "9defc0d4e55d49c65a6d042f3e571a39fcea499ade6dbe741b53b8c65e03611f",
        "Version": "sf_19"
      }
    }
  }
}
```

键名可用 `windows-x64` / `windows-arm64` / `linux-x64` / `linux-arm64` / `macos`,
也可以只写系统名(`linux`)当该系统的通用兜底,再或者写 `default` 给所有平台兜底。
兜底地址里的资产名必须与当前平台匹配,否则会被忽略(避免在 Linux 上装成 Windows 包)。

## 发布到 Ubuntu

```bash
dotnet publish src/ChessGameD.Web -c Release -r linux-x64 --self-contained false -o /opt/chessgame
```

发布后直接跑 `/opt/chessgame/ChessGameD.Web` 即可。几点注意:

- **引擎不用提前准备**:第一次进「设置」页点「下载并安装 / 更新」,程序会自己下 Linux 版
  (`stockfish-linux-x86-64-universal.tar.gz`)并解压到 `App_Data/engines`。
- **数据库跟着走**:`App_Data/chessgame.db` 在发布目录里,升级版本时把它一起拷过去,棋局和设置不会丢。
  升级如果改了表结构,程序会**先备份旧库**到 `App_Data/backup/chessgame-<时间戳>.db` 再重建,日志里会打出备份路径。
- **写权限**:发布目录要可写(引擎解压、SQLite 写入都需要),建议给运行用户单独开一个目录并 `chown`。
- **后台常驻**:用 systemd 托管时记得加上 `WorkingDirectory=/opt/chessgame`,否则相对路径的 `App_Data` 会落到别处;
  默认只监听 `http://localhost:5000`,想让局域网访问就设 `ASPNETCORE_URLS=http://0.0.0.0:5055`。
- **反向代理**:放在 Nginx 后面时,`AllowedHosts` 保持 `*` 即可。
- **管理员口令**:`Chess__Admin__Username` / `Chess__Admin__Password` 用环境变量注入最省事,
  不要写进发布目录里的配置文件。
- **架构要对上**:`linux-x64` 与 `linux-arm64` 的发布产物不能混用;一键安装会按运行时的架构选引擎包,
  ARM64 机器上跑 x64 程序(靠模拟)时下载到的仍然是 x64 引擎包。

## 项目结构

```
ChessGameD.sln
├── src/ChessGameD.Web/            # Web 应用
│   ├── Controllers/                # 页面控制器(Home / Games / Settings / Account)
│   │   ├── Admin/                  # 管理员页面(用户管理)
│   │   └── Api/                    # JSON API(导入、分析、引擎安装、报告导出、账号)
│   ├── Data/                       # 实体、DbContext、键值配置
│   ├── Services/
│   │   ├── Pgn/                    # PGN 清洗与导入
│   │   ├── Engine/                 # UCI 客户端、引擎管理、先进先出队列、一键安装
│   │   ├── Analysis/               # 走子分类、整局分析作业、UCI→SAN 转换
│   │   ├── Auth/                   # 注册登录、密码哈希、归属过滤、匿名会话与清理
│   │   └── Reports/                # 复盘报告生成(HTML / 纯文本)
│   ├── Models/                     # 视图模型
│   ├── Views/                      # Razor 页面
│   └── wwwroot/
│       ├── css/ js/                # 自有样式与前端脚本
│       └── lib/                    # vendored 的 chess.js / cm-chessboard
├── samples/example.pgn             # 示例 PGN(三局经典名局)
├── docs/htmldemo/                 # 分析页的静态交互原型(不进发布产物)
└── tests/
    ├── ChessGameD.Tests/           # xUnit 测试(164 个)
    └── ChessGameD.FakeEngine/      # 假的 UCI 引擎,给测试用
```

### 关键设计

- **引擎层自己写**,没用第三方 UCI 包:常见实现要么阻塞到收到响应(没法发 `stop` 中断搜索),要么用共享可变字段(非线程安全)。`Services/Engine/UciEngineClient.cs` 用 Channel 管道 + 单写锁实现,支持取消、超时、崩溃重启后重试一次。
- **导入全程串行**:`PgnParser` 内部有静态可变状态,并发解析会串味,所以用静态锁排队。
- **分值统一存"相对白方"**,分类用的损失量按走子方符号还原。
- **UCI→SAN 在服务端做**(`Services/Analysis/SanConverter.cs`):引擎给的是 `e2e4`,人看的是 `e4`,导出报告与"逐步推荐着法"都要用后者。
- **数据库用 `EnsureCreated()`**,没有引入 EF Migrations 工具链;启动时发现表结构不匹配会自动重建(重建前先把旧库备份到 `App_Data/backup/`)。
- **归属过滤不用 EF 全局查询过滤器**:整局分析是 singleton,后台作业会把请求的 `HttpContext` 一起带走,
  用全局过滤器会踩到已回收的 `HttpContext`;所以改成**显式谓词**(`Services/Auth/GameOwnership.cs`),
  所有按 id 取棋局的入口都必须先过 `Owned()`,越权一律 404。
- **引擎只有一份,分析请求排队**:整局分析与即时分析共用一条先进先出的队列
  (`Services/Engine/FifoGate.cs`),排队时能看到自己是第几位,不用报「引擎忙」。
- **引擎包按运行平台选**:`Services/Engine/EnginePlatform.cs` 把「系统 + 架构 → 官方资产名 / 压缩格式」固化成一张表;
  一键安装时按它挑 Release 资产,解压按文件头判断 zip 还是 tar.gz,解压完给引擎补执行权限。

## 测试

```powershell
dotnet test ChessGameD.sln
```

164 个用例,覆盖 PGN 清洗、导入、走子分类(含漏着判定)、UCI→SAN 转换、报告生成、UCI 协议解析、一键安装(含 HTTP 打桩、平台匹配、zip / tar.gz 解压)、账号体系(注册 / 登录 / 改密 / 停用即时生效)、用户之间的数据隔离、匿名会话与定时清理、引擎队列,以及走完整 HTTP 链路的集成测试。
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

### 安全

- **站点目前只跑 HTTP,登录口令是明文过网的**。认证 Cookie 也没加 `Secure` 标记(加了浏览器在 HTTP 下会直接不存这个 Cookie,登录会静默失败)。
  如果要放到公网,必须先上 HTTPS:配置证书、把 `CookieAuthenticationOptions.Cookie.SecurePolicy` 改成 `CookieSecurePolicy.Always`、再打开 HSTS。
- **管理员种子口令由部署者注入**(`appsettings.Local.json` 或环境变量),仓库里不存明文;
  种子账号首次登录会强制改密,但在改密之前它就是配置文件里的那个口令,部署完请尽快登录改掉。
- 匿名会话 Cookie 是会话级的(关掉浏览器就没了),不是加密令牌;它只用来区分「本次会话」,不是身份凭证。
- 写操作全部要过防伪 token(CSRF),前端统一在 `CG.fetchJson` 里带;表单用 `@Html.AntiForgeryToken()`。

### 数据

- **匿名数据保留 24 小时**:匿名访客上传的棋局、它的走子与分析结果会在每天凌晨 02:00 被定时任务删掉
  (保留期可用 `Chess:AnonymousRetentionHours` 调整)。匿名会话本身只存在于浏览器会话里,清掉 Cookie 就再也回不来了。
- **没有邮件找回密码**:邮箱是可选字段,不参与登录;密码忘了只能由管理员在「用户管理」页重置,并线下告知。
- **注册开放但有频率限制**:同一 IP 每小时 5 个(可配);限流计数在进程内,多实例部署要换成共享存储。
- 账号之间靠**显式归属过滤**隔离,新增按 id 取棋局的接口时必须走 `Owned()`(见「关键设计」),漏一处就会越权。
- 数据库用 `EnsureCreated()`,改了表结构会**重建数据库**(重建前会自动备份旧库);这是开发工具的做法,别拿它当正式的生产库。

### 功能

- 一键安装已支持 Windows / Linux / macOS 官方发行包;官方没有的架构(例如 macOS ARM64)仍需自行指定引擎路径。
- 整局分析是**单次搜索**口径。严格的"双方各搜一次再比对"的双搜索口径留作后续版本。
- 「漏着」的判定阈值是写死的(走子前优势 ≥ +1.5、走完优势 ≤ +0.3),没做成可配置项。
- 主变(PV)可以点击演示;「候选」里的其他变着暂时只是文字,还不能点击演示。
- 最佳步骤行只挂在**失误 / 错误 / 严重失误 / 漏着**下面,好棋不挂;长度取引擎 PV 主线的全部手数,最多 16 手(上限写死,没做成可配置项)。行内每一着都可以单独点击,棋盘会演示到那一步为止。
- 引擎只有一个,所有分析请求排一条队;整局分析跑起来时,即时分析要等它轮到(最多等 2 分钟,超时会提示队列情况)。
- 不支持在线对战、人机对弈、双人对弈。
- `docs/htmldemo/` 是分析页的**静态交互原型**:不连后端、不进发布产物,里面的评估与分类是手工编造的演示数据,
  只用来评审「走法 + 最佳步骤行 + 评估曲线」的呈现方式(用任意静态服务器打开,见该目录下的 README)。
- 顶部菜单的 Git 角标已隐藏(代码保留在 `_Layout.cshtml` 的注释里),只有 GitHub 按钮;分支/提交号仍是构建时写进程序集的,以后想恢复显示,重新 `dotnet build` 即可。
