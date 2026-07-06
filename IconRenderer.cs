using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ClaudeStatusbar;

/// <summary>
/// トレイアイコンを動的に描画する。円形リングゲージ＋中央の使用率数字で、
/// severity（normal/warning/critical）に応じて色分けする。
/// </summary>
public static class IconRenderer
{
    // GetHicon() で作った HICON は明示的に破棄しないとリークする（GDI ハンドル）
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>使用率アイコン（リング＋数字）を生成。呼び出し側は古い Icon を Dispose すること。</summary>
    public static Icon Render(double percent, string severity)
    {
        const int size = 32; // 高DPIでも潰れないよう 32px で描いて OS に縮小させる
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            Color fill = ColorFor(severity, percent);
            double p = Math.Clamp(percent, 0, 100);

            // リング本体。ペン幅の半分＋1px を余白にして端が切れないようにする
            const float penWidth = 4.5f;
            float margin = penWidth / 2f + 1f;
            var ring = new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin);

            // 下地トラック（薄いグレーの全周リング）
            using (var track = new Pen(Color.FromArgb(90, 130, 130, 130), penWidth))
                g.DrawEllipse(track, ring);

            // 進捗アーク（12時=-90°から時計回り）
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

            // 中央の数字（100以上は桁あふれ回避で "!"）
            string text = p >= 100 ? "!" : ((int)Math.Round(p)).ToString();
            float fontPx = text.Length >= 2 ? 13f : 15f;
            using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            var box = new RectangleF(0, 0, size, size);
            using (var textBrush = new SolidBrush(fill))
                g.DrawString(text, font, textBrush, box, sf);
        }
        return ToIcon(bmp);
    }

    /// <summary>エラー/未認証時のアイコン（グレーのスパーク）。</summary>
    public static Icon RenderError()
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            DrawSpark(g, size / 2f, size / 2f, size * 0.42f, 5f, Color.FromArgb(160, 160, 160));
        }
        return ToIcon(bmp);
    }

    // 8方向のスパーク（放射状の線）。ブランド的な「データ無し」表現
    private static void DrawSpark(Graphics g, float cx, float cy, float len, float width, Color color)
    {
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (int i = 0; i < 4; i++)
        {
            double a = i * Math.PI / 4.0; // 0,45,90,135°（反対側も引くので実質8方向）
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
