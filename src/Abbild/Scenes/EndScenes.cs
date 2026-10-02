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
        bool hasSave = S.Store.HasSave;
        _menu.SetItems([new("記録した階からやり直す", hasSave, hasSave ? $"B{run.Floor}F" : ""), new("タイトルへ")], keepIndex: false);
        if (!hasSave) _menu.Index = 1;
    }

    private static Rectangle MenuRect => new(Gfx.Width / 2 - 330, 840, 660, 132);

    protected override void Update(float dt)
    {
        if (Time < 1.2f) return;
        int i = _menu.Update(S, MenuRect);
        if (i == 0)
        {
            var save = S.Store.LoadSave();
            if (save is not null) S.Game.Scenes.Go(new DungeonScene(S, save.ToRun(), fromSave: true), 0.8f);
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
        float a = Math.Min(1, Time / 1.5f);
        var tex = S.Assets.Background("gameover");
        g.Rect(new Rectangle(0, 0, Gfx.Width, Gfx.Height), Color.Black);
        // 絵は上に収めて、下に文字を置く
        float sc = 650f / tex.Height * (1 + (Time * 0.002f));
        g.Batch.Draw(tex, new Vector2(Gfx.Width / 2f, 20 + (650 / 2f)), null, Color.White * a, 0, new Vector2(tex.Width / 2f, tex.Height / 2f), sc, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0);
        var h = run.Hero;
        g.TextCentered($"{h.Name} は 地下 {run.Floor} 階で力尽きた…", new Vector2(Gfx.Width / 2f, 712), 44, Color.White * a);
        g.TextCentered($"Lv {h.Level}　倒した敵 {run.BattlesWon}　冒険の時間 {TimeSpan.FromSeconds(run.PlaySeconds):h\\:mm\\:ss}", new Vector2(Gfx.Width / 2f, 770), 30, Palette.Dim * a);
        if (Time >= 1.2f) _menu.Draw(g, MenuRect, Time);
        g.Batch.End();
    }
}

public sealed class EndingScene(Services s, RunState run) : Scene(s)
{
    private string[] _credits = [];

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
        Art.Cover(g, S.Assets.Background("end"), 1.0f, default, Color.White * Math.Min(1, Time / 2f));
        if (Time > 4f)
        {
            g.Rect(new Rectangle(0, 0, Gfx.Width, Gfx.Height), Color.Black * Math.Min(0.6f, (Time - 4f) / 3f));
            float y = ScrollY();
            foreach (var line in _credits)
            {
                if (y > -60 && y < Gfx.Height + 60)
                {
                    bool head = line.StartsWith('―') || line.StartsWith('【');
                    g.TextCentered(line, new Vector2(Gfx.Width / 2f, y), head ? 44 : 34, head ? Palette.Gold : Color.White, bold: true);
                }
                y += 54;
            }
            if (y < 200)
            {
                g.TextCentered("Thank you for playing!", new Vector2(Gfx.Width / 2f, Gfx.Height / 2f), 72, Palette.Gold, bold: true);
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
            S.Game.Scenes.Go(new TitleScene(S));
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
