using Abbild.Engine;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Abbild.Ui;

/// <summary>
/// タイトルのロゴを、フォントから一度だけ作る（金属のグラデーション・縁取り・上の縁の光・影）。
/// 画像素材を増やさずに、ロゴらしい見た目にする。
/// </summary>
public static class Logo
{
    private static Texture2D? _logo;
    private static Texture2D? _mask;
    private static readonly List<Rectangle> _letters = [];

    /// <summary>文字の数。</summary>
    public static int LetterCount => _letters.Count;

    /// <summary>i 文字目が着地する時刻（intro の秒）。最初に地面に当たる瞬間。</summary>
    public static float LandTime(int i) => LetterStart(i) + (0.5f * 0.3636f);

    private static float LetterStart(int i) => 0.15f + (i * 0.09f);

    /// <summary>i 文字目の、着地したときの足もと（画面の座標）。</summary>
    public static Vector2 LetterFoot(int i, Vector2 center, float scale = 1f)
    {
        if (_logo is null || i < 0 || i >= _letters.Count) return center;
        var r = _letters[i];
        float x = center.X + ((r.Center.X - (_logo.Width / 2f)) * scale);
        return new Vector2(x, center.Y + (_logo.Height * 0.32f * scale));
    }
    private static readonly RasterizerState Clip = new() { ScissorTestEnable = true, CullMode = CullMode.None };

    public static (Texture2D Logo, Texture2D Mask) Get(Gfx g, string text = "Abbild", float size = 208)
    {
        if (_logo is not null && _mask is not null) return (_logo, _mask);
        var dev = g.Device;
        var font = g.Font(size);
        // 1 文字ずつ広いマスに描き、インクの幅で詰め直す（等幅フォントの i・l がすかすかにならないように）
        var m = font.MeasureString(text);
        const int pad = 36;
        int cell = (int)(size * 1.1f);
        int rw = (cell * text.Length) + (pad * 2), h = (int)m.Y + (pad * 2);
        var prev = dev.GetRenderTargets();
        var raw = new Color[rw * h];
        using (var rt = new RenderTarget2D(dev, rw, h))
        {
            dev.SetRenderTarget(rt);
            dev.Clear(Color.Transparent);
            // 描画中のバッチとぶつからないよう、専用のバッチで描く（描画の外＝シーンの Enter で呼ぶ）
            using (var sb = new SpriteBatch(dev))
            {
                sb.Begin(samplerState: SamplerState.PointClamp);
                for (int c = 0; c < text.Length; c++)
                {
                    sb.DrawString(font, text[c].ToString(), new Vector2(pad + (c * cell), pad), Color.White);
                }
                sb.End();
            }
            dev.SetRenderTargets(prev);
            rt.GetData(raw);
        }

        // 各文字のインクの左右を調べる
        var spans = new List<(int From, int To)>();
        for (int c = 0; c < text.Length; c++)
        {
            int x0 = pad + (c * cell), x1 = Math.Min(rw, x0 + cell);
            int l = -1, r = -1;
            for (int x = x0; x < x1; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (raw[(y * rw) + x].A > 0) { if (l < 0) l = x; r = x; break; }
                }
            }
            if (l >= 0) spans.Add((l, r));
            else spans.Add((x0, x0 + (cell / 3))); // 空白
        }
        int gap = (int)(size * 0.07f);
        int w = pad * 2;
        foreach (var sp in spans) w += sp.To - sp.From + 1;
        w += gap * (spans.Count - 1);

        var a = new float[w * h];
        int ox = pad;
        var placed = new List<(int From, int To)>();
        foreach (var sp in spans)
        {
            int start = ox;
            for (int x = sp.From; x <= sp.To; x++, ox++)
            {
                for (int y = 0; y < h; y++) a[(y * w) + ox] = raw[(y * rw) + x].A / 255f;
            }
            placed.Add((start, ox - 1));
            ox += gap;
        }
        // 1 文字ずつ動かすための区切り（文字と文字のあいだの真ん中で切る）
        _letters.Clear();
        for (int i = 0; i < placed.Count; i++)
        {
            int l = i == 0 ? 0 : (placed[i - 1].To + placed[i].From) / 2;
            int r = i == placed.Count - 1 ? w : (placed[i].To + placed[i + 1].From) / 2;
            _letters.Add(new Rectangle(l, 0, r - l, h));
        }
        int top = h, bottom = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] > 0.5f)
            {
                int y = i / w;
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        float[] Dilate(float[] input, int r)
        {
            var o = new float[input.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float best = 0;
                    for (int dy = -r; dy <= r && best < 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w) continue;
                            if ((dx * dx) + (dy * dy) > r * r) continue;
                            best = Math.Max(best, input[(yy * w) + xx]);
                            if (best >= 1) break;
                        }
                    }
                    o[(y * w) + x] = best;
                }
            }
            return o;
        }
        var outline = Dilate(a, 8);
        var outer = Dilate(outline, 3);

        var dst = new Color[w * h];
        var mask = new Color[w * h];
        var c0 = new Color(255, 248, 205);
        var c1 = new Color(246, 198, 82);
        var c2 = new Color(178, 98, 26);
        for (int y = 0; y < h; y++)
        {
            float k = bottom > top ? Math.Clamp((y - top) / (float)(bottom - top), 0, 1) : 0.5f;
            var fill = k < 0.5f ? Color.Lerp(c0, c1, k * 2) : Color.Lerp(c1, c2, (k - 0.5f) * 2);
            for (int x = 0; x < w; x++)
            {
                int i = (y * w) + x;
                var c = Color.Transparent;
                if (outer[i] > 0) c = Color.Black * outer[i];
                if (outline[i] > 0) c = Color.Lerp(c, new Color(46, 22, 8), outline[i]);
                if (a[i] > 0)
                {
                    var f = fill;
                    // 上の縁は明るく（立体感）
                    bool edgeTop = y >= 5 && a[i - (5 * w)] < 0.5f;
                    bool edgeBottom = y + 5 < h && a[i + (5 * w)] < 0.5f;
                    if (edgeTop) f = Color.Lerp(f, Color.White, 0.6f);
                    else if (edgeBottom) f = Color.Lerp(f, new Color(110, 50, 10), 0.55f);
                    c = Color.Lerp(c, f, a[i]);
                    mask[i] = Color.White * a[i];
                }
                dst[i] = c;
            }
        }
        _logo = new Texture2D(dev, w, h);
        _logo.SetData(dst);
        _mask = new Texture2D(dev, w, h);
        _mask.SetData(mask);
        return (_logo, _mask);
    }

    /// <summary>
    /// ロゴを描く（影・本体・ときどき光が走る）。center は中心。
    /// intro は出てからの秒数：1 文字ずつ上から落ちて弾み、そろったら光が走る。
    /// </summary>
    public static void Draw(Gfx g, Vector2 center, float time, float alpha, float scale = 1f, float intro = 99f)
    {
        var (logo, mask) = Get(g);
        var origin = new Vector2(logo.Width / 2f, logo.Height / 2f);
        var topLeft = center - (origin * scale);
        for (int i = 0; i < _letters.Count; i++)
        {
            var src = _letters[i];
            float k = Ease.Span(intro, LetterStart(i), LetterStart(i) + 0.5f);
            if (k <= 0) continue;
            float drop = (1 - Ease.OutBounce(k)) * 300;
            // そろったあとは、文字がゆっくり波打つ
            float wave = MathF.Sin((time * 2.2f) - (i * 0.7f)) * 3 * Ease.Span(intro, 1.6f, 2.2f);
            var pos = topLeft + (new Vector2(src.X, -drop + wave) * scale);
            float la = alpha * Math.Min(1, k * 4);
            g.Batch.Draw(logo, pos + new Vector2(10, 14 + (drop * 0.3f)), src, Color.Black * (0.55f * la * (1 - (drop / 300f))), 0, Vector2.Zero, scale, SpriteEffects.None, 0);
            g.Batch.Draw(logo, pos, src, Color.White * la, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        }
        g.Batch.End();

        // 光の帯が左から右へ走る（4 秒ごと。文字がそろってから）
        float cycle = time % 4f;
        if (cycle < 1.1f && intro > 1.6f)
        {
            float k = cycle / 1.1f;
            int bandW = 90;
            var dest = new Rectangle((int)(center.X - (logo.Width * scale / 2)), (int)(center.Y - (logo.Height * scale / 2)), (int)(logo.Width * scale), (int)(logo.Height * scale));
            int bx = dest.X + (int)((dest.Width + bandW) * k) - bandW;
            var vp = g.Device.Viewport.Bounds;
            var band = Rectangle.Intersect(new Rectangle(bx, dest.Y, bandW, dest.Height), vp);
            if (band.Width > 0)
            {
                g.Device.ScissorRectangle = band;
                g.Batch.Begin(blendState: BlendState.Additive, rasterizerState: Clip);
                g.Batch.Draw(mask, center, null, Color.White * (0.55f * alpha), 0, origin, scale, SpriteEffects.None, 0);
                g.Batch.End();
            }
        }
        g.Batch.Begin();
    }
}
