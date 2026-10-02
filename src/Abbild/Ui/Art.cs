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

    /// <summary>敵を描く（WinForms 版の動き方を受け継ぐ）。</summary>
    public static void Enemy(Gfx g, EnemyArt art, EnemyDef def, Vector2 feet, float time, float scaleMul, Color color, bool attacking, float attackT)
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
        // 攻撃のときは前に出る
        float lunge = attacking ? MathF.Sin(Math.Clamp(attackT, 0, 1) * MathF.PI) * 0.12f : 0;
        var origin = new Vector2(fw / 2f, tex.Height);
        var pos = feet + new Vector2(ox, oy);
        var tint = new Color(color.ToVector4() * art.Tint.ToVector4());
        g.Batch.Draw(tex, pos, src, tint, 0, origin, new Vector2(scale * sx * (1 + lunge), scale * sy * (1 + lunge)), SpriteEffects.None, 0);
    }

    public static float EnemyHeight(EnemyArt art, float scaleMul) => art.Idle.Height * art.BaseScale * scaleMul;

    /// <summary>浮かんで消える数字。</summary>
    public sealed class Popup
    {
        public required string Text { get; init; }
        public required Vector2 Pos { get; init; }
        public required Color Color { get; init; }
        public float Size { get; init; } = 64;
        public float Age { get; set; }
        public float Life { get; init; } = 1.1f;
    }

    public static void Age(List<Popup> list, float dt)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            list[i].Age += dt;
            if (list[i].Age > list[i].Life) list.RemoveAt(i);
        }
    }

    public static void Popups(Gfx g, List<Popup> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var p = list[i];
            float k = p.Age / p.Life;
            float rise = Ease.OutCubic(Math.Min(1, k * 2.2f)) * 70;
            float alpha = k < 0.7f ? 1 : 1 - ((k - 0.7f) / 0.3f);
            float pop = k < 0.12f ? 1.25f - (k / 0.12f * 0.25f) : 1f;
            g.TextCentered(p.Text, p.Pos - new Vector2(0, rise), p.Size * pop, p.Color * alpha, bold: true);
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
