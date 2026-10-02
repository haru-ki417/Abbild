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
                            return new MenuItem(s.Name, why is null, cost > 0 ? $"MP {cost}" : "", why is null ? s.Description : $"{s.Description}（{why}）", s.Id);
                        }), keepIndex: true);
                        break;
                    case 2:
                        _pane = Pane.Items;
                        _list.SetItems(_run.Hero.Inventory.Stacks.Select(st => new MenuItem(st.Item.Name, true, $"×{st.Count}", st.Item.Description, st.Item)), keepIndex: true);
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

        batch.Begin();
        DrawTopBar(g);
        DrawStatus(g);
        DrawMessages(g);
        if (_wait is WaitCommand) DrawCommand(g);
        if (_wait is WaitChoice wc) DrawChoice(g, wc);
        if (_wait is WaitPanel { Kind: PanelKind.LevelUp } lp) DrawLevelUp(g, lp);
        Art.Popups(g, _popups);
        if (_heroFlash > 0) g.Rect(new Rectangle(0, 0, Gfx.Width, Gfx.Height), new Color(255, 0, 0) * (_heroFlash * 0.22f));
        batch.End();

        if (_wait is WaitChallenge ch) ch.View.Draw(g, Time);
    }

    private void DrawFloorIntro(Gfx g, WaitPanel p)
    {
        Art.Cover(g, S.Assets.Background("corridor"), 1.08f - (p.T * 0.02f), default, new Color(120, 120, 135));
        Art.Vignette(g, 0.7f, 0.7f);
        float a = Math.Min(1, p.T / 0.4f);
        bool boss = EnemyFactory.IsBossFloor(_run.Floor);
        g.TextCentered($"地下 {_run.Floor} 階", new Vector2(Gfx.Width / 2f, 440), 120, (boss ? Palette.Bad : Palette.Gold) * a, bold: true);
        g.TextCentered(_run.Biome.Name, new Vector2(Gfx.Width / 2f, 560), 56, Color.White * a, bold: true);
        g.TextCentered(boss ? "強大な気配がする…" : _run.Biome.EffectText, new Vector2(Gfx.Width / 2f, 650), 34, (boss ? Palette.Bad : Palette.Dim) * a);
        string w = $"{Names.Of(_run.Weather)}{(_run.RealWeather ? "（外の天気）" : "")}　{Names.Of(_run.Day)}曜日";
        g.TextCentered(w, new Vector2(Gfx.Width / 2f, 720), 30, Palette.Dim * a);
        DrawProgress(g, new Rectangle(560, 820, 800, 18));
    }

    private void DrawEnemy(Gfx g)
    {
        var b = _battle!;
        var art = _art!;
        float scale = EnemyScale * (0.6f + (0.4f * Ease.OutBack(_enemyAppear)));
        float alpha = Math.Min(1, _enemyAppear * 2);
        if (_enemyDeath >= 0) alpha *= 1 - _enemyDeath;
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
        Art.Enemy(g, art, b.Enemy.Def, feet, Time, scale, color, attacking, Math.Max(0, _enemyAttackT));
        g.Batch.End();
        if (_enemyFlash > 0)
        {
            // 当たったときに白く光らせる（加算）
            g.Batch.Begin(blendState: BlendState.Additive, samplerState: art.Pixel ? SamplerState.PointClamp : SamplerState.LinearClamp);
            Art.Enemy(g, art, b.Enemy.Def, feet, Time, scale, Color.White * (_enemyFlash * 0.8f), attacking, Math.Max(0, _enemyAttackT));
            g.Batch.End();
        }

        // 名前と HP
        g.Batch.Begin();
        if (_enemyDeath < 0.5f)
        {
            if (b.Enemy.IsBoss)
            {
                var r = new Rectangle(460, 120, 1000, 30);
                g.TextCentered(b.Enemy.Name, new Vector2(r.Center.X, r.Y - 34), 44, Palette.Bad, bold: true);
                g.Bar(r, _enemyDisplayHp / b.Enemy.MaxHp, Palette.Hp, Palette.HpDark);
            }
            else
            {
                float top = Math.Max(150, EnemyFeet.Y - Art.EnemyHeight(art, EnemyScale) - 40);
                string name = b.EnemyDisplayName;
                g.TextCentered(name, new Vector2(960, top - 30), 40, Color.White, bold: true);
                if (name != "？？？") g.Bar(new Rectangle(810, (int)top + 4, 300, 16), _enemyDisplayHp / b.Enemy.MaxHp, Palette.Hp, Palette.HpDark);
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
        g.Window(r, 0.94f, border: h.Hp <= h.MaxHp / 4 ? Palette.Bad : null);
        var tex = S.Assets.Hero(h.Gender);
        var tint = Color.Lerp(Color.White, Palette.Bad, _heroFlash * 0.7f);
        Art.HeroBust(g, tex, new Rectangle(r.X + 18, r.Y + 22, 150, 150), tint);
        float x = r.X + 186;
        g.Text(h.Name, new Vector2(x, r.Y + 18), 40, Color.White);
        g.TextRight($"Lv {h.Level}", new Vector2(r.Right - 22, r.Y + 22), 34, Palette.Gold);
        g.Text("HP", new Vector2(x, r.Y + 78), 28, Palette.Hp);
        g.Bar(new Rectangle((int)x + 54, r.Y + 84, 300, 22), _displayHp / h.MaxHp, Palette.Hp, Palette.HpDark);
        g.TextRight($"{h.Hp} / {h.MaxHp}", new Vector2(r.Right - 22, r.Y + 110), 28, Color.White);
        g.Text("MP", new Vector2(x, r.Y + 146), 28, Palette.Mp);
        g.Bar(new Rectangle((int)x + 54, r.Y + 152, 300, 16), h.MaxMp == 0 ? 0 : _displayMp / h.MaxMp, Palette.Mp, Palette.MpDark);
        g.TextRight($"{h.Mp} / {h.MaxMp}", new Vector2(r.Right - 22, r.Y + 172), 26, Color.White);

        // 状態・蘇生の残り
        float bx = r.X + 22;
        float by = r.Y + 214;
        var badges = new List<(string, Color)>();
        if (h.Condition != Condition.Normal) badges.Add((Names.Of(h.Condition), Palette.Cursor));
        if (h.IsBerserk) badges.Add(("興奮", Palette.Bad));
        if (h.ChargeMultiplier > 1) badges.Add(($"ため×{h.ChargeMultiplier:0.0}", Palette.Gold));
        foreach (var (t, c) in badges)
        {
            var w = g.Measure(t, 26).X + 20;
            g.Rect(new Rectangle((int)bx, (int)by, (int)w, 38), c * 0.3f);
            g.Outline(new Rectangle((int)bx, (int)by, (int)w, 38), c, 2);
            g.Text(t, new Vector2(bx + 10, by + 4), 26, Color.White);
            bx += w + 10;
        }
        string hearts = new string('♥', Math.Min(5, h.ReviveCharges));
        g.TextRight(hearts.Length > 0 ? $"蘇生 {hearts}" : "蘇生 なし", new Vector2(r.Right - 22, r.Y + 220), 26, hearts.Length > 0 ? Palette.Hp : Palette.Dim);
        if (badges.Count == 0) g.Text($"EXP {h.Exp} / {h.ExpToNext}", new Vector2(r.X + 22, r.Y + 224), 24, Palette.Dim);
    }

    private void DrawMessages(Gfx g)
    {
        var r = MessageRect;
        g.Window(r, 0.94f);
        const float size = 36;
        float lineH = size * 1.5f;
        int maxLines = 4;
        var lines = new List<(string Text, bool Current)>();
        // 古いものから、折り返した行で後ろ 4 行を出す
        for (int i = 0; i < _log.Count; i++)
        {
            bool current = i == _log.Count - 1;
            string text = current ? _typer.Visible : _log[i];
            foreach (var l in g.Wrap(text, size, r.Width - 80)) lines.Add((l, current));
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
}
