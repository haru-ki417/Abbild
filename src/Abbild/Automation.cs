using Abbild.Core;
using Abbild.Engine;
using Abbild.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Abbild;

/// <summary>
/// 動作確認用の自動操作。--snapshots では決まった画面を順に撮影し、
/// --autoplay ではでたらめな操作で長く遊ばせて、落ちないことを確かめる。
/// </summary>
internal sealed class Automation(Services s, SceneManager scenes, LaunchOptions options)
{
    private abstract record AutoWait;
    private sealed record Frames(int Count, Func<int, Act[]>? Inject = null) : AutoWait;
    private sealed record Until(Func<bool> Done, int Max, Func<int, Act[]>? Inject = null, string What = "") : AutoWait;
    private sealed record Snap(string Name) : AutoWait;
    private sealed record Do(Action Action) : AutoWait;

    /// <summary>動きの確認用：Count フレームのあいだ、Every フレームごとに撮影する（Name/000.png …）。</summary>
    private sealed record Rec(string Name, int Count, int Every = 3, Func<int, Act[]>? Inject = null) : AutoWait;

    private IEnumerator<AutoWait>? _script;
    private AutoWait? _current;
    private int _frame;
    private string? _pendingSnap;
    private readonly Random _rnd = new(42);
    private readonly List<string> _log = [];

    public void Start()
    {
        s.Settings = new Settings { Fullscreen = false, BgmVolume = 0, SeVolume = 0, UseController = false, TextSpeed = 2 };
        s.ApplyAudioSettings();
        _script = (options.AutoPlay ? AutoPlay() : options.Only == "fx" ? FxGallery() : options.Only == "challenges" ? ChallengeGallery() : options.Only == "motion" ? MotionReel() : Snapshots()).GetEnumerator();
        Next();
    }

    private void Next()
    {
        while (_script is not null)
        {
            if (!_script.MoveNext())
            {
                _script = null;
                Log("おわり");
                Directory.CreateDirectory(OutDir);
                File.WriteAllLines(Path.Combine(OutDir, "automation.log"), _log);
                s.Game.Exit();
                return;
            }
            _current = _script.Current;
            _frame = 0;
            switch (_current)
            {
                case Do d:
                    d.Action();
                    continue;
                case Snap sn:
                    // 撮影が終わるまで、次へ進まない
                    _pendingSnap = sn.Name;
                    return;
            }
            return;
        }
    }

    private string OutDir => options.SnapshotDir ?? Path.Combine(Path.GetTempPath(), "abbild-autoplay");

    private void Log(string m)
    {
        string line = $"[{_frame,5}] {m}";
        _log.Add(line);
        Console.WriteLine(line);
    }

    public void BeforeUpdate(float dt)
    {
        _ = dt;
        switch (_current)
        {
            case Snap when _pendingSnap is null:
                Next();
                break;
            case Frames f:
                if (f.Inject is not null) s.Input.Inject(f.Inject(_frame));
                if (++_frame >= f.Count) Next();
                break;
            case Rec r:
                if (r.Inject is not null) s.Input.Inject(r.Inject(_frame));
                if (_frame % r.Every == 0) _pendingSnap = $"{r.Name}/{_frame / r.Every:000}";
                if (++_frame >= r.Count) Next();
                break;
            case Until u:
                if (u.Inject is not null) s.Input.Inject(u.Inject(_frame));
                _frame++;
                if (u.Done())
                {
                    Next();
                }
                else if (_frame >= u.Max)
                {
                    Log($"待ちきれず先へ: {u.What}");
                    Next();
                }
                break;
        }
    }

    public void AfterDraw(RenderTarget2D target)
    {
        if (_pendingSnap is null) return;
        Directory.CreateDirectory(OutDir);
        string path = Path.Combine(OutDir, _pendingSnap + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var fs = File.Create(path)) target.SaveAsPng(fs, target.Width, target.Height);
        if (!_pendingSnap.Contains('/', StringComparison.Ordinal)) Log($"撮影 {_pendingSnap}");
        _pendingSnap = null;
    }

    private static Func<int, Act[]> Every(int n, params Act[] acts) => i => i % n == n - 1 ? acts : [];

    private Scene? Cur => scenes.Current;

    private RunState NewRun(int level, int floor, Difficulty d = Difficulty.Normal, ulong seed = 7)
    {
        var hero = Hero.Create("ハルキ", Gender.Male, StartingStats.FromTrials(84, 24, 60), d);
        var rng = new GameRandom(99);
        while (hero.Level < level) hero.GainExp(hero.ExpToNext, rng);
        hero.Inventory.Add(new Item("whetstone"));
        hero.Inventory.Add(new Item("molotov", "burning"), 2);
        hero.Inventory.Add(new Item("onigiri", "moms"));
        var run = RunState.Start(hero, d, seed);
        run.Floor = floor;
        return run;
    }

    private IEnumerable<AutoWait> Go(Scene scene)
    {
        yield return new Do(() => scenes.Go(scene, 0.05f));
        yield return new Frames(12);
    }

    // ------------------------------------------------------------------

    private IEnumerable<AutoWait> Snapshots()
    {
        foreach (var w in Go(new TitleScene(s))) yield return w;
        yield return new Frames(150);
        yield return new Snap("01-title");

        var create = new CreateScene(s);
        foreach (var w in Go(create)) yield return w;
        yield return new Do(() => s.Input.InjectText("ハルキ"));
        yield return new Frames(20);
        yield return new Snap("02-create-name");
        yield return new Do(() => create.Jump("gender"));
        yield return new Frames(20);
        yield return new Snap("03-create-gender");
        yield return new Do(() => create.Jump("trialchoice"));
        yield return new Frames(10);
        yield return new Snap("04-create-trial-choice");
        yield return new Do(() => create.Jump("trials"));
        yield return new Frames(10);
        yield return new Snap("05-trial-intro");
        yield return new Frames(2, i => [Act.Confirm]);
        yield return new Frames(170, i => i % 26 == 0 ? [Act.Confirm] : []);
        yield return new Snap("06-trial-heart");
        yield return new Do(() => create.Jump("result"));
        yield return new Frames(10);
        yield return new Snap("07-create-result");

        foreach (var w in Go(new StoryScene(s, StoryScene.Prologue, "corridor", null, () => new TitleScene(s)))) yield return w;
        yield return new Frames(120);
        yield return new Snap("08-prologue");

        // 迷宮（平原）
        var run = NewRun(16, 3);
        var dungeon = new DungeonScene(s, run, fromSave: false);
        foreach (var w in Go(dungeon)) yield return w;
        yield return new Frames(55);
        yield return new Snap("09-floor-intro");
        yield return new Until(() => dungeon.AwaitingCommand, 900, Every(8, Act.Confirm), "コマンド");
        yield return new Frames(30);
        yield return new Snap("10-battle-command");
        yield return new Frames(3, i => i == 0 ? [Act.Down] : []);
        yield return new Frames(3, i => i == 0 ? [Act.Confirm] : []);
        yield return new Frames(20);
        yield return new Snap("11-battle-skills");
        yield return new Frames(3, i => i == 0 ? [Act.Cancel] : []);
        yield return new Frames(3, i => i == 0 ? [Act.Up] : []);
        yield return new Frames(3, i => i == 0 ? [Act.Confirm] : []);
        yield return new Frames(28);
        yield return new Snap("12-battle-attack");

        // 瞑想（呼吸のガイド）
        yield return new Until(() => dungeon.AwaitingCommand, 900, Every(8, Act.Confirm), "コマンド");
        yield return new Frames(3, i => i == 0 ? [Act.Down] : []);
        yield return new Frames(3, i => i == 0 ? [Act.Confirm] : []);
        foreach (var _ in Enumerable.Range(0, 5))
        {
            yield return new Frames(3, i => i == 0 ? [Act.Down] : []);
        }
        yield return new Frames(10);
        yield return new Snap("13-skill-select");
        yield return new Frames(3, i => i == 0 ? [Act.Confirm] : []);
        yield return new Frames(20);
        yield return new Snap("14-challenge-intro");
        yield return new Frames(3, i => i == 0 ? [Act.Confirm] : []);
        yield return new Frames(260, i => (i % 480) < 240 ? [Act.Confirm] : []);
        yield return new Snap("15-challenge-meditation");
        yield return new Until(() => dungeon.ActiveChallenge is null, 900, null, "瞑想おわり");

        // 毒の沼のボス
        var bossRun = NewRun(30, 50, seed: 11);
        var boss = new DungeonScene(s, bossRun, fromSave: false);
        // 場面の切り替え（ひし形のワイプ）の途中と、ボスの階の入り口
        yield return new Do(() => scenes.Go(boss, 0.5f));
        yield return new Frames(12);
        yield return new Snap("16a-transition");
        yield return new Frames(76);
        yield return new Snap("16b-boss-floor-intro");
        yield return new Until(() => boss.AwaitingCommand, 900, Every(8, Act.Confirm), "ボスのコマンド");
        yield return new Frames(40);
        yield return new Snap("16-boss");

        // 洞窟：敵の技（心眼など）が出るまで攻撃を続ける
        var caveRun = NewRun(40, 64, seed: 5);
        var cave = new DungeonScene(s, caveRun, fromSave: false);
        foreach (var w in Go(cave)) yield return w;
        yield return new Until(() => cave.ActiveChallenge is not null, 6000, Every(8, Act.Confirm), "敵の技");
        yield return new Frames(20);
        yield return new Snap("17-enemy-skill-intro");
        yield return new Frames(3, i => i == 0 ? [Act.Confirm] : []);
        yield return new Frames(200);
        yield return new Snap("18-enemy-skill-running");
        yield return new Until(() => cave.ShowingLevelUp, 9000, Every(8, Act.Confirm), "レベルアップ");
        yield return new Frames(100);
        yield return new Snap("19-levelup");

        foreach (var w in Go(new GameOverScene(s, caveRun))) yield return w;
        yield return new Frames(140);
        yield return new Snap("20-gameover");

        var endRun = NewRun(58, 100);
        endRun.PlaySeconds = 5432;
        endRun.BattlesWon = 99;
        foreach (var w in Go(new EndingScene(s, endRun))) yield return w;
        yield return new Frames(500);
        yield return new Snap("21-ending");

        foreach (var w in Go(new SettingsScene(s, () => new TitleScene(s)))) yield return w;
        yield return new Frames(20);
        yield return new Snap("22-settings");
        foreach (var w in Go(new SensorTestScene(s, () => new TitleScene(s)))) yield return w;
        yield return new Frames(90, i => i % 7 == 0 ? [i % 14 == 0 ? Act.Left : Act.Right] : []);
        yield return new Snap("23-sensors");
        foreach (var w in Go(new CreditsScene(s))) yield return w;
        yield return new Frames(20);
        yield return new Snap("24-credits");
        yield return new Frames(2);
    }

    // ------------------------------------------------------------------

    /// <summary>動きの確認：場面ごとに数フレームおきに撮影する（あとで GIF にする）。</summary>
    private IEnumerable<AutoWait> MotionReel()
    {
        // タイトル
        foreach (var w in Go(new TitleScene(s))) yield return w;
        yield return new Rec("m01-title", 156, 3);

        // キャラ作成（姿の切りかえ・能力の発表）とプロローグ
        var cr = new CreateScene(s);
        foreach (var w in Go(cr)) yield return w;
        yield return new Do(() => cr.Jump("gender", "ハルキ"));
        yield return new Rec("m01b-create-gender", 60, 3, i => i == 30 ? [Act.Right] : []);
        yield return new Do(() => cr.Jump("result"));
        yield return new Rec("m01c-create-result", 110, 3);
        foreach (var w in Go(new StoryScene(s, StoryScene.Prologue, "corridor", null, () => new TitleScene(s)))) yield return w;
        yield return new Rec("m01d-prologue", 120, 3);

        // 階の入り口 → 敵の登場
        var run = NewRun(16, 3);
        var d = new DungeonScene(s, run, fromSave: false);
        foreach (var w in Go(d)) yield return w;
        yield return new Rec("m02-floor-encounter", 200, 3);
        yield return new Until(() => d.AwaitingCommand, 900, Every(8, Act.Confirm), "コマンド");
        // コマンドが出てくるところと、カーソルの動き
        yield return new Do(() => { });
        yield return new Rec("m03-command", 70, 2, i => i is 24 or 34 ? [Act.Down] : i is 50 ? [Act.Up] : []);
        // 攻撃 → 敵の反撃
        yield return new Rec("m04-attack", 170, 2, i => i == 1 ? [Act.Up] : i == 4 ? [Act.Confirm] : (i > 60 && i % 20 == 0) ? [Act.Confirm] : []);
        yield return new Until(() => d.AwaitingCommand, 900, Every(8, Act.Confirm), "コマンド");
        // スキル（ファイア）のカットイン
        yield return new Rec("m05-skill", 150, 2, i => i switch { 1 => [Act.Down], 6 => [Act.Confirm], 26 => [Act.Confirm], _ => [] });

        // ボスの階：警告 → 登場
        var boss = new DungeonScene(s, NewRun(30, 50, seed: 11), fromSave: false);
        foreach (var w in Go(boss)) yield return w;
        yield return new Rec("m06-boss", 300, 3);
        yield return new Until(() => boss.AwaitingCommand, 900, Every(8, Act.Confirm), "ボスのコマンド");
        yield return new Rec("m06b-boss-turn", 240, 2, i => i == 1 ? [Act.Confirm] : (i > 90 && i % 30 == 0 && boss.ActiveChallenge is null) ? [Act.Confirm] : []);

        // 勝利 → レベルアップ
        var vr = NewRun(14, 1, seed: 21);
        vr.Hero.GainExp(vr.Hero.ExpToNext - 1, new GameRandom(1));
        var v = new DungeonScene(s, vr, fromSave: false);
        foreach (var w in Go(v)) yield return w;
        yield return new Until(() => v.AwaitingCommand, 900, Every(8, Act.Confirm), "コマンド");
        yield return new Rec("m07-victory", 330, 3, i => i == 1 ? [Act.Confirm] : (i > 120 && i % 45 == 0 && !v.ShowingLevelUp) ? [Act.Confirm] : []);

        // ゲームオーバーとエンディング
        var gr = NewRun(20, 33);
        yield return new Do(() => scenes.Go(new GameOverScene(s, gr), 0.05f));
        yield return new Rec("m08-gameover", 150, 3);
        yield return new Do(() => scenes.Go(new EndingScene(s, NewRun(58, 100)), 0.05f));
        yield return new Frames(400);
        yield return new Rec("m09-ending", 60, 3);
        yield return new Frames(2);
    }

    /// <summary>ミニゲームの舞台を、種類ごとに本番の途中で撮る。</summary>
    private IEnumerable<AutoWait> ChallengeGallery()
    {
        static Act[] Shake(int i) => (i % 6) switch { 0 => [Act.Left], 3 => [Act.Right], _ => [] };
        static Act[] Breathe(int i) => (i % 240) < 120 ? [Act.Confirm] : [];
        var list = new (ChallengeKind Kind, int Frames, Func<int, Act[]>? Inject, string Bg)[]
        {
            (ChallengeKind.HeartTrial, 200, Every(25, Act.Confirm), "dungeon"),
            (ChallengeKind.ShakeTrial, 150, Shake, "dungeon"),
            (ChallengeKind.MashTrial, 240, Every(4, Act.Confirm), "dungeon"),
            (ChallengeKind.Charge, 100, Shake, "plain"),
            (ChallengeKind.Alchemy, 120, Shake, "plain"),
            (ChallengeKind.Meditation, 200, Breathe, "temple"),
            (ChallengeKind.Glare, 120, null, "cave"),
            (ChallengeKind.Negotiation, 200, Breathe, "desert"),
            (ChallengeKind.Rage, 100, Every(3, Act.Confirm), "volcano"),
            (ChallengeKind.Dowsing, 60, i => [Act.Right], "desert"),
            (ChallengeKind.Bridge, 200, null, "ice"),
            (ChallengeKind.Fishing, 220, i => i == 2 ? [Act.Confirm] : i > 150 ? Shake(i) : [], "plain"),
            (ChallengeKind.BlindDefense, 80, null, "cave"),
            (ChallengeKind.Iai, 60, null, "temple"),
            (ChallengeKind.Blacksmith, 105, null, "volcano"),
            (ChallengeKind.Lockpick, 60, null, "demon"),
            (ChallengeKind.Thaw, 40, i => [Act.Breath], "ice"),
            (ChallengeKind.Breath, 70, i => [Act.Breath], "poison"),
            (ChallengeKind.EscapeRun, 150, Shake, "cave"),
            (ChallengeKind.Revive, 130, Every(5, Act.Confirm), "final"),
            (ChallengeKind.ShakeFree, 60, Shake, "divine"),
        };
        int n = 0;
        foreach (var item in list)
        {
            var scene = new ChallengeGalleryScene(s, item.Kind, item.Bg);
            foreach (var w in Go(scene)) yield return w;
            yield return new Do(() => scene.View.DebugBegin());
            yield return new Frames(item.Frames, item.Inject);
            yield return new Snap($"ch-{n++:00}-{item.Kind}");
            if (item.Kind is ChallengeKind.Iai or ChallengeKind.Lockpick)
            {
                if (item.Kind == ChallengeKind.Iai)
                {
                    yield return new Until(() => scene.View.Challenge is IaiChallenge { Signaled: true }, 400, null, "合図");
                    yield return new Frames(3, i => i == 1 ? [Act.Confirm] : []);
                }
                else
                {
                    yield return new Until(() => scene.View.Challenge.Lamp == LedColor.White, 400, null, "白");
                    yield return new Frames(2, i => i == 0 ? [Act.Confirm] : []);
                }
                yield return new Frames(30);
                yield return new Snap($"ch-{n++:00}-{item.Kind}-result");
            }
        }
        yield return new Frames(2);
    }

    /// <summary>ミニゲームだけを出す確認用の場面。</summary>
    private sealed class ChallengeGalleryScene : Scene
    {
        private readonly string _bg;

        public ChallengeGalleryScene(Services s, ChallengeKind kind, string bg) : base(s)
        {
            _bg = bg;
            var ctx = new ChallengeContext(s.Body.Mode, s.Body.Thresholds(s.Settings), 1234, 35);
            var def = EnemyFactory.Choose(new GameRandom(5), 24, DayOfWeek.Monday);
            View = new Abbild.Ui.ChallengeView(s, kind, ctx).WithActors(s.Assets.Hero(Gender.Male), s.Assets.Enemy(def.Sprite));
        }

        public Abbild.Ui.ChallengeView View { get; }

        protected override void Update(float dt) => View.Update(dt);

        public override void Draw()
        {
            G.Batch.Begin();
            Abbild.Ui.Art.Cover(G, S.Assets.Background(_bg), 1.02f, default, new Color(200, 200, 210));
            G.Batch.End();
            View.Draw(G, Time);
        }
    }

    private IEnumerable<AutoWait> FxGallery()
    {
        string[] kinds = ["slash", "crit", "fire", "ice", "thunder", "poison", "holy", "heal", "claw", "pillar", "shatter"];
        int[] floors = [3, 12, 25, 34, 45, 55, 66, 75, 85, 95, 50];
        for (int i = 0; i < kinds.Length; i++)
        {
            var d = new DungeonScene(s, NewRun(30, floors[i], seed: (ulong)(i + 3)), fromSave: false);
            foreach (var w in Go(d)) yield return w;
            yield return new Until(() => d.AwaitingCommand, 900, Every(8, Act.Confirm), "コマンド");
            yield return new Frames(120);
            string k = kinds[i];
            yield return new Do(() => d.DebugFx(k));
            yield return new Frames(k is "slash" or "crit" or "thunder" or "claw" ? 4 : k == "shatter" ? 14 : 10);
            yield return new Snap($"fx-{i:00}-{k}");
        }
        yield return new Frames(2);
    }

    private IEnumerable<AutoWait> AutoPlay()
    {
        int frames = (int)(options.AutoPlaySeconds * 60);
        int start = 0;
        int best = 0;
        var run = NewRun(1, 1, Difficulty.Easy, (ulong)_rnd.Next());
        foreach (var w in Go(new DungeonScene(s, run, fromSave: false))) yield return w;
        for (int t = 0; t < frames; t += 6)
        {
            var cur = Cur;
            Act[] acts = [];
            if (cur is DungeonScene d)
            {
                best = Math.Max(best, d.Run.Floor);
                if (d.ActiveChallenge is not null)
                {
                    acts = _rnd.Next(5) switch
                    {
                        0 => [Act.Left],
                        1 => [Act.Right],
                        2 => [Act.Breath],
                        _ => [Act.Confirm],
                    };
                }
                else if (d.AwaitingCommand)
                {
                    int r = _rnd.Next(100);
                    acts = r < 70 ? [Act.Confirm] : r < 85 ? [Act.Down] : [Act.Up];
                }
                else
                {
                    acts = _rnd.Next(4) == 0 ? [Act.Cancel] : [Act.Confirm];
                    if (d.PaneName is "System" or "ConfirmTitle") acts = [Act.Cancel];
                }
            }
            else if (cur is GameOverScene)
            {
                Log($"ゲームオーバー（B{best}F まで）");
                acts = [Act.Confirm];
            }
            else if (cur is TitleScene or SettingsScene or SensorTestScene or CreditsScene)
            {
                var nr = NewRun(1, 1, Difficulty.Easy, (ulong)_rnd.Next());
                foreach (var w in Go(new DungeonScene(s, nr, fromSave: false))) yield return w;
                continue;
            }
            else
            {
                acts = [Act.Confirm];
            }
            var a = acts;
            yield return new Frames(6, i => i == 0 ? a : []);
            if (t - start > 60 * 60)
            {
                start = t;
                Log($"{t / 60} 秒：B{best}F まで到達");
            }
        }
        Log($"自動プレイ終了：最高 B{best}F");
    }
}
