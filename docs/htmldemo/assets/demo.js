/* 分析页 Demo 的前端逻辑。
 *
 * 这是一个纯静态原型:不与后端、引擎交互,棋局与评估数据全部来自 assets/data.js。
 * 关键点是「最佳步骤行」:每个失误/错误/严重失误/漏着的正下方,紧跟一行引擎的最佳续着,
 * 点它可以在棋盘上从出错前的局面逐步演示。
 */

import { Chessboard } from '../../../src/ChessGameD.Web/wwwroot/lib/cm-chessboard/src/Chessboard.js';
import { Arrows, ARROW_TYPE } from '../../../src/ChessGameD.Web/wwwroot/lib/cm-chessboard/src/extensions/arrows/Arrows.js';
import { Chess } from '../../../src/ChessGameD.Web/wwwroot/lib/chess.js/chess.js';
import { DEMO } from './data.js';

var moves = DEMO.moves;
var points = DEMO.points;
var classifications = DEMO.classifications;

var state = {
    ply: 0,             // 当前局面(0 = 开局)
    orientation: 'w',
    analyzed: false,    // 是否已经"分析本局"
    analyzing: false,
    trialFen: null,     // 用户在棋盘上试走之后的局面
    token: 0,           // 演示令牌,用来打断上一次演示
    playback: null      // { kind: 'pv' | 'best', steps: [...], index: n }
};

var boardEl = document.getElementById('board');
var board = new Chessboard(boardEl, {
    position: moves[0].fenBefore,
    orientation: state.orientation,
    assetsUrl: '../../../src/ChessGameD.Web/wwwroot/lib/cm-chessboard/assets/',
    style: {
        cssClass: 'paper',
        showCoordinates: true,
        borderType: 'none',
        animationDuration: 200
    }
});
board.addExtension(Arrows, { sprite: 'extensions/arrows/arrows.svg' });

/* ---------------- 小工具 ---------------- */

function el(id) {
    return document.getElementById(id);
}

function delay(ms) {
    return new Promise(function (resolve) { setTimeout(resolve, ms); });
}

/** 分值格式:与线上 CG.formatScore 保持一致(相对白方)。 */
function formatScore(cp, mate) {
    if (mate !== null && mate !== undefined) { return 'M' + mate; }
    if (cp === null || cp === undefined) { return '—'; }
    var pawns = cp / 100;
    return (pawns > 0 ? '+' : '') + pawns.toFixed(2);
}

/** 局面分值:统一折成"相对白方"的厘兵数,杀棋取极大值。 */
function pointCp(ply) {
    var point = null;
    for (var i = 0; i < points.length; i++) {
        if (points[i].ply === ply) { point = points[i]; break; }
    }
    if (!point) { return 0; }
    if (point.mate !== null && point.mate !== undefined) { return point.mate >= 0 ? 10000 : -10000; }
    return point.cp === null ? 0 : point.cp;
}

function pointOf(ply) {
    for (var i = 0; i < points.length; i++) {
        if (points[i].ply === ply) { return points[i]; }
    }
    return null;
}

function clsOf(ply) {
    for (var i = 0; i < classifications.length; i++) {
        if (classifications[i].ply === ply) { return classifications[i]; }
    }
    return null;
}

/** 走完第 ply 手之后的局面。moves[i] 是第 i+1 手,fen 是走完之后的局面。 */
function fenOf(ply) {
    if (ply <= 0) { return moves[0].fenBefore; }
    return moves[ply - 1].fen;
}

/** 字符串哈希:用来给假数据一个稳定的抖动,避免每次刷新都在变。 */
function hashOf(text) {
    var h = 2166136261;
    for (var i = 0; i < text.length; i++) {
        h ^= text.charCodeAt(i);
        h = Math.imul(h, 16777619);
    }
    return h >>> 0;
}

var VALUE = { p: 1, n: 3, b: 3, r: 5, q: 9, k: 0 };

/** 假引擎:从某个局面里挑一个"像引擎"的首着(优先吃子,其次将军)。 */
function pickEngineMove(chess) {
    var legal = chess.moves({ verbose: true });
    if (!legal.length) { return null; }

    var captures = legal
        .filter(function (m) { return m.captured; })
        .sort(function (a, b) { return (VALUE[b.captured] || 0) - (VALUE[a.captured] || 0); });
    if (captures.length) { return captures[0]; }

    var checks = legal.filter(function (m) { return m.san.indexOf('+') >= 0 || m.san.indexOf('#') >= 0; });
    if (checks.length) { return checks[0]; }

    return legal[0];
}

/** 把 UCI 列表拆成带 SAN 与局面的步骤,顺便校验合法性。 */
function buildSteps(startFen, uciList) {
    var steps = [];
    if (!uciList || !uciList.length) { return steps; }

    var chess;
    try { chess = new Chess(startFen); } catch (e) { return steps; }

    for (var i = 0; i < uciList.length; i++) {
        var uci = uciList[i];
        if (!uci || uci.length < 4) { break; }
        var move;
        try {
            move = chess.move({
                from: uci.slice(0, 2),
                to: uci.slice(2, 4),
                promotion: uci.length > 4 ? uci[4] : 'q'
            });
        } catch (e) { break; }
        if (!move) { break; }
        steps.push({ uci: uci, san: move.san, fen: chess.fen() });
    }
    return steps;
}

/* ---------------- 走法树 + 最佳步骤行 ---------------- */

/** 坏棋才挂最佳行:失误 / 错误 / 严重失误 / 漏着。 */
function hasBestRow(cls) {
    if (!cls || !cls.line || !cls.line.length) { return false; }
    return cls.key === 'mc-inaccuracy' || cls.key === 'mc-mistake'
        || cls.key === 'mc-blunder' || cls.key === 'mc-missed';
}

function renderMoveList() {
    var list = el('movelist');
    list.innerHTML = '';

    for (var i = 0; i < moves.length; i++) {
        var move = moves[i];
        var cls = state.analyzed ? clsOf(move.ply) : null;

        list.appendChild(buildMoveRow(move, cls));

        if (hasBestRow(cls)) {
            list.appendChild(buildBestRow(cls, move));
        }
    }

    highlightCurrentMove();
    updateSummary();
}

/** 单独抽出来,免得循环里的 var 闭包把点击事件都指向最后一行。 */
function buildMoveRow(move, cls) {
    var row = document.createElement('div');
    row.className = 'mrow';
    row.dataset.ply = String(move.ply);

    var no = document.createElement('span');
    no.className = 'mno';
    no.textContent = Math.ceil(move.ply / 2) + (move.ply % 2 === 1 ? '.' : '…');
    row.appendChild(no);

    // 分类徽标占固定宽度,各行的着法才对得齐
    var slot = document.createElement('span');
    slot.className = 'mchip';
    if (cls) {
        var chip = document.createElement('span');
        chip.className = 'chip ' + cls.key;
        chip.textContent = cls.label;
        slot.appendChild(chip);
    }
    row.appendChild(slot);

    var san = document.createElement('span');
    san.className = 'msan';
    san.textContent = move.san;
    row.appendChild(san);

    if (cls && cls.note) {
        var note = document.createElement('span');
        note.className = 'mnote';
        note.textContent = cls.note;
        note.title = cls.note;
        row.appendChild(note);
    }

    var evalEl = document.createElement('span');
    evalEl.className = 'meval';
    var point = pointOf(move.ply);
    evalEl.textContent = state.analyzed && point ? formatScore(point.cp, point.mate) : '';
    row.appendChild(evalEl);

    row.addEventListener('click', function () { goTo(move.ply); });
    return row;
}

/** 最佳步骤行:显示引擎首着 + 后续续着 + 损失量,点一下在棋盘上演示。 */
function buildBestRow(cls, move) {
    var row = document.createElement('div');
    row.className = 'brow';
    row.dataset.ply = String(move.ply);

    var steps = buildSteps(move.fenBefore, cls.line);
    if (!steps.length) { return document.createComment('empty'); }

    var mark = document.createElement('span');
    mark.className = 'bmark';
    mark.textContent = '↳';
    row.appendChild(mark);

    var line = document.createElement('span');
    line.className = 'bline';
    steps.forEach(function (step, index) {
        if (index > 0) { line.appendChild(document.createTextNode(' ')); }
        var text = document.createElement(index === 0 ? 'b' : 'span');
        text.textContent = step.san;
        line.appendChild(text);
    });
    row.appendChild(line);

    var loss = document.createElement('span');
    loss.className = 'bloss';
    // 走完最佳着法之后的分值:坏棋损失了多少,补回去就是引擎给的分数
    var bestCp = pointCp(move.ply) + cls.lossCp;
    loss.textContent = '损失 ' + (cls.lossCp / 100).toFixed(2) + ' · ' + formatScore(bestCp, null);
    row.appendChild(loss);

    row.title = '点击:从这一步之前的局面开始,演示引擎的最佳续着';
    row.addEventListener('click', function () {
        playLine(move.fenBefore, cls.line, 'best', move.ply);
    });

    return row;
}

function highlightCurrentMove() {
    var rows = document.querySelectorAll('#movelist .mrow');
    for (var i = 0; i < rows.length; i++) {
        rows[i].classList.toggle('current', Number(rows[i].dataset.ply) === state.ply);
    }
}

function updateSummary() {
    var summary = el('moves-summary');
    if (!state.analyzed) {
        summary.textContent = '尚未分析';
        return;
    }

    var bad = 0;
    for (var i = 0; i < classifications.length; i++) {
        if (hasBestRow(classifications[i])) { bad++; }
    }
    summary.textContent = '共 ' + moves.length + ' 手 · ' + bad + ' 处需要复盘';
}

/* ---------------- 导航 ---------------- */

function goTo(ply) {
    stopPlayback();
    state.ply = Math.max(0, Math.min(moves.length - 1, ply));
    state.trialFen = null;

    board.setPosition(fenOf(state.ply), true);
    board.removeArrows();

    updateHeader();
    highlightCurrentMove();
    updateGauge();
    updateStoredRec();
    updateInstant();
    drawChart();
}

function updateHeader() {
    var label = el('ply-label');
    if (state.ply === 0) {
        label.textContent = '开局';
        return;
    }
    var move = moves[state.ply - 1];
    label.textContent = Math.ceil(state.ply / 2) + (state.ply % 2 === 1 ? '. ' : '… ') + move.san;
}

function flipBoard() {
    state.orientation = state.orientation === 'w' ? 'b' : 'w';
    board.setOrientation(state.orientation, true);
}

/* ---------------- 评估条 ---------------- */

function updateGauge() {
    var white = el('gauge-white');
    var black = el('gauge-black');
    var label = el('gauge-label');

    var cp = state.trialFen ? trialEval() : pointCp(state.ply);
    var share = 1 / (1 + Math.pow(10, -cp / 400));
    share = Math.max(0.03, Math.min(0.97, share));

    white.style.height = (share * 100).toFixed(1) + '%';
    black.style.height = ((1 - share) * 100).toFixed(1) + '%';

    if (state.trialFen) {
        label.textContent = formatScore(cp, null);
        label.title = '试走局面(演示估算)';
    } else {
        var point = pointOf(state.ply);
        label.textContent = state.analyzed ? formatScore(point.cp, point.mate) : '—';
        label.title = state.analyzed ? '相对白方' : '尚未分析';
    }
}

/* ---------------- 评估曲线 ---------------- */

function chartY(cp, mid, half) {
    var v = Math.max(-1000, Math.min(1000, cp));
    var t = (v < 0 ? -1 : 1) * Math.sqrt(Math.abs(v) / 1000);
    return mid - t * half;
}

function drawChart() {
    var canvas = el('chart');
    if (!canvas) { return; }

    var dpr = window.devicePixelRatio || 1;
    var cssWidth = canvas.clientWidth || 420;
    var cssHeight = 220;
    canvas.width = Math.round(cssWidth * dpr);
    canvas.height = Math.round(cssHeight * dpr);

    var ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, cssWidth, cssHeight);

    var padX = 8;
    var padTop = 10;
    var padBottom = 18;
    var mid = padTop + (cssHeight - padTop - padBottom) / 2;
    var half = (cssHeight - padTop - padBottom) / 2;
    var maxPly = Math.max(1, moves.length);
    var xOf = function (ply) { return padX + (ply / maxPly) * (cssWidth - padX * 2); };

    // 0 线
    ctx.strokeStyle = '#ded7ca';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(padX, mid);
    ctx.lineTo(cssWidth - padX, mid);
    ctx.stroke();

    if (!state.analyzed) {
        ctx.fillStyle = '#a79f93';
        ctx.font = '12px sans-serif';
        ctx.fillText('分析本局后显示评估曲线', padX + 4, mid - 8);
        return;
    }

    // 白方占优的部分填浅靛蓝,黑方占优填暖灰
    function fillArea(sign, color) {
        ctx.beginPath();
        ctx.moveTo(xOf(0), mid);
        for (var i = 0; i < points.length; i++) {
            var p = points[i];
            var cp = p.mate !== null && p.mate !== undefined ? (p.mate >= 0 ? 10000 : -10000) : p.cp;
            if (cp === null) { continue; }
            var y = chartY(cp, mid, half);
            if ((sign > 0) !== (y < mid)) { continue; }
            ctx.lineTo(xOf(p.ply), y);
        }
        ctx.lineTo(xOf(moves.length), mid);
        ctx.closePath();
        ctx.fillStyle = color;
        ctx.fill();
    }
    fillArea(1, 'rgba(79, 70, 229, .13)');
    fillArea(-1, 'rgba(150, 60, 40, .10)');

    // 折线
    ctx.strokeStyle = '#4f46e5';
    ctx.lineWidth = 1.8;
    ctx.beginPath();
    var started = false;
    for (var i = 0; i < points.length; i++) {
        var p = points[i];
        var cp = p.mate !== null && p.mate !== undefined ? (p.mate >= 0 ? 10000 : -10000) : p.cp;
        if (cp === null) { continue; }
        var x = xOf(p.ply);
        var y = chartY(cp, mid, half);
        if (!started) { ctx.moveTo(x, y); started = true; } else { ctx.lineTo(x, y); }
    }
    ctx.stroke();

    // 坏棋打点
    for (var j = 0; j < classifications.length; j++) {
        var cls = classifications[j];
        if (!hasBestRow(cls)) { continue; }
        var color = cls.key === 'mc-missed' ? '#7c3aed'
            : cls.key === 'mc-blunder' ? '#c02b2b'
                : cls.key === 'mc-mistake' ? '#d0563f' : '#c07c12';
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.arc(xOf(cls.ply), chartY(pointCp(cls.ply), mid, half), 3, 0, Math.PI * 2);
        ctx.fill();
    }

    // 当前局面
    var cur = xOf(state.ply);
    ctx.strokeStyle = 'rgba(38, 34, 29, .35)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(cur, padTop);
    ctx.lineTo(cur, cssHeight - padBottom);
    ctx.stroke();
}

if (el('chart')) {
    el('chart').addEventListener('click', function (evt) {
        if (!state.analyzed) { return; }
        var rect = el('chart').getBoundingClientRect();
        var x = evt.clientX - rect.left;
        var maxPly = Math.max(1, moves.length);
        var ply = Math.round((x / rect.width) * maxPly);
        goTo(Math.max(0, Math.min(moves.length - 1, ply)));
    });
}

/* ---------------- 假引擎:当前局面评估 ---------------- */

/** 试走局面的假分值:在当前分值上叠一点稳定的抖动。 */
function trialEval() {
    var base = pointCp(state.ply);
    var jitter = (hashOf(state.trialFen) % 61) - 30;
    return base + jitter;
}

/** 当前局面的主变:引擎和实战一致时用实战续着,不一致时用引擎的最佳行。 */
function currentPvSteps() {
    var next = state.analyzed ? clsOf(state.ply + 1) : null;

    if (next && hasBestRow(next)) {
        var move = moves[next.ply - 1];
        return buildSteps(move.fenBefore, next.line);
    }

    if (state.analyzed) {
        var ucis = [];
        for (var i = state.ply; i < Math.min(moves.length, state.ply + 4); i++) {
            ucis.push(moves[i].uci);
        }
        if (ucis.length) { return buildSteps(fenOf(state.ply), ucis); }
    }

    // 还没分析:随便给一条像样的续着
    var fen = state.trialFen || fenOf(state.ply);
    var fake = [];
    var chess = new Chess(fen);
    for (var j = 0; j < 4; j++) {
        var m = pickEngineMove(chess);
        if (!m) { break; }
        fake.push(m.from + m.to + (m.promotion || ''));
        chess.move({ from: m.from, to: m.to, promotion: m.promotion || 'q' });
    }
    return buildSteps(fen, fake);
}

function updateStoredRec() {
    var rec = el('stored-rec');
    var next = state.analyzed ? clsOf(state.ply + 1) : null;
    if (next && next.bestSan) {
        rec.textContent = next.bestSan;
        rec.title = '引擎在这个局面推荐的着法(来自整局分析)';
    } else {
        rec.textContent = state.analyzed ? '—' : '未分析';
        rec.title = state.analyzed ? '引擎没有给出不同的推荐' : '分析本局后显示引擎推荐着法';
    }
}

function updateInstant() {
    var steps = currentPvSteps();
    var fen = state.trialFen || fenOf(state.ply);
    var cp = state.trialFen ? trialEval() : pointCp(state.ply);
    var point = pointOf(state.ply);
    var hash = hashOf(fen);

    el('inst-score').textContent = formatScore(cp, state.trialFen ? null : (point ? point.mate : null));
    el('inst-depth').textContent = String(20 + (hash % 6));
    el('inst-nodes').textContent = (900000 + (hash % 1200000)).toLocaleString('en-US');

    // 主变
    var pv = el('inst-pv');
    pv.innerHTML = '';
    if (!steps.length) {
        pv.textContent = '—';
    } else {
        steps.forEach(function (step, index) {
            var span = document.createElement('span');
            span.className = 'pv-move';
            span.textContent = step.san;
            span.title = '点击在棋盘上演示到这一步';
            span.addEventListener('click', function () {
                playLine(fen, uciListOf(steps), 'pv', state.ply);
            });
            pv.appendChild(span);
            if (index < steps.length - 1) { pv.appendChild(document.createTextNode(' ')); }
        });
    }

    // 候选
    var cands = el('inst-cands');
    cands.innerHTML = '';
    if (point && point.mate !== null && !state.trialFen) {
        // 已经是杀棋局面,再列候选没有意义
        cands.innerHTML = '<div class="small muted">已是杀棋,不用再找别的着法。</div>';
        return;
    }
    buildCandidates(fen, cp, steps).forEach(function (cand) {
        var row = document.createElement('div');
        row.className = 'cand';

        var score = document.createElement('span');
        score.className = 'score';
        score.textContent = formatScore(cand.cp, null);
        row.appendChild(score);

        var movesEl = document.createElement('span');
        movesEl.className = 'moves';
        movesEl.textContent = cand.san.join(' ');
        row.appendChild(movesEl);

        cands.appendChild(row);
    });
}

function uciListOf(steps) {
    return steps.map(function (step) { return step.uci; });
}

/** 候选着法:主变首着排第一,再补两个别的着法,配上递减的假分值。 */
function buildCandidates(fen, baseCp, pvSteps) {
    var chess;
    try { chess = new Chess(fen); } catch (e) { return []; }

    var legal = chess.moves({ verbose: true });
    if (!legal.length) { return []; }

    // 主变的第一着必须排在候选列表第一位
    var first = null;
    if (pvSteps && pvSteps.length) {
        var pvUci = pvSteps[0].uci;
        for (var i = 0; i < legal.length; i++) {
            if (legal[i].from + legal[i].to + (legal[i].promotion || '') === pvUci) {
                first = legal[i];
                break;
            }
        }
    }

    var rest = legal.filter(function (m) { return m !== first; });
    var captures = rest.filter(function (m) { return m.captured; });
    var checks = rest.filter(function (m) { return m.san.indexOf('+') >= 0 || m.san.indexOf('#') >= 0; });
    var picked = [];
    if (first) { picked.push(first); }
    picked = picked.concat(captures, checks);
    for (var r = 0; r < rest.length && picked.length < 3; r++) {
        if (picked.indexOf(rest[r]) < 0) { picked.push(rest[r]); }
    }
    picked = picked.slice(0, 3);

    var out = [];
    for (var j = 0; j < picked.length; j++) {
        var move = picked[j];
        var uci = move.from + move.to + (move.promotion || '');
        var line = buildSteps(fen, [uci]);

        // 续着再补两手,看起来更像一条真的 PV
        var extra = [];
        var sub = new Chess(line.length ? line[0].fen : fen);
        for (var k = 0; k < 2; k++) {
            var m = pickEngineMove(sub);
            if (!m) { break; }
            extra.push(m.from + m.to + (m.promotion || ''));
            sub.move({ from: m.from, to: m.to, promotion: m.promotion || 'q' });
        }
        if (extra.length) {
            line = buildSteps(fen, [uci].concat(extra));
        }

        var jitter = ((hashOf(fen + move.from + move.to) % 25) - 12);
        out.push({
            cp: (j === 0 ? baseCp : baseCp - j * 22) + jitter,
            san: line.map(function (step) { return step.san; }),
            uci: uciListOf(line)
        });
    }

    return out;
}

/* ---------------- 演示:主变 / 最佳行 ---------------- */

/**
 * 在棋盘上逐步演示一条线。
 * kind = 'pv'(当前局面的主变)| 'best'(坏棋下面的最佳续着)
 */
async function playLine(startFen, uciList, kind, ply) {
    stopPlayback();

    var steps = buildSteps(startFen, uciList);
    if (!steps.length) { return; }

    var token = ++state.token;
    state.playback = { kind: kind, steps: steps, index: -1, ply: ply };

    el('btn-back').classList.remove('hidden');
    setMoveInputEnabled(false);

    await board.setPosition(startFen, true);
    if (token !== state.token) { return; }

    for (var i = 0; i < steps.length; i++) {
        if (token !== state.token) { return; }

        state.playback.index = i;
        var step = steps[i];

        board.removeArrows();
        board.addArrow(ARROW_TYPE.default, step.uci.slice(0, 2), step.uci.slice(2, 4));
        await board.setPosition(step.fen, true);
        if (token !== state.token) { return; }

        updatePlaybackStatus(kind, step, i, steps.length);
        await delay(560);
    }
}

function updatePlaybackStatus(kind, step, index, total) {
    var status = el('play-status');
    if (kind === 'best') {
        status.textContent = '最佳行演示 ' + (index + 1) + '/' + total + ':' + step.san
            + ' —— 点「返回原局面」回到棋局';
    } else {
        status.textContent = '主变演示 ' + (index + 1) + '/' + total + ':' + step.san
            + ' —— 点「返回原局面」回到棋局';
    }
    markPlayingRow(kind, step);
}

/** 演示时高亮对应的走法行/最佳行。 */
function markPlayingRow(kind, step) {
    var rows = document.querySelectorAll('#movelist .brow');
    for (var i = 0; i < rows.length; i++) {
        rows[i].classList.remove('playing');
    }
    if (kind !== 'best' || !state.playback) { return; }

    var target = null;
    var bestRows = document.querySelectorAll('#movelist .brow');
    for (var j = 0; j < bestRows.length; j++) {
        var line = bestRows[j].querySelector('.bline');
        if (line && line.textContent.indexOf(step.san) >= 0) {
            target = bestRows[j];
            break;
        }
    }
    if (target) { target.classList.add('playing'); }
}

/** 打断演示:棋盘由调用方复位。 */
function stopPlayback() {
    state.token++;
    state.playback = null;
    el('play-status').textContent = '';
    el('btn-back').classList.add('hidden');
    setMoveInputEnabled(true);
}

/* ---------------- 棋盘试走 ---------------- */

var moveInputEnabled = false;

function setMoveInputEnabled(enabled) {
    if (enabled === moveInputEnabled) { return; }
    moveInputEnabled = enabled;
    if (enabled) {
        board.enableMoveInput(onMoveInput);
    } else {
        board.disableMoveInput();
    }
}

function onMoveInput(evt) {
    if (evt.type === 'validateMoveInput') {
        return tryUserMove(evt.squareFrom, evt.squareTo);
    }
    if (evt.type === 'moveInputFinished') {
        // 棋盘只给出棋盘摆法,完整 FEN 在 validateMoveInput 里已经算好了
        if (!pendingTrialFen) { return true; }
        state.trialFen = pendingTrialFen;
        pendingTrialFen = null;
        updateGauge();
        updateInstant();
        el('play-status').textContent = '试走局面(演示估算)—— 点「返回原局面」回到棋局。';
    }
    return true;
}

var pendingTrialFen = null;

function tryUserMove(from, to) {
    var baseFen = fenOf(state.ply);
    var chess;
    try { chess = new Chess(baseFen); } catch (e) { return false; }

    try {
        var move = chess.move({ from: from, to: to, promotion: 'q' });
        if (!move) { return false; }
        pendingTrialFen = chess.fen();
        return true;
    } catch (e) {
        return false;
    }
}

/* ---------------- 模拟整局分析 ---------------- */

var analysisTimer = null;

function startAnalysis(force) {
    if (state.analyzing) { return; }
    state.analyzing = true;

    var btn = el('btn-analyze');
    var btnForce = el('btn-analyze-force');
    var btnStop = el('btn-stop');
    btn.disabled = true;
    btnForce.disabled = true;
    btnStop.disabled = false;

    var progress = el('progress');
    var bar = el('progress-bar');
    var status = el('analysis-status');
    progress.classList.remove('hidden');

    if (force) {
        state.analyzed = false;
        renderMoveList();
        drawChart();
    }

    // 排队(和线上一样先进先出)
    status.textContent = '排队中(第 1 位,共 1 个任务)…';
    bar.style.width = '0%';

    setTimeout(function () {
        if (!state.analyzing) { return; }
        var total = moves.length + 1;
        var done = 0;
        analysisTimer = setInterval(function () {
            done += 3 + Math.floor(Math.random() * 5);
            if (done > total) { done = total; }
            bar.style.width = ((done / total) * 100).toFixed(1) + '%';
            status.textContent = '分析中 ' + done + ' / ' + total + ' 个局面('
                + ((done / total) * 100).toFixed(0) + '%)';

            if (done >= total) {
                clearInterval(analysisTimer);
                analysisTimer = null;
                finishAnalysis();
            }
        }, 120);
    }, 420);
}

function finishAnalysis() {
    state.analyzing = false;
    state.analyzed = true;

    el('btn-analyze').disabled = false;
    el('btn-analyze-force').disabled = false;
    el('btn-stop').disabled = true;
    el('progress').classList.add('hidden');
    el('analysis-status').textContent = '分析完成。';

    renderMoveList();
    updateStoredRec();
    updateGauge();
    updateInstant();
    drawChart();
}

function stopAnalysis() {
    if (!state.analyzing) { return; }
    state.analyzing = false;
    if (analysisTimer) {
        clearInterval(analysisTimer);
        analysisTimer = null;
    }
    el('btn-analyze').disabled = false;
    el('btn-analyze-force').disabled = false;
    el('btn-stop').disabled = true;
    el('progress').classList.add('hidden');
    el('analysis-status').textContent = '已停止。';
}

/* ---------------- 跳到下一个问题手 ---------------- */

/** 跳到当前局面之后的下一个"需要复盘"的着法。 */
function gotoNextProblem() {
    var bad = [];
    for (var i = 0; i < classifications.length; i++) {
        if (hasBestRow(classifications[i])) { bad.push(classifications[i].ply); }
    }
    if (!bad.length) { return; }

    var target = null;
    for (var j = 0; j < bad.length; j++) {
        if (bad[j] > state.ply) { target = bad[j]; break; }
    }
    if (target === null) { target = bad[0]; }   // 到头了就回到第一个

    goTo(target);
    var row = document.querySelector('#movelist .mrow[data-ply="' + target + '"]');
    if (row) { row.scrollIntoView({ block: 'center' }); }
}

/* ---------------- 事件绑定 ---------------- */

function bind() {
    el('btn-first').addEventListener('click', function () { goTo(0); });
    el('btn-prev').addEventListener('click', function () { goTo(state.ply - 1); });
    el('btn-next').addEventListener('click', function () { goTo(state.ply + 1); });
    el('btn-last').addEventListener('click', function () { goTo(moves.length - 1); });
    el('btn-flip').addEventListener('click', flipBoard);
    el('btn-back').addEventListener('click', function () { goTo(state.ply); });

    el('btn-analyze').addEventListener('click', function () { startAnalysis(false); });
    el('btn-analyze-force').addEventListener('click', function () { startAnalysis(true); });
    el('btn-stop').addEventListener('click', stopAnalysis);
    el('btn-next-problem').addEventListener('click', gotoNextProblem);

    document.addEventListener('keydown', function (evt) {
        if (evt.target && /input|textarea/i.test(evt.target.tagName)) { return; }
        switch (evt.key) {
            case 'ArrowLeft': goTo(state.ply - 1); break;
            case 'ArrowRight': goTo(state.ply + 1); break;
            case 'Home': goTo(0); break;
            case 'End': goTo(moves.length - 1); break;
            case 'f': case 'F': flipBoard(); break;
        }
    });

    window.addEventListener('resize', drawChart);
}

/* ---------------- 启动 ---------------- */

el('pgn').textContent = DEMO.game.pgn;
el('engine-badge').textContent = DEMO.game.engine;

bind();
renderMoveList();
updateHeader();
updateGauge();
updateStoredRec();
updateInstant();
drawChart();
setMoveInputEnabled(true);

// 暴露给控制台,方便调试
window.CGDemo = {
    state: state,
    goTo: goTo,
    startAnalysis: startAnalysis
};
