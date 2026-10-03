using Abbild.Core;
using Abbild.Engine;
using Abbild.Ui;
using Microsoft.Xna.Framework;

namespace Abbild.Scenes;

public sealed class GameOverScene(Services s, RunState run) : Scene(s)
{
    private readonly Menu _menu = new() { RowHeight = 66, FontSize = 40 };

    public override void Enter()
    {
        S.Audio.PlayBgm("gameover", loop: false);
        S.Controller.Led(LedColor.Red);
        bool hasSave = S.Store.HasSave(run.Slot);
        _menu.SetItems([new("記録した階からやり直す", hasSave, hasSave ? $"B{run.Floor}F" : ""), new("タイトルへ")], keepIndex: false);
        if (!hasSave) _menu.Index = 1;
    }

    private static Rectangle MenuRect => new(Gfx.Width / 2 - 330, 840, 660, 132);

    private readonly Fx _fx = new();
    private float _ash;
    private const float DropStart = 0.25f, DropEnd = 1.05f, MenuAt = 1.9f;

    private static float ImpactTime => DropStart + ((DropEnd - DropStart) * 0.3636f);

    /// <summary>絵が落ちてきて地面に当たった瞬間だけ、画面が揺れる。</summary>
    public override Vector2 Shake
    {
        get
        {
            float k = Time - ImpactTime;
            if (k < 0 || k > 0.5f) return Vector2.Zero;
            float a = 1 - (k / 0.5f);
            return new Vector2(MathF.Sin(Time * 80) * 22 * a, MathF.Cos(Time * 67) * 14 * a);
        }
    }

    protected override void Update(float dt)
    {
        _fx.Update(dt);
        // 灰が静かに降る
        _ash += dt;
        var r = new Random((int)(Time * 1000));
        while (_ash > 0.06f)
        {
            _ash -= 0.06f;
            _fx.Add(new Particle { Pos = new Vector2(r.Next(0, Gfx.Width), -10), Vel = new Vector2(r.Next(-20, 20), r.Next(30, 70)), Life = 14, Size = r.Next(4, 9), EndSize = 4, Color = new Color(150, 140, 140) * 0.7f, EndColor = new Color(90, 80, 80) * 0.3f, Shape = ParticleShape.Pixel });
        }
        if (Time - dt < ImpactTime && Time >= ImpactTime)
        {
            _fx.Sparks(new Vector2(Gfx.Width / 2f, 640), 40, new Color(255, 120, 80), 700);
            for (int n = 0; n < 24; n++)
            {
                _fx.Add(new Particle { Pos = new Vector2(r.Next(500, 1420), 650), Vel = new Vector2(r.Next(-200, 200), r.Next(-160, -40)), Life = 1.2f, Size = r.Next(40, 80), EndSize = 160, Color = new Color(80, 70, 70) * 0.5f, EndColor = new Color(40, 30, 30) * 0f, Shape = ParticleShape.Glow });
            }
        }
        if (Time < MenuAt) return;
        int i = _menu.Update(S, MenuRect);
        if (i == 0)
        {
            var again = S.Store.LoadRun(run.Slot);
            if (again is not null) S.Game.Scenes.Go(new DungeonScene(S, again, fromSave: true), 0.8f);
            else S.Game.Scenes.Go(new TitleScene(S), 0.8f);
        }
        else if (i == 1)
        {
            S.Game.Scenes.Go(new TitleScene(S), 0.8f);
        }
    }

    public override void Draw()
    {
        var g = G;
        g.Batch.Begin();
        var tex = S.Assets.Background("gameover");
        g.Rect(new Rectangle(0, 0, Gfx.Width, Gfx.Height), Color.Black);
        // 絵が上から落ちてきて、ドスンと止まる（そのあとはゆっくり近づく）
        float drop = Ease.OutBounce(Ease.Span(Time, DropStart, DropEnd));
        float a = Math.Min(1, Time / 0.4f);
        float sc = 650f / tex.Height * (1 + (Math.Max(0, Time - DropEnd) * 0.004f));
        var pos = new Vector2(Gfx.Width / 2f, 20 + (650 / 2f) - ((1 - drop) * 760));
        g.Batch.Draw(tex, pos, null, Color.White * a, 0, new Vector2(tex.Width / 2f, tex.Height / 2f), sc, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0);
        g.Batch.End();
        g.Batch.Begin();
        _fx.DrawNormal(g);
        g.Batch.End();
        g.Batch.Begin(blendState: Microsoft.Xna.Framework.Graphics.BlendState.Additive);
        _fx.DrawAdditive(g);
        g.Batch.End();
        g.Batch.Begin();
        Art.EdgeGlow(g, new Color(120, 0, 0), 0.35f + (0.1f * MathF.Sin(Time * 2)));
        var h = run.Hero;
        float t1 = Ease.Span(Time, 1.15f, 1.6f), t2 = Ease.Span(Time, 1.4f, 1.85f);
        g.TextCentered($"{h.Name} は 地下 {run.Floor} 階で力尽きた…", new Vector2(Gfx.Width / 2f, 712 + ((1 - Ease.OutCubic(t1)) * 20)), 44, Color.White * t1);
        g.TextCentered($"Lv {h.Level}　倒した敵 {run.BattlesWon}　冒険の時間 {TimeSpan.FromSeconds(run.PlaySeconds):h\\:mm\\:ss}", new Vector2(Gfx.Width / 2f, 770), 30, Palette.Dim * t2);
        if (Time >= MenuAt)
        {
            _menu.Reveal = Time - MenuAt;
            _menu.Draw(g, MenuRect, Time);
        }
        g.Batch.End();
    }
}

public sealed class EndingScene(Services s, RunState run) : Scene(s)
{
    private string[] _credits = [];
    private readonly Fx _fx = new();
    private float _spawn;
    private float _thanks = -1;

    public override void Enter()
    {
        S.Audio.PlayBgm("ending");
        S.Controller.Led(LedColor.White);
        S.Controller.Oled(OledAnim.Win);
        var h = run.Hero;
        var head = new List<string>
        {
            "― 迷宮踏破 ―",
            "",
            $"{h.Name}（Lv {h.Level}・{Names.Of(run.Difficulty)}）",
            $"冒険の時間　{TimeSpan.FromSeconds(run.PlaySeconds):h\\:mm\\:ss}",
            $"倒した敵　{run.BattlesWon}　開けた宝箱　{run.TreasuresOpened}　蘇生　{h.RevivesUsed} 回",
            "",
            "",
        };
        _credits = [.. head, .. CreditsScene.LoadCredits(S)];
    }

    protected override void Update(float dt)
    {
        _fx.Update(dt);
        _spawn += dt;
        var r = new Random((int)(Time * 1000));
        while (_spawn > 0.08f)
        {
            _spawn -= 0.08f;
            // 夕日の中を舞う光の粒
            _fx.Add(new Particle { Pos = new Vector2(r.Next(0, Gfx.Width), Gfx.Height + 10), Vel = new Vector2(r.Next(-20, 20), r.Next(-70, -30)), Life = 12, Size = r.Next(5, 10), EndSize = 4, Color = new Color(255, 220, 150) * 0.9f, EndColor = new Color(255, 160, 90) * 0.2f, Shape = ParticleShape.Pixel, Additive = true });
        }
        if (_thanks < 0 && ScrollY() + (_credits.Length * 54) < 200) _thanks = Time;
        if (Time > 2.5f && (In.Pressed(Act.Confirm) || In.Pressed(Act.Cancel) || In.MouseClicked) && ScrollY() < -(_credits.Length * 54) + 200)
        {
            S.Game.Scenes.Go(new TitleScene(S), 1.2f);
        }
        else if (Time > 3f && In.Pressed(Act.Menu))
        {
            S.Game.Scenes.Go(new TitleScene(S), 1.2f);
        }
    }

    private float ScrollY() => Gfx.Height + 40 - (Math.Max(0, Time - 4f) * 70f);

    public override void Draw()
    {
        var g = G;
        g.Batch.Begin();
        Art.Cover(g, S.Assets.Background("end"), 1.0f + (Time * 0.0015f), default, Color.White * Math.Min(1, Time / 2f));
        g.Batch.End();
        g.Batch.Begin(blendState: Microsoft.Xna.Framework.Graphics.BlendState.Additive);
        g.Glow(new Vector2(Gfx.Width * 0.45f, Gfx.Height * 0.58f), 520 + (30 * MathF.Sin(Time * 0.8f)), new Color(255, 170, 90) * (0.22f * Math.Min(1, Time / 2f)));
        _fx.DrawAdditive(g);
        g.Batch.End();
        g.Batch.Begin();
        if (Time > 4f)
        {
            g.Rect(new Rectangle(0, 0, Gfx.Width, Gfx.Height), Color.Black * Math.Min(0.6f, (Time - 4f) / 3f));
            float y = ScrollY();
            foreach (var line in _credits)
            {
                if (y > -60 && y < Gfx.Height + 60)
                {
                    bool head = line.StartsWith('―') || line.StartsWith('【');
                    // 画面の上下のふちでは、ふわっと消える
                    float edge = Math.Clamp(MathF.Min(y - 40, Gfx.Height - 40 - y) / 160f, 0, 1);
                    g.TextCentered(line, new Vector2(Gfx.Width / 2f, y), head ? 44 : 34, (head ? Palette.Gold : Color.White) * edge, bold: true);
                }
                y += 54;
            }
            if (y < 200)
            {
                float tk = _thanks < 0 ? 0 : Time - _thanks;
                g.TextFx("Thank you for playing!", new Vector2(Gfx.Width / 2f, Gfx.Height / 2f), 72, Palette.Gold * Math.Min(1, tk * 3), 0.6f + (0.4f * Ease.OutBack(tk / 0.6f)));
                g.TextCentered($"決定（{In.ConfirmLabel}）でタイトルへ", new Vector2(Gfx.Width / 2f, (Gfx.Height / 2f) + 100), 32, Palette.Dim);
            }
        }
        g.Batch.End();
    }
}

/// <summary>文章を読む画面（クレジット・あそびかた）。</summary>
public sealed class CreditsScene(Services s, string file = "credits.txt", string? title = null) : Scene(s)
{
    private string[] _lines = [];
    private float _scroll;

    public static string[] LoadCredits(Services s) => LoadText(s, "credits.txt");

    public static string[] LoadText(Services s, string file)
    {
        string path = Path.Combine(s.Assets.Root, file);
        return File.Exists(path) ? File.ReadAllLines(path) : ["Abbild", "Haruki Takahashi"];
    }

    public override void Enter()
    {
        S.Audio.PlayBgm("title");
        var lines = LoadText(S, file).ToList();
        if (title is not null) lines.InsertRange(0, [$"― {title} ―", ""]);
        _lines = [.. lines];
    }

    protected override void Update(float dt)
    {
        if (In.Held(Act.Down)) _scroll += dt * 600;
        if (In.Held(Act.Up)) _scroll -= dt * 600;
        _scroll -= In.Wheel * 80;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, (_lines.Length * 46) - 760));
        if (In.Pressed(Act.Cancel) || In.Pressed(Act.Confirm) || In.MouseRightClicked)
        {
            S.Cue(Cue.Cancel);
            S.Game.Scenes.Go(new TitleScene(S, quick: true));
        }
    }

    public override void Draw()
    {
        var g = G;
        g.Batch.Begin();
        Art.Cover(g, S.Assets.Background("dungeon"), 1.05f, default, new Color(70, 70, 85));
        var r = new Rectangle(260, 80, 1400, 900);
        g.Window(r, 0.95f);
        float y = r.Y + 50 - _scroll;
        foreach (var line in _lines)
        {
            if (y > r.Y + 10 && y < r.Bottom - 50)
            {
                bool head = line.StartsWith('【') || line.StartsWith('―');
                g.Text(line, new Vector2(r.X + 70, y), head ? 38 : 30, head ? Palette.Gold : Palette.Text);
            }
            y += 46;
        }
        Hints.Draw(g, In, ("↑↓", "スクロール"), Hints.Cancel(In));
        g.Batch.End();
    }
}
