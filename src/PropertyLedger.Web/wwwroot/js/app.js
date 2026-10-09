// 手机端：点击侧边栏导航后自动收起抽屉（桌面常驻，无需处理）
document.addEventListener('click', function (e) {
    var link = e.target.closest('.sidebar-nav a');
    if (link && window.matchMedia('(max-width: 767px)').matches) {
        var t = document.getElementById('nav-toggle');
        if (t) t.checked = false;
    }
});
