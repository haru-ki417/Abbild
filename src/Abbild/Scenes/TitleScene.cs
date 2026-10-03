using Abbild.Controller;
using Abbild.Core;
using Abbild.Engine;
using Abbild.Ui;
using Microsoft.Xna.Framework;

namespace Abbild.Scenes;

/// <summary>タイトル。quick なら（設定やクレジットから戻ったとき）出てくる演出を飛ばす。</summary>
public sealed class TitleScene(Services s, bool quick = false) : Scene(s)
{
    private readonly Menu _menu = new() { RowHeight = 70, FontSize = 42 };
    private SaveData? _save;

    public override void Enter()
    {
        S.Audio.PlayBgm("title");
        Logo.Get(G);
        // いちばん新しく記録した冒険の書を、「つづきから」の横に出す
        _save = SafeLoad();
        string cont = _save is null ? "" : $"B{_save.Floor}F  Lv{_save.Hero.Level}";
        _menu.SetItems(
        [
            new("はじめから"),
            new("つづきから", _save is not null, cont),
            new("あそびかた"),
            new("せってい"),
            new("クレジット"),
            new("おわる"),
        ], keepIndex: false);
        if (_save is not null) _menu.Index = 1;
    }

    private SaveData? SafeLoad()
    {
        try
        {
            int latest = S.Store.LatestSlot();
            return latest > 0 ? S.Store.LoadSave(latest) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static Rectangle MenuArea => new(Gfx.Width / 2 - 260, 500, 520, 70 * 6);


    /// <summary>出てからの演出の時間。決定ボタンで最後まで飛ばせる。</summary>
    private float _intro = quick ? MenuAt + 0.5f : 0f;
    private int _landed;

    private const float MenuAt = 1.35f;

    protected override void Update(float dt)
    {
        UpdateFx(dt);
        float before = _intro;
        _intro += dt;
        if (before < MenuAt && (In.Pressed(Act.Confirm) || In.MouseClicked))
        {
            _intro = MenuAt + 0.4f;
            return;
        }
        // ロゴの文字が着地するたびに、火花と小さな揺れ
        while (_landed < Logo.LetterCount && _intro >= Logo.LandTime(_landed))
        {
            if (before < Logo.LandTime(_landed))
            {
                var foot = Logo.LetterFoot(_landed, LogoPos);
                _fx.Sparks(foot, 14, new Color(255, 220, 140), 380);
                _fx.Add(new Particle { Pos = foot, Size = 20, EndSize = 120, Life = 0.4f, Color = new Color(255, 230, 160) * 0.6f, EndColor = new Color(255, 230, 160) * 0f, Shape = ParticleShape.Ring, Additive = true });
                S.Cue(Cue.Tick, 0.5f);
            }
            _landed++;
        }
        // メニューが見えるまでは選べない（前の画面の連打で選んでしまわないように）
        if (_intro < MenuAt) return;
        int i = _menu.Update(S, MenuArea);
        switch (i)
        {
            case 0:
                S.Game.Scenes.Go(new SlotScene(S, continueMode: false));
                break;
            case 1 when _save is not null:
                S.Game.Scenes.Go(new SlotScene(S, continueMode: true));
                break;
            case 2:
                S.Game.Scenes.Go(new CreditsScene(S, "howto.txt", "あそびかた"));
                break;
            case 3:
                S.Game.Scenes.Go(new SettingsScene(S, () => new TitleScene(S, quick: true)));
                break;
            case 4:
                S.Game.Scenes.Go(new CreditsScene(S));
                break;
            case 5:
                S.Game.Exit();
                break;
        }
    }

    private readonly Fx _fx = new();
    private float _spawn;

    private void UpdateFx(float dt)
    {
        _fx.Update(dt);
        _spawn += dt;
        var r = new Random((int)(Time * 1000));
        while (_spawn > 0.05f)
        {
            _spawn -= 0.05f;
            // 火の粉
            _fx.Add(new Particle
            {
                Pos = new Vector2(r.Next(0, Gfx.Width), Gfx.Height + 10),
                Vel = new Vector2(r.Next(-30, 30), r.Next(-160, -60)),
                Life = 4 + (float)r.NextDouble() * 4,
                Size = r.Next(6, 12),
                EndSize = 4,
                Color = new Color(255, 210, 120),
                EndColor = new Color(200, 60, 20) * 0.3f,
                Shape = ParticleShape.Pixel,
                Additive = true,
            });
            // 霧
            if (r.Next(6) == 0)
            {
                _fx.Add(new Particle
                {
                    Pos = new Vector2(r.Next(-200, Gfx.Width), r.Next(700, 1100)),
                    Vel = new Vector2(r.Next(10, 40), 0),
                    Life = 10,
                    Size = r.Next(200, 360),
                    EndSize = 420,
                    Color = new Color(120, 130, 170) * 0.10f,
                    EndColor = new Color(120, 130, 170) * 0.0f,
                    Shape = ParticleShape.Glow,
                    Additive = true,
                });
            }
        }
    }

    public override void Draw()
    {
        var g = G;
        var b = g.Batch;
        b.Begin();
        float zoom = 1.06f + (0.03f * MathF.Sin(Time * 0.15f));
        Art.Cover(g, S.Assets.Background("dungeon"), zoom, new Vector2(MathF.Sin(Time * 0.1f) * 12, 0), new Color(120, 120, 140));
        Art.Vignette(g, 0.8f, 0.9f);

        // 勇者ふたり（キーアート）
        float appear = Ease.OutCubic(Time / 1.4f);
        DrawHero(g, Gender.Female, new Vector2(360, Gfx.Height + 40), appear, false);
        DrawHero(g, Gender.Male, new Vector2(Gfx.Width - 360, Gfx.Height + 40), appear, true);
        b.End();

        b.Begin(blendState: Microsoft.Xna.Framework.Graphics.BlendState.Additive);
        // 天井のすき間から差しこむ光の筋（ゆっくり揺れる）
        var top = new Vector2(Gfx.Width / 2f, -160);
        for (int i = 0; i < 7; i++)
        {
            float ang = (MathF.PI / 2) + ((i - 3) * 0.16f) + (MathF.Sin((Time * 0.25f) + (i * 1.7f)) * 0.04f);
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            float flick = 0.75f + (0.25f * MathF.Sin((Time * 0.7f) + (i * 2.3f)));
            g.Batch.Draw(g.GlowTex, top + (dir * 620), null, new Color(255, 220, 160) * (0.07f * flick), ang, new Vector2(64, 64), new Vector2(10f, 0.55f + (0.15f * (i % 2))), Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0);
        }
        _fx.DrawAdditive(g);
        b.End();

        b.Begin();
        // ロゴ（1 文字ずつ落ちてくる）
        var logoPos = LogoPos;
        g.Glow(logoPos, 600, new Color(255, 180, 60) * (0.16f * Ease.Span(_intro, 0.6f, 1.4f)));
        Logo.Draw(g, logoPos, Time, 1f, 1f, _intro);
        // サブタイトル（両側に飾りの線）
        string sub = "古の迷宮と勇者の魂";
        var sm = g.Measure(sub, 40);
        float sy = 368;
        float subK = Ease.Span(_intro, 0.9f, 1.3f);
        g.TextCentered(sub, new Vector2(Gfx.Width / 2f, sy), 40, new Color(240, 230, 210) * subK, bold: true);
        appear = subK;
        float lw = 180 * Ease.OutCubic(subK);
        float lx0 = (Gfx.Width / 2f) - (sm.X / 2) - 30;
        float lx1 = (Gfx.Width / 2f) + (sm.X / 2) + 30;
        g.Rect(lx0 - lw, sy - 2, lw, 3, Palette.Frame * appear);
        g.Rect(lx1, sy - 2, lw, 3, Palette.Frame * appear);
        g.Rect(lx0 - 6, sy - 6, 10, 10, Palette.Gold * appear);
        g.Rect(lx1 - 4, sy - 6, 10, 10, Palette.Gold * appear);

        {
            var mr = MenuArea;
            var wr = new Rectangle(mr.X - 30, mr.Y - 24, mr.Width + 60, mr.Height + 48);
            float open = Ease.Span(_intro, MenuAt - 0.25f, MenuAt);
            if (open > 0)
            {
                g.Window(open < 1 ? Gfx.Opening(wr, open) : wr, 0.9f);
                _menu.Reveal = _intro - MenuAt;
                if (open >= 1) _menu.Draw(g, mr, Time);
            }
        }

        // 下の情報（勇者の足元に重なっても読めるよう、下に暗い帯を敷く）
        for (int i = 0; i < 12; i++)
        {
            g.Rect(0, Gfx.Height - 120 + (i * 10), Gfx.Width, 10, Color.Black * (0.06f * (i + 1)));
        }
        var link = S.Controller.Link;
        string ctl = S.Controller.Active
            ? $"♥ 自作コントローラー：{link.PortName} で接続中"
            : link.State == LinkState.Searching ? "コントローラーを探しています…" : "キーボード・ゲームパッドで遊べます（自作コントローラーにも対応）";
        g.Text(ctl, new Vector2(40, Gfx.Height - 56), 28, S.Controller.Active ? Palette.Good : Palette.Dim);
        if (S.Records.BestFloor > 0)
        {
            string rec = S.Records.Clears > 0 ? $"踏破 {S.Records.Clears} 回" : $"最高到達 B{S.Records.BestFloor}F";
            g.TextRight(rec, new Vector2(Gfx.Width - 40, 30), 28, Palette.Gold);
        }
        g.TextRight("ver 1.1.0  © 2026 Haruki Takahashi", new Vector2(Gfx.Width - 40, Gfx.Height - 56), 26, Palette.Dim);
        b.End();
    }

    private static Vector2 LogoPos => new(Gfx.Width / 2f, 220);

    private void DrawHero(Gfx g, Gender gender, Vector2 feet, float appear, bool flip)
    {
        var tex = S.Assets.Hero(gender);
        float sc = 720f / tex.Height;
        float breathe = 1 + (0.008f * MathF.Sin((Time * 1.6f) + (flip ? 1 : 0)));
        var origin = new Vector2(tex.Width / 2f, tex.Height);
        var pos = feet + new Vector2((feet.X < Gfx.Width / 2f ? -1 : 1) * (1 - appear) * 120, 0);
        // 後ろの光
        g.Glow(pos - new Vector2(0, 360), 420, new Color(80, 120, 255) * (0.18f * appear));
        var fx = flip ? Microsoft.Xna.Framework.Graphics.SpriteEffects.FlipHorizontally : Microsoft.Xna.Framework.Graphics.SpriteEffects.None;
        g.Batch.Draw(tex, pos, null, new Color(150, 160, 190) * appear, 0, origin, new Vector2(sc, sc * breathe), fx, 0);
    }
}
