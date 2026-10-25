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
    if (detailEl) {
        detailEl.textContent = '深度 ' + result.depth + ' · 相对白方 · ' + CG.formatScore(result.scoreCp, result.scoreMate)
            + (userMoveFen ? ' · 试走局面(升变按后计算)' : '');
    }

    board.removeArrows();
    if (result.pv && result.pv.length) {
        const first = result.pv[0];
        if (first && first.length >= 4) {
            board.addArrow(ARROW_TYPE.default, first.slice(0, 2), first.slice(2, 4));
        }
    }

    pvEl.innerHTML = renderPv(result.pv || [], fen);
    candEl.innerHTML = (result.lines || []).map(function (line) {
        return '<div class="cand"><span class="score">' + CG.escapeHtml(CG.formatScore(line.scoreCp, line.scoreMate)) +
            '</span><span class="moves">' + CG.escapeHtml(renderPlainSan(line.pv || [], fen)) + '</span></div>';
    }).join('');
}

function uciToSanList(fen, pv) {
    if (!pv || !pv.length) { return []; }
    let chess;
    try { chess = new Chess(fen); } catch (e) { return []; }

    const sans = [];
    for (const uci of pv) {
        if (!uci || uci.length < 4) { break; }
        const from = uci.slice(0, 2);
        const to = uci.slice(2, 4);
        const promotion = uci.length > 4 ? uci[4] : 'q';
        try {
            const move = chess.move({ from: from, to: to, promotion: promotion });
            if (!move) { break; }
            sans.push(move.san);
        } catch (e) { break; }
    }
    return sans;
}

function renderPlainSan(pv, fen) {
    return uciToSanList(fen, pv).join(' ');
}

function renderPv(pv, fen) {
    const sans = uciToSanList(fen, pv);
    if (!sans.length) { return '—'; }
    return sans.map(function (san, i) {
        const uci = pv[i];
        return '<span class="pv-move" data-uci="' + CG.escapeHtml(uci) + '">' + CG.escapeHtml(san) + '</span>';
    }).join(' ');
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
        status.textContent = job.state === 'running'
            ? '分析中 ' + job.done + ' / ' + job.total + ' 个局面(' + (job.percent || 0) + '%)'
            : (job.state === 'done' ? '分析完成。'
                : job.state === 'cancelled' ? '已停止。已完成的局面已保存。'
                : '失败:' + (job.error || '未知错误'));

        if (job.state === 'running') {
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

board.enableMoveInput(function (evt) {
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
});

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
