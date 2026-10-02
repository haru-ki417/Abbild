using Abbild.Controller;
using Abbild.Core;
using Abbild.Engine;
using Abbild.Ui;
using Microsoft.Xna.Framework;

namespace Abbild.Scenes;

public sealed class TitleScene(Services s) : Scene(s)
{
    private readonly Menu _menu = new() { RowHeight = 70, FontSize = 42 };
    private readonly Menu _confirm = new() { RowHeight = 64, FontSize = 40 };
    private SaveData? _save;
    private bool _asking;

    public override void Enter()
    {
        S.Audio.PlayBgm("title");
        _save = S.Store.HasSave ? SafeLoad() : null;
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
        _confirm.SetItems([new("いいえ"), new("はい（上書きする）")], keepIndex: false);
        _asking = false;
    }

    private SaveData? SafeLoad()
    {
        try { return S.Store.LoadSave(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static Rectangle MenuArea => new(Gfx.Width / 2 - 280, 570, 560, 70 * 6);

    private static Rectangle ConfirmArea => new(Gfx.Width / 2 - 300, 640, 600, 128);

    protected override void Update(float dt)
    {
        if (_asking)
        {
            int c = _confirm.Update(S, ConfirmArea);
            if (In.Pressed(Act.Cancel)) { S.Cue(Cue.Cancel); _asking = false; }
            else if (c == 0) _asking = false;
            else if (c == 1) S.Game.Scenes.Go(new CreateScene(S));
            return;
        }
        int i = _menu.Update(S, MenuArea);
        switch (i)
        {
            case 0:
                if (_save is not null) { _asking = true; _confirm.Index = 0; }
                else S.Game.Scenes.Go(new CreateScene(S));
                break;
            case 1 when _save is not null:
                S.Game.Scenes.Go(new DungeonScene(S, _save.ToRun(), fromSave: true));
                break;
            case 2:
                S.Game.Scenes.Go(new CreditsScene(S, "howto.txt", "あそびかた"));
                break;
            case 3:
                S.Game.Scenes.Go(new SettingsScene(S, () => new TitleScene(S)));
                break;
            case 4:
                S.Game.Scenes.Go(new CreditsScene(S));
                break;
            case 5:
                S.Game.Exit();
                break;
        }
    }

    public override void Draw()
    {
        var g = G;
        g.Batch.Begin();
        float zoom = 1.04f + (0.03f * MathF.Sin(Time * 0.15f));
        Art.Cover(g, S.Assets.Background("dungeon"), zoom, new Vector2(MathF.Sin(Time * 0.1f) * 12, 0), new Color(170, 170, 185));
        Art.Vignette(g, 0.75f, 0.85f);

        // ロゴ
        float appear = Ease.OutCubic(Time / 1.2f);
        var logoPos = new Vector2(Gfx.Width / 2f, 235 - ((1 - appear) * 40));
        g.Glow(logoPos, 520, Palette.Gold * 0.12f * appear);
        g.TextCentered("Abbild", logoPos, 190, Palette.Gold * appear, bold: true);
        g.TextCentered("～ 古の迷宮と勇者の魂 ～", new Vector2(Gfx.Width / 2f, 385), 44, Color.White * appear, bold: true);

        if (_asking)
        {
            var r = new Rectangle(Gfx.Width / 2 - 420, 520, 840, 300);
            g.Window(r);
            g.TextCentered("冒険の書を上書きして、はじめから始めますか？", new Vector2(r.Center.X, r.Y + 64), 36, Color.White);
            _confirm.Draw(g, ConfirmArea, Time);
        }
        else
        {
            var mr = MenuArea;
            g.Window(new Rectangle(mr.X - 30, mr.Y - 24, mr.Width + 60, mr.Height + 48), 0.92f);
            _menu.Draw(g, mr, Time);
        }

        // 下の情報
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
        g.TextRight("ver 1.0.0  © 2026 Haruki Takahashi", new Vector2(Gfx.Width - 40, Gfx.Height - 56), 26, Palette.Dim);
        g.Batch.End();
    }
}
