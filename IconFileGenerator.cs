using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ClaudeStatusbar;

/// <summary>
/// アプリ/exe 用の app.ico を生成する。clay 色の角丸バッジに白い上昇バー
/// （＝ステータスバー）を描き、16〜256px の複数解像度を1ファイルに束ねる。
/// PNG 圧縮の ICO（Vista 以降が対応）として手書きで組み立てる。
/// </summary>
public static class IconFileGenerator
{
    private static readonly Color Clay = Color.FromArgb(0xD9, 0x77, 0x57);
    private static readonly int[] Sizes = { 16, 32, 48, 64, 128, 256 };

    public static void WriteIco(string path)
    {
        var pngs = new List<byte[]>();
        foreach (var s in Sizes)
        {
            using var bmp = DrawBadge(s);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }

        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);

        // ICONDIR ヘッダ
        w.Write((short)0);            // reserved
        w.Write((short)1);            // type = 1 (icon)
        w.Write((short)Sizes.Length); // 画像数

        // 各 ICONDIRENTRY（16バイト）。画像本体はヘッダ群の直後に連結
        int offset = 6 + 16 * Sizes.Length;
        for (int i = 0; i < Sizes.Length; i++)
        {
            int s = Sizes[i];
            w.Write((byte)(s >= 256 ? 0 : s)); // 幅（256は0で表現）
            w.Write((byte)(s >= 256 ? 0 : s)); // 高さ
            w.Write((byte)0);                  // パレット色数
            w.Write((byte)0);                  // reserved
            w.Write((short)1);                 // カラープレーン
            w.Write((short)32);                // ビット深度
            w.Write(pngs[i].Length);           // この画像のバイト数
            w.Write(offset);                   // ファイル先頭からのオフセット
            offset += pngs[i].Length;
        }
        foreach (var png in pngs)
            w.Write(png);
    }

    private static Bitmap DrawBadge(int s)
    {
        var bmp = new Bitmap(s, s);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // clay の角丸バッジ
        float radius = s * 0.22f;
        using (var bg = new SolidBrush(Clay))
        using (var bgPath = Rounded(new RectangleF(0, 0, s, s), radius))
            g.FillPath(bg, bgPath);

        // 白い上昇バー4本（左→右で高くなる）
        float baseY = s * 0.72f;
        float barW = s * 0.11f;
        float gap = s * 0.062f;
        float startX = s * 0.22f;
        float[] heights = { 0.20f, 0.32f, 0.44f, 0.56f };
        using var bar = new SolidBrush(Color.White);
        for (int i = 0; i < heights.Length; i++)
        {
            float h = s * heights[i];
            float x = startX + i * (barW + gap);
            float y = baseY - h;
            using var barPath = Rounded(new RectangleF(x, y, barW, h), barW * 0.3f);
            g.FillPath(bar, barPath);
        }
        return bmp;
    }

    // 角丸矩形パス
    private static GraphicsPath Rounded(RectangleF r, float rad)
    {
        float d = rad * 2f;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
