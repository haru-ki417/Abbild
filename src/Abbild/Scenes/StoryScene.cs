using Abbild.Core;
using Abbild.Engine;
using Abbild.Ui;
using Microsoft.Xna.Framework;

namespace Abbild.Scenes;

/// <summary>プロローグ・エピローグなど、文章を 1 枚ずつ見せる画面。</summary>
public sealed class StoryScene(Services s, IReadOnlyList<string> lines, string? background, string? bgm, Func<Scene> next) : Scene(s)
{
    private readonly Typewriter _tw = new();
    private int _index;
    private float _lineTime;
    private bool _leaving;
    private readonly Fx _fx = new();
    private float _dust;

    public static readonly string[] Prologue =
    [
        "古の時代より、この地の地下には「勇者の魂」が眠ると言われている。",
        "しかし、その迷宮は邪悪な魔物に支配され、\n生きて帰った者は一人もいない。",
        "あなたは王の命を受け、単身この迷宮へと足を踏み入れた。",
        "手にあるのは、不思議な力が宿るコントローラーのみ…",
        "鼓動も、息づかいも、その手の震えも、すべてが力になる。",
        "地下 100 階に潜むという「深淵の主」を倒すため、\n今、孤独な戦いが始まる。",
    ];

    public static readonly string[] Epilogue =
    [
        "激しい光と共に、魔王の体は崩れ去っていった…",
        "「見事だ… 人間よ…」",
        "迷宮を覆っていた暗雲が晴れ、天井の隙間から朝日が差し込んでくる。",
        "長きにわたる戦いは、ついに終わったのだ。",
        "あなたは深く息を吐き、剣を鞘に収めた。",
        "さあ、地上へ帰ろう。英雄の帰還だ！",
    ];

    public override void Enter()
    {
        S.Audio.PlayBgm(bgm);
        _tw.Set(lines[0]);
        S.Controller.Led(LedColor.Blue);
    }

    protected override void Update(float dt)
    {
        _fx.Update(dt);
        _dust += dt;
        var r = new Random((int)(Time * 1000));
        while (_dust > 0.12f)
        {
            _dust -= 0.12f;
            _fx.Add(new Particle { Pos = new Vector2(r.Next(0, Gfx.Width), r.Next(0, Gfx.Height)), Vel = new Vector2(r.Next(-12, 12), r.Next(-14, -4)), Life = 6, Size = r.Next(4, 8), EndSize = 4, Color = new Color(255, 230, 180) * 0.5f, EndColor = new Color(255, 200, 140) * 0f, Shape = ParticleShape.Pixel, Additive = true });
        }
        if (_leaving) return;
        _lineTime += dt;
        _tw.Update(dt, S.TextCps * 0.6f);
        if (In.Pressed(Act.Menu) && Time > 0.5f)
        {
            Leave(true);
            return;
        }
        if (In.Pressed(Act.Confirm) || In.MouseClicked || S.Controller.Sensors.TakeEvents().A)
        {
            if (!_tw.Done)
            {
                _tw.Finish();
                return;
            }
            _index++;
            S.Cue(Cue.Tick, 0.6f);
            if (_index >= lines.Count) Leave(false);
            else
            {
                _tw.Set(lines[_index]);
                _lineTime = 0;
            }
        }
    }

    private void Leave(bool skipped)
    {
        _ = skipped;
        _leaving = true;
        S.Game.Scenes.Go(next(), 0.8f);
    }

    public override void Draw()
    {
        var g = G;
        g.Batch.Begin();
        g.Rect(new Rectangle(0, 0, Gfx.Width, Gfx.Height), Color.Black);
        if (background is not null)
        {
            float k = Math.Min(1, Time / 2f);
            Art.Cover(g, S.Assets.Background(background), 1.06f - (Time * 0.002f), default, Color.White * (0.22f * k));
        }
        g.Batch.End();
        g.Batch.Begin(blendState: Microsoft.Xna.Framework.Graphics.BlendState.Additive);
        _fx.DrawAdditive(g);
        g.Batch.End();
        g.Batch.Begin();
        // 映画のような上下の黒い帯
        float bar = 110 * Ease.OutCubic(Time / 0.8f);
        g.Rect(new Rectangle(0, 0, Gfx.Width, (int)bar), Color.Black);
        g.Rect(new Rectangle(0, Gfx.Height - (int)bar, Gfx.Width, (int)bar), Color.Black);

        // 文章：行全体の幅で中央にそろえ、1 文字ずつ下からふわっと出す
        var lines2 = g.Wrap(_tw.Text, 44, 1400);
        float total = lines2.Count * 44 * 1.6f;
        float y = (Gfx.Height / 2f) - (total / 2);
        float shown = _tw.Shown;
        int index = 0;
        foreach (var l in lines2)
        {
            var m = g.Measure(l, 44);
            float x = (Gfx.Width - m.X) / 2;
            foreach (char ch in l)
            {
                // 改行の文字は折り返しで消えるので、数えるときに飛ばす
                while (index < _tw.Text.Length && _tw.Text[index] == '\n') index++;
                float a = Math.Clamp((shown - index) / 3f, 0, 1);
                string cs = ch.ToString();
                float w = g.Measure(cs, 44).X;
                if (a > 0) g.Text(cs, new Vector2(x, y + ((1 - a) * 10)), 44, new Color(232, 232, 240) * a);
                x += w;
                index++;
            }
            y += 44 * 1.6f;
        }
        if (_tw.Done)
        {
            float blink = 0.5f + (0.5f * MathF.Sin(Time * 5));
            g.TextCentered("▼", new Vector2(Gfx.Width / 2f, (Gfx.Height / 2f) + (total / 2) + 50), 32, Palette.Gold * blink);
        }
        // 何枚目か（小さなひし形を並べる）
        for (int i = 0; i < lines.Count; i++)
        {
            var p = new Vector2(60 + (i * 28), Gfx.Height - 40);
            var c = i < _index ? Palette.Frame : i == _index ? Palette.Gold : Palette.Disabled * 0.6f;
            g.Batch.Draw(g.Pixel, p, null, c, MathF.PI / 4, new Vector2(0.5f, 0.5f), i == _index ? 12 : 8, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0);
        }
        Hints.Draw(g, In, Hints.Confirm(In, "すすむ"), (In.MenuLabel, "とばす"));
        g.Batch.End();
    }
}
