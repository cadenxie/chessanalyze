import { Chessboard } from '/lib/cm-chessboard/src/Chessboard.js';
import { Arrows, ARROW_TYPE } from '/lib/cm-chessboard/src/extensions/arrows/Arrows.js';
import { Chess } from '/lib/chess.js/chess.js';

const data = CG.readJsonScript('game-data');
const moves = data ? data.moves : [];
const gameId = data ? data.gameId : 0;
const initialFen = data && data.initialFen
    ? data.initialFen
    : 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

const state = {
    ply: 0,                 // 当前局面(0 = 开局)
    orientation: 'w',
    analyzed: false,        // 是否已经有整局分析结果
    analyzing: false,
    classifications: {},    // ply -> 分类信息(含最佳续着)
    points: [],             // [{ply, scoreCp, scoreMate, depth}]
    instant: null,          // 当前局面的即时分析结果
    trialFen: null,         // 用户在棋盘上试走之后的局面
    token: 0,               // 演示令牌,用来打断上一次演示
    playback: null          // { kind: 'pv' | 'best', steps: [...], index: n }
};

const boardEl = document.getElementById('board');
const chartCanvas = document.getElementById('chart');
const board = new Chessboard(boardEl, {
    position: initialFen,
    orientation: state.orientation,
    assetsUrl: '/lib/cm-chessboard/assets/',
    style: {
        cssClass: 'paper',
        showCoordinates: true,
        borderType: 'none',
        animationDuration: 180
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

/** 走完第 ply 手之后的局面。moves[i] 是第 i+1 手,fen 是走完之后的局面。 */
function fenOf(ply) {
    if (ply <= 0) { return initialFen; }
    return moves[ply - 1].fen;
}

function pointOf(ply) {
    for (const p of state.points) {
        if (p.ply === ply) { return p; }
    }
    return null;
}

/** 局面分值统一折成"相对白方"的厘兵数,杀棋取极大值。 */
function pointCp(ply) {
    const point = pointOf(ply);
    if (!point) { return 0; }
    if (point.scoreMate !== null && point.scoreMate !== undefined) {
        return point.scoreMate >= 0 ? 10000 : -10000;
    }
    return point.scoreCp === null || point.scoreCp === undefined ? 0 : point.scoreCp;
}

function clsOf(ply) {
    return state.classifications[ply] || null;
}

/** 只有坏棋才挂「最佳步骤行」:失误 / 错误 / 严重失误 / 漏着。 */
function hasBestRow(cls) {
    if (!cls || !cls.bestLineSan || !cls.bestLineSan.length) { return false; }
    return cls.cssClass === 'mc-inaccuracy' || cls.cssClass === 'mc-mistake'
        || cls.cssClass === 'mc-blunder' || cls.cssClass === 'mc-missed';
}

/* ---------------- 走法与最佳行 ---------------- */

function renderMoveList() {
    const list = el('movelist');
    list.innerHTML = '';

    for (const move of moves) {
        const cls = state.analyzed ? clsOf(move.ply) : null;
        list.appendChild(buildMoveRow(move, cls));
        if (hasBestRow(cls)) {
            list.appendChild(buildBestRow(cls, move));
        }
    }

    highlightCurrentMove();
    updateSummary();
}

/** 单独抽出来,免得循环里的闭包把点击事件都指向最后一行。 */
function buildMoveRow(move, cls) {
    const row = document.createElement('div');
    row.className = 'mrow';
    row.dataset.ply = String(move.ply);

    const no = document.createElement('span');
    no.className = 'mno';
    no.textContent = Math.ceil(move.ply / 2) + (move.ply % 2 === 1 ? '.' : '…');
    row.appendChild(no);

    // 分类徽标占固定宽度,各行的着法才对得齐
    const slot = document.createElement('span');
    slot.className = 'mchip';
    if (cls) {
        const chip = document.createElement('span');
        chip.className = 'chip ' + cls.cssClass;
        chip.textContent = cls.label;
        slot.appendChild(chip);
    }
    row.appendChild(slot);

    const san = document.createElement('span');
    san.className = 'msan';
    san.textContent = move.san;
    row.appendChild(san);

    if (cls && cls.lossCp !== undefined && cls.lossCp !== null) {
        const loss = document.createElement('span');
        loss.className = 'mnote';
        loss.textContent = '损失 ' + (cls.lossCp / 100).toFixed(2);
        row.appendChild(loss);
    }

    const evalEl = document.createElement('span');
    evalEl.className = 'meval';
    const point = pointOf(move.ply);
    evalEl.textContent = state.analyzed && point ? CG.formatScore(point.scoreCp, point.scoreMate) : '';
    row.appendChild(evalEl);

    row.addEventListener('click', function () { goTo(move.ply); });
    return row;
}

/**
 * 最佳步骤行:引擎在这个局面上的主线。
 * 服务端已经把它转成了 SAN,这里只负责排版与点击演示。
 */
function buildBestRow(cls, move) {
    const row = document.createElement('div');
    row.className = 'brow';
    row.dataset.ply = String(move.ply);

    const mark = document.createElement('span');
    mark.className = 'bmark';
    mark.textContent = '↳';
    row.appendChild(mark);

    const line = document.createElement('span');
    line.className = 'bline';
    cls.bestLineSan.forEach(function (san, index) {
        if (index > 0) { line.appendChild(document.createTextNode(' ')); }
        const text = document.createElement(index === 0 ? 'b' : 'span');
        text.textContent = san;
        line.appendChild(text);
    });
    row.appendChild(line);

    const tag = document.createElement('span');
    tag.className = 'bloss';
    tag.textContent = '引擎主线';
    row.appendChild(tag);

    row.title = '点击:从这一步之前的局面开始,演示引擎的最佳续着';
    row.addEventListener('click', function () {
        playLine(fenOf(move.ply - 1), cls.bestLineUci || [], 'best', move.ply);
    });

    return row;
}

function highlightCurrentMove() {
    const rows = document.querySelectorAll('#movelist .mrow');
    for (const row of rows) {
        row.classList.toggle('current', Number(row.dataset.ply) === state.ply);
    }
}

function updateSummary() {
    const summary = el('moves-summary');
    if (!state.analyzed) {
        summary.textContent = '尚未分析';
        return;
    }

    let bad = 0;
    for (const key of Object.keys(state.classifications)) {
        if (hasBestRow(state.classifications[key])) { bad++; }
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
    updateStoredRecommendation();
    scheduleInstantAnalysis();
    drawChart();
}

function updateHeader() {
    const label = el('ply-label');
    if (state.ply === 0) {
        label.textContent = '开局';
        return;
    }
    // moves[i] 是第 i+1 手,所以第 ply 手对应 moves[ply - 1]
    const move = moves[state.ply - 1];
    label.textContent = Math.ceil(state.ply / 2) + (move.ply % 2 === 1 ? '. ' : '… ') + move.san;
}

function flipBoard() {
    state.orientation = state.orientation === 'w' ? 'b' : 'w';
    board.setOrientation(state.orientation, true);
}

/* ---------------- 竖评估条 ---------------- */

function updateGauge() {
    const white = el('gauge-white');
    const black = el('gauge-black');
    const label = el('gauge-label');

    const point = pointOf(state.ply);
    let cp = pointCp(state.ply);
    let text = '—';
    let title = '尚未分析';

    if (state.trialFen) {
        cp = trialEval();
        text = CG.formatScore(cp, null);
        title = '试走局面';
    } else if (state.analyzed && point) {
        text = CG.formatScore(point.scoreCp, point.scoreMate);
        title = '相对白方';
    } else if (state.instant) {
        cp = state.instant.scoreMate !== null && state.instant.scoreMate !== undefined
            ? (state.instant.scoreMate >= 0 ? 10000 : -10000)
            : (state.instant.scoreCp || 0);
        text = CG.formatScore(state.instant.scoreCp, state.instant.scoreMate);
        title = '即时评估 · 相对白方';
    }

    const share = Math.max(0.03, Math.min(0.97, 1 / (1 + Math.pow(10, -cp / 400))));
    white.style.height = (share * 100).toFixed(1) + '%';
    black.style.height = ((1 - share) * 100).toFixed(1) + '%';

    label.textContent = text;
    label.title = title;
}

/* ---------------- 评估曲线 ---------------- */

function chartY(cp, mid, half) {
    const v = Math.max(-1000, Math.min(1000, cp));
    const t = (v < 0 ? -1 : 1) * Math.sqrt(Math.abs(v) / 1000);
    return mid - t * half;
}

function drawChart() {
    if (!chartCanvas) { return; }

    const dpr = window.devicePixelRatio || 1;
    const cssWidth = chartCanvas.clientWidth || 420;
    const cssHeight = 220;
    chartCanvas.width = Math.round(cssWidth * dpr);
    chartCanvas.height = Math.round(cssHeight * dpr);

    const ctx = chartCanvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, cssWidth, cssHeight);

    const padX = 8;
    const padTop = 10;
    const padBottom = 18;
    const mid = padTop + (cssHeight - padTop - padBottom) / 2;
    const half = (cssHeight - padTop - padBottom) / 2;
    const maxPly = Math.max(1, moves.length);
    const xOf = function (ply) { return padX + (ply / maxPly) * (cssWidth - padX * 2); };

    ctx.strokeStyle = '#ded7ca';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(padX, mid);
    ctx.lineTo(cssWidth - padX, mid);
    ctx.stroke();

    if (!state.analyzed || state.points.length < 2) {
        ctx.fillStyle = '#a79f93';
        ctx.font = '12px sans-serif';
        ctx.fillText('分析本局后显示评估曲线', padX + 4, mid - 8);
        return;
    }

    // 白方占优填浅靛蓝,黑方占优填暖灰
    function fillArea(sign, color) {
        ctx.beginPath();
        ctx.moveTo(xOf(0), mid);
        for (const p of state.points) {
            const cp = p.scoreMate !== null && p.scoreMate !== undefined
                ? (p.scoreMate >= 0 ? 10000 : -10000)
                : p.scoreCp;
            if (cp === null || cp === undefined) { continue; }
            const y = chartY(cp, mid, half);
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

    ctx.strokeStyle = '#4f46e5';
    ctx.lineWidth = 1.8;
    ctx.beginPath();
    let started = false;
    for (const p of state.points) {
        const cp = p.scoreMate !== null && p.scoreMate !== undefined
            ? (p.scoreMate >= 0 ? 10000 : -10000)
            : p.scoreCp;
        if (cp === null || cp === undefined) { continue; }
        const x = xOf(p.ply);
        const y = chartY(cp, mid, half);
        if (!started) { ctx.moveTo(x, y); started = true; } else { ctx.lineTo(x, y); }
    }
    ctx.stroke();

    // 坏棋打点
    const colors = {
        'mc-missed': '#7c3aed',
        'mc-blunder': '#c02b2b',
        'mc-mistake': '#d0563f',
        'mc-inaccuracy': '#c07c12'
    };
    for (const key of Object.keys(state.classifications)) {
        const cls = state.classifications[key];
        if (!hasBestRow(cls)) { continue; }
        ctx.fillStyle = colors[cls.cssClass] || '#c07c12';
        ctx.beginPath();
        ctx.arc(xOf(cls.ply), chartY(pointCp(cls.ply), mid, half), 3, 0, Math.PI * 2);
        ctx.fill();
    }

    // 当前局面
    const cur = xOf(state.ply);
    ctx.strokeStyle = 'rgba(38, 34, 29, .35)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(cur, padTop);
    ctx.lineTo(cur, cssHeight - padBottom);
    ctx.stroke();
}

if (chartCanvas) {
    chartCanvas.addEventListener('click', function (evt) {
        if (!state.analyzed) { return; }
        const rect = chartCanvas.getBoundingClientRect();
        const x = evt.clientX - rect.left;
        const maxPly = Math.max(1, moves.length);
        const ply = Math.round((x / rect.width) * maxPly);
        goTo(Math.max(0, Math.min(moves.length - 1, ply)));
    });
}

/* ---------------- 即时分析 ---------------- */

let instantTimer = null;

function scheduleInstantAnalysis() {
    if (instantTimer) { clearTimeout(instantTimer); }
    instantTimer = setTimeout(runInstantAnalysis, 350);
}

async function runInstantAnalysis() {
    if (!data || !data.engineInstalled) { return; }
    if (state.analyzing) { return; }

    const fen = state.trialFen || fenOf(state.ply);
    if (!fen) { return; }

    try {
        const result = await CG.fetchJson('/api/engine/analyze', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ fen: fen })
        });
        setInstantPanel(result, fen);
    } catch (e) {
        if (e.status === 409) { return; }   // 整局分析占用引擎,静默跳过
        const detail = el('eval-detail');
        if (detail) { detail.textContent = e.message; }
    }
}

function setInstantPanel(result, fen) {
    state.instant = result;

    const scoreEl = el('inst-score');
    const depthEl = el('inst-depth');
    const nodesEl = el('inst-nodes');
    const pvEl = el('inst-pv');
    const candEl = el('inst-cands');

    // 新的分析结果来了,先把上一次的主变演示打断
    stopPlayback();

    if (!result) {
        scoreEl.textContent = '…';
        depthEl.textContent = '…';
        nodesEl.textContent = '…';
        pvEl.textContent = '…';
        candEl.innerHTML = '';
        return;
    }

    scoreEl.textContent = CG.formatScore(result.scoreCp, result.scoreMate);
    depthEl.textContent = result.depth;
    nodesEl.textContent = result.nodes + (result.nps ? ' (' + result.nps + ' nps)' : '');

    // 主变拆成可点击的步骤,点击后棋盘会逐步演示
    const steps = buildPvSteps(result.pv || [], fen);
    renderPvSteps(steps, fen);
    showPvArrow(steps);

    candEl.innerHTML = (result.lines || []).map(function (line) {
        return '<div class="cand"><span class="score">' + CG.escapeHtml(CG.formatScore(line.scoreCp, line.scoreMate)) +
            '</span><span class="moves">' + CG.escapeHtml(renderPlainSan(line.pv || [], fen)) + '</span></div>';
    }).join('');

    updateGauge();
}

/**
 * 把 UCI 主变拆成逐步的着法,并记录每一步之后的局面。
 * 引擎给的是 UCI(e2e4),这里顺带转成人看的 SAN(e4)。
 */
function buildPvSteps(pv, fen) {
    const steps = [];
    if (!pv || !pv.length) { return steps; }

    let chess;
    try { chess = new Chess(fen); } catch (e) { return steps; }

    for (const uci of pv) {
        if (!uci || uci.length < 4) { break; }
        const from = uci.slice(0, 2);
        const to = uci.slice(2, 4);
        const promotion = uci.length > 4 ? uci[4] : 'q';
        try {
            const move = chess.move({ from: from, to: to, promotion: promotion });
            if (!move) { break; }
            steps.push({ uci: uci, san: move.san, fen: chess.fen() });
        } catch (e) { break; }
    }
    return steps;
}

function renderPlainSan(pv, fen) {
    return buildPvSteps(pv, fen).map(function (step) { return step.san; }).join(' ');
}

/** 渲染主变:每一步都是可点击的着法。内容没变就不重画,避免打断正在进行的点击。 */
function renderPvSteps(steps, fen) {
    const pvEl = el('inst-pv');
    if (!pvEl) { return; }

    const signature = steps.map(function (step) { return step.uci; }).join(',');
    if (pvEl.dataset.signature === signature) { return; }
    pvEl.dataset.signature = signature;
    pvEl.innerHTML = '';

    if (!steps.length) {
        pvEl.textContent = '—';
        return;
    }

    steps.forEach(function (step, index) {
        const span = document.createElement('span');
        span.className = 'pv-move';
        span.dataset.index = String(index);
        span.dataset.uci = step.uci;
        span.textContent = step.san;
        span.title = '点击在棋盘上演示到这一步';
        span.addEventListener('click', function () {
            if (!span.isConnected) { return; }   // 主变已经重画,这次点击作废
            playLine(fen, steps.map(function (s) { return s.uci; }), 'pv', state.ply);
        });
        pvEl.appendChild(span);
        if (index < steps.length - 1) {
            pvEl.appendChild(document.createTextNode(' '));
        }
    });
}

/** 只画箭头,不高亮任何一步。 */
function setPvArrow(uci) {
    board.removeArrows();
    if (uci && uci.length >= 4) {
        board.addArrow(ARROW_TYPE.default, uci.slice(0, 2), uci.slice(2, 4));
    }
}

/** 没在演示时,箭头指向主变第一着(引擎推荐)。 */
function showPvArrow(steps) {
    const first = steps && steps.length ? steps[0] : null;
    setPvArrow(first ? first.uci : null);
}

/* ---------------- 演示:主变 / 最佳续着 ---------------- */

/**
 * 在棋盘上逐步演示一条线。
 * kind = 'pv'(当前局面的主变)| 'best'(坏棋下面的最佳续着)
 */
async function playLine(startFen, uciList, kind, ply) {
    stopPlayback();

    const steps = buildPvSteps(uciList, startFen);
    if (!steps.length) { return; }

    const token = ++state.token;
    state.playback = { kind: kind, steps: steps, index: -1, ply: ply };

    el('btn-back').classList.remove('hidden');
    setMoveInputEnabled(false);

    await board.setPosition(startFen, true);
    if (token !== state.token) { return; }

    for (let i = 0; i < steps.length; i++) {
        if (token !== state.token) { return; }

        state.playback.index = i;
        const step = steps[i];

        board.removeArrows();
        board.addArrow(ARROW_TYPE.default, step.uci.slice(0, 2), step.uci.slice(2, 4));
        await board.setPosition(step.fen, true);
        if (token !== state.token) { return; }

        updatePlaybackStatus(kind, step, i, steps.length);
        await delay(520);
    }
}

function updatePlaybackStatus(kind, step, index, total) {
    const status = el('pv-status');
    const label = kind === 'best' ? '最佳续着演示' : '主变演示';
    status.textContent = label + ' ' + (index + 1) + '/' + total + ':' + step.san
        + ' —— 点「返回原局面」回到棋局';
    markPlayingRow(kind, step);
}

/** 演示时高亮对应的最佳步骤行。 */
function markPlayingRow(kind, step) {
    const rows = document.querySelectorAll('#movelist .brow');
    for (const row of rows) {
        row.classList.remove('playing');
    }
    if (kind !== 'best') { return; }

    for (const row of rows) {
        const line = row.querySelector('.bline');
        if (line && line.textContent.indexOf(step.san) >= 0) {
            row.classList.add('playing');
            break;
        }
    }
}

/** 打断演示:棋盘由调用方复位。 */
function stopPlayback() {
    state.token++;
    state.playback = null;
    el('pv-status').textContent = '';
    el('btn-back').classList.add('hidden');
    setMoveInputEnabled(true);
}

/* ---------------- 整局分析 ---------------- */

async function startWholeGameAnalysis(force) {
    if (state.analyzing) { return; }
    state.analyzing = true;

    const btn = el('btn-analyze');
    const btnForce = el('btn-analyze-force');
    const btnStop = el('btn-stop');
    const progress = el('analysis-progress');
    const bar = el('analysis-bar');
    const status = el('analysis-status');

    btn.disabled = true;
    btnForce.disabled = true;
    btnStop.disabled = false;
    progress.classList.remove('hidden');

    try {
        await CG.fetchJson('/api/games/' + gameId + '/analyze' + (force ? '?force=true' : ''), { method: 'POST' });
        pollAnalysis(bar, status);
    } catch (e) {
        state.analyzing = false;
        btn.disabled = false;
        btnForce.disabled = false;
        btnStop.disabled = true;
        CG.toast(e.message, 'error');
    }
}

async function pollAnalysis(bar, status) {
    try {
        const job = await CG.fetchJson('/api/games/' + gameId + '/analysis/status');
        bar.style.width = (job.percent || 0) + '%';
        status.textContent = job.state === 'queued'
            ? '排队中(第 ' + (job.queuePosition || 1) + ' 位' + (job.queueWaiting > 1 ? ',共 ' + job.queueWaiting + ' 个任务' : '') + ')…'
            : job.state === 'running'
                ? '分析中 ' + job.done + ' / ' + job.total + ' 个局面(' + (job.percent || 0) + '%)'
                : (job.state === 'done' ? '分析完成。'
                    : job.state === 'cancelled' ? '已停止。已完成的局面已保存。'
                        : '失败:' + (job.error || '未知错误'));

        if (job.state === 'running' || job.state === 'queued') {
            setTimeout(function () { pollAnalysis(bar, status); }, 1000);
            return;
        }

        state.analyzing = false;
        el('btn-analyze').disabled = false;
        el('btn-analyze-force').disabled = false;
        el('btn-stop').disabled = true;

        if (job.state === 'done') {
            await loadAnalysisResult();
            CG.toast('整局分析完成。', 'ok');
        }
    } catch (e) {
        state.analyzing = false;
        el('btn-analyze').disabled = false;
        el('btn-analyze-force').disabled = false;
        el('btn-stop').disabled = true;
        status.textContent = '读取进度失败:' + e.message;
    }
}

async function loadAnalysisResult() {
    try {
        const result = await CG.fetchJson('/api/games/' + gameId + '/analysis');
        state.points = result.points || [];
        state.classifications = {};
        (result.classifications || []).forEach(function (c) {
            state.classifications[c.ply] = c;
        });
        state.analyzed = true;
        renderMoveList();
        updateGauge();
        updateStoredRecommendation();
        drawChart();
    } catch (e) {
        // 还没有结果,忽略
    }
}

/**
 * 当前局面下,整局分析算出来的引擎推荐着法。
 *
 * 注意方向:classifications[ply] 里的推荐是「走第 ply 手之前那个局面」的最佳着法,
 * 所以当前局面(currentPly)对应的推荐在 classifications[currentPly + 1] 里。
 */
function updateStoredRecommendation() {
    const rec = el('stored-rec');
    const next = clsOf(state.ply + 1);

    if (next && next.bestMoveSan) {
        rec.textContent = next.bestMoveSan;
        rec.title = '引擎在这个局面推荐的着法(来自整局分析)';
    } else {
        rec.textContent = state.analyzed ? '—' : '未分析';
        rec.title = state.analyzed ? '引擎没有给出不同的推荐' : '分析本局后显示引擎推荐着法';
    }
}

/* ---------------- 棋盘试走 ---------------- */

let moveInputEnabled = false;
let pendingTrialFen = null;

function onMoveInput(evt) {
    if (evt.type === 'validateMoveInput') {
        return tryUserMove(evt.squareFrom, evt.squareTo);
    }
    if (evt.type === 'moveInputFinished') {
        if (!pendingTrialFen) { return true; }
        state.trialFen = pendingTrialFen;
        pendingTrialFen = null;
        updateGauge();
        scheduleInstantAnalysis();
        el('pv-status').textContent = '试走局面 —— 点「返回原局面」回到棋局。';
    }
    return true;
}

/** 开启/关闭棋盘拖子。主变演示期间棋盘展示的是主变局面,先禁掉试走。 */
function setMoveInputEnabled(enabled) {
    if (enabled === moveInputEnabled) { return; }
    moveInputEnabled = enabled;
    if (enabled) {
        board.enableMoveInput(onMoveInput);
    } else {
        board.disableMoveInput();
    }
}

function tryUserMove(from, to) {
    let chess;
    try { chess = new Chess(fenOf(state.ply)); } catch (e) { return false; }

    try {
        const move = chess.move({ from: from, to: to, promotion: 'q' });
        if (!move) { return false; }
        pendingTrialFen = chess.fen();
        return true;
    } catch (e) {
        return false;
    }
}

/** 试走局面的分值:用当前分值加一点抖动,够看就行。 */
function trialEval() {
    const base = pointCp(state.ply);
    let hash = 0;
    const fen = state.trialFen || '';
    for (let i = 0; i < fen.length; i++) {
        hash = (hash * 31 + fen.charCodeAt(i)) | 0;
    }
    return base + (Math.abs(hash) % 61) - 30;
}

/* ---------------- 跳到下一个问题手 ---------------- */

/** 跳到当前局面之后的下一个"需要复盘"的着法。 */
function gotoNextProblem() {
    const bad = [];
    for (const key of Object.keys(state.classifications)) {
        if (hasBestRow(state.classifications[key])) {
            bad.push(Number(key));
        }
    }
    if (!bad.length) { return; }
    bad.sort(function (a, b) { return a - b; });

    let target = null;
    for (const ply of bad) {
        if (ply > state.ply) { target = ply; break; }
    }
    if (target === null) { target = bad[0]; }   // 到头了就回到第一个

    goTo(target);
    const row = document.querySelector('#movelist .mrow[data-ply="' + target + '"]');
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
    el('btn-next-problem').addEventListener('click', gotoNextProblem);

    el('btn-analyze').addEventListener('click', function () { startWholeGameAnalysis(false); });
    el('btn-analyze-force').addEventListener('click', function () { startWholeGameAnalysis(true); });
    el('btn-stop').addEventListener('click', async function () {
        try {
            await CG.fetchJson('/api/games/' + gameId + '/analyze/stop', { method: 'POST' });
            CG.toast('已请求停止分析。', 'ok');
        } catch (e) {
            CG.toast(e.message, 'error');
        }
    });

    const reportHtml = el('btn-report-html');
    if (reportHtml) {
        reportHtml.href = '/api/games/' + gameId + '/report?format=html';
    }
    const reportText = el('btn-report-text');
    if (reportText) {
        reportText.addEventListener('click', function () {
            window.location.href = '/api/games/' + gameId + '/report?format=text';
        });
    }

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

bind();
renderMoveList();
updateHeader();
updateGauge();
updateStoredRecommendation();
drawChart();
setMoveInputEnabled(true);     // 默认允许在棋盘上试走
loadAnalysisResult();
scheduleInstantAnalysis();   // 打开页面就先算当前局面的评估

if (!data || !data.engineInstalled) {
    const hint = el('engine-hint');
    if (hint) {
        hint.innerHTML = '<span class="badge warn">引擎未安装</span> <a href="/Settings/Index">去安装</a>';
    }
    const btn = el('btn-analyze');
    if (btn) { btn.disabled = true; }
}
