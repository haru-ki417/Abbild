using Abbild.Core;
using Abbild.Engine;
using Abbild.Ui;
using Microsoft.Xna.Framework;

namespace Abbild.Scenes;

/// <summary>
/// 冒険の書（セーブデータ）を選ぶ画面。3 冊まで別々に記録でき、1 冊ずつ消せる。
/// continueMode なら「つづける」、そうでなければ「新しくはじめる」を先に選んだ状態で出す。
/// select（1〜）を渡すと、その冊を選んだ状態で出す。
/// </summary>
public sealed class SlotScene(Services s, bool continueMode, int select = 0) : Scene(s)
{
    private enum Stage { List, Actions, Confirm }

    private enum Choice { Continue, New, Delete, Cancel }

    private IReadOnlyList<SaveData?> _data = new SaveData?[SaveStore.SlotCount];
    private int _index;
    private float _band = -1;
    private Stage _stage = Stage.List;
    private float _stageT;
    private readonly Menu _actions = new() { RowHeight = 64, FontSize = 36 };
    private readonly Menu _confirm = new() { RowHeight = 64, FontSize = 36 };
    private readonly List<Choice> _choices = [];
    private Choice _pending;
    private readonly Fx _fx = new();
    private readonly float[] _flash = new float[SaveStore.SlotCount];
    private float _ember;

    private const float ReadyAt = 0.45f;

    public override void Enter()
    {
        S.Audio.PlayBgm("title");
        Reload();
        if (select is >= 1 and <= SaveStore.SlotCount) _index = select - 1;
        else if (continueMode) _index = Math.Max(0, SafeLatest() - 1);
        else
        {
            // 新しくはじめるときは、空いている冊を先に選ぶ
            int empty = Enumerable.Range(0, SaveStore.SlotCount).FirstOrDefault(i => _data[i] is null, -1);
            _index = empty >= 0 ? empty : 0;
        }
        _band = _index;
    }

    private void Reload()
    {
        try { _data = S.Store.LoadAll(); }
        catch (IOException) { _data = new SaveData?[SaveStore.SlotCount]; }
        catch (UnauthorizedAccessException) { _data = new SaveData?[SaveStore.SlotCount]; }
    }

    private int SafeLatest()
    {
        try { return S.Store.LatestSlot(); }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static Rectangle CardRect(int i) => new(300, 196 + (i * 250), 1320, 216);

    private Rectangle ActionsRect
    {
        get
        {
            var c = CardRect(_index);
            int h = (int)(_actions.Items.Count * _actions.RowHeight);
            int y = Math.Clamp(c.Center.Y - (h / 2), 160, Gfx.Height - 140 - h);
            return new Rectangle(c.Right - 500, y, 440, h);
        }
    }

    private static Rectangle ConfirmMenuRect => new(Gfx.Width / 2 - 260, 600, 520, 64 * 2);

    private void SetStage(Stage st)
    {
        _stage = st;
        _stageT = 0;
    }

    private void OpenActions()
    {
        var d = _data[_index];
        _choices.Clear();
        var items = new List<MenuItem>();
        if (d is not null)
        {
            _choices.AddRange([Choice.Continue, Choice.New, Choice.Delete, Choice.Cancel]);
            items.AddRange([new("この冒険をつづける"), new("新しくはじめる（上書き）"), new("この冒険の書を消す"), new("やめる")]);
        }
        else
        {
            _choices.AddRange([Choice.New, Choice.Cancel]);
            items.AddRange([new("ここに新しい冒険をはじめる"), new("やめる")]);
        }
        _actions.SetItems(items, keepIndex: false);
        _actions.Index = d is not null && !continueMode ? 1 : 0;
        SetStage(Stage.Actions);
    }

    protected override void Update(float dt)
    {
        _fx.Update(dt);
        _stageT += dt;
        for (int i = 0; i < _flash.Length; i++) _flash[i] = Math.Max(0, _flash[i] - (dt * 2.5f));
        _band += (_index - _band) * Math.Min(1, dt * 16);
        _ember += dt;
        var r = new Random((int)(Time * 1000));
        while (_ember > 0.1f)
        {
            _ember -= 0.1f;
            _fx.Add(new Particle { Pos = new Vector2(r.Next(0, Gfx.Width), Gfx.Height + 10), Vel = new Vector2(r.Next(-25, 25), r.Next(-110, -50)), Life = 6, Size = r.Next(5, 10), EndSize = 4, Color = new Color(255, 210, 120), EndColor = new Color(200, 60, 20) * 0.3f, Shape = ParticleShape.Pixel, Additive = true });
        }
        // 冊がすべり込みきるまでは選べない
        if (Time < ReadyAt) return;

        switch (_stage)
        {
            case Stage.List:
            {
                int before = _index;
                if (In.Repeat(Act.Up)) _index = (_index + SaveStore.SlotCount - 1) % SaveStore.SlotCount;
                if (In.Repeat(Act.Down)) _index = (_index + 1) % SaveStore.SlotCount;
                int hovered = -1;
                for (int i = 0; i < SaveStore.SlotCount; i++)
                {
                    if (CardRect(i).Contains(In.Mouse)) hovered = i;
                }
                if (hovered >= 0 && In.MouseMoved) _index = hovered;
                if (_index != before) S.Cue(Cue.Cursor);
                if (In.Pressed(Act.Confirm) || (In.MouseClicked && hovered == _index))
                {
                    S.Cue(Cue.Confirm);
                    OpenActions();
                }
                else if (In.Pressed(Act.Cancel) || In.MouseRightClicked)
                {
                    S.Cue(Cue.Cancel);
                    S.Game.Scenes.Go(new TitleScene(S, quick: true));
                }
                break;
            }
            case Stage.Actions:
            {
                if (_stageT < 0.2f) return;
                if (In.Pressed(Act.Cancel) || In.MouseRightClicked)
                {
                    S.Cue(Cue.Cancel);
                    SetStage(Stage.List);
                    return;
                }
                int i = _actions.Update(S, ActionsRect);
                if (i < 0) return;
                switch (_choices[i])
                {
                    case Choice.Continue:
                    {
                        RunState? run = null;
                        try { run = S.Store.LoadRun(_index + 1); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                        if (run is null)
                        {
                            S.Cue(Cue.Buzzer);
                            Reload();
                            SetStage(Stage.List);
                            return;
                        }
                        S.Game.Scenes.Go(new DungeonScene(S, run, fromSave: true));
                        break;
                    }
                    case Choice.New:
                        if (_data[_index] is not null)
                        {
                            _pending = Choice.New;
                            OpenConfirm();
                        }
                        else
                        {
                            S.Game.Scenes.Go(new CreateScene(S, _index + 1));
                        }
                        break;
                    case Choice.Delete:
                        _pending = Choice.Delete;
                        OpenConfirm();
                        break;
                    default:
                        SetStage(Stage.List);
                        break;
                }
                break;
            }
            case Stage.Confirm:
            {
                if (_stageT < 0.22f) return;
                int i = _confirm.Update(S, ConfirmMenuRect);
                if (In.Pressed(Act.Cancel) || In.MouseRightClicked || i == 0)
                {
                    if (i != 0) S.Cue(Cue.Cancel);
                    SetStage(Stage.Actions);
                    return;
                }
                if (i != 1) return;
                if (_pending == Choice.Delete)
                {
                    try { S.Store.DeleteSave(_index + 1); }
                    catch (IOException) { S.Cue(Cue.Buzzer); }
                    catch (UnauthorizedAccessException) { S.Cue(Cue.Buzzer); }
                    // 冊が砕けて、からっぽになる
                    _fx.Shatter(CardRect(_index), new Color(220, 190, 120), 220);
                    _flash[_index] = 1;
                    S.Cue(Cue.BigDamage, 0.7f);
                    Reload();
                    SetStage(Stage.List);
                }
                else
                {
                    S.Game.Scenes.Go(new CreateScene(S, _index + 1));
                }
                break;
            }
        }
    }

    private void OpenConfirm()
    {
        _confirm.SetItems([new("いいえ"), new(_pending == Choice.Delete ? "はい（消す）" : "はい（上書きする）")], keepIndex: false);
        _confirm.Index = 0;
        SetStage(Stage.Confirm);
    }

    // ------------------------------------------------------------------

    public override void Draw()
    {
        var g = G;
        var b = g.Batch;
        b.Begin();
        Art.Cover(g, S.Assets.Background("dungeon"), 1.05f + (0.02f * MathF.Sin(Time * 0.12f)), default, new Color(80, 80, 95));
        Art.Vignette(g, 0.7f, 0.8f);
        b.End();
        b.Begin(blendState: Microsoft.Xna.Framework.Graphics.BlendState.Additive);
        _fx.DrawAdditive(g);
        b.End();
        b.Begin();

        // 見出し
        string head = continueMode ? "どの冒険をつづけますか？" : "どの冒険の書に記録しますか？";
        float hk = Ease.OutCubic(Time / 0.35f);
        g.TextFx(head, new Vector2(Gfx.Width / 2f, 100 - ((1 - hk) * 30)), 56, Palette.Gold * hk);
        float hw = g.Measure(head, 56).X;
        float lw = 150 * Ease.OutCubic(Ease.Span(Time, 0.1f, 0.5f));
        g.Rect((Gfx.Width / 2f) - (hw / 2) - 36 - lw, 98, lw, 3, Palette.Frame);
        g.Rect((Gfx.Width / 2f) + (hw / 2) + 36, 98, lw, 3, Palette.Frame);
        g.TextCentered($"冒険の書は {SaveStore.SlotCount} 冊まで。それぞれ別の冒険を記録できます", new Vector2(Gfx.Width / 2f, 150), 26, Palette.Dim * hk);

        for (int i = 0; i < SaveStore.SlotCount; i++) DrawCard(g, i);
        b.End();

        b.Begin();
        _fx.DrawNormal(g);
        b.End();
        b.Begin();
        if (_stage is Stage.Actions or Stage.Confirm)
        {
            var ar = ActionsRect;
            var wr = new Rectangle(ar.X - 26, ar.Y - 20, ar.Width + 52, ar.Height + 40);
            float open = _stage == Stage.Actions ? _stageT / 0.2f : 1;
            g.Window(open < 1 ? Gfx.Opening(wr, open) : wr, 0.97f, border: Palette.Gold);
            if (open >= 1)
            {
                _actions.Reveal = _stage == Stage.Actions ? _stageT - 0.2f : 99;
                _actions.Draw(g, ar, Time, _stage == Stage.Actions);
            }
        }
        if (_stage == Stage.Confirm) DrawConfirm(g);
        if (_stage == Stage.List) Hints.Draw(g, In, ("↑↓", "えらぶ"), Hints.Confirm(In), Hints.Cancel(In));
        else Hints.Draw(g, In, Hints.Confirm(In), Hints.Cancel(In));
        b.End();
    }

    private void DrawCard(Gfx g, int i)
    {
        var d = _data[i];
        float enter = Ease.OutCubic(Ease.Span(Time, 0.05f + (i * 0.08f), 0.45f + (i * 0.08f)));
        if (enter <= 0) return;
        float sel = Math.Clamp(1 - MathF.Abs(_band - i), 0, 1);
        var r = CardRect(i);
        r.Offset((int)((1 - enter) * 900) - (int)(18 * sel), 0);
        bool dim = _stage != Stage.List && i != _index;

        if (sel > 0.05f) g.Glow(r.Center.ToVector2(), 620, Palette.Gold * (0.12f * sel));
        var fill = d is null ? new Color(14, 16, 30, 230) : (Color?)null;
        g.Window(r, enter * (dim ? 0.55f : 1f), fill, Color.Lerp(Palette.BorderInner, Palette.Gold, sel));
        float a = enter * (dim ? 0.45f : 1f) * (d is null ? 0.85f : 1f);

        // 冊の番号（ひし形の札）
        var em = new Vector2(r.X + 78, r.Center.Y);
        g.Batch.Draw(g.Pixel, em, null, Color.Black * a, MathF.PI / 4, new Vector2(0.5f, 0.5f), 74, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0);
        g.Batch.Draw(g.Pixel, em, null, Color.Lerp(Palette.Frame, Palette.Gold, sel) * a, MathF.PI / 4, new Vector2(0.5f, 0.5f), 64, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0);
        g.Batch.Draw(g.Pixel, em, null, new Color(40, 26, 10) * a, MathF.PI / 4, new Vector2(0.5f, 0.5f), 52, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0);
        g.TextFx((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), em, 44, Palette.Gold * a);

        if (d is null)
        {
            g.TextCentered("― からっぽ ―", new Vector2(r.Center.X + 40, r.Center.Y - (continueMode ? 0 : 16)), 40, Palette.Dim * a);
            if (!continueMode) g.TextCentered("新しい冒険を記録できます", new Vector2(r.Center.X + 40, r.Center.Y + 34), 26, Palette.Disabled * a);
        }
        else
        {
            var h = d.Hero ?? new HeroData();
            // 顔
            var face = new Rectangle(r.X + 150, r.Y + 30, 156, 156);
            g.Rect(new Rectangle(face.X - 4, face.Y - 4, face.Width + 8, face.Height + 8), Color.Black * a);
            g.Rect(face, new Color(30, 40, 80) * a);
            Art.HeroBust(g, S.Assets.Hero(h.Gender), face, Color.White * a);
            g.Outline(new Rectangle(face.X - 4, face.Y - 4, face.Width + 8, face.Height + 8), Palette.Frame * a, 2);

            float x = face.Right + 34;
            g.Text(string.IsNullOrWhiteSpace(h.Name) ? "？？？" : h.Name, new Vector2(x, r.Y + 22), 44, Color.White * a);
            // Lv と難しさの札
            float tx = x + g.Measure(h.Name ?? "", 44).X + 24;
            DrawTag(g, $"Lv {h.Level}", new Vector2(tx, r.Y + 30), Palette.Gold, a);
            DrawTag(g, Names.Of(d.Difficulty), new Vector2(tx + g.Measure($"Lv {h.Level}", 26).X + 40, r.Y + 30), d.Difficulty switch { Difficulty.Easy => Palette.Good, Difficulty.Hard => Palette.Bad, _ => Palette.Cursor }, a);

            int floor = Math.Clamp(d.Floor, 1, Progression.TopFloor);
            var biome = Biome.ForFloor(floor);
            g.Rect(x, r.Y + 96, 16, 16, Palette.Of(biome.Led) * a);
            g.Text($"地下 {floor} 階　{biome.Name}", new Vector2(x + 28, r.Y + 84), 34, Color.White * a);
            g.Text($"倒した敵 {d.BattlesWon}", new Vector2(x + 440, r.Y + 88), 28, Palette.Dim * a);
            string saved = d.SavedAt == default ? "" : d.SavedAt.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            g.Text($"冒険の時間 {TimeSpan.FromSeconds(Math.Max(0, d.PlaySeconds)):h\\:mm\\:ss}　　最後の記録 {saved}", new Vector2(x, r.Y + 136), 26, Palette.Dim * a);

            // 深さの目盛り
            var bar = new Rectangle((int)x, r.Bottom - 34, r.Right - 40 - (int)x, 10);
            g.Rect(new Rectangle(bar.X - 2, bar.Y - 2, bar.Width + 4, bar.Height + 4), Color.Black * (0.7f * a));
            for (int k = 0; k < 10; k++)
            {
                int x0 = bar.X + (bar.Width * k / 10), x1 = bar.X + (bar.Width * (k + 1) / 10);
                float done = Math.Clamp((floor - (k * 10)) / 10f, 0, 1);
                var c = Palette.Of(Biome.All[k].Led);
                g.Rect(new Rectangle(x0, bar.Y, x1 - x0 - 2, bar.Height), c * (0.2f * a));
                g.Rect(new Rectangle(x0, bar.Y, (int)((x1 - x0 - 2) * done), bar.Height), c * (0.85f * a));
            }
        }
        if (_flash[i] > 0) g.Rect(r, Color.White * (0.6f * _flash[i]));
    }

    private static void DrawTag(Gfx g, string text, Vector2 at, Color c, float a)
    {
        var w = g.Measure(text, 26).X + 20;
        var tr = new Rectangle((int)at.X, (int)at.Y, (int)w, 38);
        g.Rect(tr, c * (0.25f * a));
        g.Outline(tr, c * a, 2);
        g.TextCentered(text, new Vector2(tr.Center.X, tr.Center.Y), 26, Color.White * a);
    }

    private void DrawConfirm(Gfx g)
    {
        g.Rect(new Rectangle(0, 0, Gfx.Width, Gfx.Height), Color.Black * (0.5f * Math.Min(1, _stageT / 0.2f)));
        var r = new Rectangle(Gfx.Width / 2 - 470, 380, 940, 400);
        float open = _stageT / 0.22f;
        bool del = _pending == Choice.Delete;
        g.Window(open < 1 ? Gfx.Opening(r, open) : r, 1f, border: del ? Palette.Bad : Palette.Gold);
        if (open < 1) return;
        var d = _data[_index];
        string who = d is null ? "" : $"（{d.Hero?.Name}・Lv {d.Hero?.Level}・地下 {d.Floor} 階）";
        g.TextCentered(del ? $"冒険の書 {_index + 1} を消しますか？" : $"冒険の書 {_index + 1} に上書きしますか？", new Vector2(r.Center.X, r.Y + 64), 40, del ? new Color(255, 160, 150) : Color.White);
        g.TextCentered(who, new Vector2(r.Center.X, r.Y + 118), 28, Palette.Dim);
        g.TextCentered(del ? "消した冒険は、もとに戻せません。" : "いまの記録は、新しい冒険を始めると消えます。", new Vector2(r.Center.X, r.Y + 166), 30, Palette.Text);
        _confirm.Reveal = _stageT - 0.22f;
        _confirm.Draw(g, ConfirmMenuRect, Time);
    }
}
