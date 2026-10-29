import { Chessboard } from '/lib/cm-chessboard/src/Chessboard.js';
import { Arrows, ARROW_TYPE } from '/lib/cm-chessboard/src/extensions/arrows/Arrows.js';
import { Chess } from '/lib/chess.js/chess.js';

const data = CG.readJsonScript('game-data');
const moves = data ? data.moves : [];
const gameId = data ? data.gameId : 0;

let currentPly = 0;
let orientation = 'w';
let analysisPoints = [];        // [{ply, scoreCp, scoreMate, depth}]
let classifications = {};       // ply -> {label, cssClass, lossCp}
let analysisRunning = false;
let instantToken = 0;
let userMoveFen = null;         // 用户在棋盘上试走后的局面
let pendingUserMove = null;
let pvBaseFen = null;           // 主变起算局面的 FEN
let pvSteps = [];               // 主变分解:[{ uci, san, fen }]
let pvToken = 0;                // 演示令牌,用于打断上一次主变演示
let pvCurrent = -1;             // 当前演示到主变第几步(-1 表示未在演示)

const boardEl = document.getElementById('board');
const chartCanvas = document.getElementById('evalchart');
const board = new Chessboard(boardEl, {
    position: moves.length ? moves[0].fen : 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1',
    orientation: orientation,
    assetsUrl: '/lib/cm-chessboard/assets/',
    style: { showCoordinates: true, animationDuration: 180 }
});
board.addExtension(Arrows, { sprite: 'extensions/arrows/arrows.svg' });

/* ---------------- 走法列表 ---------------- */

function renderMoveList() {
    const el = document.getElementById('movelist');
    if (!el) { return; }
    el.innerHTML = '';

    for (let ply = 1; ply < moves.length; ply += 2) {
        const no = document.createElement('div');
        no.className = 'no';
        no.textContent = (ply + 1) / 2 + '.';
        el.appendChild(no);

        el.appendChild(buildSanCell(ply));
        el.appendChild(moves[ply + 1] ? buildSanCell(ply + 1) : document.createElement('div'));
    }
}

function buildSanCell(ply) {
    const move = moves[ply];
    const cell = document.createElement('div');
    cell.className = 'san';
    cell.dataset.ply = String(ply);

    const dot = document.createElement('span');
    dot.className = 'mc';
    dot.style.display = 'none';
    cell.appendChild(dot);

    const text = document.createElement('span');
    text.textContent = move.san;
    cell.appendChild(text);

    // 引擎推荐着法(与实际走法不同时才显示,避免和走法本身重复)
    const rec = document.createElement('span');
    rec.className = 'rec';
    rec.style.display = 'none';
    cell.appendChild(rec);

    cell.addEventListener('click', function () {
        goTo(ply);
    });

    refreshMoveCell(cell, ply);
    return cell;
}

/** 刷新一个走法格子的分类圆点、引擎推荐着法与悬浮提示。 */
function refreshMoveCell(cell, ply) {
    const move = moves[ply];
    const cls = classifications[ply];

    const dot = cell.querySelector('.mc');
    if (dot) {
        if (cls) {
            dot.className = 'mc ' + cls.cssClass;
            dot.style.display = '';
        } else {
            dot.style.display = 'none';
        }
    }

    const rec = cell.querySelector('.rec');
    if (rec) {
        if (cls && cls.bestMoveSan && cls.bestMoveSan !== move.san) {
            rec.textContent = cls.bestMoveSan;
            rec.title = '引擎推荐的着法:' + cls.bestMoveSan;
            rec.style.display = '';
        } else {
            rec.style.display = 'none';
        }
    }

    const tips = [];
    if (cls && cls.bestMoveSan) { tips.push('引擎推荐:' + cls.bestMoveSan); }
    if (cls && cls.label) { tips.push('评价:' + cls.label); }
    if (cls && typeof cls.lossCp === 'number') { tips.push('损失:' + cls.lossCp + ' 厘兵'); }
    if (move.comment) { tips.push(move.comment); }
    cell.title = tips.join('\n');
}

function refreshMoveListBadges() {
    const cells = document.querySelectorAll('#movelist .san');
    cells.forEach(function (cell) {
        refreshMoveCell(cell, Number(cell.dataset.ply));
    });
}

function highlightCurrentMove() {
    const cells = document.querySelectorAll('#movelist .san');
    cells.forEach(function (cell) {
        cell.classList.toggle('current', Number(cell.dataset.ply) === currentPly);
    });
}

/* ---------------- 导航 ---------------- */

function goTo(ply) {
    if (!moves.length) { return; }
    stopPvPlayback();
    currentPly = Math.max(0, Math.min(moves.length - 1, ply));
    userMoveFen = null;
    board.setPosition(moves[currentPly].fen, true);
    board.removeArrows();
    updateHeader();
    highlightCurrentMove();
    updateEvalBar();
    updateStoredRecommendation();
    scheduleInstantAnalysis();
}

/**
 * 当前局面下,整局分析算出来的引擎推荐着法。
 *
 * 注意方向:classifications[ply] 里的推荐是「走第 ply 手之前那个局面」的最佳着法,
 * 所以当前局面(currentPly)对应的推荐在 classifications[currentPly + 1] 里。
 */
function updateStoredRecommendation() {
    const el = document.getElementById('stored-rec');
    if (!el) { return; }

    const next = classifications[currentPly + 1];
    if (next && next.bestMoveSan) {
        el.textContent = next.bestMoveSan;
        el.title = '引擎在这个局面推荐的着法(来自整局分析)';
    } else {
        el.textContent = '—';
        el.title = '分析本局后显示引擎推荐着法';
    }
}

function updateHeader() {
    const label = document.getElementById('ply-label');
    if (!label) { return; }
    if (currentPly === 0) {
        label.textContent = '开局';
    } else {
        const m = moves[currentPly];
        const no = Math.ceil(currentPly / 2);
        label.textContent = no + (currentPly % 2 === 1 ? '. ' : '… ') + m.san;
    }
}

function flipBoard() {
    orientation = orientation === 'w' ? 'b' : 'w';
    board.setOrientation(orientation, true);
}

/* ---------------- 评估条与曲线 ---------------- */

function scoreOfPoint(point) {
    if (!point) { return null; }
    if (point.scoreMate !== null && point.scoreMate !== undefined) {
        return point.scoreMate >= 0 ? 10000 : -10000;
    }
    return point.scoreCp === null || point.scoreCp === undefined ? null : point.scoreCp;
}

function findPoint(ply) {
    for (const p of analysisPoints) {
        if (p.ply === ply) { return p; }
    }
    return null;
}

function updateEvalBar() {
    const point = findPoint(currentPly);
    const white = document.getElementById('eval-white');
    const label = document.getElementById('eval-label');
    const detail = document.getElementById('eval-detail');
    if (!white || !label) { return; }

    if (!point) {
        white.style.width = '50%';
        label.textContent = '—';
        if (detail) { detail.textContent = '该局面还没有分析结果。'; }
        return;
    }

    const cp = scoreOfPoint(point);
    if (cp === null) {
        white.style.width = '50%';
        label.textContent = '—';
    } else {
        const clamped = Math.max(-1000, Math.min(1000, cp));
        const ratio = (clamped + 1000) / 2000;
        white.style.width = (ratio * 100).toFixed(1) + '%';
        label.textContent = CG.formatScore(point.scoreCp, point.scoreMate);
    }

    if (detail) {
        detail.textContent = '深度 ' + point.depth + ' · 相对白方';
    }
}

function drawChart() {
    const canvas = chartCanvas;
    if (!canvas) { return; }

    const dpr = window.devicePixelRatio || 1;
    const cssWidth = canvas.clientWidth || 800;
    const cssHeight = 150;
    canvas.width = Math.round(cssWidth * dpr);
    canvas.height = Math.round(cssHeight * dpr);
    const ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, cssWidth, cssHeight);

    const maxPly = Math.max(1, moves.length - 1);
    const xOf = ply => 20 + (ply / maxPly) * (cssWidth - 32);
    const yOf = cp => {
        const clamped = Math.max(-1000, Math.min(1000, cp));
        return 8 + ((1000 - clamped) / 2000) * (cssHeight - 24);
    };

    // 0 线
    ctx.strokeStyle = '#3a4150';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(20, yOf(0));
    ctx.lineTo(cssWidth - 12, yOf(0));
    ctx.stroke();

    if (analysisPoints.length < 2) {
        ctx.fillStyle = '#6b7383';
        ctx.font = '12px sans-serif';
        ctx.fillText('分析本局后显示评估曲线', 24, cssHeight / 2);
        return;
    }

    ctx.strokeStyle = '#5b8dd6';
    ctx.lineWidth = 1.6;
    ctx.beginPath();
    let started = false;
    for (const p of analysisPoints) {
        const cp = scoreOfPoint(p);
        if (cp === null) { continue; }
        const x = xOf(p.ply);
        const y = yOf(cp);
        if (!started) { ctx.moveTo(x, y); started = true; } else { ctx.lineTo(x, y); }
    }
    ctx.stroke();

    // 当前局面标记
    const current = findPoint(currentPly);
    if (current) {
        const cp = scoreOfPoint(current);
        if (cp !== null) {
            ctx.fillStyle = '#e0b341';
            ctx.beginPath();
            ctx.arc(xOf(currentPly), yOf(cp), 3.5, 0, Math.PI * 2);
            ctx.fill();
        }
    }
}

if (chartCanvas) {
    chartCanvas.addEventListener('click', function (evt) {
        if (!analysisPoints.length) { return; }
        const rect = chartCanvas.getBoundingClientRect();
        const x = evt.clientX - rect.left;
        const maxPly = Math.max(1, moves.length - 1);
        const ply = Math.round(((x - 20) / (rect.width - 32)) * maxPly);
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
    if (analysisRunning) { return; }

    const fen = userMoveFen || (moves[currentPly] ? moves[currentPly].fen : null);
    if (!fen) { return; }

    const token = ++instantToken;
    setInstantPanel(null);

    try {
        const result = await CG.fetchJson('/api/engine/analyze', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ fen: fen })
        });
        if (token !== instantToken) { return; }
        setInstantPanel(result, fen);
    } catch (e) {
        if (token !== instantToken) { return; }
        if (e.status === 409) { return; }   // 整局分析占用引擎,静默跳过
        document.getElementById('eval-detail').textContent = e.message;
    }
}

function setInstantPanel(result, fen) {
    const scoreEl = document.getElementById('inst-score');
    const depthEl = document.getElementById('inst-depth');
    const nodesEl = document.getElementById('inst-nodes');
    const pvEl = document.getElementById('inst-pv');
    const candEl = document.getElementById('inst-candidates');
    const detailEl = document.getElementById('eval-detail');

    // 新的分析结果来了,先把上一次的主变演示打断
    stopPvPlayback();
    pvBaseFen = null;
    pvSteps = [];

    // 演示被打断后,棋盘要回到分析所对应的局面
    if (fen) { board.setPosition(fen, true); }

    if (!result) {
        scoreEl.textContent = '…';
        depthEl.textContent = '…';
        nodesEl.textContent = '…';
        pvEl.textContent = '…';
        pvEl.dataset.signature = '…';   // 标记成占位,下一次有结果时一定会重画
        candEl.innerHTML = '';
        return;
    }

    scoreEl.textContent = CG.formatScore(result.scoreCp, result.scoreMate);
    depthEl.textContent = result.depth;
    nodesEl.textContent = result.nodes + (result.nps ? ' (' + result.nps + ' nps)' : '');
    if (detailEl) {
        detailEl.textContent = '深度 ' + result.depth + ' · 相对白方 · ' + CG.formatScore(result.scoreCp, result.scoreMate)
            + (userMoveFen ? ' · 试走局面(升变按后计算)' : '');
    }

    // 主变拆成可点击的步骤,点击后棋盘会逐步演示
    pvBaseFen = fen;
    pvSteps = buildPvSteps(result.pv || [], fen);
    renderPvSteps(pvSteps);
    showPvArrow();

    candEl.innerHTML = (result.lines || []).map(function (line) {
        return '<div class="cand"><span class="score">' + CG.escapeHtml(CG.formatScore(line.scoreCp, line.scoreMate)) +
            '</span><span class="moves">' + CG.escapeHtml(renderPlainSan(line.pv || [], fen)) + '</span></div>';
    }).join('');
}

function uciToSanList(fen, pv) {
    return buildPvSteps(pv, fen).map(function (step) { return step.san; });
}

function renderPlainSan(pv, fen) {
    return uciToSanList(fen, pv).join(' ');
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

/* ---------------- 主变(PV)逐步演示 ---------------- */

const PV_STEP_PAUSE = 220;    // 每步演示之间的停顿(毫秒)

function delay(ms) {
    return new Promise(function (resolve) { setTimeout(resolve, ms); });
}

/** 渲染主变:每一步都是可点击的着法。内容没变就不重画,避免打断正在进行的点击。 */
function renderPvSteps(steps) {
    const pvEl = document.getElementById('inst-pv');
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
            // 主变已经重画,这次点击作废,免得演示到别的着法上
            if (!span.isConnected) { return; }
            playPvTo(index);
        });
        pvEl.appendChild(span);
        if (index < steps.length - 1) {
            pvEl.appendChild(document.createTextNode(' '));
        }
    });
}

/**
 * 高亮主变中的某一步,并把箭头指到这一步。
 * 按 uci 匹配,主变重画后也不会高亮错位置。
 */
function highlightPvStep(uci) {
    const cells = document.querySelectorAll('#inst-pv .pv-move');
    cells.forEach(function (cell) {
        cell.classList.toggle('pv-current', !!uci && cell.dataset.uci === uci);
    });

    setPvArrow(uci);
}

/** 只画箭头,不高亮任何一步。 */
function setPvArrow(uci) {
    board.removeArrows();
    if (uci && uci.length >= 4) {
        board.addArrow(ARROW_TYPE.default, uci.slice(0, 2), uci.slice(2, 4));
    }
}

/** 没在演示时,箭头指向主变第一着(引擎推荐)。 */
function showPvArrow() {
    const first = pvSteps.length ? pvSteps[0] : null;
    setPvArrow(first ? first.uci : null);
}

function updatePvStatus(san, total) {
    const back = document.getElementById('btn-pv-back');
    if (back) {
        back.style.display = pvCurrent >= 0 ? '' : 'none';
    }

    const el = document.getElementById('pv-status');
    if (!el) { return; }

    if (pvCurrent < 0 || !pvSteps.length) {
        el.textContent = '';
        return;
    }

    const stepSan = san || (pvSteps[pvCurrent] ? pvSteps[pvCurrent].san : '');
    const stepTotal = total || pvSteps.length;
    el.textContent = '主变演示 ' + (pvCurrent + 1) + '/' + stepTotal + ':' + stepSan
        + ' —— 点「返回原局面」或按 ← 回到棋局';
}

/** 打断正在进行的主变演示,棋盘由调用方负责复位。 */
function stopPvPlayback() {
    pvToken++;
    pvCurrent = -1;
    showPvArrow();
    updatePvStatus();
    setMoveInputEnabled(true);
}

/**
 * 从主变起点开始,把棋盘逐步演示到第 index 步。
 * 每一步都是一次带动画的 setPosition,棋子会自己走过去。
 */
async function playPvTo(index) {
    if (!pvSteps.length || !pvBaseFen) { return; }

    const target = Math.max(0, Math.min(pvSteps.length - 1, index));
    const token = ++pvToken;
    const steps = pvSteps;
    const base = pvBaseFen;

    // 演示期间棋盘展示的是主变局面,先禁掉试走
    setMoveInputEnabled(false);

    // 先回到主变起点
    await board.setPosition(base, true);
    if (token !== pvToken) { return; }

    for (let i = 0; i <= target; i++) {
        pvCurrent = i;
        highlightPvStep(steps[i].uci);
        updatePvStatus(steps[i].san, steps.length);
        await board.setPosition(steps[i].fen, true);
        if (token !== pvToken) { return; }
        await delay(PV_STEP_PAUSE);
    }
}

/* ---------------- 整局分析 ---------------- */

async function startWholeGameAnalysis(force) {
    if (analysisRunning) { return; }
    analysisRunning = true;

    const btn = document.getElementById('btn-analyze');
    const btnForce = document.getElementById('btn-analyze-force');
    const btnStop = document.getElementById('btn-stop');
    const progress = document.getElementById('analysis-progress');
    const bar = document.getElementById('analysis-bar');
    const status = document.getElementById('analysis-status');

    btn.disabled = true;
    btnForce.disabled = true;
    btnStop.disabled = false;
    progress.style.display = '';

    try {
        await CG.fetchJson('/api/games/' + gameId + '/analyze' + (force ? '?force=true' : ''), { method: 'POST' });
        pollAnalysis(bar, status);
    } catch (e) {
        analysisRunning = false;
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

        analysisRunning = false;
        document.getElementById('btn-analyze').disabled = false;
        document.getElementById('btn-analyze-force').disabled = false;
        document.getElementById('btn-stop').disabled = true;

        if (job.state === 'done') {
            await loadAnalysisResult();
            CG.toast('整局分析完成。', 'ok');
        }
    } catch (e) {
        analysisRunning = false;
        document.getElementById('btn-analyze').disabled = false;
        document.getElementById('btn-analyze-force').disabled = false;
        document.getElementById('btn-stop').disabled = true;
        status.textContent = '读取进度失败:' + e.message;
    }
}

async function loadAnalysisResult() {
    try {
        const result = await CG.fetchJson('/api/games/' + gameId + '/analysis');
        analysisPoints = result.points || [];
        classifications = {};
        (result.classifications || []).forEach(function (c) {
            classifications[c.ply] = c;
        });
        refreshMoveListBadges();
        updateEvalBar();
        updateStoredRecommendation();
        drawChart();
    } catch (e) {
        // 还没有结果,忽略
    }
}

/* ---------------- 棋盘试走 ---------------- */

let moveInputEnabled = false;

function onMoveInput(evt) {
    if (evt.type === 'validateMoveInput') {
        // 返回值决定这一步是否合法
        return tryUserMove(evt.squareFrom, evt.squareTo);
    }
    if (evt.type === 'moveInputFinished') {
        if (pendingUserMove) {
            userMoveFen = pendingUserMove.fen;
            pendingUserMove = null;
            scheduleInstantAnalysis();
        }
    }

    // 其余事件(moveInputStarted 等)必须返回 true,否则棋子根本不会被拾起来
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
    const baseFen = moves[currentPly] ? moves[currentPly].fen : null;
    if (!baseFen) { return false; }

    let chess;
    try { chess = new Chess(baseFen); } catch (e) { return false; }

    try {
        const move = chess.move({ from: from, to: to, promotion: 'q' });
        if (!move) { return false; }
        pendingUserMove = { fen: chess.fen(), san: move.san };
        return true;
    } catch (e) {
        return false;
    }
}

/* ---------------- 事件绑定 ---------------- */

function bind() {
    document.getElementById('btn-first').addEventListener('click', function () { goTo(0); });
    document.getElementById('btn-prev').addEventListener('click', function () { goTo(currentPly - 1); });
    document.getElementById('btn-next').addEventListener('click', function () { goTo(currentPly + 1); });
    document.getElementById('btn-last').addEventListener('click', function () { goTo(moves.length - 1); });
    document.getElementById('btn-flip').addEventListener('click', flipBoard);
    document.getElementById('btn-analyze').addEventListener('click', function () { startWholeGameAnalysis(false); });
    document.getElementById('btn-analyze-force').addEventListener('click', function () { startWholeGameAnalysis(true); });
    document.getElementById('btn-stop').addEventListener('click', async function () {
        try {
            await CG.fetchJson('/api/games/' + gameId + '/analyze/stop', { method: 'POST' });
            CG.toast('已请求停止分析。', 'ok');
        } catch (e) {
            CG.toast(e.message, 'error');
        }
    });

    const reportHtml = document.getElementById('btn-report-html');
    if (reportHtml) {
        reportHtml.href = '/api/games/' + gameId + '/report?format=html';
    }
    const reportText = document.getElementById('btn-report-text');
    if (reportText) {
        reportText.addEventListener('click', function () {
            window.location.href = '/api/games/' + gameId + '/report?format=text';
        });
    }

    // 主变演示结束后,回到棋局的当前局面
    const pvBack = document.getElementById('btn-pv-back');
    if (pvBack) {
        pvBack.addEventListener('click', function () { goTo(currentPly); });
    }

    document.addEventListener('keydown', function (evt) {
        if (evt.target && /input|textarea/i.test(evt.target.tagName)) { return; }
        switch (evt.key) {
            case 'ArrowLeft': goTo(currentPly - 1); break;
            case 'ArrowRight': goTo(currentPly + 1); break;
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
highlightCurrentMove();
updateHeader();
updateEvalBar();
updateStoredRecommendation();
drawChart();
setMoveInputEnabled(true);     // 默认允许在棋盘上试走
loadAnalysisResult();
scheduleInstantAnalysis();   // 打开页面就先算当前局面的评估

if (!data || !data.engineInstalled) {
    const hint = document.getElementById('engine-hint');
    if (hint) {
        hint.innerHTML = '<span class="badge warn">引擎未安装</span> <a href="/Settings/Index">去安装</a>';
    }
    const btn = document.getElementById('btn-analyze');
    if (btn) { btn.disabled = true; }
}
