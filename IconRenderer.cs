using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ClaudeStatusbar;

/// <summary>トレイアイコンの表示スタイル。右クリックメニューで切替可能。</summary>
public enum IconStyle
{
    Number, // A案: 数字のみ（大きく縁取りありで最も読みやすい）
    Ring,   // B案: リングゲージ＋中央に数字
}

/// <summary>
/// トレイアイコンを動的に描画する。スタイル（数字/リング）と severity に応じて描き分ける。
/// </summary>
public static class IconRenderer
{
    // GetHicon() で作った HICON は明示的に破棄しないとリークする（GDI ハンドル）
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private const int Size = 32; // 高DPIでも潰れないよう 32px で描いて OS に縮小させる

    /// <summary>使用率アイコンを生成。呼び出し側は古い Icon を Dispose すること。</summary>
    public static Icon Render(double percent, string severity, IconStyle style) => style switch
    {
        IconStyle.Ring => RenderRing(percent, severity),
        _ => RenderNumber(percent, severity),
    };

    // A案: 数字のみ。暗い縁取り＋下部の薄い使用率バーで、明暗どちらのタスクバーでも読める
    private static Icon RenderNumber(double percent, string severity)
    {
        using var bmp = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            Color fill = ColorFor(severity, percent);
            double p = Math.Clamp(percent, 0, 100);

            // 下部の薄い使用率バー
            int barH = (int)Math.Round(Size * p / 100.0);
            using (var barBrush = new SolidBrush(Color.FromArgb(60, fill)))
                g.FillRectangle(barBrush, 0, Size - barH, Size, barH);

            string text = p >= 100 ? "!!" : ((int)Math.Round(p)).ToString();
            float fontPx = text.Length >= 2 ? 17f : 21f;
            using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            var box = new RectangleF(0, 0, Size, Size);

            // 縁取り（濃色）→ 本体（fill）の順で描き視認性を確保
            using (var outline = new SolidBrush(Color.FromArgb(210, 0, 0, 0)))
            {
                foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, 1), (-1, 1), (1, -1) })
                    g.DrawString(text, font, outline, new RectangleF(dx, dy, Size, Size), sf);
            }
            using (var textBrush = new SolidBrush(fill))
                g.DrawString(text, font, textBrush, box, sf);
        }
        return ToIcon(bmp);
    }

    // B案: リングゲージ＋数字
    private static Icon RenderRing(double percent, string severity)
    {
        using var bmp = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            Color fill = ColorFor(severity, percent);
            double p = Math.Clamp(percent, 0, 100);

            const float penWidth = 4.5f;
            float margin = penWidth / 2f + 1f;
            var ring = new RectangleF(margin, margin, Size - 2 * margin, Size - 2 * margin);

            using (var track = new Pen(Color.FromArgb(90, 130, 130, 130), penWidth))
                g.DrawEllipse(track, ring);

            float sweep = (float)(360.0 * p / 100.0);
            if (sweep > 0)
            {
                using var prog = new Pen(fill, penWidth)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                g.DrawArc(prog, ring, -90f, sweep);
            }

            string text = p >= 100 ? "!" : ((int)Math.Round(p)).ToString();
            float fontPx = text.Length >= 2 ? 13f : 15f;
            using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            using (var textBrush = new SolidBrush(fill))
                g.DrawString(text, font, textBrush, new RectangleF(0, 0, Size, Size), sf);
        }
        return ToIcon(bmp);
    }

    /// <summary>エラー/未認証時のアイコン（グレーのスパーク）。</summary>
    public static Icon RenderError()
    {
        using var bmp = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            DrawSpark(g, Size / 2f, Size / 2f, Size * 0.42f, 5f, Color.FromArgb(160, 160, 160));
        }
        return ToIcon(bmp);
    }

    // 8方向のスパーク（放射状の線）
    private static void DrawSpark(Graphics g, float cx, float cy, float len, float width, Color color)
    {
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (int i = 0; i < 4; i++)
        {
            double a = i * Math.PI / 4.0;
            float dx = (float)(Math.Cos(a) * len);
            float dy = (float)(Math.Sin(a) * len);
            g.DrawLine(pen, cx - dx, cy - dy, cx + dx, cy + dy);
        }
    }

    // Bitmap → Icon 変換（HICON を複製してから破棄しリーク防止）
    private static Icon ToIcon(Bitmap bmp)
    {
        IntPtr hicon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hicon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    // severity 優先で色を決め、normal のときだけ使用率で緑→黄へ寄せる
    private static Color ColorFor(string severity, double percent) => severity switch
    {
        "critical" => Color.FromArgb(226, 75, 74),    // 赤
        "warning" => Color.FromArgb(224, 149, 43),    // 黄
        _ => percent >= 80
            ? Color.FromArgb(224, 149, 43)
            : Color.FromArgb(47, 169, 94),             // 緑
    };
}
