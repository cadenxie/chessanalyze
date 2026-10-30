/* 设置页:引擎安装、参数保存、连接测试。 */
(function () {
    var pollTimer = null;
    var platformSupported = true;

    async function loadStatus() {
        var stateEl = document.getElementById('engine-state');
        var versionEl = document.getElementById('engine-version');
        var pathEl = document.getElementById('engine-path');
        if (!stateEl) { return; }

        try {
            var status = await CG.fetchJson('/api/engine/status');
            var labels = {
                NotInstalled: '未安装', Starting: '启动中', Ready: '就绪',
                Searching: '计算中', Faulted: '异常'
            };
            stateEl.textContent = labels[status.state] || status.state;
            stateEl.className = 'badge ' + (status.state === 'Ready' ? 'ok'
                : status.state === 'NotInstalled' ? 'warn'
                : status.state === 'Faulted' ? 'bad' : '');
            versionEl.textContent = status.version || '—';
            pathEl.textContent = status.path || '—';
            if (status.lastError) { pathEl.title = status.lastError; }

            var platformEl = document.getElementById('engine-platform');
            if (platformEl && status.currentPlatform) {
                platformEl.textContent = status.currentPlatform;
            }

            var warnEl = document.getElementById('platform-warning');
            if (warnEl) {
                warnEl.style.display = status.platformMismatch ? '' : 'none';
            }

            // 当前系统没有官方发行包时,禁用一键安装,只能手动指定路径
            platformSupported = status.platformSupported !== false;
            var installBtn = document.getElementById('btn-install');
            if (installBtn) {
                installBtn.disabled = !platformSupported;
                installBtn.title = platformSupported ? '' : '当前系统没有官方发行包,请手动指定引擎路径。';
            }
        } catch (e) {
            stateEl.textContent = '读取失败';
            stateEl.className = 'badge bad';
        }
    }

    async function startInstall() {
        var btn = document.getElementById('btn-install');
        var stage = document.getElementById('install-stage');
        var bar = document.getElementById('install-bar');
        var progress = document.getElementById('install-progress');
        var errorEl = document.getElementById('install-error');

        btn.disabled = true;
        progress.style.display = '';
        bar.style.width = '0%';
        errorEl.textContent = '';
        stage.textContent = '准备中…';

        try {
            var res = await CG.fetchJson('/api/engine/install', { method: 'POST' });
            pollInstall(res.jobId, stage, bar, errorEl, btn);
        } catch (e) {
            btn.disabled = false;
            stage.textContent = '';
            errorEl.textContent = e.message;
            CG.toast(e.message, 'error');
        }
    }

    function pollInstall(jobId, stage, bar, errorEl, btn) {
        if (pollTimer) { clearTimeout(pollTimer); }

        pollTimer = setTimeout(async function () {
            try {
                var s = await CG.fetchJson('/api/engine/install/status?jobId=' + encodeURIComponent(jobId));
                bar.style.width = (s.percent || 0) + '%';
                stage.textContent = (s.stage || '') + (s.detail ? ' · ' + s.detail : '');

                if (s.success === true) {
                    stage.textContent = '安装完成';
                    btn.disabled = !platformSupported;
                    CG.toast('引擎安装完成:' + (s.version || ''), 'ok');
                    await loadStatus();
                } else if (s.success === false) {
                    btn.disabled = !platformSupported;
                    errorEl.textContent = s.error || '安装失败';
                    stage.textContent = '失败';
                    CG.toast(s.error || '安装失败', 'error');
                    await loadStatus();
                } else {
                    pollInstall(jobId, stage, bar, errorEl, btn);
                }
            } catch (e) {
                btn.disabled = false;
                errorEl.textContent = e.message;
            }
        }, 900);
    }

    async function saveSettings() {
        var status = document.getElementById('save-status');
        var body = {
            enginePath: document.getElementById('manual-path').value,
            threads: Number(document.getElementById('opt-threads').value || 0),
            hashMb: Number(document.getElementById('opt-hash').value || 256),
            multiPv: Number(document.getElementById('opt-multipv').value || 3),
            defaultDepth: Number(document.getElementById('opt-depth').value || 18),
            analysisDepth: Number(document.getElementById('opt-analysis-depth').value || 16),
            proxyUrl: document.getElementById('opt-proxy').value,
            playHintQuota: Number(document.getElementById('opt-play-hints').value || 3),
            playTakebackQuota: Number(document.getElementById('opt-play-takebacks').value || 3)
        };

        try {
            var res = await CG.fetchJson('/Settings/Save', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });
            if (res && res.ok === false) {
                status.textContent = '';
                CG.toast(res.error || '保存失败', 'error');
                await loadStatus();
                return;
            }
            status.textContent = '已保存';
            CG.toast('设置已保存。', 'ok');
            await loadStatus();
        } catch (e) {
            status.textContent = '';
            CG.toast(e.message, 'error');
        }
    }

    async function setPath() {
        var path = document.getElementById('manual-path').value.trim();
        if (!path) {
            CG.toast('请填写引擎可执行文件的完整路径。', 'error');
            return;
        }
        await saveSettings();
    }

    async function testEngine() {
        var btn = document.getElementById('btn-test');
        var status = document.getElementById('test-status');
        btn.disabled = true;
        status.textContent = '测试中…';

        try {
            var res = await CG.fetchJson('/api/engine/test', { method: 'POST' });
            status.textContent = '正常 · ' + (res.version || '');
            CG.toast('引擎连接正常。', 'ok');
        } catch (e) {
            status.textContent = '';
            CG.toast(e.message, 'error');
        } finally {
            btn.disabled = false;
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        loadStatus();
        document.getElementById('btn-install').addEventListener('click', startInstall);
        document.getElementById('btn-save').addEventListener('click', saveSettings);
        document.getElementById('btn-set-path').addEventListener('click', setPath);
        document.getElementById('btn-test').addEventListener('click', testEngine);
    });
})();
