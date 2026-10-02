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

    public SpriteFontBase Font(float size) => fonts.GetFont(size);


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

    /// <summary>WinForms 版から受け継いだ「白とグレーの二重枠」のウィンドウ。</summary>
    public void Window(Rectangle r, float alpha = 1f, Color? fill = null, Color? border = null)
    {
        Rect(r, (fill ?? Palette.Window) * alpha);
        Outline(r, (border ?? Palette.Border) * alpha, 3);
        Outline(new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12), Palette.BorderInner * (0.8f * alpha), 1);
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

    /// <summary>ゲージ（中身・背景・枠）。</summary>
    public void Bar(Rectangle r, float ratio, Color fill, Color back, Color? frame = null)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        Rect(r, new Color(28, 28, 36));
        Rect(new Rectangle(r.X, r.Y, r.Width, r.Height), back * 0.55f);
        int w = (int)(r.Width * ratio);
        if (w > 0)
        {
            Rect(new Rectangle(r.X, r.Y, w, r.Height), fill);
            Rect(new Rectangle(r.X, r.Y, w, Math.Max(1, r.Height / 3)), Color.White * 0.25f);
        }
        Outline(r, frame ?? Color.White * 0.9f, 2);
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
