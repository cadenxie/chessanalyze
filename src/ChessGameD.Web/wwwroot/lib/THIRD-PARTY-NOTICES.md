# 第三方组件与许可

本目录下的文件均为第三方开源项目,直接随应用分发。

## chess.js

- 版本:1.4.0
- 来源:https://github.com/jhlywa/chess.js
- 许可:BSD-2-Clause(见 `chess.js/LICENSE`)
- 包含文件:`chess.js/chess.js`(ESM 构建)

## cm-chessboard

- 版本:8.15.4
- 来源:https://github.com/shaack/cm-chessboard
- 许可:代码为 MIT(见 `cm-chessboard/LICENSE`)
- 棋子素材:
  - `cm-chessboard/assets/pieces/standard.svg` —— Wikimedia 标准棋子,CC BY-SA 3.0
  - `cm-chessboard/assets/pieces/staunty.svg` —— Staunty 棋子,CC BY-NC-SA 4.0(本应用默认未使用)
- 箭头与标记素材:`cm-chessboard/assets/extensions/arrows/arrows.svg`、`markers.svg`,随项目 MIT 许可分发

## 站点图标(favicon)

- `wwwroot/favicon.svg` / `favicon.png` / `favicon.ico` —— 黑车棋子图标
- 素材来源:`cm-chessboard/assets/pieces/standard.svg` 里的「黑车」(`<g id="br">`),原始素材为
  Wikimedia 标准棋子(Cburnett),**CC BY-SA 3.0**
- 生成脚本:`tools/make_favicon.py`(从精灵图里抽出黑车,输出 SVG + PNG + ICO)

## Stockfish(引擎,不在本目录)

- 版本:19(`sf_19`),由应用在运行时从官方 GitHub Releases 下载到 `App_Data/engines/`
- 来源:https://stockfishchess.org/ · https://github.com/official-stockfish/Stockfish
- 许可:GPL-3.0
- 使用方式:**独立进程**,应用仅通过 stdin/stdout 的 UCI 文本协议与之通信,不链接、不内嵌其代码
