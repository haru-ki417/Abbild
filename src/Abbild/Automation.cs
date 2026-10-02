using Abbild.Core;
using Abbild.Engine;
using Abbild.Scenes;
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
        _script = (options.AutoPlay ? AutoPlay() : options.Only == "fx" ? FxGallery() : Snapshots()).GetEnumerator();
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
        using (var fs = File.Create(path)) target.SaveAsPng(fs, target.Width, target.Height);
        Log($"撮影 {_pendingSnap}");
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
        yield return new Frames(80);
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
        yield return new Frames(15);
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
        foreach (var w in Go(boss)) yield return w;
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
        yield return new Frames(30);
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
