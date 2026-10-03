using Abbild.Core;
using Abbild.Engine;
using Abbild.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Abbild.Scenes;

/// <summary>名前・見た目・難しさを選び、体を使った測定で最初の能力を決める。</summary>
public sealed class CreateScene(Services s) : Scene(s)
{
    private enum Step { Name, Gender, Difficulty, TrialChoice, Trials, Result }

    private static readonly string[] Grid =
    [
        "アイウエオハヒフヘホ",
        "カキクケコマミムメモ",
        "サシスセソヤ　ユ　ヨ",
        "タチツテトラリルレロ",
        "ナニヌネノワヲンー・",
        "ガギグゲゴバビブベボ",
        "ザジズゼゾパピプペポ",
        "ダヂヅデドァィゥェォ",
        "ャュョッヴ☆♪！？　",
    ];

    private const int MaxName = 8;
    private static readonly string[] Presets = ["アルト", "リオ", "ミナ", "カイ", "セナ", "ハルキ"];

    private Step _step = Step.Name;
    private string _name = "";
    private int _col, _row;
    private Gender _gender = Gender.Male;
    private Difficulty _difficulty = Difficulty.Normal;
    private readonly Menu _menu = new() { RowHeight = 76, FontSize = 42 };
    private ChallengeView? _trial;
    private int _trialIndex;
    private double _heart = 72;
    private int _shakes;
    private int _mashes;
    private StartingStats _stats = StartingStats.Default;
    private bool _measured;

    // 演出
    private float _stepT;
    private readonly Fx _fx = new();
    private float _ember;
    private Vector4 _cursor;
    private float _genderK;

    public override void Enter()
    {
        S.Audio.PlayBgm("title");
        In.TextMode = true;
    }

    public override void Leave() => In.TextMode = false;

    private void GoStep(Step st)
    {
        _step = st;
        _stepT = 0;
        In.TextMode = st == Step.Name;
        switch (st)
        {
            case Step.Difficulty:
                _menu.SetItems(Enum.GetValues<Difficulty>().Select(d => new MenuItem(Names.Of(d), Description: Names.Describe(d))), keepIndex: false);
                _menu.Index = (int)_difficulty;
                break;
            case Step.TrialChoice:
                _menu.SetItems(
                [
                    new MenuItem("体で測定する（おすすめ）", Description: "心・技・力の 3 つを測って、能力と才能を決めます"),
                    new MenuItem("おまかせ（平均的な能力）", Description: "測定をとばして、バランスのよい能力で始めます"),
                ], keepIndex: false);
                break;
            case Step.Result:
                _menu.SetItems([new MenuItem("この能力で冒険へ！"), new MenuItem("測りなおす", _measured || true)], keepIndex: false);
                break;
        }
    }

    protected override void Update(float dt)
    {
        _stepT += dt;
        _fx.Update(dt);
        _ember += dt;
        var rnd = new Random((int)(Time * 1000));
        while (_ember > 0.09f)
        {
            _ember -= 0.09f;
            _fx.Add(new Particle { Pos = new Vector2(rnd.Next(0, Gfx.Width), Gfx.Height + 10), Vel = new Vector2(rnd.Next(-25, 25), rnd.Next(-120, -50)), Life = 5 + ((float)rnd.NextDouble() * 4), Size = rnd.Next(5, 10), EndSize = 4, Color = new Color(255, 210, 120), EndColor = new Color(200, 60, 20) * 0.3f, Shape = ParticleShape.Pixel, Additive = true });
        }
        _genderK += ((_gender == Gender.Female ? 1 : 0) - _genderK) * Math.Min(1, dt * 12);
        // 結果が出た瞬間に、才能の札が押されて火花が散る
        if (_step == Step.Result && _stepT - dt < 1.05f && _stepT >= 1.05f)
        {
            _fx.Sparks(new Vector2(780, 420), 30, Palette.Gold, 520);
            S.Cue(Cue.Coin);
        }
        // 画面が出そろうまでは選べない（前の画面の連打で決めてしまわないように）。結果の画面は決定で早送り
        float ready = _step switch { Step.Name => 0.3f, Step.Difficulty or Step.TrialChoice => 0.35f, Step.Result => 1.55f, _ => 0f };
        if (_stepT < ready)
        {
            if (_step == Step.Result && _stepT > 0.3f && In.Pressed(Act.Confirm)) _stepT = ready;
            return;
        }
        switch (_step)
        {
            case Step.Name: UpdateName(); break;
            case Step.Gender: UpdateGender(); break;
            case Step.Difficulty:
            {
                int i = _menu.Update(S, MenuRect);
                if (i >= 0) { _difficulty = (Difficulty)i; GoStep(Step.TrialChoice); }
                else if (In.Pressed(Act.Cancel)) { S.Cue(Cue.Cancel); GoStep(Step.Gender); }
                break;
            }
            case Step.TrialChoice:
            {
                int i = _menu.Update(S, MenuRect);
                if (i == 0) StartTrials();
                else if (i == 1)
                {
                    _stats = StartingStats.Default;
                    _measured = false;
                    GoStep(Step.Result);
                }
                else if (In.Pressed(Act.Cancel)) { S.Cue(Cue.Cancel); GoStep(Step.Difficulty); }
                break;
            }
            case Step.Trials: UpdateTrials(dt); break;
            case Step.Result:
            {
                int i = _menu.Update(S, ResultMenuRect);
                if (i == 0) Begin();
                else if (i == 1) StartTrials();
                else if (In.Pressed(Act.Cancel)) { S.Cue(Cue.Cancel); GoStep(Step.TrialChoice); }
                break;
            }
        }
    }

    private static Rectangle MenuRect => new(560, 560, 800, 76 * 3);

    private static Rectangle ResultMenuRect => new(560, 640, 800, 76 * 2);

    private void UpdateName()
    {
        // キーボードで直接打った文字（日本語入力も可）
        foreach (char c in In.Typed)
        {
            if (_name.Length < MaxName && !char.IsWhiteSpace(c)) _name += c;
        }
        if (In.BackspacePressed && _name.Length > 0) _name = _name[..^1];
        if (In.KeyPressed(Keys.Enter))
        {
            FinishName();
            return;
        }
        int rows = Grid.Length + 1;
        if (In.Repeat(Act.Up)) { _row = (_row + rows - 1) % rows; S.Cue(Cue.Cursor); }
        if (In.Repeat(Act.Down)) { _row = (_row + 1) % rows; S.Cue(Cue.Cursor); }
        int cols = _row == Grid.Length ? 3 : 10;
        if (_col >= cols) _col = cols - 1;
        if (In.Repeat(Act.Left)) { _col = (_col + cols - 1) % cols; S.Cue(Cue.Cursor); }
        if (In.Repeat(Act.Right)) { _col = (_col + 1) % cols; S.Cue(Cue.Cursor); }

        // マウス
        for (int r = 0; r <= Grid.Length; r++)
        {
            int cn = r == Grid.Length ? 3 : 10;
            for (int c = 0; c < cn; c++)
            {
                if (CellRect(r, c).Contains(In.Mouse) && In.MouseMoved) { _row = r; _col = c; }
            }
        }
        bool click = In.MouseClicked && CellRect(_row, _col).Contains(In.Mouse);
        if (In.Pressed(Act.Confirm) || click)
        {
            if (_row == Grid.Length)
            {
                switch (_col)
                {
                    case 0: if (_name.Length > 0) { _name = _name[..^1]; S.Cue(Cue.Cancel); } break;
                    case 1: _name = Presets[(Array.IndexOf(Presets, _name) + 1) % Presets.Length]; S.Cue(Cue.Confirm); break;
                    default: FinishName(); break;
                }
                return;
            }
            char ch = Grid[_row][_col];
            if (ch != '　' && _name.Length < MaxName)
            {
                _name += ch;
                S.Cue(Cue.Confirm);
            }
        }
        if (In.Pressed(Act.Cancel))
        {
            if (_name.Length > 0) { _name = _name[..^1]; S.Cue(Cue.Cancel); }
            else { S.Cue(Cue.Cancel); S.Game.Scenes.Go(new TitleScene(S, quick: true)); }
        }
    }

    private void FinishName()
    {
        if (_name.Length == 0) _name = Presets[0];
        S.Cue(Cue.Success);
        GoStep(Step.Gender);
    }

    private static Rectangle CellRect(int r, int c)
    {
        if (r == Grid.Length) return new Rectangle(500 + (c * 310), 330 + (r * 66) + 10, 290, 62);
        return new Rectangle(500 + (c * 92), 330 + (r * 66), 84, 60);
    }

    private void UpdateGender()
    {
        if (In.Repeat(Act.Left) || In.Repeat(Act.Right))
        {
            _gender = _gender == Gender.Male ? Gender.Female : Gender.Male;
            S.Cue(Cue.Cursor);
        }
        var left = new Rectangle(460, 260, 440, 560);
        var right = new Rectangle(1020, 260, 440, 560);
        if (In.MouseMoved)
        {
            if (left.Contains(In.Mouse)) _gender = Gender.Male;
            if (right.Contains(In.Mouse)) _gender = Gender.Female;
        }
        if (In.Pressed(Act.Confirm) || (In.MouseClicked && (left.Contains(In.Mouse) || right.Contains(In.Mouse))))
        {
            S.Cue(Cue.Confirm);
            GoStep(Step.Difficulty);
        }
        else if (In.Pressed(Act.Cancel))
        {
            S.Cue(Cue.Cancel);
            GoStep(Step.Name);
        }
    }

    private void StartTrials()
    {
        _trialIndex = 0;
        _measured = true;
        GoStep(Step.Trials);
        NewTrial();
    }

    private static readonly ChallengeKind[] TrialKinds = [ChallengeKind.HeartTrial, ChallengeKind.ShakeTrial, ChallengeKind.MashTrial];

    private void NewTrial()
    {
        var ctx = new ChallengeContext(S.Body.Mode, S.Body.Thresholds(S.Settings), (ulong)Environment.TickCount64);
        _trial = new ChallengeView(S, TrialKinds[_trialIndex], ctx, cancelable: _trialIndex == 0,
            lead: $"測定 {_trialIndex + 1} / 3").WithActors(S.Assets.Hero(_gender), null);
    }

    private void UpdateTrials(float dt)
    {
        if (_trial is null) return;
        _trial.Update(dt);
        if (!_trial.Finished) return;
        if (_trial.Cancelled)
        {
            _trial = null;
            GoStep(Step.TrialChoice);
            return;
        }
        var o = _trial.Outcome;
        switch (TrialKinds[_trialIndex])
        {
            case ChallengeKind.HeartTrial:
                _heart = o.Score;
                if (S.Body.Mode == BodyMode.Sensor && o.Score is > 40 and < 130)
                {
                    // 本物の心拍なら、安静時の基準として覚えておく
                    S.Settings.RestingBpm = o.Score;
                    S.SaveSettings();
                }
                break;
            case ChallengeKind.ShakeTrial: _shakes = o.Count; break;
            case ChallengeKind.MashTrial: _mashes = o.Count; break;
        }
        _trialIndex++;
        if (_trialIndex < TrialKinds.Length)
        {
            NewTrial();
            return;
        }
        _trial = null;
        // キーボードの左右交互は振るより速いので、少し割り引いて同じ尺度にそろえる
        int shakes = S.Body.Mode == BodyMode.Keys ? (int)Math.Round(_shakes * 0.75) : _shakes;
        _stats = StartingStats.FromTrials(_heart, shakes, _mashes);
        S.Cue(Cue.LevelUp);
        S.Controller.Led(LedColor.White);
        GoStep(Step.Result);
    }

    private void Begin()
    {
        var hero = Hero.Create(_name, _gender, _stats, _difficulty);
        var run = RunState.Start(hero, _difficulty, (ulong)DateTime.Now.Ticks);
        S.Game.Scenes.Go(new StoryScene(S, StoryScene.Prologue, "corridor", null, () => new DungeonScene(S, run, fromSave: false)), 0.6f);
    }

    public override void Draw()
    {
        var g = G;
        g.Batch.Begin();
        Art.Cover(g, S.Assets.Background("dungeon"), 1.05f + (0.02f * MathF.Sin(Time * 0.12f)), new Vector2(MathF.Sin(Time * 0.08f) * 10, 0), new Color(90, 90, 105));
        Art.Vignette(g);
        g.Batch.End();
        g.Batch.Begin(blendState: Microsoft.Xna.Framework.Graphics.BlendState.Additive);
        _fx.DrawAdditive(g);
        g.Batch.End();
        g.Batch.Begin();
        string head = _step switch
        {
            Step.Name => "あなたの名前は？",
            Step.Gender => "姿をえらぶ",
            Step.Difficulty => "難しさをえらぶ",
            Step.TrialChoice or Step.Trials => "あなたの体で、強さが決まる",
            _ => "あなたの能力",
        };
        if (_step != Step.Trials)
        {
            // 見出し：上からすっと降り、両側の飾りの線がのびる
            float hk = Ease.OutCubic(_stepT / 0.35f);
            g.TextFx(head, new Vector2(Gfx.Width / 2f, 110 - ((1 - hk) * 30)), 64, Palette.Gold * hk);
            float hw = g.Measure(head, 64).X;
            float lw = 160 * Ease.OutCubic(Ease.Span(_stepT, 0.1f, 0.5f));
            g.Rect((Gfx.Width / 2f) - (hw / 2) - 40 - lw, 108, lw, 3, Palette.Frame);
            g.Rect((Gfx.Width / 2f) + (hw / 2) + 40, 108, lw, 3, Palette.Frame);
            DrawSteps(g);
        }

        switch (_step)
        {
            case Step.Name: DrawName(g); break;
            case Step.Gender: DrawGender(g); break;
            case Step.Difficulty:
            case Step.TrialChoice:
                DrawMenuStep(g);
                break;
            case Step.Result: DrawResult(g); break;
        }
        if (_step != Step.Trials)
        {
            Hints.Draw(g, In, Hints.Confirm(In), Hints.Cancel(In));
        }
        g.Batch.End();
        if (_step == Step.Trials) _trial?.Draw(g, Time);
    }

    private void DrawName(Gfx g)
    {
        var box = new Rectangle(660, 190, 600, 100);
        g.Window(box);
        string shown = _name + ((int)(Time * 2) % 2 == 0 && _name.Length < MaxName ? "＿" : "");
        g.TextCentered(shown.Length == 0 ? " " : shown, new Vector2(box.Center.X, box.Center.Y), 56, Color.White);
        var gridWin = new Rectangle(470, 312, 980, 690);
        float open = _stepT / 0.3f;
        if (open < 1)
        {
            g.Window(Gfx.Opening(gridWin, open), 0.9f);
            return;
        }
        g.Window(gridWin, 0.9f);
        // カーソル：文字から文字へなめらかに動き、縁が脈打つ
        var tr = CellRect(_row, _col);
        var target = new Vector4(tr.X, tr.Y, tr.Width, tr.Height);
        if (_cursor.Z == 0) _cursor = target;
        _cursor = Vector4.Lerp(_cursor, target, 0.35f);
        var cur = new Rectangle((int)MathF.Round(_cursor.X), (int)MathF.Round(_cursor.Y), (int)MathF.Round(_cursor.Z), (int)MathF.Round(_cursor.W));
        float pulse = 0.6f + (0.4f * MathF.Sin(Time * 7));
        g.Rect(cur, Palette.Cursor * 0.3f);
        g.Outline(new Rectangle(cur.X - 2, cur.Y - 2, cur.Width + 4, cur.Height + 4), Palette.Cursor * pulse, 3);
        for (int r = 0; r < Grid.Length; r++)
        {
            for (int c = 0; c < 10; c++)
            {
                char ch = Grid[r][c];
                var cell = CellRect(r, c);
                bool sel = r == _row && c == _col;
                if (ch != '　') g.TextCentered(ch.ToString(), new Vector2(cell.Center.X, cell.Center.Y), 44, sel ? Color.White : Palette.Text * 0.9f);
            }
        }
        string[] specials = ["けす", "おまかせ", "けってい"];
        for (int c = 0; c < 3; c++)
        {
            var cell = CellRect(Grid.Length, c);
            bool sel = _row == Grid.Length && _col == c;
            g.Rect(cell, (sel ? Palette.Cursor * 0.2f : new Color(40, 44, 70) * 0.8f));
            g.Outline(cell, sel ? Color.White : Palette.BorderInner, 2);
            g.TextCentered(specials[c], new Vector2(cell.Center.X, cell.Center.Y), 38, c == 2 ? Palette.Gold : Color.White);
        }
        g.TextCentered("キーボードで直接入力もできます（Enter で決定・Backspace で消す）", new Vector2(Gfx.Width / 2f, 1015), 26, Palette.Dim);
    }

    private void DrawGender(Gfx g)
    {
        for (int i = 0; i < 2; i++)
        {
            var gd = i == 0 ? Gender.Male : Gender.Female;
            // 選んでいる方へ 0 → 1（なめらかに切りかわる）
            float k = i == 0 ? 1 - _genderK : _genderK;
            float enter = Ease.OutBack(Ease.Span(_stepT, 0.05f + (i * 0.1f), 0.45f + (i * 0.1f)));
            if (enter <= 0) continue;
            var r = new Rectangle(i == 0 ? 460 : 1020, 260 - (int)(18 * k) + (int)((1 - enter) * 80), 440, 560);
            bool sel = gd == _gender;
            if (k > 0.05f)
            {
                g.Glow(r.Center.ToVector2(), 420, Palette.Gold * (0.18f * k));
            }
            g.Window(r, 0.6f + (0.4f * k), border: Color.Lerp(Palette.BorderInner, Palette.Gold, k));
            var tex = S.Assets.Hero(gd);
            float bob = MathF.Sin((Time * 2.4f) + i) * 4 * k;
            float sc = Math.Min(380f / tex.Width, 460f / tex.Height) * (0.92f + (0.08f * k));
            var pos = new Vector2(r.Center.X - (tex.Width * sc / 2), r.Bottom - 50 - (tex.Height * sc) + bob);
            g.Batch.Draw(tex, pos, null, Color.Lerp(new Color(70, 70, 90), Color.White, k) * enter, 0, Vector2.Zero, sc, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0);
            g.TextCentered(i == 0 ? "勇者（男）" : "勇者（女）", new Vector2(r.Center.X, r.Bottom + 40), 40, (sel ? Palette.Gold : Palette.Dim) * enter);
        }
        g.TextCentered($"{_name}", new Vector2(Gfx.Width / 2f, 200), 44, Color.White);
        g.TextCentered("見た目だけで、能力は変わりません", new Vector2(Gfx.Width / 2f, 940), 28, Palette.Dim);
    }

    private void DrawMenuStep(Gfx g)
    {
        var mr = MenuRect;
        if (_step == Step.TrialChoice)
        {
            var info = new Rectangle(360, 200, 1200, 320);
            g.Window(info, 0.95f);
            bool sensor = S.Body.Mode == BodyMode.Sensor;
            string[] lines = sensor
                ?
                [
                    "心：心拍センサーで脈を測る → 体力（HP）",
                    "技：コントローラーを振った回数 → 素早さ",
                    "力：決定ボタンを連打した回数 → 攻撃力",
                    "いちばん目立った能力から「才能」が決まります。",
                ]
                :
                [
                    "心：自分の鼓動のリズムでボタンを叩く → 体力（HP）",
                    "技：← → を交互に押した回数 → 素早さ",
                    "力：決定ボタンを連打した回数 → 攻撃力",
                    "いちばん目立った能力から「才能」が決まります。",
                ];
            float y = info.Y + 40;
            foreach (var l in lines)
            {
                g.Text(l, new Vector2(info.X + 70, y), 36, l.StartsWith("いちばん", StringComparison.Ordinal) ? Palette.Gold : Color.White);
                y += 64;
            }
            g.TextCentered(sensor ? "♥ 自作コントローラーで測ります" : "キーボード・ゲームパッドで測ります（自作コントローラーをつなぐと体で測れます）",
                new Vector2(Gfx.Width / 2f, 960), 28, sensor ? Palette.Good : Palette.Dim);
        }
        int rows = _menu.Items.Count;
        var mw = new Rectangle(mr.X - 30, mr.Y - 24, mr.Width + 60, (int)(rows * _menu.RowHeight) + 48 + 70);
        float open = Ease.Span(_stepT, 0.1f, 0.35f);
        if (open < 1)
        {
            if (open > 0) g.Window(Gfx.Opening(mw, open), 0.95f);
            return;
        }
        g.Window(mw, 0.95f);
        _menu.Reveal = _stepT - 0.35f;
        _menu.Draw(g, mr, Time);
        var desc = _menu.Selected?.Description ?? "";
        g.TextCentered(desc, new Vector2(Gfx.Width / 2f, mr.Y + (rows * _menu.RowHeight) + 40), 30, Palette.Dim);
    }

    private void DrawResult(Gfx g)
    {
        var r = new Rectangle(360, 190, 1200, 340);
        g.Window(r);
        var tex = S.Assets.Hero(_gender);
        Art.HeroBust(g, tex, new Rectangle(r.X + 40, r.Y + 40, 260, 260), Color.White);
        float x = r.X + 340;
        g.Text($"{_name}", new Vector2(x, r.Y + 36), 52, Color.White);
        g.Text($"難しさ：{Names.Of(_difficulty)}", new Vector2(x + 520, r.Y + 50), 32, Palette.Dim);
        // 数字が 0 から数え上がり、細いゲージがのびる
        var stats = new (string Label, int Value, int Max, Color C)[]
        {
            ("HP", _stats.MaxHp, 160, Palette.Hp),
            ("攻撃", _stats.Attack, 30, Palette.Gold),
            ("素早さ", _stats.Speed, 30, Palette.Cursor),
        };
        for (int i = 0; i < stats.Length; i++)
        {
            var (label, value, max, c) = stats[i];
            float k = Ease.OutCubic(Ease.Span(_stepT, 0.15f + (i * 0.15f), 0.85f + (i * 0.15f)));
            float sx = x + (i * 260);
            g.Text($"{label} {(int)MathF.Round(value * k)}", new Vector2(sx, r.Y + 112), 44, c);
            var bar = new Rectangle((int)sx, r.Y + 170, 220, 10);
            g.Rect(bar, Color.Black * 0.5f);
            g.Rect(new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Clamp(value / (float)max, 0, 1) * k), bar.Height), c);
        }
        // 才能の札：上からポンと押される
        float tk = Ease.Span(_stepT, 0.95f, 1.25f);
        if (tk > 0)
        {
            float stamp = 1 + ((1 - Ease.OutBack(tk)) * 1.2f);
            string tal = $"才能：{Names.Of(_stats.Talent)}";
            var tm = g.Measure(tal, 44);
            g.TextFx(tal, new Vector2(x + (tm.X / 2), r.Y + 222), 44, Palette.Good * Math.Min(1, tk * 3), stamp, -0.04f * (1 - tk));
            float dk = Ease.Span(_stepT, 1.2f, 1.5f);
            g.Text(Names.Describe(_stats.Talent), new Vector2(x, r.Y + 266), 32, Palette.Text * dk);
        }
        if (_measured)
        {
            string detail = S.Body.Mode == BodyMode.Sensor
                ? $"心拍 {_heart:0} / 振り {_shakes} 回 / 連打 {_mashes} 回"
                : $"鼓動 {_heart:0} / 左右 {_shakes} 回 / 連打 {_mashes} 回";
            g.TextCentered(detail, new Vector2(Gfx.Width / 2f, 572), 28, Palette.Dim);
        }
        var mr = ResultMenuRect;
        float mo = Ease.Span(_stepT, 1.3f, 1.55f);
        if (mo <= 0) return;
        var mw = new Rectangle(mr.X - 30, mr.Y - 24, mr.Width + 60, mr.Height + 48);
        g.Window(mo < 1 ? Gfx.Opening(mw, mo) : mw, 0.95f);
        if (mo < 1) return;
        _menu.Reveal = _stepT - 1.55f;
        _menu.Draw(g, mr, Time);
    }

    /// <summary>上の進み具合：名前 → 姿 → 難しさ → 測定 → 能力。</summary>
    private void DrawSteps(Gfx g)
    {
        string[] names = ["名前", "姿", "難しさ", "測定", "能力"];
        int cur = _step switch { Step.Name => 0, Step.Gender => 1, Step.Difficulty => 2, Step.TrialChoice or Step.Trials => 3, _ => 4 };
        float x0 = Gfx.Width / 2f - 320, gap = 160;
        float y = 30;
        g.Rect(x0, y - 1, gap * 4, 2, Palette.BorderInner * 0.6f);
        g.Rect(x0, y - 1, gap * cur, 2, Palette.Gold);
        for (int i = 0; i < names.Length; i++)
        {
            var p = new Vector2(x0 + (i * gap), y);
            bool done = i < cur, now = i == cur;
            float sz = now ? 12 + (2 * MathF.Sin(Time * 5)) : 9;
            g.Batch.Draw(g.Pixel, p, null, Color.Black, MathF.PI / 4, new Vector2(0.5f, 0.5f), sz + 5, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0);
            g.Batch.Draw(g.Pixel, p, null, now ? Palette.Gold : done ? Palette.Frame : Palette.Disabled, MathF.PI / 4, new Vector2(0.5f, 0.5f), sz, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0);
            g.TextCentered(names[i], p + new Vector2(0, 30), 22, now ? Color.White : done ? Palette.Dim : Palette.Disabled);
        }
    }

    // ---- 動作確認（自動操作）用 ----
    internal void Jump(string step, string? name = null)
    {
        if (name is not null) _name = name;
        switch (step)
        {
            case "gender": GoStep(Step.Gender); break;
            case "difficulty": GoStep(Step.Difficulty); break;
            case "trialchoice": GoStep(Step.TrialChoice); break;
            case "trials": StartTrials(); break;
            case "result":
                _stats = StartingStats.FromTrials(88, 26, 64);
                _measured = true;
                _heart = 88; _shakes = 26; _mashes = 64;
                GoStep(Step.Result);
                break;
        }
    }
}
