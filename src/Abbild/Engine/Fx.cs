using Abbild.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Abbild.Engine;

public enum ParticleShape { Pixel, Glow, Streak, Ring }

/// <summary>粒ひとつ。ドット絵に合わせて、四角い点で描く。</summary>
public struct Particle
{
    public Vector2 Pos;
    public Vector2 Vel;
    public float Life;
    public float Age;
    public float Size;
    public float EndSize;
    public Color Color;
    public Color EndColor;
    public float Gravity;
    public float Drag;
    public ParticleShape Shape;
    public bool Additive;
    public float Spin;
}

/// <summary>線で描く一瞬の演出（斬撃・稲妻）。</summary>
public sealed class Stroke
{
    public required Vector2[] Points { get; init; }
    public float Life { get; init; } = 0.3f;
    public float Age { get; set; }
    public float Width { get; init; } = 8;
    public Color Color { get; init; } = Color.White;
    public Color Core { get; init; } = Color.White;

    /// <summary>描き始めから描き終わりまでの時間の割合（斬撃が走る）。</summary>
    public float Sweep { get; init; } = 0.35f;
}

/// <summary>
/// 粒子と線の演出をまとめて扱う。戦闘の攻撃・魔法・回復・敵の消滅、場所ごとの漂う粒などに使う。
/// </summary>
public sealed class Fx
{
    private readonly List<Particle> _p = new(2048);
    private readonly List<Stroke> _strokes = [];
    private readonly Random _r = new(77);

    public int Count => _p.Count;

    private float R(float a, float b) => a + ((float)_r.NextDouble() * (b - a));

    private static Vector2 Dir(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));

    public void Add(Particle p)
    {
        if (_p.Count < 4000) _p.Add(p);
    }

    public void Add(Stroke s) => _strokes.Add(s);

    public void Clear()
    {
        _p.Clear();
        _strokes.Clear();
    }

    public void Update(float dt)
    {
        for (int i = _p.Count - 1; i >= 0; i--)
        {
            var p = _p[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _p[i] = _p[^1];
                _p.RemoveAt(_p.Count - 1);
                continue;
            }
            p.Vel.Y += p.Gravity * dt;
            p.Vel *= MathF.Max(0, 1 - (p.Drag * dt));
            p.Pos += p.Vel * dt;
            _p[i] = p;
        }
        for (int i = _strokes.Count - 1; i >= 0; i--)
        {
            _strokes[i].Age += dt;
            if (_strokes[i].Age >= _strokes[i].Life) _strokes.RemoveAt(i);
        }
    }

    /// <summary>ふつうの合成で描くもの（煙・砂・破片）。</summary>
    public void DrawNormal(Gfx g) => DrawParticles(g, false);

    /// <summary>光を足す合成で描くもの（炎・光・稲妻）。Begin(blendState: Additive) の中で呼ぶ。</summary>
    public void DrawAdditive(Gfx g)
    {
        DrawParticles(g, true);
        foreach (var s in _strokes) DrawStroke(g, s);
    }

    private void DrawParticles(Gfx g, bool additive)
    {
        foreach (var p in _p)
        {
            if (p.Additive != additive) continue;
            float k = p.Age / p.Life;
            float size = MathHelper.Lerp(p.Size, p.EndSize, k);
            if (size <= 0.2f) continue;
            var c = Color.Lerp(p.Color, p.EndColor, k);
            float fade = k > 0.7f ? 1 - ((k - 0.7f) / 0.3f) : 1;
            c *= fade;
            switch (p.Shape)
            {
                case ParticleShape.Pixel:
                    // 4px 単位にそろえて、ドット絵らしく
                    float s = MathF.Max(4, MathF.Round(size / 4) * 4);
                    g.Rect(MathF.Round(p.Pos.X / 4) * 4 - (s / 2), MathF.Round(p.Pos.Y / 4) * 4 - (s / 2), s, s, c);
                    break;
                case ParticleShape.Glow:
                    g.Glow(p.Pos, size, c);
                    break;
                case ParticleShape.Streak:
                {
                    var v = p.Vel;
                    float len = MathF.Max(size, v.Length() * 0.04f);
                    if (v.LengthSquared() < 1) v = new Vector2(0, -1);
                    v.Normalize();
                    g.Line(p.Pos - (v * len), p.Pos, c, MathF.Max(2, size / 4));
                    break;
                }
                case ParticleShape.Ring:
                    g.Ring(p.Pos, size, MathF.Max(2, 10 * (1 - k)), c, 48);
                    break;
            }
        }
    }

    private static void DrawStroke(Gfx g, Stroke s)
    {
        float k = s.Age / s.Life;
        int n = s.Points.Length;
        if (n < 2) return;
        float head = Math.Min(1, k / s.Sweep) * (n - 1);
        float tail = k < s.Sweep ? 0 : (k - s.Sweep) / (1 - s.Sweep) * (n - 1);
        for (int i = 0; i < n - 1; i++)
        {
            float a = Math.Max(i, tail), b = Math.Min(i + 1, head);
            if (b <= a) continue;
            var p0 = Vector2.Lerp(s.Points[i], s.Points[i + 1], a - i);
            var p1 = Vector2.Lerp(s.Points[i], s.Points[i + 1], b - i);
            float mid = (i + 0.5f) / (n - 1);
            float w = s.Width * MathF.Sin(mid * MathF.PI) + 2;
            g.Line(p0, p1, s.Color * 0.8f, w * 2.2f);
            g.Line(p0, p1, s.Core, w * 0.7f);
        }
    }

    // ------------------------------------------------------------------
    // 決まった演出
    // ------------------------------------------------------------------

    /// <summary>剣の斬撃（斜めの弧＋火花）。</summary>
    public void Slash(Vector2 at, float size, bool critical)
    {
        int n = 14;
        float a0 = R(-2.6f, -2.2f);
        float a1 = a0 + MathF.PI * R(0.75f, 0.9f);
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = MathHelper.Lerp(a0, a1, i / (float)(n - 1));
            pts[i] = at + (Dir(a) * size * 0.55f) + new Vector2(size * 0.05f, 0);
        }
        var col = critical ? new Color(255, 210, 90) : new Color(170, 220, 255);
        Add(new Stroke { Points = pts, Life = 0.28f, Width = critical ? 18 : 12, Color = col, Core = Color.White });
        if (critical)
        {
            var pts2 = pts.Select(p => at + ((p - at) * 0.8f) + new Vector2(20, 30)).ToArray();
            Add(new Stroke { Points = pts2, Life = 0.32f, Width = 12, Color = col, Core = Color.White });
        }
        Sparks(at, critical ? 34 : 20, col, critical ? 900 : 650);
        Flash(at, critical ? 260 : 170, col);
    }

    /// <summary>敵の攻撃：画面を引き裂く 3 本の爪あと。</summary>
    public void Claw(Vector2 center, bool big)
    {
        float len = big ? 520 : 380;
        for (int k = -1; k <= 1; k++)
        {
            var off = new Vector2(k * 70, k * 20);
            var a = center + off + new Vector2(-len / 2, -len / 2.6f);
            var b = center + off + new Vector2(len / 2, len / 2.6f);
            var pts = Enumerable.Range(0, 8).Select(i => Vector2.Lerp(a, b, i / 7f)).ToArray();
            Add(new Stroke { Points = pts, Life = 0.3f, Width = big ? 16 : 11, Color = new Color(255, 40, 40), Core = new Color(255, 220, 220), Sweep = 0.3f });
        }
    }

    public void Sparks(Vector2 at, int count, Color color, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            float a = R(0, MathF.Tau);
            Add(new Particle
            {
                Pos = at,
                Vel = Dir(a) * R(speed * 0.3f, speed),
                Life = R(0.2f, 0.45f),
                Size = R(10, 22),
                EndSize = 4,
                Color = Color.White,
                EndColor = color,
                Drag = 4,
                Gravity = 600,
                Shape = ParticleShape.Streak,
                Additive = true,
            });
        }
    }

    public void Flash(Vector2 at, float radius, Color color)
    {
        Add(new Particle { Pos = at, Life = 0.22f, Size = radius * 0.6f, EndSize = radius, Color = color, EndColor = color * 0, Shape = ParticleShape.Glow, Additive = true });
        Add(new Particle { Pos = at, Life = 0.3f, Size = radius * 0.2f, EndSize = radius * 0.9f, Color = Color.White * 0.9f, EndColor = color * 0, Shape = ParticleShape.Ring, Additive = true });
    }

    /// <summary>属性ごとの魔法・アイテムの当たり。</summary>
    public void Element(Element e, Vector2 at, float size)
    {
        switch (e)
        {
            case Core.Element.Fire:
                for (int i = 0; i < 90; i++)
                {
                    float a = R(0, MathF.Tau);
                    float r = R(0, size * 0.3f);
                    Add(new Particle
                    {
                        Pos = at + (Dir(a) * r),
                        Vel = (Dir(a) * R(100, 420)) + new Vector2(0, R(-380, -120)),
                        Life = R(0.4f, 0.9f),
                        Size = R(18, 40),
                        EndSize = 4,
                        Color = new Color(255, 200, 70),
                        EndColor = new Color(190, 20, 10),
                        Drag = 1.5f,
                        Shape = ParticleShape.Pixel,
                        Additive = true,
                    });
                }
                Flash(at, size * 0.9f, new Color(255, 120, 30));
                break;
            case Core.Element.Ice:
                for (int i = 0; i < 34; i++)
                {
                    float a = R(0, MathF.Tau);
                    Add(new Particle
                    {
                        Pos = at + (Dir(a) * 30),
                        Vel = Dir(a) * R(450, 950),
                        Life = R(0.35f, 0.7f),
                        Size = R(40, 80),
                        EndSize = 10,
                        Color = new Color(200, 245, 255),
                        EndColor = new Color(40, 150, 255),
                        Drag = 5,
                        Shape = ParticleShape.Streak,
                        Additive = true,
                    });
                }
                for (int i = 0; i < 40; i++)
                {
                    Add(new Particle
                    {
                        Pos = at + new Vector2(R(-size * 0.4f, size * 0.4f), R(-size * 0.4f, size * 0.4f)),
                        Vel = new Vector2(R(-30, 30), R(20, 90)),
                        Life = R(0.6f, 1.2f),
                        Size = R(6, 12),
                        EndSize = 4,
                        Color = new Color(220, 250, 255),
                        EndColor = new Color(120, 200, 255),
                        Shape = ParticleShape.Pixel,
                        Additive = true,
                    });
                }
                Flash(at, size * 0.8f, new Color(120, 210, 255));
                break;
            case Core.Element.Thunder:
                for (int k = 0; k < 3; k++) Lightning(new Vector2(at.X + R(-60, 60), -40), at + new Vector2(R(-40, 40), R(-30, 30)));
                Sparks(at, 30, new Color(255, 240, 120), 800);
                Flash(at, size, new Color(255, 250, 160));
                break;
            case Core.Element.Poison:
                for (int i = 0; i < 46; i++)
                {
                    Add(new Particle
                    {
                        Pos = at + new Vector2(R(-size * 0.4f, size * 0.4f), R(-size * 0.2f, size * 0.4f)),
                        Vel = new Vector2(R(-60, 60), R(-160, -40)),
                        Life = R(0.6f, 1.3f),
                        Size = R(14, 34),
                        EndSize = 40,
                        Color = new Color(170, 80, 220) * 0.9f,
                        EndColor = new Color(60, 160, 60) * 0.4f,
                        Drag = 1,
                        Shape = ParticleShape.Glow,
                        Additive = false,
                    });
                }
                break;
            case Core.Element.Holy:
                for (int i = 0; i < 18; i++)
                {
                    float x = at.X + R(-size * 0.4f, size * 0.4f);
                    Add(new Particle { Pos = new Vector2(x, at.Y + R(-40, 80)), Vel = new Vector2(0, R(-700, -400)), Life = R(0.4f, 0.8f), Size = R(30, 70), EndSize = 10, Color = Color.White, EndColor = new Color(255, 230, 140), Shape = ParticleShape.Streak, Additive = true });
                }
                Flash(at, size, new Color(255, 245, 200));
                break;
            default:
                Slash(at, size, false);
                break;
        }
    }

    public void Lightning(Vector2 from, Vector2 to)
    {
        int n = 10;
        var pts = new Vector2[n];
        var d = to - from;
        var normal = new Vector2(-d.Y, d.X);
        normal.Normalize();
        for (int i = 0; i < n; i++)
        {
            float k = i / (float)(n - 1);
            float off = i == 0 || i == n - 1 ? 0 : R(-50, 50);
            pts[i] = from + (d * k) + (normal * off);
        }
        Add(new Stroke { Points = pts, Life = 0.25f, Width = 9, Color = new Color(255, 230, 90), Core = Color.White, Sweep = 0.15f });
    }

    /// <summary>回復の光（下から上へ、きらきら）。</summary>
    public void Heal(Vector2 at, float width)
    {
        for (int i = 0; i < 36; i++)
        {
            Add(new Particle
            {
                Pos = at + new Vector2(R(-width / 2, width / 2), R(-20, 40)),
                Vel = new Vector2(R(-20, 20), R(-260, -90)),
                Life = R(0.6f, 1.2f),
                Size = R(8, 16),
                EndSize = 4,
                Color = new Color(200, 255, 200),
                EndColor = new Color(60, 220, 120),
                Shape = ParticleShape.Pixel,
                Additive = true,
            });
        }
        Flash(at, width * 0.6f, new Color(80, 255, 140));
    }

    /// <summary>敵が倒れたとき：絵の色で崩れ落ちる粒＋光。</summary>
    public void Shatter(Rectangle area, Color tint, int count = 160)
    {
        for (int i = 0; i < count; i++)
        {
            var pos = new Vector2(R(area.Left, area.Right), R(area.Top, area.Bottom));
            Add(new Particle
            {
                Pos = pos,
                Vel = new Vector2(R(-140, 140), R(-260, -20)),
                Life = R(0.6f, 1.3f),
                Size = R(8, 16),
                EndSize = 4,
                Color = Color.Lerp(tint, Color.White, R(0, 0.6f)),
                EndColor = tint * 0.3f,
                Gravity = 420,
                Drag = 0.8f,
                Shape = ParticleShape.Pixel,
                Additive = false,
            });
        }
        Flash(new Vector2(area.Center.X, area.Center.Y), Math.Max(area.Width, area.Height) * 0.6f, Color.White);
    }

    /// <summary>レベルアップ：光の柱と昇る粒。</summary>
    public void Pillar(Vector2 at, Color color)
    {
        for (int i = 0; i < 60; i++)
        {
            Add(new Particle
            {
                Pos = at + new Vector2(R(-140, 140), R(0, 120)),
                Vel = new Vector2(0, R(-700, -250)),
                Life = R(0.6f, 1.2f),
                Size = R(30, 80),
                EndSize = 10,
                Color = Color.White,
                EndColor = color,
                Shape = ParticleShape.Streak,
                Additive = true,
            });
        }
        Flash(at, 420, color);
    }

    /// <summary>キラッと光る点（宝箱・コイン）。</summary>
    public void Twinkle(Vector2 at, int count, Color color)
    {
        for (int i = 0; i < count; i++)
        {
            float a = R(0, MathF.Tau);
            Add(new Particle { Pos = at, Vel = Dir(a) * R(80, 380), Life = R(0.5f, 1f), Size = R(8, 14), EndSize = 4, Color = Color.White, EndColor = color, Gravity = 300, Drag = 2, Shape = ParticleShape.Pixel, Additive = true });
        }
    }

    // ------------------------------------------------------------------
    // 場所ごとに漂う粒
    // ------------------------------------------------------------------

    private float _ambientAcc;

    public void Ambient(Biome? biome, float dt, float strength = 1f)
    {
        if (biome is null) return;
        _ambientAcc += dt * strength;
        float every = biome.Effect switch
        {
            BiomeEffect.Burn => 0.025f,
            BiomeEffect.Slow => 0.03f,
            BiomeEffect.Miss => 0.02f,
            BiomeEffect.Poison => 0.06f,
            BiomeEffect.Blessing => 0.05f,
            BiomeEffect.Curse => 0.05f,
            BiomeEffect.Void => 0.04f,
            BiomeEffect.Dark => 0.12f,
            _ => 0.08f,
        };
        while (_ambientAcc >= every)
        {
            _ambientAcc -= every;
            SpawnAmbient(biome);
        }
    }

    private void SpawnAmbient(Biome biome)
    {
        const float W = Gfx.Width, H = Gfx.Height;
        switch (biome.Index)
        {
            case 0: // 平原：ふわふわ光る胞子
                Add(new Particle { Pos = new Vector2(R(0, W), R(H * 0.3f, H)), Vel = new Vector2(R(-15, 15), R(-30, -8)), Life = R(4, 8), Size = R(6, 10), EndSize = 4, Color = new Color(140, 255, 200) * 0.8f, EndColor = new Color(80, 200, 255) * 0.4f, Shape = ParticleShape.Pixel, Additive = true });
                break;
            case 1: // 火山：火の粉
            case 9:
                Add(new Particle { Pos = new Vector2(R(0, W), H + 10), Vel = new Vector2(R(-30, 30), R(-220, -90)), Life = R(3, 6), Size = R(6, 12), EndSize = 4, Color = new Color(255, 220, 120), EndColor = new Color(220, 50, 20) * 0.5f, Drag = 0.2f, Shape = ParticleShape.Pixel, Additive = true });
                break;
            case 2: // 氷河：雪
                Add(new Particle { Pos = new Vector2(R(-100, W), -10), Vel = new Vector2(R(20, 70), R(60, 140)), Life = R(8, 14), Size = R(6, 12), EndSize = 6, Color = Color.White * 0.9f, EndColor = Color.White * 0.6f, Shape = ParticleShape.Pixel });
                break;
            case 3: // 砂漠：横に流れる砂
                Add(new Particle { Pos = new Vector2(-20, R(0, H)), Vel = new Vector2(R(300, 600), R(-20, 30)), Life = R(3, 6), Size = R(4, 8), EndSize = 4, Color = new Color(240, 200, 130) * 0.7f, EndColor = new Color(200, 150, 80) * 0.3f, Shape = ParticleShape.Pixel });
                break;
            case 4: // 毒の沼：泡
                Add(new Particle { Pos = new Vector2(R(0, W), H + 10), Vel = new Vector2(R(-10, 10), R(-90, -40)), Life = R(5, 10), Size = R(10, 22), EndSize = 30, Color = new Color(150, 240, 90) * 0.6f, EndColor = new Color(170, 80, 220) * 0.2f, Shape = ParticleShape.Ring, Additive = true });
                break;
            case 5: // 神殿：舞うほこりと光
            case 7: // 神界：光の粒
                Add(new Particle { Pos = new Vector2(R(0, W), R(0, H)), Vel = new Vector2(R(-10, 10), R(-25, -5)), Life = R(3, 6), Size = R(6, 12), EndSize = 4, Color = new Color(255, 245, 200), EndColor = new Color(255, 200, 120) * 0.3f, Shape = ParticleShape.Pixel, Additive = true });
                break;
            case 6: // 洞窟：しずく
                Add(new Particle { Pos = new Vector2(R(0, W), -10), Vel = new Vector2(0, R(300, 500)), Life = 3f, Size = 8, EndSize = 6, Color = new Color(120, 160, 255) * 0.7f, EndColor = new Color(120, 160, 255) * 0.4f, Gravity = 400, Shape = ParticleShape.Streak, Additive = true });
                break;
            case 8: // 魔界：赤黒い灰
                Add(new Particle { Pos = new Vector2(R(0, W), -10), Vel = new Vector2(R(-40, 40), R(30, 90)), Life = R(6, 12), Size = R(6, 12), EndSize = 4, Color = new Color(255, 80, 120) * 0.7f, EndColor = new Color(60, 0, 40) * 0.3f, Shape = ParticleShape.Pixel, Additive = true });
                break;
        }
    }
}
