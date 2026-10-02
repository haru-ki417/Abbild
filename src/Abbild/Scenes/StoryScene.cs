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
        float alpha = Math.Min(1, _lineTime / 0.5f);
        var lines2 = g.Wrap(_tw.Visible, 44, 1400);
        float total = lines2.Count * 44 * 1.6f;
        float y = (Gfx.Height / 2f) - (total / 2);
        foreach (var l in lines2)
        {
            var m = g.Measure(l, 44);
            g.Text(l, new Vector2((Gfx.Width - m.X) / 2, y), 44, new Color(232, 232, 240) * alpha);
            y += 44 * 1.6f;
        }
        if (_tw.Done)
        {
            float blink = 0.5f + (0.5f * MathF.Sin(Time * 5));
            g.TextCentered("▼", new Vector2(Gfx.Width / 2f, (Gfx.Height / 2f) + (total / 2) + 50), 32, Palette.Gold * blink);
        }
        g.Text($"{_index + 1} / {lines.Count}", new Vector2(40, Gfx.Height - 56), 26, Palette.Dim);
        Hints.Draw(g, In, Hints.Confirm(In, "すすむ"), (In.MenuLabel, "とばす"));
        g.Batch.End();
    }
}
