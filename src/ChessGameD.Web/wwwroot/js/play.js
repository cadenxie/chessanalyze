import { Chessboard } from '/lib/cm-chessboard/src/Chessboard.js';
import { Arrows, ARROW_TYPE } from '/lib/cm-chessboard/src/extensions/arrows/Arrows.js';
import { Chess } from '/lib/chess.js/chess.js';

const data = CG.readJsonScript('play-data');
const initialFen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

var state = {
    session: null,       // 服务端返回的对局状态
    busy: false,         // 请求进行中(引擎思考中)
    orientation: 'w'
};

var boardEl = document.getElementById('board');
var board = new Chessboard(boardEl, {
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

/** 相对白方的厘兵分值;杀棋取极大值。 */
function evalCp(evalInfo) {
    if (!evalInfo) { return 0; }
    if (evalInfo.scoreCp !== null && evalInfo.scoreCp !== undefined) {
        return evalInfo.scoreCp;
    }
    if (evalInfo.scoreMate !== null && evalInfo.scoreMate !== undefined) {
        return evalInfo.scoreMate >= 0 ? 10000 : -10000;
    }
    return 0;
}

/** 分值 → 评估条的高度(白方占多少)。 */
function gaugeRatio(cp) {
    var ratio = 1 / (1 + Math.pow(10, -cp / 400));
    return Math.max(0.02, Math.min(0.98, ratio));
}

/* ---------------- 渲染 ---------------- */

function renderSession() {
    var session = state.session;
    if (!session) {
        el('turn-label').textContent = '准备开始';
        el('session-info').textContent = '还没开局';
        el('status-line').textContent = '';
        el('coach-notes').innerHTML = '<div class="small muted">还没有棋谱。</div>';
        el('hint-box').classList.add('hidden');
        updateQuotaLine();
        setMoveInputEnabled(false);
        el('btn-review').classList.add('unavailable');
        el('btn-review').title = '还未结束棋局（认输或将军）不可复盘';
        return;
    }

    el('turn-label').textContent = session.status === 'Active'
        ? (session.yourTurn ? '轮到你' : '引擎思考中…')
        : ('对局结束 ' + (session.result || ''));

    el('session-info').textContent = '对局 #' + session.id
        + ' · 档位 ' + session.levelName
        + ' · 已走 ' + session.plyCount + ' 手';

    el('opponent-label').textContent = '陪练引擎(' + session.levelName + ')';

    board.setPosition(session.fen || initialFen, true);
    board.removeArrows();

    renderCoachNotes();
    updateGauge();
    updateQuotaLine();
    renderHintBox(null);
    setMoveInputEnabled(session.status === 'Active' && session.yourTurn && !state.busy);

    el('btn-takeback').disabled = session.status !== 'Active' || session.takebacksLeft <= 0 || state.busy;
    el('btn-resign').disabled = session.status !== 'Active' || state.busy;
    el('btn-draw').disabled = session.status !== 'Active' || state.busy;
    el('btn-hint-eval').disabled = session.status !== 'Active' || !session.yourTurn || state.busy;
    el('btn-hint-candidates').disabled = session.status !== 'Active' || !session.yourTurn || state.busy || session.hintsLeft <= 0;
    el('btn-hint-best').disabled = session.status !== 'Active' || !session.yourTurn || state.busy || session.hintsLeft <= 0;

    // 「去复盘」只有终局(认输 / 将军)之后才能用
    var canReview = session.status !== 'Active' && !!session.gameId;
    el('btn-review').classList.toggle('unavailable', !canReview);
    el('btn-review').title = canReview ? '打开这局的复盘页' : '还未结束棋局（认输或将军）不可复盘';

    if (session.status !== 'Active') {
        var info = '对局结束:' + (session.termination || '') + ' ' + (session.result || '');
        if (session.gameId) {
            info += ' · 棋谱已存入棋局列表,点「去复盘」查看。';
        }
        el('status-line').textContent = info;
    }
}

function updateGauge() {
    var session = state.session;
    var cp = evalCp(session ? session.eval : null);
    var ratio = gaugeRatio(cp);

    el('gauge-white').style.height = (ratio * 100).toFixed(1) + '%';
    el('gauge-black').style.height = ((1 - ratio) * 100).toFixed(1) + '%';
    el('gauge-label').textContent = CG.formatScore(
        session && session.eval ? session.eval.scoreCp : null,
        session && session.eval ? session.eval.scoreMate : null);
}

function updateQuotaLine() {
    var session = state.session;
    if (!session) {
        el('quota-line').textContent = '';
        return;
    }

    el('quota-line').textContent = '提示剩余 ' + session.hintsLeft + ' 次(已用 ' + session.hintsUsed
        + ')· 悔棋剩余 ' + session.takebacksLeft + ' 次(已用 ' + session.takebacksUsed
        + ')· 失误 ' + session.mistakeCount + ' 次(严重失误 ' + session.blunderCount
        + ',漏着 ' + session.missedWinCount + ')';
}

/** 教练记录:玩家的每一步 + 引擎的应手。 */
function renderCoachNotes() {
    var box = el('coach-notes');
    var moves = (state.session && state.session.moves) || [];

    if (!moves.length) {
        box.innerHTML = '<div class="small muted">还没有棋谱。</div>';
        return;
    }

    var html = '';
    moves.forEach(function (m) {
        var cls = m.cssClass || 'mc-good';
        var label = m.isMissedWin ? '漏着' : (m.label || '');
        html += '<div class="prow ' + (m.isEngineMove ? 'engine' : '') + '">';
        html += '<span class="pno mono">' + m.ply + '.</span>';
        html += '<span class="psan mono">' + CG.escapeHtml(m.san) + '</span>';
        if (m.isEngineMove) {
            html += '<span class="chip mc-good">引擎</span>';
            if (m.engineForgave) {
                html += '<span class="chip mc-excellent">放水</span>';
            }
        } else if (label) {
            html += '<span class="chip ' + cls + '">' + CG.escapeHtml(label) + '</span>';
        }
        if (m.comment) {
            html += '<span class="pcomment">' + CG.escapeHtml(m.comment) + '</span>';
        }
        html += '</div>';
    });

    box.innerHTML = html;
}

/** 提示结果。 */
function renderHintBox(hint) {
    var box = el('hint-box');
    if (!hint) {
        box.classList.add('hidden');
        box.innerHTML = '';
        return;
    }

    var html = '';
    if (hint.evaluation) {
        html += '<div class="small">局面:' + CG.escapeHtml(hint.evaluation)
            + '(' + CG.formatScore(hint.scoreCp, hint.scoreMate) + ')</div>';
    }
    if (hint.candidates && hint.candidates.length) {
        html += '<div class="cands">';
        hint.candidates.forEach(function (c) {
            html += '<div class="cand"><span class="score">'
                + CG.formatScore(c.scoreCp, null)
                + '</span><span class="moves">' + CG.escapeHtml(c.san || c.uci) + '</span></div>';
        });
        html += '</div>';
    }
    if (hint.bestMoveSan) {
        html += '<div class="small">引擎推荐:<b class="mono">' + CG.escapeHtml(hint.bestMoveSan) + '</b></div>';
    }

    box.innerHTML = html;
    box.classList.remove('hidden');
}

/* ---------------- 棋盘交互 ---------------- */

function setMoveInputEnabled(enabled) {
    if (enabled) {
        board.enableMoveInput(onMoveInput);
    } else {
        board.disableMoveInput();
    }
}

function onMoveInput(evt) {
    if (evt.type === 'validateMoveInput') {
        return canMove(evt.squareFrom, evt.squareTo);
    }
    if (evt.type === 'moveInputFinished') {
        if (!evt.legalMove) { return true; }
        submitUserMove(evt.squareFrom, evt.squareTo);
    }
    return true;
}

/** 本地先粗筛一下(服务端才是裁判)。这里不能弹升变询问,否则会问两次。 */
function canMove(from, to) {
    if (!state.session || state.busy) { return false; }
    if (!state.session.yourTurn) { return false; }
    try {
        var chess = new Chess(state.session.fen || initialFen);
        return chess.moves({ square: from, verbose: true }).some(function (m) {
            return m.to === to;
        });
    } catch (e) {
        return false;
    }
}

/** 找到 from → to 的合法走法(升变的话要问用户选什么)。 */
function findMove(chess, from, to) {
    var candidates = chess.moves({ square: from, verbose: true }).filter(function (m) {
        return m.to === to;
    });
    if (!candidates.length) { return null; }

    var promotion = candidates.filter(function (m) { return m.promotion; });
    if (!promotion.length) {
        return candidates[0];
    }

    var answer = window.prompt('升变成什么?q=后 r=车 b=象 n=马', 'q');
    if (answer === null) { return null; }
    answer = answer.trim().toLowerCase();
    if (['q', 'r', 'b', 'n'].indexOf(answer) < 0) { answer = 'q'; }

    var picked = promotion.filter(function (m) { return m.promotion === answer; })[0];
    return picked || promotion[0];
}

/** 提交玩家的走法。 */
async function submitUserMove(from, to) {
    if (state.busy || !state.session) { return; }

    var uci = null;
    try {
        var chess = new Chess(state.session.fen || initialFen);
        var move = findMove(chess, from, to);
        if (!move) {
            // 用户取消升变询问:棋盘已经动了,拉回服务端的状态
            board.setPosition(state.session.fen || initialFen, false);
            return;
        }
        uci = move.from + move.to + (move.promotion ? move.promotion : '');
    } catch (e) {
        CG.toast('这一步处理不了:' + e.message, 'error');
        board.setPosition(state.session.fen || initialFen, false);
        return;
    }

    state.busy = true;
    setMoveInputEnabled(false);
    el('status-line').textContent = '引擎思考中…';

    try {
        var result = await CG.fetchJson('/api/play/sessions/' + state.session.id + '/move', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ uci: uci })
        });

        state.session = result.session;
        renderSession();

        if (result.outcome && result.outcome.isBadMove) {
            var message = '教练:' + (result.outcome.alertLabel || '这步有问题');
            if (result.outcome.alertComment) {
                message = result.outcome.alertComment;
            }
            CG.toast(message, 'error', 8000);
        }
    } catch (e) {
        CG.toast(e.message, 'error');
        el('status-line').textContent = '';
    } finally {
        state.busy = false;
        renderSession();
    }
}

/* ---------------- 操作 ---------------- */

async function newGame() {
    if (state.busy) { return; }

    var level = parseInt(el('level-select').value, 10) || 2;
    state.busy = true;
    el('status-line').textContent = '正在开局…';

    try {
        var session = await CG.fetchJson('/api/play/sessions', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ level: level })
        });
        state.session = session;
        el('status-line').textContent = '';
        renderSession();
    } catch (e) {
        CG.toast(e.message, 'error');
        el('status-line').textContent = '';
    } finally {
        state.busy = false;
        renderSession();
    }
}

async function askHint(strength) {
    if (state.busy || !state.session) { return; }

    state.busy = true;
    renderSession();

    try {
        var result = await CG.fetchJson('/api/play/sessions/' + state.session.id + '/hint', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ strength: strength })
        });

        state.session = result.session;
        renderHintBox(result.hint);

        // 候选 / 最佳走法在棋盘上画箭头
        board.removeArrows();
        if (result.hint.bestMoveUci) {
            board.addArrow(ARROW_TYPE.default, result.hint.bestMoveUci.slice(0, 2), result.hint.bestMoveUci.slice(2, 4));
        } else if (result.hint.candidates) {
            result.hint.candidates.forEach(function (c) {
                if (c.uci && c.uci.length >= 4) {
                    board.addArrow(ARROW_TYPE.default, c.uci.slice(0, 2), c.uci.slice(2, 4));
                }
            });
        }
    } catch (e) {
        CG.toast(e.message, 'error');
    } finally {
        state.busy = false;
        renderSession();
    }
}

async function takeback() {
    if (state.busy || !state.session) { return; }

    state.busy = true;
    try {
        var result = await CG.fetchJson('/api/play/sessions/' + state.session.id + '/takeback', { method: 'POST' });
        state.session = result.session;
        CG.toast('已悔棋:' + result.takeback.removedSan, 'ok');
    } catch (e) {
        CG.toast(e.message, 'error');
    } finally {
        state.busy = false;
        renderSession();
    }
}

async function finishGame(result, termination) {
    if (state.busy || !state.session) { return; }

    state.busy = true;
    try {
        var payload = await CG.fetchJson('/api/play/sessions/' + state.session.id + '/finish', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ result: result, termination: termination })
        });
        state.session = payload.session;
    } catch (e) {
        CG.toast(e.message, 'error');
    } finally {
        state.busy = false;
        renderSession();
    }
}

/* ---------------- 事件绑定 ---------------- */

function bind() {
    el('btn-new').addEventListener('click', newGame);
    el('btn-takeback').addEventListener('click', takeback);
    el('btn-resign').addEventListener('click', function () { finishGame('0-1', '认输'); });
    el('btn-draw').addEventListener('click', function () { finishGame('1/2-1/2', '和棋'); });
    el('btn-flip').addEventListener('click', function () {
        state.orientation = state.orientation === 'w' ? 'b' : 'w';
        board.setOrientation(state.orientation, true);
    });

    el('btn-hint-eval').addEventListener('click', function () { askHint('eval'); });
    el('btn-hint-candidates').addEventListener('click', function () { askHint('candidates'); });
    el('btn-hint-best').addEventListener('click', function () { askHint('best'); });

    // 去复盘:没结束就提示一下,别让人以为按钮坏了
    el('btn-review').addEventListener('click', function () {
        var session = state.session;
        if (!session || session.status === 'Active' || !session.gameId) {
            CG.toast('还未结束棋局（认输或将军）不可复盘', 'error');
            return;
        }
        window.location.href = '/Games/Review/' + session.gameId;
    });

    el('level-select').addEventListener('change', function () {
        var levels = (state.session && state.session.levels) || (data ? data.levels : []);
        var picked = levels.filter(function (l) { return l.level === parseInt(el('level-select').value, 10); })[0];
        el('level-desc').textContent = picked ? picked.description : '';
    });
}

/* ---------------- 启动 ---------------- */

bind();
renderSession();

if (data && !data.engineInstalled) {
    var hint = el('engine-hint');
    if (hint) {
        hint.innerHTML = '<span class="badge warn">引擎未安装</span> <a href="/Settings/Index">去安装</a>';
    }
    el('btn-new').disabled = true;
}

// 有没下完的对局就接着下
CG.fetchJson('/api/play/sessions/active')
    .then(function (result) {
        if (result && result.active) {
            state.session = result.session;
            renderSession();
            CG.toast('接着上次那局继续。', 'ok');
        }
    })
    .catch(function () { /* 没有就算了 */ });
