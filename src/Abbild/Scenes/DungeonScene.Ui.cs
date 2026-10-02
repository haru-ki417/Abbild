using Abbild.Core;
using Abbild.Engine;
using Abbild.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Abbild.Scenes;

public enum ChoiceKind { Attack, Skill, Item }

public sealed record PlayerChoice(ChoiceKind Kind, SkillId Skill = SkillId.Fire, Item? Item = null);

public sealed partial class DungeonScene
{
    private enum Pane { Main, Skills, Items, Status, System, ConfirmTitle }

    private Pane _pane = Pane.Main;
    private readonly Menu _main = new() { RowHeight = 62, FontSize = 40 };
    private readonly Menu _list = new() { RowHeight = 54, FontSize = 34, VisibleRows = 8, Wrap = false };
    private readonly Menu _system = new() { RowHeight = 62, FontSize = 38 };
    private readonly Menu _yesNo = new() { RowHeight = 62, FontSize = 38 };
    private int _mainIndex;

    private static readonly Rectangle CommandRect = new(30, 445, 400, 310);
    private static readonly Rectangle CommandMenuRect = new(46, 470, 368, 62 * 4);
    private static readonly Rectangle ListRect = new(450, 120, 1060, 620);
    private static readonly Rectangle ListMenuRect = new(470, 190, 1020, 54 * 8);
    private static readonly Rectangle StatusRect = new(30, 770, 560, 285);
    private static readonly Rectangle MessageRect = new(610, 770, 1280, 285);

    private void OpenMain()
    {
        _pane = Pane.Main;
        _main.SetItems(
        [
            new("たたかう"),
            new("スキル"),
            new("どうぐ", !_run.Hero.Inventory.IsEmpty),
            new("ようす"),
        ], keepIndex: true);
        _main.Index = Math.Clamp(_mainIndex, 0, 3);
    }

    /// <summary>コマンドを選ぶ。決まったら true。</summary>
    private bool UpdateCommand()
    {
        var b = _battle!;
        if (_choice is null && _pane == Pane.Main && _main.Items.Count == 0) OpenMain();
        switch (_pane)
        {
            case Pane.Main:
            {
                if (_main.Items.Count == 0 || _main.Items[2].Enabled == _run.Hero.Inventory.IsEmpty) OpenMain();
                if (In.Pressed(Act.Menu))
                {
                    S.Cue(Cue.Confirm);
                    _pane = Pane.System;
                    _system.SetItems([new("つづける"), new("せってい"), new("タイトルへもどる")], keepIndex: false);
                    return false;
                }
                int i = _main.Update(S, CommandMenuRect);
                _mainIndex = _main.Index;
                switch (i)
                {
                    case 0:
                        _choice = new PlayerChoice(ChoiceKind.Attack);
                        return true;
                    case 1:
                        _pane = Pane.Skills;
                        _list.SetItems(_run.Hero.Skills.Select(s =>
                        {
                            string? why = b.CannotUse(s);
                            int cost = b.MpCost(s);
                            return new MenuItem(s.Name, why is null, cost > 0 ? $"MP {cost}" : "", why is null ? s.Description : $"{s.Description}（{why}）", s.Id, Icons.For(s));
                        }), keepIndex: true);
                        break;
                    case 2:
                        _pane = Pane.Items;
                        _list.SetItems(_run.Hero.Inventory.Stacks.Select(st => new MenuItem(st.Item.Name, true, $"×{st.Count}", st.Item.Description, st.Item, Icons.For(st.Item))), keepIndex: true);
                        break;
                    case 3:
                        _pane = Pane.Status;
                        break;
                }
                return false;
            }
            case Pane.Skills:
            case Pane.Items:
            {
                int i = _list.Update(S, ListMenuRect);
                if (In.Pressed(Act.Cancel) || In.MouseRightClicked)
                {
                    S.Cue(Cue.Cancel);
                    OpenMain();
                    return false;
                }
                if (i >= 0)
                {
                    var tag = _list.Items[i].Tag;
                    _choice = tag is SkillId id ? new PlayerChoice(ChoiceKind.Skill, id) : new PlayerChoice(ChoiceKind.Item, Item: (Item)tag!);
                    OpenMain();
                    return true;
                }
                return false;
            }
            case Pane.Status:
                if (In.Pressed(Act.Cancel) || In.Pressed(Act.Confirm) || In.MouseClicked)
                {
                    S.Cue(Cue.Cancel);
                    OpenMain();
                }
                return false;
            case Pane.System:
            {
                int i = _system.Update(S, SystemMenuRect);
                if (In.Pressed(Act.Cancel) || i == 0)
                {
                    if (i != 0) S.Cue(Cue.Cancel);
                    OpenMain();
                }
                else if (i == 1)
                {
                    OpenMain();
                    S.Game.Scenes.Go(new SettingsScene(S, () => this));
                }
                else if (i == 2)
                {
                    _pane = Pane.ConfirmTitle;
                    _yesNo.SetItems([new("いいえ"), new("はい")], keepIndex: false);
                }
                return false;
            }
            case Pane.ConfirmTitle:
            {
                int i = _yesNo.Update(S, SystemMenuRect);
                if (In.Pressed(Act.Cancel) || i == 0) { _pane = Pane.System; S.Cue(Cue.Cancel); }
                else if (i == 1) S.Game.Scenes.Go(new TitleScene(S));
                return false;
            }
        }
        return false;
    }

    private static Rectangle SystemMenuRect => new(760, 470, 400, 62 * 3);

    // ------------------------------------------------------------------
    // 描画
    // ------------------------------------------------------------------

    public override void Draw()
    {
        var g = G;
        var batch = g.Batch;
        var biome = _run.Biome;
        bool boss = _battle?.Enemy.IsBoss == true;

        batch.Begin();
        var floorIntro = _wait is WaitPanel { Kind: PanelKind.FloorIntro };
        if (floorIntro)
        {
            DrawFloorIntro(g, (WaitPanel)_wait!);
            batch.End();
            return;
        }
        var bgTint = boss ? new Color(200, 160, 160) : new Color(215, 215, 225);
        Art.Cover(g, S.Assets.Background(biome.Background), 1.02f + (MathF.Sin(Time * 0.2f) * 0.006f), default, bgTint);
        Art.Vignette(g, 0.5f, 0.8f);
        if (boss) g.Rect(new Rectangle(0, 0, Gfx.Width, Gfx.Height), new Color(80, 0, 0) * (0.12f + (0.05f * MathF.Sin(Time * 2))));
        batch.End();

        // 敵
        if (_battle is not null && _art is not null) DrawEnemy(g);

        // エフェクト（ふつう → 光）
        batch.Begin();
        _fx.DrawNormal(g);
        batch.End();
        batch.Begin(blendState: BlendState.Additive);
        _fx.DrawAdditive(g);
        batch.End();

        batch.Begin();
        DrawTopBar(g);
        DrawStatus(g);
        DrawMessages(g);
        if (_wait is WaitCommand) DrawCommand(g);
        if (_wait is WaitChoice wc) DrawChoice(g, wc);
        batch.End();
        batch.Begin(blendState: BlendState.Additive);
        _fxTop.DrawAdditive(g);
        batch.End();
        batch.Begin();
        if (_wait is WaitPanel { Kind: PanelKind.LevelUp } lp) DrawLevelUp(g, lp);
        Art.Popups(g, _popups);
        if (_heroFlash > 0) g.Rect(new Rectangle(0, 0, Gfx.Width, Gfx.Height), new Color(255, 0, 0) * (_heroFlash * 0.12f));
        if (_redVignette > 0) Art.EdgeGlow(g, new Color(200, 0, 0), _redVignette * 0.8f);
        // HP が少ないときは、画面のふちが脈打つ
        var hh = _run.Hero;
        if (hh.Hp > 0 && hh.Hp <= hh.MaxHp / 4 && _battle is not null) Art.EdgeGlow(g, new Color(160, 0, 0), 0.25f + (0.15f * MathF.Sin(Time * 6)));
        batch.End();

        if (_wait is WaitChallenge ch) ch.View.Draw(g, Time);
    }

    private void DrawFloorIntro(Gfx g, WaitPanel p)
    {
        var biome = _run.Biome;
        bool boss = EnemyFactory.IsBossFloor(_run.Floor);
        var accent = boss ? Palette.Bad : Palette.Of(biome.Led);
        float t = p.T;
        float a = Math.Min(1, t / 0.35f);

        // その場所の景色を暗く、ゆっくり近づく（降りてきた感じ）
        float zoom = 1.16f - (Ease.OutCubic(t / 2.2f) * 0.1f);
        Art.Cover(g, S.Assets.Background(biome.Background), zoom, default, boss ? new Color(110, 70, 70) : new Color(95, 95, 110));
        Art.Vignette(g, 0.75f, 0.85f);
        g.Glow(new Vector2(Gfx.Width / 2f, 540), 900, accent * (0.10f * a));
        g.Batch.End();
        g.Batch.Begin();
        _fx.DrawNormal(g);
        g.Batch.End();
        g.Batch.Begin(blendState: BlendState.Additive);
        _fx.DrawAdditive(g);
        g.Batch.End();
        g.Batch.Begin();

        // 中央の帯（左右はぼかす）
        float bandA = Ease.OutCubic(t / 0.3f);
        int bandY = 330, bandH = 430;
        for (int i = 0; i < 24; i++)
        {
            float edge = MathF.Min(i, 23 - i) / 6f;
            float k = Math.Clamp(edge, 0, 1);
            g.Rect(new Rectangle(Gfx.Width * i / 24, bandY, (Gfx.Width / 24) + 1, bandH), Color.Black * (0.55f * k * bandA));
        }
        // 帯の上下の金の線が中央から伸びる
        float lineW = 1500 * Ease.OutCubic((t - 0.05f) / 0.5f);
        var cx = Gfx.Width / 2f;
        g.Rect(cx - (lineW / 2), bandY, lineW, 3, Palette.Frame * a);
        g.Rect(cx - (lineW / 2), bandY + bandH - 3, lineW, 3, Palette.Frame * a);
        g.Rect(cx - (lineW / 2), bandY + 7, lineW, 1, accent * (0.6f * a));
        g.Rect(cx - (lineW / 2), bandY + bandH - 8, lineW, 1, accent * (0.6f * a));

        // 「地下 N 階」：上から落ちてきて止まる
        float drop = Ease.OutBack(Math.Clamp((t - 0.1f) / 0.45f, 0, 1));
        var titleCol = boss ? Palette.Bad : Palette.Gold;
        float ty = 430 - ((1 - drop) * 60);
        g.TextCentered($"地下 {_run.Floor} 階", new Vector2(cx, ty), 124, titleCol * Math.Clamp((t - 0.1f) / 0.2f, 0, 1), bold: true);

        // 場所の名前（両側に飾り）
        float na = Math.Clamp((t - 0.35f) / 0.3f, 0, 1);
        var nm = g.Measure(biome.Name, 58);
        float ny = 548;
        g.TextCentered(biome.Name, new Vector2(cx, ny), 58, Color.White * na, bold: true);
        float ow = 120 * na;
        g.Rect(cx - (nm.X / 2) - 40 - ow, ny - 2, ow, 3, accent * na);
        g.Rect(cx + (nm.X / 2) + 40, ny - 2, ow, 3, accent * na);
        DrawDiamond(g, new Vector2(cx - (nm.X / 2) - 28, ny), 9, accent * na);
        DrawDiamond(g, new Vector2(cx + (nm.X / 2) + 28, ny), 9, accent * na);

        // 説明と、よく効く・効きにくい属性
        float da = Math.Clamp((t - 0.55f) / 0.3f, 0, 1);
        g.TextCentered(boss ? "強大な気配がする…" : biome.EffectText, new Vector2(cx, 628), 34, (boss ? Palette.Bad : new Color(220, 220, 230)) * da);
        var parts = new List<(string Label, Element E, Color C)>();
        if (biome.WeakTo != Element.None) parts.Add(("よく効く", biome.WeakTo, Palette.Good));
        if (biome.Resists != Element.None) parts.Add(("効きにくい", biome.Resists, Palette.Bad));
        string w = $"{Names.Of(_run.Weather)}{(_run.RealWeather ? "（外の天気）" : "")}・{Names.Of(_run.Day)}曜日";
        float rowY = 690;
        float total = g.Measure(w, 28).X;
        foreach (var pt in parts) total += 60 + g.Measure(pt.Label, 28).X + 12 + 32 + 8 + g.Measure(Names.Of(pt.E), 28).X;
        float x = cx - (total / 2);
        g.Text(w, new Vector2(x, rowY - 16), 28, Palette.Dim * da);
        x += g.Measure(w, 28).X;
        foreach (var pt in parts)
        {
            x += 60;
            g.Text(pt.Label, new Vector2(x, rowY - 16), 28, pt.C * da);
            x += g.Measure(pt.Label, 28).X + 12;
            Icons.Draw(g, Icons.For(pt.E), new Vector2(x, rowY - 16), 30, da);
            x += 32 + 8;
            g.Text(Names.Of(pt.E), new Vector2(x, rowY - 16), 28, Color.White * da);
            x += g.Measure(Names.Of(pt.E), 28).X;
        }

        // 深さの目盛り
        DrawDepth(g, new Rectangle(460, 840, 1000, 16), a);
        if (boss) Art.EdgeGlow(g, new Color(200, 0, 0), 0.35f + (0.15f * MathF.Sin(Time * 5)));
    }

    private static void DrawDiamond(Gfx g, Vector2 c, float r, Color col) =>
        g.Batch.Draw(g.Pixel, c, null, col, MathF.PI / 4, new Vector2(0.5f, 0.5f), r * 1.414f, SpriteEffects.None, 0);

    /// <summary>B1F〜B100F の深さの目盛り（場所ごとの色、ボスの階に印、いまの位置に光る印）。</summary>
    private void DrawDepth(Gfx g, Rectangle r, float alpha)
    {
        g.Rect(new Rectangle(r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8), Color.Black * (0.8f * alpha));
        for (int i = 0; i < 10; i++)
        {
            var c = Palette.Of(Biome.All[i].Led);
            int x0 = r.X + (r.Width * i / 10);
            int x1 = r.X + (r.Width * (i + 1) / 10);
            float done = Math.Clamp((_run.Floor - (i * 10)) / 10f, 0, 1);
            g.Rect(new Rectangle(x0, r.Y, x1 - x0 - 3, r.Height), c * (0.22f * alpha));
            g.Rect(new Rectangle(x0, r.Y, (int)((x1 - x0 - 3) * done), r.Height), c * (0.85f * alpha));
            g.Rect(new Rectangle(x0, r.Y, (int)((x1 - x0 - 3) * done), 3), Color.White * (0.35f * alpha));
            // 10 階ごとのボス
            DrawDiamond(g, new Vector2(x1 - 2, r.Y + r.Height + 14), 6, (_run.Floor >= (i + 1) * 10 ? Palette.Disabled : Palette.Bad) * alpha);
        }
        g.Text("B1F", new Vector2(r.X - 70, r.Y - 10), 24, Palette.Dim * alpha);
        g.Text("B100F", new Vector2(r.Right + 14, r.Y - 10), 24, Palette.Dim * alpha);
        float px = r.X + (r.Width * (_run.Floor - 0.5f) / 100f);
        float pulse = 0.6f + (0.4f * MathF.Sin(Time * 6));
        g.Glow(new Vector2(px, r.Center.Y), 46, Color.White * (0.5f * pulse * alpha));
        g.Rect(px - 3, r.Y - 10, 6, r.Height + 20, Color.White * alpha);
        DrawDiamond(g, new Vector2(px, r.Y - 22), 8, Palette.Gold * alpha);
    }

    private void DrawEnemy(Gfx g)
    {
        var b = _battle!;
        var art = _art!;
        float scale = EnemyScale * (0.6f + (0.4f * Ease.OutBack(_enemyAppear)));
        float alpha = Math.Min(1, _enemyAppear * 2);
        if (_enemyDeath >= 0)
        {
            // 白く光って、縦につぶれながら消える
            alpha *= 1 - Ease.OutCubic(_enemyDeath);
            scale *= 1 + (_enemyDeath * 0.15f);
        }
        var feet = EnemyFeet + new Vector2(MathF.Sin(Time * 70) * 14 * _enemyShake, 0);
        // 影
        g.Batch.Begin();
        float shadowW = Math.Min(560, art.FrameWidth(false) * art.BaseScale * EnemyScale * 0.9f);
        g.Batch.Draw(g.CircleTex, new Rectangle((int)(feet.X - (shadowW / 2)), (int)feet.Y - 20, (int)shadowW, 44), Color.Black * (0.45f * alpha));
        if (b.Enemy.IsBoss) g.Glow(EnemyCenter, 420, new Color(255, 60, 60) * (0.18f * alpha));
        g.Batch.End();

        g.Batch.Begin(samplerState: art.Pixel ? SamplerState.PointClamp : SamplerState.LinearClamp);
        var color = Color.White * alpha;
        bool attacking = _enemyAttackT >= 0;
        Art.Enemy(g, art, b.Enemy.Def, feet, _animTime, scale, color, attacking, Math.Max(0, _enemyAttackT));
        g.Batch.End();
        if (_enemyFlash > 0)
        {
            // 当たったときに白く光らせる（加算）
            g.Batch.Begin(blendState: BlendState.Additive, samplerState: art.Pixel ? SamplerState.PointClamp : SamplerState.LinearClamp);
            float flash = Math.Max(_enemyFlash * 0.8f, _enemyDeath >= 0 ? 1 - _enemyDeath : 0);
            Art.Enemy(g, art, b.Enemy.Def, feet, _animTime, scale, Color.White * (flash * alpha * 1.5f), attacking, Math.Max(0, _enemyAttackT));
            g.Batch.End();
        }

        // 名前と HP
        g.Batch.Begin();
        if (_enemyDeath < 0.5f)
        {
            float trail = b.Enemy.MaxHp == 0 ? -1 : _enemyTrail / b.Enemy.MaxHp;
            float ratio = _enemyDisplayHp / Math.Max(1, b.Enemy.MaxHp);
            if (b.Enemy.IsBoss)
            {
                var plate = new Rectangle(440, 96, 1040, 96);
                g.Window(plate, 0.95f, new Color(60, 14, 20, 236), new Color(220, 70, 60));
                var tag = new Rectangle(plate.X + 22, plate.Y + 16, 96, 34);
                g.Rect(tag, new Color(200, 40, 40));
                g.Outline(tag, Color.Black, 2);
                g.TextCentered(b.Enemy.IsFinalBoss ? "LAST" : "BOSS", new Vector2(tag.Center.X, tag.Center.Y), 26, Color.White);
                g.Text(b.Enemy.Name, new Vector2(plate.X + 136, plate.Y + 12), 38, Color.White);
                g.TextRight($"{b.Enemy.Hp} / {b.Enemy.MaxHp}", new Vector2(plate.Right - 26, plate.Y + 18), 26, Palette.Dim);
                g.Bar(new Rectangle(plate.X + 24, plate.Y + 62, plate.Width - 48, 18), ratio, new Color(230, 60, 60), Palette.HpDark, trail: trail);
            }
            else
            {
                float top = Math.Max(150, EnemyFeet.Y - Art.EnemyHeight(art, EnemyScale) - 50);
                string name = b.EnemyDisplayName;
                var m = g.Measure(name, 34);
                var plate = new Rectangle((int)(960 - Math.Max(170, (m.X / 2) + 30)), (int)top - 50, (int)Math.Max(340, m.X + 60), 82);
                g.Rect(new Rectangle(plate.X + 4, plate.Y + 6, plate.Width, plate.Height), Color.Black * 0.35f);
                g.Rect(plate, new Color(12, 14, 30) * 0.82f);
                g.Outline(plate, Palette.Frame * 0.9f, 2);
                g.TextCentered(name, new Vector2(960, plate.Y + 24), 34, Color.White);
                if (name != "？？？") g.Bar(new Rectangle(plate.X + 20, plate.Bottom - 26, plate.Width - 40, 12), ratio, new Color(230, 70, 70), Palette.HpDark, trail: trail, ticks: false);
            }
        }
        g.Batch.End();
    }

    private void DrawTopBar(Gfx g)
    {
        var r = new Rectangle(24, 20, 470, 92);
        g.Window(r, 0.9f);
        g.Text($"B{_run.Floor}F", new Vector2(r.X + 22, r.Y + 14), 60, Palette.Gold);
        g.Text(_run.Biome.Name, new Vector2(r.X + 200, r.Y + 14), 34, Color.White);
        string w = $"{Names.Of(_run.Weather)}・{Names.Of(_run.Day)}曜";
        g.Text(w, new Vector2(r.X + 200, r.Y + 52), 26, Palette.Dim);
        DrawProgress(g, new Rectangle(1450, 40, 440, 14));
        g.TextRight(S.Controller.Active ? "♥ コントローラー" : "", new Vector2(1890, 64), 24, Palette.Good);
    }

    private void DrawProgress(Gfx g, Rectangle r)
    {
        g.Rect(r, new Color(20, 20, 30) * 0.9f);
        for (int i = 0; i < 10; i++)
        {
            var c = Palette.Of(Biome.All[i].Led);
            int x0 = r.X + (r.Width * i / 10);
            int x1 = r.X + (r.Width * (i + 1) / 10);
            float done = Math.Clamp((_run.Floor - (i * 10)) / 10f, 0, 1);
            g.Rect(new Rectangle(x0, r.Y, x1 - x0 - 2, r.Height), c * 0.25f);
            g.Rect(new Rectangle(x0, r.Y, (int)((x1 - x0 - 2) * done), r.Height), c * 0.9f);
        }
        float px = r.X + (r.Width * (_run.Floor - 0.5f) / 100f);
        g.Rect(px - 3, r.Y - 8, 6, r.Height + 16, Color.White);
    }

    private void DrawStatus(Gfx g)
    {
        var h = _run.Hero;
        var r = StatusRect;
        r.Offset((int)(MathF.Sin(Time * 80) * 10 * _heroPanelShake), 0);
        bool danger = h.Hp > 0 && h.Hp <= h.MaxHp / 4;
        g.Window(r, 0.95f, border: danger ? Color.Lerp(Palette.Frame, Palette.Bad, 0.5f + (0.5f * MathF.Sin(Time * 6))) : null);

        // 顔（小さな額縁に入れる）
        var face = new Rectangle(r.X + 20, r.Y + 22, 146, 146);
        g.Rect(new Rectangle(face.X - 4, face.Y - 4, face.Width + 8, face.Height + 8), Color.Black);
        g.Rect(face, new Color(30, 40, 80));
        var tex = S.Assets.Hero(h.Gender);
        var tint = Color.Lerp(Color.White, Palette.Bad, _heroFlash * 0.7f);
        Art.HeroBust(g, tex, face, tint);
        g.Outline(new Rectangle(face.X - 4, face.Y - 4, face.Width + 8, face.Height + 8), Palette.Frame, 2);
        // 経験値の細いゲージ
        var expR = new Rectangle(face.X, face.Bottom + 12, face.Width, 8);
        g.Bar(expR, h.ExpToNext == 0 ? 0 : h.Exp / (float)h.ExpToNext, new Color(240, 210, 90), new Color(80, 60, 20), ticks: false);
        g.Text("EXP", new Vector2(face.X, expR.Bottom + 6), 22, Palette.Dim);

        float x = r.X + 188;
        g.Text(h.Name, new Vector2(x, r.Y + 16), 40, Color.White);
        // Lv の札
        string lv = $"Lv {h.Level}";
        var lvW = g.Measure(lv, 30).X + 20;
        var lvR = new Rectangle((int)(r.Right - 20 - lvW), r.Y + 20, (int)lvW, 40);
        g.Rect(lvR, new Color(70, 52, 18));
        g.Outline(lvR, Palette.Frame, 2);
        g.TextCentered(lv, new Vector2(lvR.Center.X, lvR.Center.Y), 30, Palette.Gold);

        // HP（少ないほど黄 → 赤、減った分が白く残る）
        float hpRatio = h.MaxHp == 0 ? 0 : _displayHp / h.MaxHp;
        var hpColor = hpRatio > 0.5f ? new Color(90, 220, 110) : hpRatio > 0.25f ? new Color(240, 200, 60) : Palette.Hp;
        Icons.Draw(g, "heart", new Vector2(x, r.Y + 76), 30);
        var hpBar = new Rectangle((int)x + 42, r.Y + 80, 316, 22);
        g.Bar(hpBar, hpRatio, hpColor, Palette.HpDark, trail: h.MaxHp == 0 ? -1 : _hpTrail / h.MaxHp);
        g.TextRight($"{h.Hp} / {h.MaxHp}", new Vector2(r.Right - 22, r.Y + 108), 28, danger ? Palette.Bad : Color.White);

        Icons.Draw(g, "drop", new Vector2(x, r.Y + 142), 30);
        g.Bar(new Rectangle((int)x + 42, r.Y + 148, 316, 16), h.MaxMp == 0 ? 0 : _displayMp / h.MaxMp, Palette.Mp, Palette.MpDark);
        g.TextRight($"{h.Mp} / {h.MaxMp}", new Vector2(r.Right - 22, r.Y + 170), 26, Color.White);

        // 状態の札
        float bx = x;
        float by = r.Y + 214;
        var badges = new List<(string, Color)>();
        if (h.Condition != Condition.Normal) badges.Add((Names.Of(h.Condition), Palette.Cursor));
        if (h.IsBerserk) badges.Add(("興奮", Palette.Bad));
        if (h.ChargeMultiplier > 1) badges.Add(($"ため×{h.ChargeMultiplier:0.0}", Palette.Gold));
        foreach (var (t, c) in badges)
        {
            var w = g.Measure(t, 24).X + 18;
            var br = new Rectangle((int)bx, (int)by, (int)w, 34);
            g.Rect(br, c * 0.35f);
            g.Outline(br, c, 2);
            g.Text(t, new Vector2(bx + 9, by + 4), 24, Color.White);
            bx += w + 8;
        }
        // 蘇生のチャンス（ハートのアイコンを並べる）
        int charges = Math.Min(5, h.ReviveCharges);
        float hx = r.Right - 22 - (charges * 30);
        if (charges == 0) g.TextRight("蘇生 なし", new Vector2(r.Right - 22, r.Y + 228), 22, Palette.Dim);
        for (int i = 0; i < charges; i++) Icons.Draw(g, "heart", new Vector2(hx + (i * 30), r.Y + 234), 24);
    }

    private void DrawMessages(Gfx g)
    {
        var r = MessageRect;
        g.Window(r, 0.94f);
        const float size = 36;
        float lineH = size * 1.5f;
        int maxLines = 4;
        var lines = new List<(string Text, bool Current)>();
        // 新しいほうから 4 行ぶんだけ折り返す（毎フレーム全部を測らない）
        for (int i = _log.Count - 1; i >= 0 && lines.Count < maxLines; i--)
        {
            bool current = i == _log.Count - 1;
            string text = current ? _typer.Visible : _log[i];
            lines.InsertRange(0, g.Wrap(text, size, r.Width - 80).Select(l => (l, current)));
        }
        int start = Math.Max(0, lines.Count - maxLines);
        float y = r.Y + 30;
        for (int i = start; i < lines.Count; i++)
        {
            g.Text(lines[i].Text, new Vector2(r.X + 40, y), size, lines[i].Current ? Color.White : Palette.Dim);
            y += lineH;
        }
        if (_typer.Done && _wait is WaitEvents)
        {
            float blink = 0.5f + (0.5f * MathF.Sin(Time * 6));
            g.TextCentered("▼", new Vector2(r.Right - 40, r.Bottom - 30), 26, Palette.Gold * blink);
        }
    }

    private void DrawCommand(Gfx g)
    {
        g.Window(CommandRect, 0.96f);
        _main.Draw(g, CommandMenuRect, Time, _pane == Pane.Main);
        switch (_pane)
        {
            case Pane.Skills:
            case Pane.Items:
            {
                g.Window(ListRect, 0.97f);
                g.Text(_pane == Pane.Skills ? "スキル" : "どうぐ", new Vector2(ListRect.X + 30, ListRect.Y + 20), 40, Palette.Gold);
                if (_pane == Pane.Skills) g.TextRight($"MP {_run.Hero.Mp} / {_run.Hero.MaxMp}", new Vector2(ListRect.Right - 30, ListRect.Y + 26), 32, Palette.Mp);
                else g.TextRight($"{_run.Hero.Inventory.Stacks.Count} / {Inventory.MaxKinds} 種類", new Vector2(ListRect.Right - 30, ListRect.Y + 26), 30, Palette.Dim);
                _list.Draw(g, ListMenuRect, Time);
                var desc = _list.Selected?.Description ?? "";
                g.Rect(new Rectangle(ListRect.X + 20, ListRect.Bottom - 116, ListRect.Width - 40, 2), Palette.BorderInner);
                g.Paragraph(desc, new Vector2(ListRect.X + 36, ListRect.Bottom - 100), 30, ListRect.Width - 72, Palette.Text);
                break;
            }
            case Pane.Status:
                DrawStatusDetail(g);
                break;
            case Pane.System:
            case Pane.ConfirmTitle:
            {
                var r = new Rectangle(SystemMenuRect.X - 40, SystemMenuRect.Y - 100, SystemMenuRect.Width + 80, SystemMenuRect.Height + 190);
                g.Window(r, 0.97f);
                if (_pane == Pane.System)
                {
                    g.TextCentered("メニュー", new Vector2(r.Center.X, r.Y + 50), 40, Palette.Gold);
                    _system.Draw(g, SystemMenuRect, Time);
                    g.TextCentered("記録は階に着くたびに自動でつけています", new Vector2(r.Center.X, r.Bottom - 40), 24, Palette.Dim);
                }
                else
                {
                    g.TextCentered("タイトルへもどりますか？", new Vector2(r.Center.X, r.Y + 50), 36, Color.White);
                    _yesNo.Draw(g, SystemMenuRect, Time);
                    g.TextCentered($"次は B{_run.Floor}F のはじめから", new Vector2(r.Center.X, r.Bottom - 40), 24, Palette.Dim);
                }
                break;
            }
        }
        if (_pane == Pane.Main) Hints.DrawAt(g, In, 708, Hints.Confirm(In), (In.MenuLabel, "メニュー"));
        else Hints.DrawAt(g, In, 708, Hints.Confirm(In), Hints.Cancel(In));
    }

    private void DrawStatusDetail(Gfx g)
    {
        var h = _run.Hero;
        var b = _battle!;
        var r = new Rectangle(450, 100, 1060, 650);
        g.Window(r, 0.97f);
        float x = r.X + 50, y = r.Y + 36;
        g.Text($"{h.Name}　Lv {h.Level}　（{Names.Of(_run.Difficulty)}）", new Vector2(x, y), 40, Palette.Gold);
        y += 66;
        g.Text($"HP {h.Hp}/{h.MaxHp}　MP {h.Mp}/{h.MaxMp}", new Vector2(x, y), 34, Color.White);
        y += 52;
        g.Text($"攻撃 {h.Attack}　防御 {h.Defense}　素早さ {h.Speed}", new Vector2(x, y), 34, Color.White);
        y += 52;
        g.Text($"才能：{Names.Of(h.Talent)} … {Names.Describe(h.Talent)}", new Vector2(x, y), 30, Palette.Good);
        y += 48;
        g.Text($"かわす確率 {b.HeroEvasion:0}%　会心の確率 {b.CritChance:0}%", new Vector2(x, y), 30, Palette.Dim);
        y += 70;
        g.Text($"場所：{_run.Biome.Name} … {_run.Biome.EffectText}", new Vector2(x, y), 30, Color.White);
        y += 46;
        var biome = _run.Biome;
        string aff = biome.WeakTo == Element.None ? "弱点なし" : $"ここの敵は {Names.Of(biome.WeakTo)} に弱い";
        if (biome.Resists != Element.None) aff += $"／{Names.Of(biome.Resists)} が効きにくい";
        g.Text(aff, new Vector2(x, y), 30, Palette.Gold);
        y += 46;
        g.Text($"天気：{Names.Of(_run.Weather)}（晴れ＝炎↑・雨＝氷↑炎↓・くもり＝雷↑）", new Vector2(x, y), 30, Palette.Dim);
        y += 70;
        g.Text($"倒した敵 {_run.BattlesWon}　冒険の時間 {TimeSpan.FromSeconds(_run.PlaySeconds):h\\:mm\\:ss}", new Vector2(x, y), 30, Palette.Dim);
    }

    private void DrawChoice(Gfx g, WaitChoice c)
    {
        var mr = WaitChoice.ChoiceRect(c.Menu.Items.Count);
        var r = new Rectangle(mr.X - 40, mr.Y - 100, mr.Width + 80, mr.Height + 130);
        g.Window(r, 0.97f);
        g.TextCentered(c.Question, new Vector2(r.Center.X, r.Y + 50), 38, Color.White);
        c.Menu.Draw(g, mr, Time);
    }

    private void DrawLevelUp(Gfx g, WaitPanel p)
    {
        var up = (LevelUpResult)p.Data;
        float k = Ease.OutBack(Math.Min(1, p.T / 0.4f));
        var r = new Rectangle(560, 200, 800, 440 + (up.NewSkills.Count * 50));
        var center = r.Center.ToVector2();
        g.Glow(center, 600 * k, Palette.Gold * 0.25f);
        g.Window(r, k, border: Palette.Gold);
        g.TextCentered("レベルアップ！", new Vector2(center.X, r.Y + 64), 64 * k, Palette.Gold, bold: true);
        g.TextCentered($"Lv {up.Level}", new Vector2(center.X, r.Y + 140), 52, Color.White);
        string[] rows =
        [
            $"最大HP +{up.Hp}　最大MP +{up.Mp}",
            $"攻撃 +{up.Attack}　防御 +{up.Defense}" + (up.Speed > 0 ? $"　素早さ +{up.Speed}" : ""),
            "HP と MP がすべて回復した！",
        ];
        float y = r.Y + 210;
        foreach (var row in rows)
        {
            g.TextCentered(row, new Vector2(center.X, y), 36, Color.White);
            y += 56;
        }
        foreach (var s in up.NewSkills)
        {
            g.TextCentered($"新しいスキル「{s.Name}」を覚えた！", new Vector2(center.X, y), 36, Palette.Good);
            y += 50;
        }
    }
}

public sealed partial class DungeonScene
{
    // ---- 動作確認（自動操作）用 ----
    internal bool AwaitingCommand => _wait is WaitCommand && _pane == Pane.Main;

    internal ChallengeView? ActiveChallenge => (_wait as WaitChallenge)?.View;

    internal bool ShowingLevelUp => _wait is WaitPanel { Kind: PanelKind.LevelUp };

    internal bool ShowingChoice => _wait is WaitChoice;

    internal string PaneName => _pane.ToString();

    /// <summary>演出だけを出す（見た目の確認用）。</summary>
    internal void DebugFx(string kind)
    {
        float size = Math.Clamp(EnemyHeightOnScreen, 220, 520);
        switch (kind)
        {
            case "slash": _fx.Slash(EnemyCenter, size, false); _enemyFlash = 1; break;
            case "crit": _fx.Slash(EnemyCenter, size, true); _enemyFlash = 1; _shake = 0.4f; break;
            case "fire": _fx.Element(Element.Fire, EnemyCenter, size); break;
            case "ice": _fx.Element(Element.Ice, EnemyCenter, size); break;
            case "thunder": _fx.Element(Element.Thunder, EnemyCenter, size); break;
            case "poison": _fx.Element(Element.Poison, EnemyCenter, size); break;
            case "holy": _fx.Element(Element.Holy, EnemyCenter, size); break;
            case "shatter": _fx.Shatter(EnemyBounds, _art?.Average ?? Color.Gray, 200); _enemyDeath = 0; break;
            case "heal": _fxTop.Heal(new Vector2(300, 930), 480); break;
            case "pillar": _fxTop.Pillar(new Vector2(960, 640), Palette.Gold); break;
            case "claw": _fxTop.Claw(new Vector2(960, 560), true); _redVignette = 1; _heroFlash = 1; break;
        }
    }
}
