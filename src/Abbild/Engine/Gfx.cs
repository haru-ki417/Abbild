using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Abbild.Engine;

/// <summary>色の決まりごと。</summary>
public static class Palette
{
    public static readonly Color Window = new(10, 14, 32, 228);
    public static readonly Color WindowLight = new(24, 30, 62, 235);
    public static readonly Color Border = new(236, 236, 236);
    public static readonly Color BorderInner = new(120, 128, 150);
    public static readonly Color Gold = new(240, 200, 90);
    public static readonly Color Text = new(246, 246, 240);
    public static readonly Color Dim = new(160, 166, 184);
    public static readonly Color Disabled = new(105, 108, 120);
    public static readonly Color Hp = new(226, 70, 70);
    public static readonly Color HpDark = new(120, 24, 30);
    public static readonly Color Mp = new(70, 150, 240);
    public static readonly Color MpDark = new(22, 50, 120);
    public static readonly Color Good = new(110, 220, 130);
    public static readonly Color Bad = new(240, 90, 90);
    public static readonly Color Cursor = new(110, 230, 255);
    public static readonly Color Frame = new(201, 162, 74);
    public static readonly Color Gem = new(90, 200, 255);

    public static Color Of(Abbild.Core.LedColor c) => c switch
    {
        Abbild.Core.LedColor.Red => new Color(255, 70, 60),
        Abbild.Core.LedColor.Green => new Color(80, 235, 110),
        Abbild.Core.LedColor.Blue => new Color(70, 130, 255),
        Abbild.Core.LedColor.Yellow => new Color(255, 220, 60),
        Abbild.Core.LedColor.Purple => new Color(180, 90, 240),
        Abbild.Core.LedColor.White => new Color(255, 255, 255),
        Abbild.Core.LedColor.Orange => new Color(255, 150, 50),
        Abbild.Core.LedColor.Magenta => new Color(255, 70, 200),
        Abbild.Core.LedColor.Cyan => new Color(80, 230, 255),
        _ => new Color(40, 40, 50),
    };
}

/// <summary>描画の道具（枠・文字・ゲージ）。座標は 1920×1080 の仮想画面。</summary>
public sealed class Gfx(GraphicsDevice device, FontSystem fonts)
{
    public const int Width = 1920;
    public const int Height = 1080;

    public SpriteBatch Batch { get; } = new(device);
    public GraphicsDevice Device { get; } = device;
    private readonly Texture2D _pixel = MakePixel(device);
    private readonly Texture2D _circle = MakeCircle(device, 128);
    private readonly Texture2D _glow = MakeGlow(device, 128);

    public Texture2D Pixel => _pixel;
    public Texture2D CircleTex => _circle;
    public Texture2D GlowTex => _glow;

    private static Texture2D MakePixel(GraphicsDevice d)
    {
        var t = new Texture2D(d, 1, 1);
        t.SetData([Color.White]);
        return t;
    }

    private static Texture2D MakeCircle(GraphicsDevice d, int size)
    {
        var t = new Texture2D(d, size, size);
        var data = new Color[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float a = Math.Clamp(r - MathF.Sqrt((dx * dx) + (dy * dy)), 0, 1);
                data[(y * size) + x] = Color.White * a;
            }
        }
        t.SetData(data);
        return t;
    }

    private static Texture2D MakeGlow(GraphicsDevice d, int size)
    {
        var t = new Texture2D(d, size, size);
        var data = new Color[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - r) / r, dy = (y + 0.5f - r) / r;
                float dist = MathF.Sqrt((dx * dx) + (dy * dy));
                float a = MathF.Pow(Math.Clamp(1 - dist, 0, 1), 1.8f);
                data[(y * size) + x] = Color.White * a;
            }
        }
        t.SetData(data);
        return t;
    }

    /// <summary>文字の大きさは整数にそろえる（大きさが毎フレーム変わっても、文字の画像が増え続けないように）。</summary>
    public SpriteFontBase Font(float size) => fonts.GetFont(MathF.Max(1, MathF.Round(size)));

    /// <summary>
    /// 動かす文字：大きさは決まった値で作り、拡大・回転は描くときにかける（拡大縮小のアニメーション用）。
    /// center を中心に描く。stroke で黒い縁取り。
    /// </summary>
    public void TextFx(string text, Vector2 center, float size, Color color, float scale = 1f, float rotation = 0f, bool stroke = true)
    {
        if (scale <= 0.01f || color.A == 0) return;
        var f = Font(size);
        var m = f.MeasureString(text);
        var origin = m / 2;
        var sc = new Vector2(scale, scale);
        float a = color.A / 255f;
        if (stroke)
        {
            Batch.DrawString(f, text, center, Color.Black * a, rotation, origin, sc, 0, 0, 0, TextStyle.None, FontSystemEffect.Stroked, Math.Max(2, (int)(size / 18)));
        }
        else
        {
            Batch.DrawString(f, text, center + new Vector2(0, Math.Max(2, size / 16) * scale), Color.Black * (a * 0.7f), rotation, origin, sc);
        }
        Batch.DrawString(f, text, center, color, rotation, origin, sc);
    }

    /// <summary>窓が開くときの形（中央の横線から上下に開く）。k は 0〜1。</summary>
    public static Rectangle Opening(Rectangle r, float k)
    {
        float e = Abbild.Engine.Ease.OutBack(Math.Clamp(k, 0, 1));
        int h = Math.Max(6, (int)(r.Height * e));
        int w = (int)(r.Width * (0.9f + (0.1f * Math.Clamp(k * 2, 0, 1))));
        return new Rectangle(r.Center.X - (w / 2), r.Center.Y - (h / 2), w, h);
    }


    // ---- 図形 ----

    public void Rect(Rectangle r, Color c) => Batch.Draw(_pixel, r, c);

    public void Rect(float x, float y, float w, float h, Color c) =>
        Batch.Draw(_pixel, new Vector2(x, y), null, c, 0, Vector2.Zero, new Vector2(w, h), SpriteEffects.None, 0);

    public void Outline(Rectangle r, Color c, int t)
    {
        Rect(new Rectangle(r.X, r.Y, r.Width, t), c);
        Rect(new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        Rect(new Rectangle(r.X, r.Y, t, r.Height), c);
        Rect(new Rectangle(r.Right - t, r.Y, t, r.Height), c);
    }

    /// <summary>
    /// 装飾つきの窓：影・上から下へのグラデーション・金の縁（光と影）・四隅の飾り。
    /// border を渡すと縁の色を変えられる（成功＝緑、失敗＝赤 など）。
    /// </summary>
    public void Window(Rectangle r, float alpha = 1f, Color? fill = null, Color? border = null)
    {
        if (alpha <= 0) return;
        // 影
        Rect(new Rectangle(r.X + 8, r.Y + 10, r.Width, r.Height), Color.Black * (0.35f * alpha));
        // 中身（上が少し明るい）
        var top = fill ?? new Color(26, 32, 68, 236);
        var bottom = fill is null ? new Color(8, 10, 26, 242) : Color.Lerp(fill.Value, Color.Black, 0.35f);
        const int bands = 10;
        for (int i = 0; i < bands; i++)
        {
            int y0 = r.Y + (r.Height * i / bands);
            int y1 = r.Y + (r.Height * (i + 1) / bands);
            Rect(new Rectangle(r.X, y0, r.Width, y1 - y0), Color.Lerp(top, bottom, i / (float)(bands - 1)) * alpha);
        }
        // 縁：外の黒 → 金（左上が明るく、右下が暗い）→ 内側の細い線
        var gold = border ?? Palette.Frame;
        var light = Color.Lerp(gold, Color.White, 0.45f);
        var dark = Color.Lerp(gold, Color.Black, 0.45f);
        Outline(new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), Color.Black * (0.8f * alpha), 2);
        Rect(new Rectangle(r.X, r.Y, r.Width, 3), light * alpha);
        Rect(new Rectangle(r.X, r.Y, 3, r.Height), light * alpha);
        Rect(new Rectangle(r.X, r.Bottom - 3, r.Width, 3), dark * alpha);
        Rect(new Rectangle(r.Right - 3, r.Y, 3, r.Height), dark * alpha);
        Outline(new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6), gold * (0.9f * alpha), 1);
        Outline(new Rectangle(r.X + 8, r.Y + 8, r.Width - 16, r.Height - 16), gold * (0.28f * alpha), 1);
        // 四隅の飾り（ひし形＋宝石）
        if (r.Width >= 120 && r.Height >= 80)
        {
            Corner(new Vector2(r.X + 2, r.Y + 2), gold, alpha);
            Corner(new Vector2(r.Right - 2, r.Y + 2), gold, alpha);
            Corner(new Vector2(r.X + 2, r.Bottom - 2), gold, alpha);
            Corner(new Vector2(r.Right - 2, r.Bottom - 2), gold, alpha);
        }
    }

    private void Corner(Vector2 c, Color gold, float alpha)
    {
        // 4px 単位のひし形
        for (int i = -3; i <= 3; i++)
        {
            int w = 3 - Math.Abs(i);
            Rect(c.X - (w * 4) - 2, c.Y + (i * 4) - 2, (w * 8) + 4, 4, Color.Black * (0.85f * alpha));
        }
        for (int i = -2; i <= 2; i++)
        {
            int w = 2 - Math.Abs(i);
            Rect(c.X - (w * 4) - 2, c.Y + (i * 4) - 2, (w * 8) + 4, 4, (i < 0 ? Color.Lerp(gold, Color.White, 0.4f) : gold) * alpha);
        }
        Rect(c.X - 2, c.Y - 2, 4, 4, Palette.Gem * alpha);
    }

    /// <summary>見出しつきの窓（上に札が出る）。</summary>
    public void TitledWindow(Rectangle r, string title, float alpha = 1f, Color? border = null)
    {
        Window(r, alpha, border: border);
        var m = Measure(title, 30);
        var tab = new Rectangle(r.X + 28, r.Y - 22, (int)m.X + 44, 44);
        Window(tab, alpha, new Color(60, 44, 18, 245), border);
        Text(title, new Vector2(tab.X + 22, tab.Y + 5), 30, Palette.Gold * alpha);
    }

    public void Circle(Vector2 center, float radius, Color c) =>
        Batch.Draw(_circle, center, null, c, 0, new Vector2(64, 64), radius / 64f, SpriteEffects.None, 0);

    public void Glow(Vector2 center, float radius, Color c) =>
        Batch.Draw(_glow, center, null, c, 0, new Vector2(64, 64), radius / 64f, SpriteEffects.None, 0);

    public void Ring(Vector2 center, float radius, float thickness, Color c, int segments = 72)
    {
        for (int i = 0; i < segments; i++)
        {
            float a0 = MathF.Tau * i / segments;
            float a1 = MathF.Tau * (i + 1) / segments;
            var p0 = center + (new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius);
            var p1 = center + (new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius);
            Line(p0, p1, c, thickness);
        }
    }

    public void Line(Vector2 a, Vector2 b, Color c, float thickness)
    {
        var d = b - a;
        Batch.Draw(_pixel, a, null, c, MathF.Atan2(d.Y, d.X), new Vector2(0, 0.5f), new Vector2(d.Length(), thickness), SpriteEffects.None, 0);
    }

    /// <summary>ゲージ。trail を渡すと、減った分が白く残って追いかける。</summary>
    public void Bar(Rectangle r, float ratio, Color fill, Color back, Color? frame = null, float trail = -1, bool ticks = true)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        // 外枠（黒 → 縁）
        Rect(new Rectangle(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), Color.Black * 0.85f);
        Rect(r, Color.Lerp(back, Color.Black, 0.55f));
        int w = (int)(r.Width * ratio);
        if (trail > ratio)
        {
            int tw = (int)(r.Width * Math.Clamp(trail, 0, 1));
            Rect(new Rectangle(r.X + w, r.Y, tw - w, r.Height), new Color(255, 250, 230) * 0.85f);
        }
        if (w > 0)
        {
            var light = Color.Lerp(fill, Color.White, 0.35f);
            var dark = Color.Lerp(fill, Color.Black, 0.3f);
            int hTop = Math.Max(2, r.Height / 3);
            Rect(new Rectangle(r.X, r.Y, w, r.Height), fill);
            Rect(new Rectangle(r.X, r.Y, w, hTop), light);
            Rect(new Rectangle(r.X, r.Bottom - Math.Max(2, r.Height / 4), w, Math.Max(2, r.Height / 4)), dark);
        }
        if (ticks && r.Width >= 160)
        {
            for (int i = 1; i < 10; i++)
            {
                int x = r.X + (r.Width * i / 10);
                Rect(new Rectangle(x, r.Y, 2, r.Height), Color.Black * 0.28f);
            }
        }
        Outline(r, frame ?? new Color(220, 200, 150) * 0.9f, 2);
    }

    // ---- 文字 ----

    public Vector2 Measure(string text, float size) => Font(size).MeasureString(text);

    public void Text(string text, Vector2 pos, float size, Color color, bool shadow = true)
    {
        var f = Font(size);
        if (shadow) Batch.DrawString(f, text, pos + new Vector2(0, Math.Max(2, size / 16)), Color.Black * (color.A / 255f * 0.7f));
        Batch.DrawString(f, text, pos, color);
    }

    /// <summary>縁取りした文字（背景が明るいところ用）。</summary>
    public void Bold(string text, Vector2 pos, float size, Color color, float alpha = 1f)
    {
        Batch.DrawString(Font(size), text, pos, Color.Black * alpha, effect: FontSystemEffect.Stroked, effectAmount: Math.Max(2, (int)(size / 18)));
        Batch.DrawString(Font(size), text, pos, color * alpha);
    }

    public void TextCentered(string text, Vector2 center, float size, Color color, bool bold = false)
    {
        var m = Measure(text, size);
        var p = new Vector2(MathF.Round(center.X - (m.X / 2)), MathF.Round(center.Y - (m.Y / 2)));
        if (bold) Bold(text, p, size, color, color.A / 255f);
        else Text(text, p, size, color);
    }

    public void TextRight(string text, Vector2 rightTop, float size, Color color)
    {
        var m = Measure(text, size);
        Text(text, new Vector2(rightTop.X - m.X, rightTop.Y), size, color);
    }

    /// <summary>日本語向けの折り返し（1 文字ずつ測り、行頭に句読点が来ないようにする）。</summary>
    public List<string> Wrap(string text, float size, float maxWidth)
    {
        var lines = new List<string>();
        var font = Font(size);
        foreach (var para in text.Split('\n'))
        {
            var line = new System.Text.StringBuilder();
            foreach (char ch in para)
            {
                line.Append(ch);
                if (font.MeasureString(line.ToString()).X > maxWidth && line.Length > 1)
                {
                    // 行頭禁則：句読点・閉じかっこは前の行に残す
                    if ("、。）」』！？…ー".Contains(ch, StringComparison.Ordinal))
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                    }
                    else
                    {
                        line.Length--;
                        lines.Add(line.ToString());
                        line.Clear();
                        line.Append(ch);
                    }
                }
            }
            lines.Add(line.ToString());
        }
        return lines;
    }

    /// <summary>折り返して描く。描いた高さを返す。</summary>
    public float Paragraph(string text, Vector2 pos, float size, float maxWidth, Color color, float lineGap = 1.35f)
    {
        float y = pos.Y;
        foreach (var l in Wrap(text, size, maxWidth))
        {
            Text(l, new Vector2(pos.X, y), size, color);
            y += size * lineGap;
        }
        return y - pos.Y;
    }
}
