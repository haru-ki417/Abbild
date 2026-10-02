using Abbild.Core;
using Abbild.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Abbild.Ui;

/// <summary>
/// ミニゲームごとの「舞台」の絵。画像素材を増やさず、図形と粒だけで描く。
/// ゲームの判定には関わらず、Challenge が公開している値を見て描くだけ。
/// </summary>
public sealed class ChallengeStage
{
    /// <summary>舞台を描く場所（仮想画面 1920×1080 の座標）。</summary>
    public static readonly Rectangle Area = new(560, 150, 800, 390);

    private readonly Fx _fx = new();
    private readonly RasterizerState _clip = new() { ScissorTestEnable = true, CullMode = CullMode.None };

    /// <summary>勇者の絵（なければ人の形で描く）。</summary>
    public Texture2D? Hero { get; set; }

    /// <summary>戦っている敵の絵（なければ角のある影で描く）。</summary>
    public EnemyArt? Enemy { get; set; }
    private readonly Random _r = new(5);
    private readonly float[] _ecg = new float[170];
    private float _ecgAcc;
    private float _sincePulse = 9;
    private float _energy;
    private float _pop;
    private float _shown;
    private float _spawn;
    private float _spawn2;
    private int _lastPulse;
    private bool _lastRoundDone;

    private float R(float a, float b) => a + ((float)_r.NextDouble() * (b - a));

    /// <summary>この種類は舞台の絵を持つか（暗闇のミニゲームは、わざと何も見せない）。</summary>
    public static bool Supports(Challenge c) => !c.DarkScreen;

    private static int PulseOf(Challenge c) => c switch
    {
        CountChallenge cc => cc.Count,
        AlchemyChallenge a => a.Count,
        HeartTrial h => h.Taps,
        FishingChallenge f => f.Gauge < 0 ? 0 : (int)Math.Round(f.Gauge * 16),
        _ => 0,
    };

    public void Update(Challenge c, float dt, bool running, float heartPhase)
    {
        _fx.Update(dt);
        _sincePulse += dt;
        _energy = Math.Max(0, _energy - (dt * 1.6f));
        _pop = Math.Max(0, _pop - (dt * 4));
        if (c.Gauge >= 0) _shown += ((float)c.Gauge - _shown) * Math.Min(1, dt * 8);

        int pulse = PulseOf(c);
        if (running && pulse > _lastPulse) OnPulse(c, pulse - _lastPulse);
        _lastPulse = pulse;

        // 心拍を見せるもの（センサーの心の測定）は、鼓動に合わせて脈打つ
        if (c is HeartTrial { Mode: BodyMode.Sensor } && running && (heartPhase % 1f) < dt * 1.5f)
        {
            _pop = 1;
            _sincePulse = 0;
        }

        if (c is BlacksmithChallenge bs)
        {
            if (bs.RoundDone && !_lastRoundDone)
            {
                bool good = bs.Status.StartsWith("Good", StringComparison.Ordinal);
                var at = new Vector2(Area.Center.X, Area.Bottom - 118);
                if (good)
                {
                    _fx.Sparks(at, 46, new Color(255, 210, 90), 620);
                    _fx.Flash(at, 160, new Color(255, 200, 120));
                    _pop = 1;
                }
                else
                {
                    for (int i = 0; i < 10; i++) _fx.Add(new Particle { Pos = at + new Vector2(R(-30, 30), 0), Vel = new Vector2(R(-40, 40), R(-90, -40)), Life = R(0.6f, 1.1f), Size = R(14, 22), EndSize = 40, Color = new Color(120, 120, 130) * 0.6f, EndColor = new Color(60, 60, 70) * 0f, Shape = ParticleShape.Glow });
                }
            }
            _lastRoundDone = bs.RoundDone;
        }

        if (running) Ambient(c, dt);
        UpdateEcg(c, dt, running);
    }

    private void OnPulse(Challenge c, int n)
    {
        // 心電図は、連打しても波形がつぶれないよう、拍の間を少しあける
        if (c.Kind != ChallengeKind.Revive || _sincePulse > 0.22f) _sincePulse = 0;
        _energy = Math.Min(1.4f, _energy + (0.25f * n));
        _pop = 1;
        var mid = new Vector2(Area.Center.X, Area.Center.Y + 20);
        switch (c.Kind)
        {
            case ChallengeKind.Charge:
                _fx.Sparks(mid, 10, new Color(255, 220, 120), 380);
                break;
            case ChallengeKind.ShakeTrial:
                for (int i = 0; i < 4; i++) _fx.Add(new Particle { Pos = new Vector2(Area.Right + 10, R(Area.Y + 40, Area.Bottom - 40)), Vel = new Vector2(R(-1500, -1000), 0), Life = 0.5f, Size = R(30, 60), EndSize = 30, Color = Color.White * 0.7f, EndColor = Color.White * 0.1f, Shape = ParticleShape.Streak, Additive = true });
                break;
            case ChallengeKind.MashTrial:
                _fx.Sparks(new Vector2(Area.Center.X, Area.Bottom - 46), 6, new Color(255, 230, 160), 260);
                break;
            case ChallengeKind.ShakeFree:
                _fx.Sparks(mid, 14, new Color(255, 240, 90), 420);
                break;
            case ChallengeKind.Revive:
                _fx.Add(new Particle { Pos = new Vector2(Area.X + 70, Area.Y + 60), Size = 20, EndSize = 70, Life = 0.5f, Color = new Color(120, 255, 160) * 0.8f, EndColor = new Color(120, 255, 160) * 0f, Shape = ParticleShape.Ring, Additive = true });
                break;
            case ChallengeKind.HeartTrial:
                _fx.Add(new Particle { Pos = mid, Size = 80, EndSize = 260, Life = 0.6f, Color = new Color(255, 90, 110) * 0.7f, EndColor = new Color(255, 90, 110) * 0f, Shape = ParticleShape.Ring, Additive = true });
                break;
            case ChallengeKind.Alchemy:
                for (int i = 0; i < 3 * n; i++) _fx.Add(new Particle { Pos = new Vector2(Area.Center.X + R(-60, 60), Area.Bottom - 70 - R(0, 50)), Vel = new Vector2(R(-20, 20), R(-160, -80)), Life = R(0.5f, 0.9f), Size = R(8, 16), EndSize = 4, Color = Color.White * 0.8f, EndColor = Color.White * 0.2f, Shape = ParticleShape.Ring, Additive = true });
                break;
            case ChallengeKind.Fishing:
                _fx.Sparks(new Vector2(Area.Center.X + 120, Area.Y + 150), 12, new Color(180, 230, 255), 300);
                break;
        }
    }

    private void Ambient(Challenge c, float dt)
    {
        _spawn += dt;
        _spawn2 += dt;
        var a = Area;
        switch (c.Kind)
        {
            case ChallengeKind.EscapeRun:
                while (_spawn > 0.07f)
                {
                    _spawn -= 0.07f;
                    float s = R(10, 26);
                    _fx.Add(new Particle { Pos = new Vector2(R(a.X, a.Right), a.Y + 30), Vel = new Vector2(R(-30, 30), R(20, 80)), Gravity = 1400, Life = 0.9f, Size = s, EndSize = s, Color = new Color(120, 90, 64), EndColor = new Color(90, 64, 44), Shape = ParticleShape.Pixel });
                    _fx.Add(new Particle { Pos = new Vector2(R(a.X, a.Right), a.Y + 30), Vel = new Vector2(R(-20, 20), R(10, 40)), Life = 1.4f, Size = R(30, 60), EndSize = 90, Color = new Color(120, 100, 80) * 0.25f, EndColor = new Color(120, 100, 80) * 0f, Shape = ParticleShape.Glow });
                }
                break;
            case ChallengeKind.Rage:
                while (_spawn > 0.02f)
                {
                    _spawn -= 0.02f;
                    float k = Math.Max(0.15f, _shown);
                    if (_r.NextDouble() > k) continue;
                    _fx.Add(new Particle { Pos = new Vector2(R(a.X, a.Right), a.Bottom + 10), Vel = new Vector2(R(-30, 30), R(-320, -160) * (0.6f + k)), Life = R(0.5f, 1.0f), Size = R(18, 36), EndSize = 6, Color = new Color(255, 220, 120), EndColor = new Color(200, 30, 10) * 0.4f, Shape = ParticleShape.Glow, Additive = true });
                }
                break;
            case ChallengeKind.Iai:
                while (_spawn > 0.35f)
                {
                    _spawn -= 0.35f;
                    _fx.Add(new Particle { Pos = new Vector2(R(a.X, a.Right + 100), a.Y - 10), Vel = new Vector2(R(-80, -30), R(40, 80)), Life = 6, Size = 8, EndSize = 8, Color = new Color(255, 190, 210), EndColor = new Color(255, 190, 210) * 0.5f, Shape = ParticleShape.Pixel });
                }
                break;
            case ChallengeKind.Bridge:
                while (_spawn > 0.12f)
                {
                    _spawn -= 0.12f;
                    float dir = c.TiltMarker >= 0 ? -1 : 1;
                    _fx.Add(new Particle { Pos = new Vector2(dir < 0 ? a.Right + 20 : a.X - 20, R(a.Y + 20, a.Bottom - 40)), Vel = new Vector2(dir * R(700, 1100), R(-20, 20)), Life = 1.0f, Size = R(30, 70), EndSize = 30, Color = Color.White * 0.35f, EndColor = Color.White * 0.05f, Shape = ParticleShape.Streak, Additive = true });
                }
                break;
            case ChallengeKind.Thaw:
                if (c.Lamp == LedColor.Orange)
                {
                    while (_spawn > 0.04f)
                    {
                        _spawn -= 0.04f;
                        _fx.Add(new Particle { Pos = new Vector2(a.Center.X + R(-70, 70), a.Bottom - R(60, 200)), Vel = new Vector2(R(-20, 20), R(-90, -40)), Life = R(0.8f, 1.4f), Size = R(20, 40), EndSize = 70, Color = Color.White * 0.25f, EndColor = Color.White * 0f, Shape = ParticleShape.Glow, Additive = true });
                    }
                }
                else _spawn = 0;
                if (_spawn2 > 0.5f && _shown > 0.15f)
                {
                    _spawn2 = 0;
                    _fx.Add(new Particle { Pos = new Vector2(a.Center.X + R(-80, 80), a.Bottom - 60), Vel = new Vector2(0, 40), Gravity = 600, Life = 0.4f, Size = 6, EndSize = 6, Color = new Color(170, 230, 255), EndColor = new Color(170, 230, 255) * 0.5f, Shape = ParticleShape.Pixel });
                }
                break;
            case ChallengeKind.Breath:
                if (c.Lamp == LedColor.Purple)
                {
                    while (_spawn > 0.03f)
                    {
                        _spawn -= 0.03f;
                        var from = new Vector2(a.X + 215, a.Bottom - 235);
                        _fx.Add(new Particle { Pos = from, Vel = new Vector2(R(260, 420), R(-60, 60)), Drag = 1.2f, Life = R(0.9f, 1.4f), Size = R(20, 34), EndSize = 110, Color = new Color(200, 120, 255) * 0.75f, EndColor = new Color(110, 220, 90) * 0.0f, Shape = ParticleShape.Glow, Additive = true });
                    }
                }
                else _spawn = 0;
                break;
        }

        if (c.Kind == ChallengeKind.Meditation)
        {
            bool calm = c.Lamp == LedColor.Blue;
            float every = calm ? 1.3f : 0.18f;
            while (_spawn2 > every)
            {
                _spawn2 -= every;
                var at = calm ? new Vector2(a.Center.X, a.Center.Y + 10) : new Vector2(R(a.X + 60, a.Right - 60), R(a.Y + 40, a.Bottom - 40));
                var col = calm ? new Color(140, 200, 255) : new Color(255, 110, 110);
                _fx.Add(new Particle { Pos = at, Size = calm ? 30 : 10, EndSize = calm ? 330 : 90, Life = calm ? 3.6f : 0.9f, Color = col * 0.7f, EndColor = col * 0f, Shape = ParticleShape.Ring, Additive = true });
            }
        }
        if (c.Kind == ChallengeKind.ShakeFree && _spawn2 > 0.22f && _shown < 1)
        {
            _spawn2 = 0;
            var mid = new Vector2(a.Center.X, a.Bottom - 150);
            float ang = R(0, MathF.Tau);
            var p0 = mid + (new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 50);
            var p1 = mid + (new Vector2(MathF.Cos(ang + 1.6f), MathF.Sin(ang + 1.6f)) * 110);
            _fx.Lightning(p0, p1);
        }
    }

    private void UpdateEcg(Challenge c, float dt, bool running)
    {
        if (c.Kind != ChallengeKind.Revive) return;
        _ecgAcc += dt * 190;
        while (_ecgAcc >= 1)
        {
            _ecgAcc -= 1;
            Array.Copy(_ecg, 1, _ecg, 0, _ecg.Length - 1);
            float t = _sincePulse;
            float v = t switch
            {
                < 0.012f => -0.15f,
                < 0.026f => 1.0f,
                < 0.04f => -0.45f,
                < 0.08f => 0.02f,
                < 0.2f => 0.2f * MathF.Sin((t - 0.08f) / 0.12f * MathF.PI),
                _ => 0,
            };
            if (!running) v = 0;
            _ecg[^1] = v + R(-0.03f, 0.03f);
        }
    }

    // ------------------------------------------------------------------
    // 描く
    // ------------------------------------------------------------------

    /// <summary>舞台を描く。バッチの中（ふつうの合成）で呼ぶ。中で区切り直すので、終わるとふつうの合成に戻る。</summary>
    public void Draw(Gfx g, Challenge c, float time, bool result, bool success, float heartPhase)
    {
        var a = Area;
        var (top, bottom) = Theme(c);
        // 枠
        g.Rect(new Rectangle(a.X - 10, a.Y - 10, a.Width + 20, a.Height + 20), Color.Black * 0.7f);
        g.Rect(new Rectangle(a.X - 6, a.Y - 6, a.Width + 12, a.Height + 12), Palette.Frame);
        g.Rect(new Rectangle(a.X - 3, a.Y - 3, a.Width + 6, a.Height + 6), new Color(40, 28, 10));
        for (int i = 0; i < 11; i++)
        {
            int y0 = a.Y + (a.Height * i / 11), y1 = a.Y + (a.Height * (i + 1) / 11);
            g.Rect(new Rectangle(a.X, y0, a.Width, y1 - y0), Color.Lerp(top, bottom, i / 10f));
        }
        g.Batch.End();

        // 中身は枠の中だけに描く
        var dev = g.Device;
        var old = dev.ScissorRectangle;
        dev.ScissorRectangle = a;
        var clip = _clip;
        g.Batch.Begin(rasterizerState: clip);
        DrawScene(g, c, time, result, success, heartPhase);
        _fx.DrawNormal(g);
        g.Batch.End();
        g.Batch.Begin(blendState: BlendState.Additive, rasterizerState: clip);
        DrawGlow(g, c, time);
        _fx.DrawAdditive(g);
        g.Batch.End();
        dev.ScissorRectangle = old;

        g.Batch.Begin();
        // 四隅の飾り
        foreach (var p in new[] { new Vector2(a.X - 6, a.Y - 6), new Vector2(a.Right + 6, a.Y - 6), new Vector2(a.X - 6, a.Bottom + 6), new Vector2(a.Right + 6, a.Bottom + 6) })
        {
            g.Batch.Draw(g.Pixel, p, null, Palette.Frame, MathF.PI / 4, new Vector2(0.5f, 0.5f), 16, SpriteEffects.None, 0);
            g.Batch.Draw(g.Pixel, p, null, Palette.Gem, MathF.PI / 4, new Vector2(0.5f, 0.5f), 8, SpriteEffects.None, 0);
        }
    }

    private static (Color Top, Color Bottom) Theme(Challenge c) => c.Kind switch
    {
        ChallengeKind.EscapeRun => (new Color(60, 40, 28), new Color(20, 12, 8)),
        ChallengeKind.Revive => (new Color(4, 24, 14), new Color(2, 10, 6)),
        ChallengeKind.HeartTrial => (new Color(54, 12, 22), new Color(12, 4, 8)),
        ChallengeKind.Alchemy => (new Color(44, 30, 56), new Color(12, 8, 16)),
        ChallengeKind.Meditation => (new Color(8, 26, 54), new Color(4, 10, 26)),
        ChallengeKind.Glare => (new Color(34, 8, 14), new Color(8, 2, 4)),
        ChallengeKind.Negotiation => (new Color(32, 28, 44), new Color(10, 8, 14)),
        ChallengeKind.Rage => (new Color(60, 10, 8), new Color(16, 2, 2)),
        ChallengeKind.Dowsing => (new Color(26, 26, 44), new Color(14, 10, 6)),
        ChallengeKind.Bridge => (new Color(46, 56, 90), new Color(4, 4, 10)),
        ChallengeKind.Fishing => (new Color(70, 50, 90), new Color(4, 14, 30)),
        ChallengeKind.Iai => (new Color(22, 26, 56), new Color(6, 6, 14)),
        ChallengeKind.Blacksmith => (new Color(40, 18, 10), new Color(12, 6, 4)),
        ChallengeKind.Lockpick => (new Color(36, 28, 22), new Color(10, 8, 6)),
        ChallengeKind.Thaw => (new Color(34, 56, 90), new Color(10, 16, 30)),
        ChallengeKind.Breath => (new Color(32, 18, 44), new Color(8, 4, 12)),
        _ => (new Color(30, 26, 62), new Color(10, 8, 20)),
    };

    private void DrawScene(Gfx g, Challenge c, float time, bool result, bool success, float heartPhase)
    {
        var a = Area;
        float cx = a.Center.X;
        float floor = a.Bottom - 40;
        switch (c.Kind)
        {
            case ChallengeKind.Charge:
            case ChallengeKind.ShakeTrial:
            {
                Ground(g, floor, new Color(20, 16, 40));
                if (c.Kind == ChallengeKind.ShakeTrial)
                {
                    // 残像
                    float bob = -MathF.Abs(MathF.Sin(time * (8 + (12 * _energy)))) * 10;
                    for (int i = 3; i >= 1; i--)
                    {
                        HeroAt(g, new Vector2(cx - 40 - (i * 46 * (0.3f + _energy)), floor + bob), 260, new Color(120, 200, 255) * (0.22f * _energy * (4 - i)), rot: 0.12f);
                    }
                    HeroAt(g, new Vector2(cx - 40, floor + bob), 260, Color.White, rot: 0.06f + (0.06f * _energy));
                }
                else
                {
                    HeroAt(g, new Vector2(cx, floor), 270, Color.Lerp(Color.White, new Color(255, 225, 150), _shown));
                }
                break;
            }
            case ChallengeKind.MashTrial:
            {
                Ground(g, floor, new Color(28, 20, 40));
                // 力だめしの塔（打つと玉が上がり、てっぺんの鐘が鳴る）
                var track = new Rectangle((int)cx - 18, a.Y + 64, 36, (int)floor - (a.Y + 64) - 20);
                g.Rect(new Rectangle(track.X - 8, track.Y - 4, track.Width + 16, track.Height + 30), new Color(70, 40, 24));
                g.Rect(track, new Color(24, 14, 10));
                for (int i = 1; i < 10; i++)
                {
                    int y = track.Bottom - (track.Height * i / 10);
                    var col = Color.Lerp(new Color(110, 220, 130), new Color(240, 80, 70), i / 10f);
                    g.Rect(track.Right + 12, y - 2, i % 5 == 0 ? 34 : 18, 4, col);
                    g.Rect(track.X - (i % 5 == 0 ? 46 : 30), y - 2, i % 5 == 0 ? 34 : 18, 4, col);
                }
                float k = Math.Clamp(_shown + (_pop * 0.04f), 0, 1);
                float py = track.Bottom - 16 - ((track.Height - 30) * k);
                g.Rect(cx - 26, py - 12, 52, 24, new Color(250, 240, 220));
                g.Rect(cx - 26, py + 6, 52, 6, new Color(180, 160, 140));
                var bell = new Vector2(cx, track.Y - 22);
                g.Circle(bell, 30, k >= 0.99f ? new Color(255, 236, 140) : new Color(200, 160, 60));
                g.Rect(cx - 36, bell.Y + 18, 72, 10, new Color(150, 110, 40));
                g.Rect(track.X - 90, floor - 26, track.Width + 180, 26, new Color(90, 60, 40));
                break;
            }
            case ChallengeKind.ShakeFree:
            {
                Ground(g, floor, new Color(30, 26, 20));
                var tint = Color.Lerp(new Color(255, 245, 140), Color.White, _shown);
                HeroAt(g, new Vector2(cx + (MathF.Sin(time * 60) * 6 * (1 - _shown)), floor), 270, tint);
                break;
            }
            case ChallengeKind.EscapeRun:
            {
                // 洞窟の天井と床、奥に出口の光
                g.Rect(new Rectangle(a.X, a.Y, a.Width, 34), new Color(40, 26, 18));
                for (int x = a.X; x < a.Right; x += 40) g.Rect(x, a.Y + 30, 24, 10 + ((x * 7) % 18), new Color(40, 26, 18));
                Ground(g, floor, new Color(44, 30, 20));
                var exit = new Vector2(a.Right - 40, floor - 100);
                g.Rect(new Rectangle((int)exit.X - 30, (int)floor - 190, 80, 190), new Color(255, 240, 200) * 0.7f);
                float x0 = a.X + 70 + ((a.Width - 170) * _shown);
                HeroAt(g, new Vector2(x0, floor - (MathF.Abs(MathF.Sin(time * 14)) * 8)), 180, Color.White, rot: 0.12f);
                break;
            }
            case ChallengeKind.Revive:
            {
                // 心電図のモニター
                for (int x = a.X; x < a.Right; x += 34) g.Rect(x, a.Y, 1, a.Height, new Color(30, 90, 50) * 0.5f);
                for (int y = a.Y; y < a.Bottom; y += 34) g.Rect(a.X, y, a.Width, 1, new Color(30, 90, 50) * 0.5f);
                float scale = 1 + (_pop * 0.3f);
                Icons.Draw(g, "heart", new Vector2(a.X + 40, a.Y + 30) - (new Vector2(30, 30) * (scale - 1)), 60 * scale);
                break;
            }
            case ChallengeKind.HeartTrial:
            {
                float beat = c.Mode == BodyMode.Sensor ? heartPhase % 1f : _sincePulse;
                float sc = 1 + (beat < 0.18f ? (0.18f - beat) * 1.2f : 0);
                float size = 200 * sc;
                Icons.Draw(g, "heart", new Vector2(cx - (size / 2), a.Center.Y - (size / 2) + 4), size);
                break;
            }
            case ChallengeKind.Alchemy:
                DrawFlask(g, c, time, floor);
                break;
            case ChallengeKind.Meditation:
            {
                // 上から見た池と、まん中の蓮
                g.Batch.Draw(g.CircleTex, new Rectangle(a.X + 40, a.Y + 20, a.Width - 80, a.Height - 40), new Color(10, 40, 80));
                g.Batch.Draw(g.CircleTex, new Rectangle(a.X + 70, a.Y + 40, a.Width - 140, a.Height - 80), new Color(14, 52, 100));
                var center = new Vector2(cx, a.Center.Y + 10);
                for (int i = 0; i < 6; i++)
                {
                    float ang = (i * MathF.Tau / 6) + (time * 0.1f);
                    var p = center + (new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 26);
                    g.Circle(p, 22, new Color(255, 170, 200));
                }
                g.Circle(center, 18, new Color(255, 230, 140));
                for (int i = 0; i < 3; i++)
                {
                    var pad = center + new Vector2(-190 + (i * 170), (i == 1 ? 90 : -70));
                    if (i == 1) pad = center + new Vector2(170, 80);
                    g.Batch.Draw(g.CircleTex, new Rectangle((int)pad.X - 36, (int)pad.Y - 20, 72, 40), new Color(40, 120, 70));
                }
                break;
            }
            case ChallengeKind.Glare:
                DrawEye(g, c, time);
                break;
            case ChallengeKind.Negotiation:
            {
                Ground(g, floor, new Color(24, 20, 34));
                HeroAt(g, new Vector2(cx - 210, floor), 250, Color.White);
                FoeAt(g, new Vector2(cx + 210, floor), 260, Color.White, new Color(60, 30, 40), time);
                break;
            }
            case ChallengeKind.Rage:
            {
                Ground(g, floor, new Color(40, 10, 8));
                var col = Color.Lerp(Color.White, new Color(255, 120, 90), _shown);
                HeroAt(g, new Vector2(cx + (MathF.Sin(time * 40) * 4 * _shown), floor), 270, col);
                break;
            }
            case ChallengeKind.Dowsing:
                DrawPendulum(g, c, time);
                break;
            case ChallengeKind.Bridge:
                DrawBridge(g, c, time);
                break;
            case ChallengeKind.Fishing:
                DrawFishing(g, c, time);
                break;
            case ChallengeKind.Iai:
                DrawIai(g, c, time, result, success);
                break;
            case ChallengeKind.Blacksmith:
                DrawForge(g, c, time);
                break;
            case ChallengeKind.Lockpick:
                DrawChest(g, c, time, result, success);
                break;
            case ChallengeKind.Thaw:
            {
                Ground(g, floor, new Color(200, 220, 240) * 0.35f);
                HeroAt(g, new Vector2(cx, floor), 240, Color.Lerp(new Color(140, 190, 255), Color.White, _shown));
                // 氷のかたまり（溶けるほど薄く小さく）
                float melt = _shown;
                float h = 290 * (1 - (melt * 0.35f));
                var ice = new Rectangle((int)cx - 140, (int)(floor - h), 280, (int)h);
                g.Rect(ice, new Color(150, 220, 255) * (0.55f * (1 - melt)));
                g.Rect(new Rectangle(ice.X, ice.Y, ice.Width, 8), Color.White * (0.6f * (1 - melt)));
                g.Rect(new Rectangle(ice.X + 18, ice.Y + 20, 10, ice.Height - 40), Color.White * (0.4f * (1 - melt)));
                g.Outline(ice, new Color(220, 245, 255) * (0.8f * (1 - melt)), 3);
                if (melt > 0.3f)
                {
                    g.Line(new Vector2(ice.X + 40, ice.Y + 30), new Vector2(ice.X + 90, ice.Y + 120), Color.White * 0.7f, 3);
                    g.Line(new Vector2(ice.X + 90, ice.Y + 120), new Vector2(ice.X + 70, ice.Y + 190), Color.White * 0.7f, 3);
                }
                if (melt > 0.6f) g.Line(new Vector2(ice.Right - 30, ice.Y + 10), new Vector2(ice.Right - 80, ice.Y + 110), Color.White * 0.7f, 3);
                break;
            }
            case ChallengeKind.Breath:
            {
                Ground(g, floor, new Color(30, 16, 40));
                HeroAt(g, new Vector2(a.X + 170, floor), 240, Color.White);
                FoeAt(g, new Vector2(a.Right - 170, floor), 250, Color.Lerp(Color.White, new Color(150, 240, 120), _shown * 0.7f), Color.Lerp(new Color(60, 30, 40), new Color(80, 140, 60), _shown * 0.7f), time);
                break;
            }
        }
    }

    /// <summary>光る部分（ふちの光・炎・ランプ）。加算合成の中で呼ばれる。</summary>
    private void DrawGlow(Gfx g, Challenge c, float time)
    {
        var a = Area;
        float cx = a.Center.X;
        float floor = a.Bottom - 40;
        switch (c.Kind)
        {
            case ChallengeKind.Charge:
                g.Glow(new Vector2(cx, floor - 110), 120 + (220 * _shown) + (40 * _energy), new Color(255, 200, 90) * (0.25f + (0.35f * _shown)));
                break;
            case ChallengeKind.Rage:
                g.Glow(new Vector2(cx, floor - 110), 160 + (200 * _shown), new Color(255, 60, 30) * (0.2f + (0.4f * _shown)));
                break;
            case ChallengeKind.ShakeFree:
                g.Glow(new Vector2(cx, floor - 110), 180, new Color(255, 240, 90) * (0.25f * (1 - _shown)));
                break;
            case ChallengeKind.MashTrial:
                if (_shown >= 0.99f) g.Glow(new Vector2(cx, a.Y + 42), 140 + (30 * MathF.Sin(time * 10)), new Color(255, 230, 120) * 0.6f);
                break;
            case ChallengeKind.EscapeRun:
                g.Glow(new Vector2(a.Right - 20, floor - 100), 220, new Color(255, 230, 180) * 0.5f);
                break;
            case ChallengeKind.Revive:
            {
                // 心電図の線（右から流れてくる）
                float baseY = a.Center.Y + 30;
                float step = a.Width / (float)(_ecg.Length - 1);
                var col = new Color(110, 255, 150);
                for (int i = 1; i < _ecg.Length; i++)
                {
                    var p0 = new Vector2(a.X + ((i - 1) * step), baseY - (_ecg[i - 1] * 120));
                    var p1 = new Vector2(a.X + (i * step), baseY - (_ecg[i] * 120));
                    float fade = i / (float)_ecg.Length;
                    g.Line(p0, p1, col * (0.25f * fade), 9);
                    g.Line(p0, p1, col * fade, 3);
                }
                g.Glow(new Vector2(a.Right - 4, baseY - (_ecg[^1] * 120)), 30, col * 0.9f);
                break;
            }
            case ChallengeKind.HeartTrial:
                g.Glow(new Vector2(cx, a.Center.Y), 260 + (80 * _pop), new Color(255, 60, 90) * (0.25f + (0.25f * _pop)));
                break;
            case ChallengeKind.Alchemy:
                g.Glow(new Vector2(cx, floor - 80), 200, Palette.Of(c.Lamp) * 0.35f);
                break;
            case ChallengeKind.Meditation:
                g.Glow(new Vector2(cx, a.Center.Y + 10), 120, (c.Lamp == LedColor.Blue ? new Color(255, 220, 160) : new Color(255, 80, 80)) * 0.35f);
                break;
            case ChallengeKind.Glare:
                if (c.Lamp == LedColor.Red) g.Glow(new Vector2(cx, a.Center.Y), 320, new Color(255, 30, 30) * (0.35f + (0.15f * MathF.Sin(time * 20))));
                break;
            case ChallengeKind.Negotiation:
            {
                var col = c.Lamp == LedColor.Cyan ? new Color(120, 230, 255) : Palette.Of(c.Lamp);
                var at = new Vector2(cx, a.Center.Y - 10);
                g.Glow(at, 90 + (10 * MathF.Sin(time * 4)), col * 0.6f);
                g.Glow(at, 30, Color.White * 0.6f);
                break;
            }
            case ChallengeKind.Thaw:
                if (c.Lamp == LedColor.Orange) g.Glow(new Vector2(cx, floor - 60), 200, new Color(255, 150, 60) * 0.4f);
                break;
            case ChallengeKind.Breath:
                g.Glow(new Vector2(a.Right - 150, floor - 110), 60 + (200 * _shown), new Color(170, 90, 240) * (0.15f + (0.35f * _shown)));
                break;
            case ChallengeKind.Blacksmith:
            {
                var at = new Vector2(cx, floor - 78);
                g.Glow(at, 140 + (60 * _pop), new Color(255, 140, 40) * (0.35f + (0.3f * _pop)));
                break;
            }
            case ChallengeKind.Lockpick:
            {
                var col = Palette.Of(c.Lamp);
                g.Glow(new Vector2(cx, a.Center.Y + 40), 90, col * 0.7f);
                break;
            }
            case ChallengeKind.Dowsing:
            {
                g.Glow(new Vector2(cx, a.Bottom - 30), 60 + (260 * _shown * _shown), new Color(255, 220, 120) * (0.15f + (0.5f * _shown * _shown)));
                break;
            }
            case ChallengeKind.Fishing:
                g.Glow(new Vector2(a.Right - 110, a.Y + 60), 70, new Color(255, 200, 150) * 0.5f);
                break;
            case ChallengeKind.Iai:
                g.Glow(new Vector2(a.Right - 140, a.Y + 90), 140, new Color(255, 250, 220) * 0.35f);
                break;
        }
    }

    // ------------------------------------------------------------------
    // 部品
    // ------------------------------------------------------------------

    private static void Ground(Gfx g, float y, Color c)
    {
        var a = Area;
        g.Rect(new Rectangle(a.X, (int)y, a.Width, a.Bottom - (int)y), c);
        g.Rect(new Rectangle(a.X, (int)y, a.Width, 3), Color.White * 0.12f);
    }

    /// <summary>人の形（頭・胴・手足・剣）。step は足の振り（-1〜1）。facing は 1 で右向き。</summary>
    private static void Figure(Gfx g, Vector2 feet, float h, Color c, int facing, float lean, float step, bool sword = false, bool swordUp = false, float rotate = 0)
    {
        Vector2 P(float x, float y)
        {
            var v = new Vector2(x * facing, y);
            if (rotate != 0)
            {
                float cs = MathF.Cos(rotate), sn = MathF.Sin(rotate);
                v = new Vector2((v.X * cs) - (v.Y * sn), (v.X * sn) + (v.Y * cs));
            }
            return feet + v;
        }
        float t = h * 0.1f;
        var hip = P(lean * h * 0.3f, -h * 0.42f);
        var neck = P(lean * h, -h * 0.8f);
        // 足
        g.Line(hip, P((step * h * 0.16f) + (lean * h * 0.1f), 0), c, t * 0.8f);
        g.Line(hip, P((-step * h * 0.16f) + (lean * h * 0.1f), 0), c * 0.85f, t * 0.8f);
        // 胴
        g.Line(hip, neck, c, t * 1.5f);
        // 頭
        g.Circle(P(lean * h * 1.1f, -h * 0.9f), h * 0.085f, c);
        // 腕と剣
        var hand = swordUp ? P((lean * h) + (h * 0.12f), -h * 1.02f) : P((lean * h) + (h * 0.24f), -h * 0.58f);
        g.Line(P(lean * h, -h * 0.76f), hand, c, t * 0.7f);
        if (sword)
        {
            var tip = swordUp ? hand + ((P(0, -h * 0.42f) - P(0, 0)) * 1f) : hand + ((P(h * 0.36f, -h * 0.24f) - P(0, 0)) * 1f);
            g.Line(hand, tip, new Color(230, 235, 245), t * 0.45f);
            g.Line(hand - ((tip - hand) * 0.12f), hand + ((tip - hand) * 0.04f), new Color(200, 160, 70), t * 0.6f);
        }
    }

    /// <summary>勇者の絵を足もと基準で描く（元の絵は右向き）。</summary>
    private void HeroAt(Gfx g, Vector2 feet, float h, Color tint, bool faceLeft = false, float rot = 0)
    {
        if (Hero is null)
        {
            Figure(g, feet, h * 0.8f, tint, faceLeft ? -1 : 1, 0, 0, sword: true, rotate: rot);
            return;
        }
        float sc = h / Hero.Height;
        g.Batch.Draw(Hero, feet, null, tint, rot, new Vector2(Hero.Width / 2f, Hero.Height), sc, faceLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
    }

    /// <summary>敵の絵を足もと基準で描く。ドット絵は点のまま拡大する。</summary>
    private void FoeAt(Gfx g, Vector2 feet, float h, Color tint, Color fallback, float time, float rot = 0)
    {
        var art = Enemy;
        if (art is null)
        {
            Monster(g, feet, h * 0.85f, fallback, time);
            return;
        }
        int fw = art.FrameWidth(false), fh = art.FrameHeight(false);
        int frame = art.IdleFrames > 1 ? (int)(time * 8) % art.IdleFrames : 0;
        var src = new Rectangle(frame * fw, 0, fw, fh);
        float sc = Math.Min(h / fh, 320f / fw);
        var col = new Color(tint.ToVector4() * art.Tint.ToVector4());
        if (art.Pixel)
        {
            g.Batch.End();
            g.Batch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: _clip);
        }
        g.Batch.Draw(art.Idle, feet, src, col, rot, new Vector2(fw / 2f, fh), sc, SpriteEffects.None, 0);
        if (art.Pixel)
        {
            g.Batch.End();
            g.Batch.Begin(rasterizerState: _clip);
        }
    }

    /// <summary>角のある大きな影（敵）。</summary>
    private static void Monster(Gfx g, Vector2 feet, float h, Color c, float time)
    {
        float breathe = MathF.Sin(time * 2) * 4;
        g.Batch.Draw(g.CircleTex, new Rectangle((int)(feet.X - (h * 0.32f)), (int)(feet.Y - (h * 0.78f) - breathe), (int)(h * 0.64f), (int)(h * 0.78f) + (int)breathe), c);
        g.Circle(new Vector2(feet.X, feet.Y - (h * 0.8f) - breathe), h * 0.16f, c);
        // 角
        g.Line(new Vector2(feet.X - (h * 0.1f), feet.Y - (h * 0.9f) - breathe), new Vector2(feet.X - (h * 0.2f), feet.Y - (h * 1.05f) - breathe), c, 10);
        g.Line(new Vector2(feet.X + (h * 0.1f), feet.Y - (h * 0.9f) - breathe), new Vector2(feet.X + (h * 0.2f), feet.Y - (h * 1.05f) - breathe), c, 10);
        // 目
        g.Rect(feet.X - (h * 0.09f), feet.Y - (h * 0.82f) - breathe, 10, 6, new Color(255, 220, 90));
        g.Rect(feet.X + (h * 0.05f), feet.Y - (h * 0.82f) - breathe, 10, 6, new Color(255, 220, 90));
    }

    private void DrawFlask(Gfx g, Challenge c, float time, float floor)
    {
        var a = Area;
        float cx = a.Center.X;
        // 台とアルコールランプ
        g.Rect(new Rectangle(a.X, (int)floor, a.Width, a.Bottom - (int)floor), new Color(60, 40, 30));
        g.Rect(new Rectangle((int)cx - 110, (int)floor - 14, 220, 14), new Color(90, 70, 50));
        var bulb = new Vector2(cx, floor - 110);
        float r = 92;
        // ガラスのふち
        g.Circle(bulb, r + 6, new Color(200, 230, 255) * 0.5f);
        g.Circle(bulb, r, new Color(20, 20, 30));
        g.Rect(new Rectangle((int)cx - 26, (int)(bulb.Y - r - 92), 52, 100), new Color(200, 230, 255) * 0.5f);
        g.Rect(new Rectangle((int)cx - 20, (int)(bulb.Y - r - 92), 40, 100), new Color(20, 20, 30));
        g.Rect(new Rectangle((int)cx - 32, (int)(bulb.Y - r - 100), 64, 12), new Color(150, 100, 60));
        // 液体（ゲージが増えるほど水位が上がる。液面はゆれる）
        var col = c.Lamp == LedColor.Cyan ? new Color(90, 200, 255) : Palette.Of(c.Lamp);
        float level = 0.35f + (0.5f * _shown);
        float surface = bulb.Y + r - (2 * r * level) + (MathF.Sin(time * 8) * 4 * (0.3f + _energy));
        g.Batch.End();
        var dev = g.Device;
        var old = dev.ScissorRectangle;
        dev.ScissorRectangle = Rectangle.Intersect(Area, new Rectangle(a.X, (int)surface, a.Width, a.Bottom - (int)surface));
        var clip = _clip;
        g.Batch.Begin(rasterizerState: clip);
        g.Circle(bulb, r - 8, col * 0.85f);
        g.Rect(new Rectangle((int)cx - 70, (int)surface, 140, 6), Color.White * 0.35f);
        g.Batch.End();
        dev.ScissorRectangle = old;
        g.Batch.Begin(rasterizerState: clip);
        // ガラスの光
        g.Rect(new Rectangle((int)(bulb.X - 58), (int)(bulb.Y - 40), 10, 50), Color.White * 0.35f);
        g.Rect(new Rectangle((int)(bulb.X - 48), (int)(bulb.Y - 60), 10, 14), Color.White * 0.35f);
    }

    private void DrawEye(Gfx g, Challenge c, float time)
    {
        var a = Area;
        var center = new Vector2(a.Center.X, a.Center.Y);
        bool found = c.Lamp == LedColor.Red;
        // まばたき
        float blinkT = time % 3.7f;
        float open = blinkT < 0.12f ? Math.Abs((blinkT / 0.06f) - 1) : 1;
        if (found) open = 1;
        int w = found ? 440 : 400, h = (int)((found ? 210 : 180) * open);
        g.Batch.Draw(g.CircleTex, new Rectangle((int)center.X - (w / 2) - 10, (int)center.Y - (h / 2) - 10, w + 20, h + 20), new Color(10, 0, 4));
        g.Batch.Draw(g.CircleTex, new Rectangle((int)center.X - (w / 2), (int)center.Y - (h / 2), w, h), found ? new Color(255, 210, 200) : new Color(230, 220, 196));
        if (h < 20) return;
        // 血管
        for (int i = 0; i < 4; i++)
        {
            float y = center.Y + ((i - 1.5f) * h * 0.18f);
            g.Line(new Vector2(center.X - (w * 0.45f), y), new Vector2(center.X - (w * 0.28f), y + 6), new Color(200, 60, 60) * 0.5f, 2);
            g.Line(new Vector2(center.X + (w * 0.45f), y), new Vector2(center.X + (w * 0.28f), y - 6), new Color(200, 60, 60) * 0.5f, 2);
        }
        float look = found ? 0 : (MathF.Sin(time * 1.1f) * 110) + (MathF.Sin(time * 2.7f) * 30);
        var iris = new Vector2(center.X + look, center.Y);
        float ir = Math.Min(h * 0.45f, 74);
        g.Circle(iris, ir, found ? new Color(230, 30, 30) : new Color(200, 140, 40));
        g.Circle(iris, ir * 0.75f, found ? new Color(170, 10, 10) : new Color(150, 90, 20));
        g.Rect(new Rectangle((int)iris.X - 8, (int)(iris.Y - (ir * 0.8f)), 16, (int)(ir * 1.6f)), Color.Black);
        g.Circle(iris + new Vector2(-ir * 0.4f, -ir * 0.4f), ir * 0.16f, Color.White * 0.8f);
    }

    private void DrawPendulum(Gfx g, Challenge c, float time)
    {
        var a = Area;
        float cx = a.Center.X;
        // 地面の地層
        float ground = a.Bottom - 70;
        g.Rect(new Rectangle(a.X, (int)ground, a.Width, 24), new Color(70, 50, 30));
        g.Rect(new Rectangle(a.X, (int)ground + 24, a.Width, 24), new Color(54, 38, 22));
        g.Rect(new Rectangle(a.X, (int)ground + 48, a.Width, 30), new Color(40, 28, 16));
        for (int i = 0; i < 18; i++) g.Rect(a.X + ((i * 97) % a.Width), ground + 6 + ((i * 13) % 50), 10, 6, new Color(90, 70, 50));
        // 振り子
        var pivot = new Vector2(cx, a.Y + 18);
        float tilt = double.IsFinite(c.TiltMarker) ? (float)c.TiltMarker : 0;
        float ang = (tilt * 0.75f) + (MathF.Sin(time * 3) * 0.02f);
        float len = 200;
        var bob = pivot + new Vector2(MathF.Sin(ang) * len, MathF.Cos(ang) * len);
        g.Rect(new Rectangle((int)cx - 50, a.Y + 6, 100, 14), new Color(120, 90, 50));
        g.Line(pivot, bob, new Color(220, 200, 150), 3);
        var col = Palette.Of(c.Lamp);
        g.Batch.Draw(g.Pixel, bob + new Vector2(0, 14), null, Color.Black, MathF.PI / 4, new Vector2(0.5f, 0.5f), 40, SpriteEffects.None, 0);
        g.Batch.Draw(g.Pixel, bob + new Vector2(0, 14), null, col, MathF.PI / 4, new Vector2(0.5f, 0.5f), 32, SpriteEffects.None, 0);
        g.Batch.Draw(g.Pixel, bob + new Vector2(-5, 9), null, Color.White * 0.6f, MathF.PI / 4, new Vector2(0.5f, 0.5f), 8, SpriteEffects.None, 0);
        // 振れ幅の目安（弧）
        for (int i = -8; i <= 8; i++)
        {
            float t = i / 8f * 0.75f;
            var p = pivot + new Vector2(MathF.Sin(t) * (len + 50), MathF.Cos(t) * (len + 50));
            g.Rect(p.X - 2, p.Y - 2, 4, 4, Color.White * 0.25f);
        }
    }

    private void DrawBridge(Gfx g, Challenge c, float time)
    {
        var a = Area;
        float cx = a.Center.X;
        // 崖
        g.Rect(new Rectangle(a.X, a.Y + 190, 90, a.Height - 190), new Color(40, 34, 40));
        g.Rect(new Rectangle(a.Right - 90, a.Y + 190, 90, a.Height - 190), new Color(40, 34, 40));
        g.Rect(new Rectangle(a.X, a.Y + 190, 90, 6), new Color(90, 80, 70));
        g.Rect(new Rectangle(a.Right - 90, a.Y + 190, 90, 6), new Color(90, 80, 70));
        float tilt = double.IsFinite(c.TiltMarker) ? (float)c.TiltMarker : 0;
        float ang = tilt * 0.45f;
        var mid = new Vector2(cx, a.Y + 240);
        bool bad = c.Lamp == LedColor.Red;
        // 両側のロープと、まん中のゆれる板
        var left = new Vector2(a.X + 90, a.Y + 192);
        var right = new Vector2(a.Right - 90, a.Y + 192);
        var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        var m0 = mid - (dir * 130);
        var m1 = mid + (dir * 130);
        var rope = new Color(160, 120, 70);
        g.Line(left, m0, rope, 4);
        g.Line(m1, right, rope, 4);
        for (int i = 0; i < 9; i++)
        {
            var p = Vector2.Lerp(m0, m1, i / 8f);
            g.Line(p - (new Vector2(-dir.Y, dir.X) * 2), p + (new Vector2(-dir.Y, dir.X) * 12), bad ? new Color(170, 70, 60) : new Color(130, 90, 50), 26);
        }
        g.Line(m0 - new Vector2(0, 60), m1 - new Vector2(0, 60), rope * 0.8f, 3);
        HeroAt(g, mid + new Vector2(0, 4), 190, bad ? new Color(255, 200, 190) : Color.White, rot: ang);
        _ = time;
    }

    private void DrawFishing(Gfx g, Challenge c, float time)
    {
        var a = Area;
        float water = a.Y + 150;
        // 夕方の空と水面
        g.Rect(new Rectangle(a.X, (int)water, a.Width, a.Bottom - (int)water), new Color(14, 40, 70));
        for (int i = 0; i < 6; i++)
        {
            float y = water + 12 + (i * 26);
            float off = (time * 30 * (i % 2 == 0 ? 1 : -1)) % 80;
            for (float x = a.X - 80 + off; x < a.Right; x += 80) g.Rect(x, y, 30, 3, new Color(120, 170, 220) * 0.3f);
        }
        g.Rect(new Rectangle(a.Right - 160, (int)water + 2, 100, 4), new Color(255, 200, 150) * 0.5f);
        var stage = c is FishingChallenge f ? f.Current : FishingChallenge.Stage.Cast;
        double tension = c is FishingChallenge f2 ? f2.Tension : 0;
        // 竿
        float bend = stage == FishingChallenge.Stage.Fight ? 40 + (MathF.Sin(time * 18) * 10) : 0;
        var butt = new Vector2(a.X + 30, a.Bottom + 10);
        var tip = new Vector2(a.X + 230, a.Y + 40 + bend);
        g.Line(butt, Vector2.Lerp(butt, tip, 0.5f) + new Vector2(0, -bend * 0.2f), new Color(110, 70, 40), 10);
        g.Line(Vector2.Lerp(butt, tip, 0.5f) + new Vector2(0, -bend * 0.2f), tip, new Color(140, 90, 50), 6);
        // うき
        var bobber = stage == FishingChallenge.Stage.Cast
            ? tip + new Vector2(0, 50)
            : new Vector2(a.Center.X + 120, water + (MathF.Sin(time * 2.4f) * 4) + (stage == FishingChallenge.Stage.Fight ? 10 + (MathF.Abs(MathF.Sin(time * 14)) * 14) : 0));
        var line = Color.Lerp(new Color(230, 230, 230), new Color(255, 70, 70), (float)tension);
        g.Line(tip, bobber, line * 0.9f, 2);
        if (stage == FishingChallenge.Stage.Fight)
        {
            // 水の中の大きな影
            float fx = bobber.X + (MathF.Sin(time * 3) * 60);
            g.Batch.Draw(g.CircleTex, new Rectangle((int)fx - 70, (int)water + 70, 140, 40), Color.Black * 0.5f);
            g.Line(new Vector2(fx + 70, water + 90), new Vector2(fx + 100, water + 70 + (MathF.Sin(time * 12) * 10)), Color.Black * 0.5f, 18);
        }
        var cap = Palette.Of(c.Lamp);
        g.Circle(bobber + new Vector2(0, 8), 12, Color.White);
        g.Circle(bobber - new Vector2(0, 4), 12, cap);
        g.Rect(bobber.X - 2, bobber.Y - 28, 4, 16, cap);
    }

    private void DrawIai(Gfx g, Challenge c, float time, bool result, bool success)
    {
        var a = Area;
        float cx = a.Center.X;
        var moon = new Vector2(a.Right - 140, a.Y + 90);
        g.Circle(moon, 62, new Color(250, 244, 220));
        g.Circle(moon + new Vector2(18, -10), 12, new Color(220, 214, 190));
        float floor = a.Bottom - 50;
        // 遠くの山と草原
        for (int i = 0; i < 6; i++)
        {
            float x = a.X + (i * 140) - 40;
            g.Batch.Draw(g.Pixel, new Vector2(x, floor - 10), null, new Color(20, 22, 44), MathF.PI / 4, new Vector2(0.5f, 0.5f), 150, SpriteEffects.None, 0);
        }
        g.Rect(new Rectangle(a.X, (int)floor, a.Width, a.Bottom - (int)floor), new Color(10, 14, 20));
        for (int x = a.X; x < a.Right; x += 16)
        {
            float sway = MathF.Sin((time * 2) + (x * 0.05f)) * 6;
            g.Line(new Vector2(x, floor + 4), new Vector2(x + sway, floor - 14), new Color(30, 50, 40), 3);
        }
        bool signaled = c is IaiChallenge { Signaled: true };
        var sil = new Color(10, 10, 18);
        var hurt = new Color(70, 16, 22);
        if (result && success)
        {
            // 一閃のあと：勇者は敵の向こうへ抜け、敵は崩れる
            FoeAt(g, new Vector2(cx - 60, floor), 230, hurt, hurt, time, rot: -0.22f);
            HeroAt(g, new Vector2(cx + 240, floor), 230, sil, faceLeft: true);
            g.Line(new Vector2(cx - 220, floor - 190), new Vector2(cx + 140, floor - 40), Color.White, 6);
        }
        else if (result)
        {
            HeroAt(g, new Vector2(cx - 170, floor), 230, hurt, rot: -0.45f);
            FoeAt(g, new Vector2(cx + 150, floor), 240, sil, sil, time);
            g.Line(new Vector2(cx - 280, floor - 170), new Vector2(cx - 40, floor - 50), new Color(255, 80, 80), 6);
        }
        else
        {
            // 月明かりのふち（少しずらした明るい形の上に、影を重ねる）
            var rim = new Color(120, 140, 220);
            HeroAt(g, new Vector2(cx - 196, floor - 2), 230, rim);
            FoeAt(g, new Vector2(cx + 204, floor - 2), 240, rim, rim, time);
            HeroAt(g, new Vector2(cx - 200, floor), 230, sil);
            FoeAt(g, new Vector2(cx + 200, floor), 240, sil, sil, time);
            if (signaled)
            {
                g.Rect(cx + 100, floor - 160, 70, 3, Color.White);
                g.Rect(cx + 133, floor - 193, 3, 70, Color.White);
            }
        }
    }

    private void DrawForge(Gfx g, Challenge c, float time)
    {
        var a = Area;
        float cx = a.Center.X;
        float floor = a.Bottom - 34;
        g.Rect(new Rectangle(a.X, (int)floor, a.Width, a.Bottom - (int)floor), new Color(30, 20, 14));
        // 金床
        g.Rect(new Rectangle((int)cx - 50, (int)floor - 60, 100, 60), new Color(50, 50, 58));
        g.Rect(new Rectangle((int)cx - 120, (int)floor - 92, 220, 34), new Color(80, 82, 94));
        g.Rect(new Rectangle((int)cx + 100, (int)floor - 92, 50, 16), new Color(80, 82, 94));
        g.Rect(new Rectangle((int)cx - 120, (int)floor - 92, 250, 4), new Color(150, 152, 165));
        // 焼けた刃
        g.Rect(new Rectangle((int)cx - 90, (int)floor - 106, 180, 14), new Color(255, 150, 60));
        g.Rect(new Rectangle((int)cx - 90, (int)floor - 106, 180, 4), new Color(255, 230, 150));
        var bs = c as BlacksmithChallenge;
        float prog = bs is null ? 0 : (float)bs.StrikeProgress;
        bool done = bs?.RoundDone == true;
        // 拍の印（・・・カーン）
        for (int i = 0; i < 4; i++)
        {
            var p = new Vector2(cx - 120 + (i * 80), a.Y + 40);
            bool lit = prog >= i / 3f;
            var col = i == 3 ? new Color(255, 80, 70) : new Color(110, 180, 255);
            g.Batch.Draw(g.Pixel, p, null, Color.Black, MathF.PI / 4, new Vector2(0.5f, 0.5f), 34, SpriteEffects.None, 0);
            g.Batch.Draw(g.Pixel, p, null, lit ? col : col * 0.25f, MathF.PI / 4, new Vector2(0.5f, 0.5f), 26, SpriteEffects.None, 0);
        }
        // 縮んでくる輪（カーンの瞬間に小さな円と重なる）
        var at = new Vector2(cx, floor - 100);
        if (!done && prog > -0.2f && prog < 1.2f)
        {
            float rr = 30 + (Math.Max(0, 1 - prog) * 150);
            g.Ring(at, rr, 4, (prog >= 0.92f ? new Color(255, 120, 90) : Color.White) * 0.8f, 48);
            g.Ring(at, 30, 2, Color.White * 0.5f, 32);
        }
        // 槌（カーンに向けて振り上げ、打ったら振り下ろす）
        float raise = done ? 0 : Math.Clamp(prog, 0, 1);
        float ang = -0.2f - (raise * 1.1f);
        var pivot = new Vector2(cx + 230, floor - 40);
        var dir = new Vector2(-MathF.Cos(ang), MathF.Sin(ang));
        var head = pivot + (dir * 210);
        g.Line(pivot, head, new Color(120, 80, 50), 14);
        var n = new Vector2(-dir.Y, dir.X);
        g.Line(head - (n * 34), head + (n * 34), new Color(70, 72, 84), 40);
        g.Line(head - (n * 34), head + (n * 34) - (dir * 14), new Color(120, 122, 136), 10);
        _ = time;
    }

    private void DrawChest(Gfx g, Challenge c, float time, bool result, bool success)
    {
        var a = Area;
        float cx = a.Center.X;
        float floor = a.Bottom - 30;
        g.Rect(new Rectangle(a.X, (int)floor, a.Width, a.Bottom - (int)floor), new Color(30, 22, 16));
        var body = new Rectangle((int)cx - 170, (int)floor - 150, 340, 150);
        float lidUp = result && success ? 60 + (MathF.Sin(time * 3) * 4) : 0;
        var lid = new Rectangle(body.X - 10, body.Y - 80 - (int)lidUp, body.Width + 20, 84);
        if (result && success)
        {
            // ふたが開いて光があふれる
            g.Rect(new Rectangle(body.X + 10, body.Y - 20, body.Width - 20, 30), new Color(255, 230, 140));
        }
        g.Rect(new Rectangle(body.X - 4, body.Y - 4, body.Width + 8, body.Height + 8), Color.Black);
        g.Rect(body, new Color(120, 70, 36));
        for (int i = 1; i < 4; i++) g.Rect(body.X, body.Y + (i * 37), body.Width, 3, new Color(90, 50, 24));
        g.Rect(new Rectangle(lid.X - 4, lid.Y - 4, lid.Width + 8, lid.Height + 8), Color.Black);
        g.Rect(lid, new Color(140, 84, 44));
        g.Rect(new Rectangle(lid.X, lid.Y, lid.Width, 10), new Color(170, 110, 60));
        // 金具
        var band = new Color(210, 170, 70);
        foreach (int x in new[] { body.X + 40, body.Right - 64 })
        {
            g.Rect(new Rectangle(x, lid.Y, 24, lid.Height), band);
            g.Rect(new Rectangle(x, body.Y, 24, body.Height), band);
        }
        // 錠前（色の変わる宝石つき）
        var plate = new Rectangle((int)cx - 44, body.Y - 30, 88, 104);
        g.Rect(new Rectangle(plate.X - 4, plate.Y - 4, plate.Width + 8, plate.Height + 8), Color.Black);
        g.Rect(plate, new Color(230, 190, 80));
        g.Rect(new Rectangle(plate.X, plate.Y, plate.Width, 6), new Color(255, 236, 150));
        var gem = new Vector2(cx, plate.Y + 30);
        var col = Palette.Of(c.Lamp);
        g.Batch.Draw(g.Pixel, gem, null, Color.Black, MathF.PI / 4, new Vector2(0.5f, 0.5f), 40, SpriteEffects.None, 0);
        g.Batch.Draw(g.Pixel, gem, null, col, MathF.PI / 4, new Vector2(0.5f, 0.5f), 32, SpriteEffects.None, 0);
        g.Batch.Draw(g.Pixel, gem + new Vector2(-5, -5), null, Color.White * 0.6f, MathF.PI / 4, new Vector2(0.5f, 0.5f), 8, SpriteEffects.None, 0);
        g.Circle(new Vector2(cx, plate.Y + 70), 9, Color.Black);
        g.Rect(cx - 4, plate.Y + 72, 8, 20, Color.Black);
        if (result && !success)
        {
            g.Line(new Vector2(plate.X + 10, plate.Y + 8), new Vector2(plate.X + 46, plate.Y + 56), Color.Black, 4);
            g.Line(new Vector2(plate.X + 46, plate.Y + 56), new Vector2(plate.X + 30, plate.Bottom - 8), Color.Black, 4);
        }
    }
}
