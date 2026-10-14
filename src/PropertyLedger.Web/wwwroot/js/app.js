// ============================================================
// 主题切换：暗色（默认）/ 浅色，偏好存 localStorage
// 启动时 App.razor 内联脚本已先设置 data-theme 防闪烁，这里只同步按钮图标与交互
// ============================================================
(function () {
    var KEY = 'pl_theme';

    function currentTheme() {
        return document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark';
    }

    function syncButtons() {
        var theme = currentTheme();
        document.querySelectorAll('.theme-toggle').forEach(function (btn) {
            var icon = theme === 'light' ? '🌙' : '☀️';
            var label = theme === 'light' ? '切换到暗色主题' : '切换到浅色主题';
            btn.textContent = icon;
            btn.setAttribute('title', label);
            btn.setAttribute('aria-label', label);
        });
    }

    window.plSetTheme = function (theme) {
        if (theme !== 'light' && theme !== 'dark') theme = 'dark';
        document.documentElement.setAttribute('data-theme', theme);
        try { localStorage.setItem(KEY, theme); } catch (e) { /* 隐私模式下忽略 */ }
        syncButtons();
    };

    window.plToggleTheme = function () {
        window.plSetTheme(currentTheme() === 'light' ? 'dark' : 'light');
    };

    document.addEventListener('click', function (e) {
        if (e.target.closest('.theme-toggle')) {
            e.preventDefault();
            window.plToggleTheme();
            return;
        }
        // 手机端：点击侧边栏导航后自动收起抽屉（桌面常驻，无需处理）
        var link = e.target.closest('.sidebar-nav a');
        if (link && window.matchMedia('(max-width: 767px)').matches) {
            var t = document.getElementById('nav-toggle');
            if (t) t.checked = false;
        }
    });

    // Blazor 交互渲染后 DOM 可能重建，统一再同步一次图标
    document.addEventListener('DOMContentLoaded', syncButtons);
    syncButtons();
})();
