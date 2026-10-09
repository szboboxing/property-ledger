using SkiaSharp;

namespace PropertyLedger.Web.Services;

/// <summary>
/// 用 SkiaSharp（MIT 许可、跨平台）绘制中文账单卡片 PNG。
/// Linux 容器需安装中文字体（fonts-noto-cjk）与 libfontconfig1。
/// </summary>
public class BillImageRenderer
{
    private const int Width = 720;
    private const int Pad = 48;

    private static readonly SKColor Teal = SKColor.Parse("#0F766E");
    private static readonly SKColor Red = SKColor.Parse("#DC2626");
    private static readonly SKColor Ink = SKColor.Parse("#1F2937");
    private static readonly SKColor MutedColor = SKColor.Parse("#6B7280");
    private static readonly SKColor LineColor = SKColor.Parse("#E5E7EB");
    private static readonly SKColor SoftColor = SKColor.Parse("#F3F4F6");

    private readonly SKTypeface _regular;
    private readonly SKTypeface _bold;
    private readonly bool _syntheticBold;

    public BillImageRenderer()
    {
        _regular = TryLoad(RegularCandidates)
                   ?? SKTypeface.FromFamilyName("sans-serif")
                   ?? SKTypeface.Default;
        var bold = TryLoad(BoldCandidates);
        if (bold is null)
        {
            _bold = _regular;
            _syntheticBold = true;   // 找不到粗体字库时用 Embolden 仿粗体
        }
        else
        {
            _bold = bold;
        }
    }

    // (字体路径, ttc 内字面索引)：Noto CJK ttc 中简体中文 SC 通常位于 index 2，失败再回落 0
    private static readonly (string Path, int Index)[] RegularCandidates =
    {
        // Windows（微软雅黑 / 黑体 / 宋体）
        (@"C:\Windows\Fonts\msyh.ttc", 0),
        (@"C:\Windows\Fonts\simhei.ttf", 0),
        (@"C:\Windows\Fonts\simsun.ttc", 0),
        // Linux（Noto CJK / 文泉驿 / DejaVu 兜底）
        ("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc", 2),
        ("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc", 0),
        ("/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc", 2),
        ("/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc", 0),
        ("/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc", 0),
        ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 0),
    };

    private static readonly (string Path, int Index)[] BoldCandidates =
    {
        (@"C:\Windows\Fonts\msyhbd.ttc", 0),
        (@"C:\Windows\Fonts\simhei.ttf", 0),
        ("/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc", 2),
        ("/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc", 0),
        ("/usr/share/fonts/truetype/noto/NotoSansCJK-Bold.ttc", 2),
        ("/usr/share/fonts/truetype/noto/NotoSansCJK-Bold.ttc", 0),
        ("/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc", 0),
    };

    private static SKTypeface? TryLoad(IEnumerable<(string Path, int Index)> candidates)
    {
        foreach (var (path, index) in candidates)
        {
            try
            {
                if (File.Exists(path))
                {
                    var tf = SKTypeface.FromFile(path, index);
                    if (tf is not null) return tf;
                }
            }
            catch { /* 尝试下一个字体 */ }
        }
        return null;
    }

    private TextStyle MakeText(float size, SKColor color, bool bold) =>
        new(bold ? _bold : _regular, size, color, bold && _syntheticBold);

    public byte[] Render(BillImageView v)
    {
        // 两遍布局：第一遍只计算高度（canvas=null），第二遍真正绘制
        var height = Compose(null, v);

        using var bitmap = new SKBitmap(Width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        Compose(canvas, v);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
                         ?? throw new InvalidOperationException("账单图片 PNG 编码失败");
        return data.ToArray();
    }

    /// <summary>排布整张账单。canvas 为 null 时只测量，返回所需总高度。</summary>
    private int Compose(SKCanvas? c, BillImageView v)
    {
        using var title = MakeText(40, Teal, true);
        using var head = MakeText(26, Teal, true);
        using var body = MakeText(24, Ink, false);
        using var bodyBold = MakeText(24, Ink, true);
        using var big = MakeText(30, Red, true);
        using var small = MakeText(20, MutedColor, false);

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var border = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            Color = LineColor,
        };
        // 二维码缩放采样（线性，避免小图放大锯齿）
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

        var left = (float)Pad;
        var right = Width - Pad;
        var y = 56f;

        // 顶部色条
        fill.Color = Teal;
        c?.DrawRect(0, 0, Width, 12, fill);

        // 标题
        Text(c, title, "房租收款账单", Width / 2f, y, SKTextAlign.Center);
        y += title.LineHeight + 14;

        // 物业 · 房间 ／ 状态
        Text(c, head, $"{v.PropertyName} · {v.RoomName}", left, y, SKTextAlign.Left);
        Text(c, head, v.StatusText, right, y, SKTextAlign.Right);
        y += head.LineHeight + 10;

        Text(c, body,
            $"租客：{v.TenantName}" + (v.TenantPhone is null ? "" : $"　电话：{v.TenantPhone}"),
            left, y, SKTextAlign.Left);
        y += body.LineHeight + 6;

        Text(c, small, $"账期：{v.PeriodRange}", left, y, SKTextAlign.Left);
        y += small.LineHeight + 2;
        Text(c, small, $"应在 {v.DueDate} 前付清", left, y, SKTextAlign.Left);
        y += small.LineHeight + 18;

        c?.DrawLine(left, y, right, y, border);
        y += 26;

        // 费用明细
        foreach (var (name, amount) in v.Lines)
        {
            Text(c, body, name, left, y, SKTextAlign.Left);
            Text(c, body, $"¥{amount:0.00}", right, y, SKTextAlign.Right);
            y += 38;
        }

        y += 10;
        var bandH = 44f;
        fill.Color = SoftColor;
        c?.DrawRect(new SKRect(left, y, right, y + bandH), fill);
        var bandTextY = y + (bandH - bodyBold.LineHeight) / 2f;
        Text(c, bodyBold, "应收合计", left + 12, bandTextY, SKTextAlign.Left);
        Text(c, bodyBold, $"¥{v.Total:0.00}", right - 12, bandTextY, SKTextAlign.Right);
        y += bandH + 22;

        // 应收 / 已收 / 剩余
        var colW = (right - left) / 3f;
        AmountBlock(c, small, bodyBold, "应收", $"¥{v.Total:0.00}", Ink, left, y, colW);
        AmountBlock(c, small, bodyBold, "已收", $"¥{v.Paid:0.00}", Ink, left + colW, y, colW);
        AmountBlock(c, small, big, "剩余", $"¥{v.Remaining:0.00}",
            v.Remaining > 0 ? Red : Teal, left + colW * 2, y, colW);
        y += 100;

        // 收款二维码
        if (v.PayQrCodeDataUri is not null)
        {
            SKBitmap? qr = null;
            try { qr = SKBitmap.Decode(DecodeDataUri(v.PayQrCodeDataUri)); }
            catch { qr = null; }

            if (qr is not null)
            {
                using (qr)
                {
                    const int size = 240;
                    var box = new SKRect(Width / 2f - size / 2f - 12, y,
                                         Width / 2f + size / 2f + 12, y + size + 24);
                    fill.Color = SKColors.White;
                    c?.DrawRect(box, fill);
                    c?.DrawRect(box, border);
                    c?.DrawBitmap(qr, new SKRect(Width / 2f - size / 2f, y + 12,
                                                  Width / 2f + size / 2f, y + 12 + size),
                                  sampling, null);
                }
                y += 240 + 24 + 8;
                Text(c, small, "扫码付款（微信 / 支付宝）", Width / 2f, y, SKTextAlign.Center);
                y += small.LineHeight + 6;
            }
        }

        if (!string.IsNullOrWhiteSpace(v.PaymentNotice))
        {
            Text(c, small, v.PaymentNotice, Width / 2f, y, SKTextAlign.Center);
            y += small.LineHeight + 6;
        }

        y += 8;
        Text(c, small, $"{v.LandlordName}　生成于 {DateTime.Now:yyyy-MM-dd}",
            Width / 2f, y, SKTextAlign.Center);
        y += small.LineHeight + 40;

        return (int)Math.Ceiling(y);
    }

    private static void AmountBlock(SKCanvas? c, TextStyle label, TextStyle value,
        string labelText, string valueText, SKColor valueColor, float x, float y, float w)
    {
        Text(c, label, labelText, x + w / 2, y, SKTextAlign.Center);
        value.Color = valueColor;
        Text(c, value, valueText, x + w / 2, y + 32, SKTextAlign.Center);
    }

    /// <summary>按顶部坐标 y 绘制一行文本（Skia 接收基线坐标，这里换算；对齐交给 DrawText 重载）。</summary>
    private static void Text(SKCanvas? c, TextStyle s, string text,
        float x, float topY, SKTextAlign align)
    {
        if (c is null || text.Length == 0) return;
        var baseline = topY - s.Font.Metrics.Ascent; // Ascent 为负值
        c.DrawText(text, x, baseline, align, s.Font, s.Paint);
    }

    private static byte[] DecodeDataUri(string dataUri)
    {
        var comma = dataUri.IndexOf(',');
        var base64 = comma >= 0 ? dataUri[(comma + 1)..] : dataUri;
        return Convert.FromBase64String(base64);
    }

    /// <summary>SkiaSharp 4.x 文本样式：字体度量在 SKFont、颜色在 SKPaint，成对使用。</summary>
    private sealed class TextStyle : IDisposable
    {
        public SKFont Font { get; }
        public SKPaint Paint { get; }

        public TextStyle(SKTypeface typeface, float size, SKColor color, bool embolden)
        {
            Font = new SKFont(typeface, size) { Embolden = embolden };
            Paint = new SKPaint
            {
                IsAntialias = true,
                Color = color,
                Style = SKPaintStyle.Fill,
            };
        }

        public SKColor Color
        {
            get => Paint.Color;
            set => Paint.Color = value;
        }

        public float LineHeight => Font.Metrics.Descent - Font.Metrics.Ascent;

        public void Dispose()
        {
            Font.Dispose();
            Paint.Dispose();
        }
    }
}
