/* 共享工具:请求封装、提示条、分值格式化。 */
window.CG = (function () {
    function toast(message, kind, timeout) {
        var existing = document.querySelector('.toast');
        if (existing) { existing.remove(); }

        var el = document.createElement('div');
        el.className = 'toast' + (kind ? ' ' + kind : '');
        el.textContent = message;
        document.body.appendChild(el);

        setTimeout(function () {
            el.remove();
        }, timeout || (kind === 'error' ? 9000 : 4000));

        return el;
    }

    /* 防伪 token:写操作要带上,否则会被服务端拒绝 */
    function csrfToken() {
        var el = document.querySelector('meta[name="csrf-token"]');
        return el ? (el.getAttribute('content') || '') : '';
    }

    async function fetchJson(url, options) {
        options = options || {};
        var method = (options.method || 'GET').toUpperCase();
        if (method !== 'GET' && method !== 'HEAD' && method !== 'OPTIONS') {
            options.headers = options.headers || {};
            var token = csrfToken();
            if (token) {
                options.headers['RequestVerificationToken'] = token;
            }
        }

        var response = await fetch(url, options);
        var text = await response.text();
        var data = null;
        if (text) {
            try { data = JSON.parse(text); } catch (e) { data = null; }
        }
        if (!response.ok) {
            var message = (data && (data.error || data.title)) || ('请求失败 (HTTP ' + response.status + ')');
            var error = new Error(message);
            error.status = response.status;
            error.data = data;
            throw error;
        }
        return data;
    }

    function formatScore(scoreCp, scoreMate) {
        if (scoreMate !== null && scoreMate !== undefined) {
            return 'M' + scoreMate;
        }
        if (scoreCp === null || scoreCp === undefined) {
            return '—';
        }
        var pawns = scoreCp / 100;
        return (pawns > 0 ? '+' : '') + pawns.toFixed(2);
    }

    function escapeHtml(text) {
        if (text === null || text === undefined) { return ''; }
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function readJsonScript(id) {
        var el = document.getElementById(id);
        if (!el) { return null; }
        try { return JSON.parse(el.textContent); } catch (e) { return null; }
    }

    async function refreshEngineIndicator() {
        var el = document.getElementById('engine-indicator');
        if (!el) { return; }
        try {
            var status = await fetchJson('/api/engine/status');
            if (status.state === 'Ready') {
                el.className = 'badge ok';
                el.textContent = '引擎就绪 · ' + (status.version || 'Stockfish');
            } else if (status.state === 'NotInstalled') {
                el.className = 'badge warn';
                el.textContent = '引擎未安装';
            } else if (status.state === 'Searching') {
                el.className = 'badge ok';
                el.textContent = '引擎计算中…';
            } else {
                el.className = 'badge bad';
                el.textContent = '引擎异常';
                el.title = status.lastError || '';
            }
        } catch (e) {
            el.className = 'badge';
            el.textContent = '引擎状态未知';
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        refreshEngineIndicator();
        setInterval(refreshEngineIndicator, 20000);
    });

    return {
        toast: toast,
        fetchJson: fetchJson,
        csrfToken: csrfToken,
        formatScore: formatScore,
        escapeHtml: escapeHtml,
        readJsonScript: readJsonScript,
        refreshEngineIndicator: refreshEngineIndicator
    };
})();
