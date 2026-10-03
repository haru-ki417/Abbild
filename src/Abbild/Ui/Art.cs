using Abbild.Core;
using Abbild.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Abbild.Ui;

/// <summary>いろいろな画面で使う描き方。</summary>
public static class Art
{
    /// <summary>画面いっぱいに背景を敷く（少しずつ拡大して動きを出せる）。</summary>
    public static void Cover(Gfx g, Texture2D tex, float zoom = 1f, Vector2 pan = default, Color? tint = null)
    {
        float s = Math.Max(Gfx.Width / (float)tex.Width, Gfx.Height / (float)tex.Height) * zoom;
        var size = new Vector2(tex.Width * s, tex.Height * s);
        var pos = new Vector2((Gfx.Width - size.X) / 2, (Gfx.Height - size.Y) / 2) + pan;
        g.Batch.Draw(tex, pos, null, tint ?? Color.White, 0, Vector2.Zero, s, SpriteEffects.None, 0);
    }

    /// <summary>上下を暗くするグラデーション（文字を読みやすくする）。</summary>
    public static void Vignette(Gfx g, float top = 0.55f, float bottom = 0.7f)
    {
        const int steps = 24;
        for (int i = 0; i < steps; i++)
        {
            float k = 1 - (i / (float)steps);
            g.Rect(0, i * 14, Gfx.Width, 14, Color.Black * (top * k * k));
            g.Rect(0, Gfx.Height - ((i + 1) * 14), Gfx.Width, 14, Color.Black * (bottom * k * k));
        }
    }

    /// <summary>画面のふちを色づける（ダメージ・ピンチの合図）。</summary>
    public static void EdgeGlow(Gfx g, Color c, float strength)
    {
        const int steps = 16;
        for (int i = 0; i < steps; i++)
        {
            float k = 1 - (i / (float)steps);
            float a = strength * k * k;
            int t = 10;
            g.Rect(0, i * t, Gfx.Width, t, c * a);
            g.Rect(0, Gfx.Height - ((i + 1) * t), Gfx.Width, t, c * a);
            g.Rect(i * t, 0, t, Gfx.Height, c * a);
            g.Rect(Gfx.Width - ((i + 1) * t), 0, t, Gfx.Height, c * a);
        }
    }

    /// <summary>敵を描く（WinForms 版の動き方を受け継ぐ）。</summary>
    public static void Enemy(Gfx g, EnemyArt art, EnemyDef def, Vector2 feet, float time, float scaleMul, Color color, bool attacking, float attackT,
        Vector2 offset = default, float rotation = 0, float squash = 0)
    {
        double phase = time * 4.0 * def.MotionSpeed;
        int power = def.MotionIntensity;
        float sx = 1, sy = 1, ox = 0, oy = 0;
        var rnd = new Random((int)(time * 30));
        switch (def.Motion)
        {
            case Motion.Breathe:
            {
                float b = (float)Math.Sin(phase) * power / 180f;
                sx += b; sy -= b;
                break;
            }
            case Motion.Squish:
            {
                float q = (float)Math.Sin(phase * 1.5) * power / 140f;
                sx += q * 2; sy -= q;
                break;
            }
            case Motion.Vibrate:
                ox = rnd.Next(-power, power + 1) * 0.6f;
                oy = rnd.Next(-power, power + 1) * 0.6f;
                break;
            case Motion.Float:
                oy = (float)Math.Sin(phase) * power * 1.2f;
                break;
            case Motion.Flicker:
            {
                float f = rnd.Next(-power, power + 1) / 200f;
                sx += f; sy += f;
                break;
            }
            case Motion.Heavy:
            {
                float h = (float)Math.Sin(phase * 0.5) * power / 160f;
                sx += h; sy += h;
                break;
            }
            case Motion.Panic:
                ox = rnd.Next(-power * 2, (power * 2) + 1);
                oy = (float)Math.Sin(phase * 2) * power * 1.5f;
                sx += rnd.Next(-power, power + 1) / 200f;
                break;
        }

        bool useAttack = attacking && art.Attack is not null;
        var tex = useAttack ? art.Attack! : art.Idle;
        int frames = useAttack ? art.AttackFrames : art.IdleFrames;
        int fw = tex.Width / frames;
        int frame = useAttack ? Math.Min(frames - 1, (int)(attackT * frames)) : (int)(time * 6) % frames;
        var src = new Rectangle(frame * fw, 0, fw, tex.Height);
        float scale = art.BaseScale * scaleMul;
        // 攻撃：少し縮んで身構え（ため）→ 一気に前へ大きく出る → ゆっくり戻る
        float lunge = 0, rise = 0;
        if (attacking)
        {
            float t = Math.Clamp(attackT, 0, 1);
            if (t < 0.4f)
            {
                float e = Ease.InOutSine(t / 0.4f);
                lunge = -0.05f * e;
                rise = -26 * e;
            }
            else if (t < 0.55f)
            {
                float e = Ease.OutCubic((t - 0.4f) / 0.15f);
                lunge = -0.05f + (0.27f * e);
                rise = -26 + (60 * e);
            }
            else
            {
                float e = Ease.InOutSine((t - 0.55f) / 0.45f);
                lunge = 0.22f * (1 - e);
                rise = 34 * (1 - e);
            }
        }
        sx *= 1 + squash;
        sy *= 1 - squash;
        var origin = new Vector2(fw / 2f, tex.Height);
        var pos = feet + new Vector2(ox, oy + rise) + offset;
        var tint = new Color(color.ToVector4() * art.Tint.ToVector4());
        g.Batch.Draw(tex, pos, src, tint, rotation, origin, new Vector2(scale * sx * (1 + lunge), scale * sy * (1 + lunge)), SpriteEffects.None, 0);
    }

    public static float EnemyHeight(EnemyArt art, float scaleMul) => art.Idle.Height * art.BaseScale * scaleMul;

    public enum PopupKind { Damage, Critical, HeroDamage, Heal, Miss }

    /// <summary>浮かんで消える数字。</summary>
    public sealed class Popup
    {
        public required string Text { get; init; }
        public required Vector2 Pos { get; init; }
        public required Color Color { get; init; }
        public float Size { get; init; } = 64;
        public PopupKind Kind { get; init; } = PopupKind.Damage;
        public float Age { get; set; }
        public float Life { get; init; } = 1.3f;
    }

    public static void Age(List<Popup> list, float dt)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            list[i].Age += dt;
            if (list[i].Age > list[i].Life) list.RemoveAt(i);
        }
    }

    /// <summary>
    /// 数字を 1 文字ずつ跳ねさせて出す。会心は「CRITICAL!」の札つきで大きく揺れる。
    /// こちらが受けたダメージは左右に震えてから下へ落ちる。
    /// </summary>
    public static void Popups(Gfx g, List<Popup> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var p = list[i];
            float t = p.Age;
            float k = t / p.Life;
            float alpha = k < 0.75f ? 1 : 1 - ((k - 0.75f) / 0.25f);
            var basePos = p.Pos;
            switch (p.Kind)
            {
                case PopupKind.HeroDamage:
                {
                    float shake = MathF.Max(0, 1 - (t / 0.3f));
                    basePos += new Vector2(MathF.Sin(t * 90) * 14 * shake, Ease.InCubic(Ease.Span(t, 0.55f, p.Life)) * 40);
                    float pop = 1 + (0.5f * MathF.Max(0, 1 - (t / 0.15f)));
                    g.TextFx(p.Text, basePos, p.Size, p.Color * alpha, pop);
                    continue;
                }
                case PopupKind.Heal:
                {
                    float rise = Ease.OutCubic(t / 0.8f) * 60;
                    float pop = 0.6f + (0.4f * Ease.OutBack(t / 0.3f));
                    g.TextFx(p.Text, basePos - new Vector2(0, rise), p.Size, p.Color * alpha, pop);
                    continue;
                }
                case PopupKind.Miss:
                {
                    float slide = Ease.OutCubic(t / 0.5f) * 50;
                    g.TextFx(p.Text, basePos + new Vector2(slide, -slide * 0.4f), p.Size, p.Color * alpha, 1, -0.12f);
                    continue;
                }
            }

            // 敵へのダメージ：1 文字ずつ上から落ちて弾む
            bool crit = p.Kind == PopupKind.Critical;
            float rise2 = Ease.InCubic(Ease.Span(t, 0.7f, p.Life)) * 50;
            float total = 0;
            var widths = new float[p.Text.Length];
            for (int c = 0; c < p.Text.Length; c++)
            {
                widths[c] = g.Measure(p.Text[c].ToString(), p.Size).X * 0.92f;
                total += widths[c];
            }
            float shakeX = crit ? MathF.Sin(t * 80) * 10 * MathF.Max(0, 1 - (t / 0.35f)) : 0;
            float x = basePos.X - (total / 2) + shakeX;
            for (int c = 0; c < p.Text.Length; c++)
            {
                float local = (t - (c * 0.04f)) / 0.45f;
                if (local <= 0) { x += widths[c]; continue; }
                float drop = (1 - Ease.OutBounce(local)) * 70;
                float pop = local < 0.2f ? 1.35f - (local / 0.2f * 0.35f) : 1f;
                var at = new Vector2(x + (widths[c] / 2), basePos.Y - drop - rise2);
                g.TextFx(p.Text[c].ToString(), at, p.Size, p.Color * alpha * Math.Min(1, local * 4), pop * (crit ? 1.1f : 1f));
                x += widths[c];
            }
            if (crit)
            {
                float lk = Ease.OutElastic(t / 0.6f);
                g.TextFx("CRITICAL!", basePos - new Vector2(0, (p.Size * 0.9f) + rise2), 40, new Color(255, 150, 60) * alpha, lk, -0.06f);
            }
        }
    }

    /// <summary>主人公の顔あたりだけを切り出して描く（ステータス欄用）。</summary>
    public static void HeroBust(Gfx g, Texture2D hero, Rectangle dest, Color color)
    {
        int s = (int)(hero.Width * 0.7f);
        var src = new Rectangle((int)(hero.Width * 0.1f), 0, s, s);
        g.Batch.Draw(hero, dest, src, color);
    }
}
