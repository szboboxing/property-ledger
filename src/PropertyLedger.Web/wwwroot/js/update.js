// 在线升级观察器：升级容器执行 pull + up -d 重建 web 容器期间，应用会短暂离线，
// Blazor 电路会断开，因此由浏览器侧轮询匿名 /healthz：版本号变为目标版本即认为
// 升级成功，回调 Blazor 更新状态文本并自动刷新页面；超过时限则回调超时（显示手动命令）。
window.updateWatcher = {
    start: function (latestVersion, dotnetRef) {
        this.stop();
        const startedAt = Date.now();
        window._updateWatcherTimer = setInterval(function () {
            fetch('/healthz', { cache: 'no-store' }).then(function (r) {
                if (!r.ok) return null;
                return r.json();
            }).then(function (data) {
                if (data && data.version === latestVersion) {
                    window.updateWatcher.stop();
                    if (dotnetRef) dotnetRef.invokeMethodAsync('OnUpgradeSucceeded', data.version);
                    setTimeout(function () { location.reload(); }, 2000);
                }
            }).catch(function () { /* 容器重启中，继续等待 */ });
            if (Date.now() - startedAt > 8 * 60 * 1000) {
                window.updateWatcher.stop();
                if (dotnetRef) dotnetRef.invokeMethodAsync('OnUpgradeTimeout');
            }
        }, 3000);
    },
    stop: function () {
        if (window._updateWatcherTimer) {
            clearInterval(window._updateWatcherTimer);
            window._updateWatcherTimer = null;
        }
    }
};

// 剪贴板小工具（手动升级命令复制）
window.updateUtil = {
    copy: function (text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }
        const ta = document.createElement('textarea');
        ta.value = text;
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
        return Promise.resolve();
    }
};
