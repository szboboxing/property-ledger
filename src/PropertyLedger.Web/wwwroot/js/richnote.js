// 富文本备注编辑器（contenteditable）：加粗 / 斜体 / 下划线 / 插图 / 清除格式。
// 图片经受保护的 /api/uploads 上传：同源 fetch 自动携带登录 Cookie，
// 并附加自定义头 X-Requested-With（跨站表单无法伪造自定义头）防 CSRF。
window.richnote = {
    init: function (editor, fileInput, dotnetRef, initialHtml) {
        editor.innerHTML = initialHtml || '';
        let timer = null;
        const report = function () {
            clearTimeout(timer);
            timer = setTimeout(function () {
                dotnetRef.invokeMethodAsync('OnEditorChange', editor.innerHTML);
            }, 300);
        };
        editor.addEventListener('input', report);
        editor.addEventListener('blur', function () {
            clearTimeout(timer);
            dotnetRef.invokeMethodAsync('OnEditorChange', editor.innerHTML);
        });
        editor._richnoteReport = report;

        fileInput.addEventListener('change', function () {
            const file = fileInput.files && fileInput.files[0];
            fileInput.value = '';
            if (!file) return;
            dotnetRef.invokeMethodAsync('OnUploadStart');
            const fd = new FormData();
            fd.append('file', file);
            fetch('/api/uploads', {
                method: 'POST',
                body: fd,
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            }).then(function (resp) {
                if (!resp.ok) {
                    return resp.text().then(function (msg) {
                        throw new Error(msg || ('上传失败（' + resp.status + '）'));
                    });
                }
                return resp.json();
            }).then(function (data) {
                editor.focus();
                document.execCommand('insertImage', false, data.url);
                report();
                dotnetRef.invokeMethodAsync('OnUploadDone', '');
            }).catch(function (err) {
                dotnetRef.invokeMethodAsync('OnUploadDone', String((err && err.message) || err));
            });
        });
    },

    setContent: function (editor, html) {
        editor.innerHTML = html || '';
    },

    exec: function (editor, cmd) {
        editor.focus();
        document.execCommand(cmd, false, null);
        if (editor._richnoteReport) editor._richnoteReport();
    },

    pickFile: function (fileInput) {
        fileInput.click();
    }
};
