/* 首页:引擎状态 + PGN 导入。 */
(function () {
    async function loadEngineStatus() {
        var stateEl = document.getElementById('engine-state');
        var versionEl = document.getElementById('engine-version');
        var pathEl = document.getElementById('engine-path');
        if (!stateEl) { return; }

        try {
            var status = await CG.fetchJson('/api/engine/status');
            var labels = {
                NotInstalled: '未安装',
                Starting: '启动中',
                Ready: '就绪',
                Searching: '计算中',
                Faulted: '异常'
            };
            stateEl.textContent = labels[status.state] || status.state;
            stateEl.className = 'badge ' + (status.state === 'Ready' ? 'ok'
                : status.state === 'NotInstalled' ? 'warn'
                : status.state === 'Faulted' ? 'bad' : '');
            versionEl.textContent = status.version || '—';
            pathEl.textContent = status.path || '—';
            if (status.lastError) {
                pathEl.title = status.lastError;
            }
        } catch (e) {
            stateEl.textContent = '读取失败';
            stateEl.className = 'badge bad';
        }
    }

    function renderResult(result) {
        var box = document.getElementById('import-result');
        var html = '<div class="kv" style="margin-top:14px;">'
            + '<div class="k">共</div><div>' + result.total + ' 局</div>'
            + '<div class="k">成功</div><div><span class="badge ok">' + result.imported + '</span></div>'
            + '<div class="k">跳过</div><div><span class="badge ' + (result.skipped ? 'warn' : '') + '">' + result.skipped + '</span></div>'
            + '</div>';

        if (result.errors && result.errors.length) {
            html += '<h3>错误信息</h3><div class="pgn-text">' + CG.escapeHtml(result.errors.join('\n')) + '</div>';
        }

        if (result.imported > 0) {
            html += '<p style="margin-top:12px"><a href="/Games/Index">前往棋局列表 →</a></p>';
        }

        box.innerHTML = html;
    }

    function initImport() {
        var form = document.getElementById('import-form');
        if (!form) { return; }

        var fileInput = document.getElementById('import-file');
        var textInput = document.getElementById('import-text');
        var button = document.getElementById('import-btn');
        var status = document.getElementById('import-status');

        form.addEventListener('submit', async function (evt) {
            evt.preventDefault();

            var file = fileInput.files && fileInput.files.length ? fileInput.files[0] : null;
            var text = textInput.value.trim();

            if (!file && !text) {
                CG.toast('请选择 PGN 文件,或粘贴 PGN 文本。', 'error');
                return;
            }

            var formData = new FormData();
            if (file) { formData.append('file', file); }
            if (text) { formData.append('text', text); }

            button.disabled = true;
            status.textContent = '导入中…';

            try {
                var result = await CG.fetchJson('/api/games/import', { method: 'POST', body: formData });
                status.textContent = '';
                renderResult(result);
                CG.toast('导入完成:成功 ' + result.imported + ' 局,跳过 ' + result.skipped + ' 局。', 'ok');
                CG.refreshEngineIndicator();
            } catch (e) {
                status.textContent = '';
                CG.toast(e.message, 'error');
            } finally {
                button.disabled = false;
            }
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        loadEngineStatus();
        initImport();
    });
})();
