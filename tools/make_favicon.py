"""从 cm-chessboard 的棋子精灵图里抽出「黑车」,做成站点图标。

- 精灵图里的黑车是一组多边形 + 直线(没有贝塞尔曲线),所以直接用 PIL 就能光栅化;
- 同一份图形再写一份独立的 SVG,给支持 SVG 图标的浏览器用;
- 棋子素材来自 Wikimedia(Cburnett),CC BY-SA 3.0,出处见 THIRD-PARTY-NOTICES.md。

用法:python tools/make_favicon.py
"""

import os
import re

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPRITE = os.path.join(ROOT, 'src', 'ChessGameD.Web', 'wwwroot', 'lib', 'cm-chessboard', 'assets', 'pieces', 'standard.svg')
OUT_SVG = os.path.join(ROOT, 'src', 'ChessGameD.Web', 'wwwroot', 'favicon.svg')
OUT_PNG = os.path.join(ROOT, 'src', 'ChessGameD.Web', 'wwwroot', 'favicon.png')
OUT_ICO = os.path.join(ROOT, 'src', 'ChessGameD.Web', 'wwwroot', 'favicon.ico')

VIEW_BOX = 40          # 精灵图的画布大小
CROP = (5.0, 5.0, 30.0)  # 只取包含棋子的那一块(x, y, 边长),免得图标四周全是空白
SIZE = 64              # 输出 PNG 的边长
SUPERSAMPLE = 8        # 超采样倍数(抗锯齿)


def extract_group(sprite: str, group_id: str) -> str:
    """截出 <g id="..."> … </g> 这一段(按标签配对,能处理嵌套)。"""
    start = sprite.index('<g id="%s"' % group_id)
    depth = 0
    pos = start
    while True:
        match = re.compile(r'<g\b|</g>').search(sprite, pos)
        if not match:
            raise ValueError('没找到 %s 的结束标签' % group_id)
        if match.group(0) == '<g':
            depth += 1
        else:
            depth -= 1
            if depth == 0:
                return sprite[start:match.end()]
        pos = match.end()


def attr(tag: str, name: str) -> str:
    match = re.search(r'%s="([^"]*)"' % name, tag)
    return match.group(1) if match else ''


def parse_shapes(group: str):
    """把组里的多边形 / 折线 / 线段解析成 (类型, 点集, 颜色) 列表。"""
    shapes = []
    for match in re.finditer(r'<(polygon|polyline|line)\b([^>]*?)/?>', group, re.S):
        kind = match.group(1)
        body = match.group(2)
        color = attr(body, 'fill')
        if kind == 'line':
            points = [(float(attr(body, 'x1')), float(attr(body, 'y2')))]
            points = [(float(attr(body, 'x1')), float(attr(body, 'y1'))),
                      (float(attr(body, 'x2')), float(attr(body, 'y2')))]
            color = attr(body, 'stroke')
            width = float(attr(body, 'stroke-width') or 1)
        else:
            raw = attr(body, 'points')
            # points 可能是 "0 26 22 26" 或 "0,26 22,26" 两种写法
            values = [float(v) for v in raw.replace(',', ' ').split()]
            points = list(zip(values[0::2], values[1::2]))
            if kind == 'polyline':
                color = attr(body, 'stroke')
                width = float(attr(body, 'stroke-width') or 1)
            else:
                width = float(attr(body, 'stroke-width') or 1)

        shapes.append((kind, points, color or '#000000', width))
    return shapes


def render(group: str, size: int = SIZE) -> Image.Image:
    """把棋子组光栅化成 RGBA 图像。"""
    translate = (0.0, 0.0)
    match = re.search(r'transform="translate\(([-\d.]+),\s*([-\d.]+)\)"', group)
    if match:
        translate = (float(match.group(1)), float(match.group(2)))

    scale = size * SUPERSAMPLE / CROP[2]
    canvas = Image.new('RGBA', (size * SUPERSAMPLE, size * SUPERSAMPLE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    def point(x: float, y: float):
        return ((x + translate[0] - CROP[0]) * scale, (y + translate[1] - CROP[1]) * scale)

    for kind, points, color, width in parse_shapes(group):
        pts = [point(x, y) for x, y in points]
        if kind == 'polygon':
            draw.polygon(pts, fill=color)
        elif kind == 'polyline':
            draw.line(pts, fill=color, width=max(1, int(round(width * scale))), joint='curve')
        else:
            draw.line(pts, fill=color, width=max(1, int(round(width * scale))))

    return canvas.resize((size, size), Image.LANCZOS)


def write_svg(group: str) -> None:
    """写一份独立的 SVG(给支持 SVG 图标的浏览器)。"""
    body = group[group.index('>') + 1:group.rindex('</g>')]
    svg = (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<!--\n'
        '  黑车(国际象棋棋子)图标,取自 cm-chessboard 的棋子精灵图 standard.svg,\n'
        '  原始素材:Wikimedia 标准棋子(Cburnett),CC BY-SA 3.0。\n'
        '  详见 wwwroot/lib/THIRD-PARTY-NOTICES.md。\n'
        '-->\n'
        '<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40" viewBox="%g %g %g %g">\n'
        '    <g stroke-linecap="round" stroke-linejoin="round" transform="translate(9,8)">\n'
        '%s\n'
        '    </g>\n'
        '</svg>\n'
    ) % (CROP[0], CROP[1], CROP[2], CROP[2], body)
    with open(OUT_SVG, 'w', encoding='utf-8', newline='\n') as f:
        f.write(svg)


def main() -> None:
    with open(SPRITE, encoding='utf-8') as f:
        sprite = f.read()

    group = extract_group(sprite, 'br')
    image = render(group)
    image.save(OUT_PNG, 'PNG')
    image.save(OUT_ICO, format='ICO', sizes=[(16, 16), (32, 32), (48, 48), (64, 64)])
    write_svg(group)
    print('已生成:\n  %s\n  %s\n  %s' % (OUT_SVG, OUT_PNG, OUT_ICO))


if __name__ == '__main__':
    main()
