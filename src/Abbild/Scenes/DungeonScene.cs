using Abbild.Core;
using Abbild.Engine;
using Abbild.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Abbild.Scenes;

/// <summary>
/// 迷宮（1 階ごとに 1 戦）。戦いの流れはコルーチン（IEnumerator）で順番に書き、
/// 「待ち」（文章の表示・コマンド選択・ミニゲーム）を 1 つずつ進める。
/// </summary>
public sealed partial class DungeonScene : Scene
{
    // ---- 待ちの種類 ----
    private abstract class Wait
    {
        public abstract bool Update(DungeonScene d, float dt);
    }

    private sealed class WaitSeconds(float seconds, bool skippable = true) : Wait
    {
        private float _t;

        public override bool Update(DungeonScene d, float dt)
        {
            _t += dt;
            return _t >= seconds || (skippable && _t > 0.3f && d.In.Pressed(Act.Confirm));
        }
    }

    private sealed class WaitEvents(List<BattleEvent> events) : Wait
    {
        private int _i = -1;
        private float _t;
        private float _hold;

        public override bool Update(DungeonScene d, float dt)
        {
            if (_i < 0 || Current(d, dt))
            {
                _i++;
                while (_i < events.Count && events[_i].Kind == BattleEventKind.Message && string.IsNullOrEmpty(events[_i].Text) && events[_i].Cue == Cue.None)
                {
                    _i++;
                }
                if (_i >= events.Count) return true;
                _t = 0;
                var e = events[_i];
                _hold = d.Begin(e);
                // 「〜の攻撃！」のすぐあとに当たるときは、待たずにテンポよく
                if (e.Kind == BattleEventKind.Message && _i + 1 < events.Count && events[_i + 1].Kind is BattleEventKind.EnemyDamaged or BattleEventKind.EnemyAttack)
                {
                    _hold = Math.Min(_hold, 0.32f);
                }
            }
            return false;
        }

        private bool Current(DungeonScene d, float dt)
        {
            _t += dt;
            bool typing = !d._typer.Done;
            if (typing)
            {
                if (d.In.Pressed(Act.Confirm)) d._typer.Finish();
                return false;
            }
            float hold = _hold * d.HoldScale;
            return _t >= hold || (_t > 0.12f && d.In.Pressed(Act.Confirm)) || (_t > 0.12f && d.In.MouseClicked);
        }
    }

    private sealed class WaitChallenge(ChallengeView view) : Wait
    {
        public ChallengeView View { get; } = view;

        public override bool Update(DungeonScene d, float dt)
        {
            View.Update(dt);
            return View.Finished;
        }
    }

    private sealed class WaitCommand : Wait
    {
        public override bool Update(DungeonScene d, float dt) => d.UpdateCommand();
    }

    private sealed class WaitChoice(string question, params string[] options) : Wait
    {
        public string Question { get; } = question;
        public Menu Menu { get; } = MakeMenu(options);
        public int Result { get; private set; } = -1;

        /// <summary>出てからの時間（開く演出）。</summary>
        public float T { get; set; }

        private static Menu MakeMenu(string[] options)
        {
            var m = new Menu { RowHeight = 66, FontSize = 40 };
            m.SetItems(options.Select(o => new MenuItem(o)), keepIndex: false);
            return m;
        }

        public override bool Update(DungeonScene d, float dt)
        {
            T += dt;
            if (T < 0.22f) return false;
            Result = Menu.Update(d.S, ChoiceRect(Menu.Items.Count));
            return Result >= 0;
        }

        public static Rectangle ChoiceRect(int n) => new(700, 560 - (n * 66), 520, n * 66);
    }

    private sealed class WaitPanel(PanelKind kind, object data, float minTime = 0.6f, float autoTime = 6f) : Wait
    {
        private float _t;
        public PanelKind Kind { get; } = kind;
        public object Data { get; } = data;
        public float T => _t;

        public override bool Update(DungeonScene d, float dt)
        {
            _t += dt;
            return _t > minTime && (d.In.Pressed(Act.Confirm) || d.In.MouseClicked || _t > autoTime);
        }
    }

    private enum PanelKind { LevelUp, FloorIntro, Warning, Victory }

    private enum ActionStyle { Dash, CutIn, Item }

    /// <summary>こちらの行動の演出（攻撃の突進・スキルのカットイン・道具を投げる）。</summary>
    private sealed class WaitAction(ActionStyle style, string label, Element element, string? icon) : Wait
    {
        private float _t;
        public ActionStyle Style { get; } = style;
        public string Label { get; } = label;
        public Element Element { get; } = element;
        public string? Icon { get; } = icon;
        public float T => _t;

        public float Duration => Style switch { ActionStyle.CutIn => 0.8f, ActionStyle.Item => 0.45f, _ => 0.28f };

        public override bool Update(DungeonScene d, float dt)
        {
            _t += dt;
            return _t >= Duration || (Style == ActionStyle.CutIn && _t > 0.3f && d.In.Pressed(Act.Confirm));
        }
    }

    // ---- 状態 ----
    private readonly RunState _run;
    private readonly bool _fromSave;
    private IEnumerator<Wait>? _flow;
    private Wait? _wait;
    private bool _started;
    private BattleSession? _battle;
    private EnemyArt? _art;
    private PlayerChoice? _choice;
    private readonly Typewriter _typer = new();
    private readonly List<string> _log = [];
    private readonly List<Art.Popup> _popups = [];

    // 演出
    private float _shake;
    private float _heroFlash;
    private float _enemyFlash;
    private float _enemyShake;
    private float _enemyAttackT = -1;
    private float _enemyDeath = -1;
    private readonly Fx _fx = new();

    /// <summary>窓より手前に出すエフェクト（回復・こちらのダメージ・レベルアップ）。</summary>
    private readonly Fx _fxTop = new();
    private float _hitStop;
    private float _animTime;
    private float _redVignette;
    private float _enemyAppear;
    private float _heroPanelShake;
    private float _displayHp;
    private float _displayMp;
    private float _hpTrail;
    private float _enemyTrail;
    private float _enemyDisplayHp;

    /// <summary>敵が現れてからの時間（登場の演出）。</summary>
    private float _encounter = 99;

    /// <summary>当たってのけぞる強さ（1 → 0）。</summary>
    private float _enemyKnock;
    private float _cmdT;

    /// <summary>攻撃の突進の演出の時間（文章と並行して進む）。</summary>
    private float _dashT = 99;
    private float _paneT;
    private int _lastPane = -1;

    public DungeonScene(Services s, RunState run, bool fromSave) : base(s)
    {
        _run = run;
        _fromSave = fromSave;
        _displayHp = run.Hero.Hp;
        _displayMp = run.Hero.Mp;
        _recordedWins = run.BattlesWon;
    }

    /// <summary>記録（通算の勝利数）にもう足した勝利数。やり直しで二重に数えないため。</summary>
    private int _recordedWins;

    public RunState Run => _run;

    private float HoldScale => S.Settings.TextSpeed switch { 0 => 1.4f, 2 => 0.55f, _ => 1f };

    public override void Enter()
    {
        if (_started)
        {
            // 設定画面から戻ってきたとき
            S.Audio.PlayBgm(_battle?.Enemy.IsBoss == true ? "dungeon" : "dungeon");
            return;
        }
        _started = true;
        S.Audio.PlayBgm("dungeon");
        if (S.Controller.LatestWeather is { } w) _run.SetRealWeather(w);
        if (S.Controller.LatestDay is { } day) _run.Day = day;
        _flow = FloorFlow().GetEnumerator();
        Advance();
    }

    private void Advance()
    {
        while (_flow is not null)
        {
            if (!_flow.MoveNext())
            {
                _flow = null;
                _wait = null;
                return;
            }
            _wait = _flow.Current;
            if (_wait is not null) return;
        }
    }

    protected override void Update(float dt)
    {
        _run.PlaySeconds += dt;
        // ヒットストップ：当たった瞬間、ほんの少しだけ動きを止める
        float adt = dt;
        if (_hitStop > 0)
        {
            _hitStop -= dt;
            adt = dt * 0.05f;
        }
        _animTime += adt;
        _fx.Update(adt);
        _fxTop.Update(dt);
        if (_battle is not null || _wait is WaitPanel { Kind: PanelKind.FloorIntro }) _fx.Ambient(_run.Biome, dt);
        _redVignette = Math.Max(0, _redVignette - (dt * 1.5f));
        if (S.Controller.LatestWeather is { } w && (!_run.RealWeather || w != _run.Weather)) _run.SetRealWeather(w);

        // 演出の時間を進める
        _shake = Math.Max(0, _shake - (dt * 2.2f));
        _heroFlash = Math.Max(0, _heroFlash - (dt * 3));
        _enemyFlash = Math.Max(0, _enemyFlash - (dt * 4));
        _enemyShake = Math.Max(0, _enemyShake - (dt * 3));
        _heroPanelShake = Math.Max(0, _heroPanelShake - (dt * 3));
        if (_enemyAttackT >= 0) { _enemyAttackT += adt * 2.2f; if (_enemyAttackT > 1) _enemyAttackT = -1; }
        if (_enemyDeath >= 0) _enemyDeath = Math.Min(1, _enemyDeath + (adt * 2.2f));
        _enemyAppear = Math.Min(1, _enemyAppear + (dt * 1.6f));
        _dashT += dt;
        float prevEnc = _encounter;
        _encounter += dt;
        if (prevEnc < 0.45f && _encounter >= 0.45f && _battle is not null) _enemyFlash = Math.Max(_enemyFlash, 0.9f);
        _enemyKnock = Math.Max(0, _enemyKnock - (adt * 3.2f));
        var h = _run.Hero;
        _displayHp += (h.Hp - _displayHp) * Math.Min(1, dt * 6);
        // 減った分の白いあとは、少し遅れてから追いかける
        if (_hpTrail < _displayHp) _hpTrail = _displayHp;
        else _hpTrail = Math.Max(_displayHp, _hpTrail - (dt * Math.Max(20, h.MaxHp * 0.6f) * (_heroFlash > 0.3f ? 0 : 1)));
        _displayMp += (h.Mp - _displayMp) * Math.Min(1, dt * 6);
        if (_battle is not null)
        {
            _enemyDisplayHp += (_battle.Enemy.Hp - _enemyDisplayHp) * Math.Min(1, dt * 5);
            if (_enemyTrail < _enemyDisplayHp) _enemyTrail = _enemyDisplayHp;
            else if (_hitStop <= 0 && _enemyFlash < 0.4f) _enemyTrail = Math.Max(_enemyDisplayHp, _enemyTrail - (dt * Math.Max(20, _battle.Enemy.MaxHp * 0.7f)));
        }
        _typer.Update(dt, S.TextCps);
        Art.Age(_popups, dt);

        if (_wait is not null && _wait.Update(this, dt)) Advance();

        // コマンドの窓の出入り（選んだ結果で窓が変わったら、その場で 0 からやり直す）
        if (_wait is WaitCommand)
        {
            _cmdT += dt;
            if ((int)_pane != _lastPane) { _lastPane = (int)_pane; _paneT = 0; }
            else _paneT += dt;
        }
        else
        {
            _cmdT = 0;
            _lastPane = -1;
        }
    }

    public override Vector2 Shake => _shake <= 0 ? Vector2.Zero
        : new Vector2(MathF.Sin(Time * 90) * 18 * _shake, MathF.Cos(Time * 77) * 12 * _shake);

    // ------------------------------------------------------------------
    // 出来事の再生
    // ------------------------------------------------------------------

    /// <summary>出来事を始める。表示しておく時間（秒）を返す。</summary>
    private float Begin(BattleEvent e)
    {
        if (e.Cue != Cue.None) S.Cue(e.Cue);
        if (e.Led != LedColor.None) S.Controller.Led(e.Led);
        if (e.Oled != OledAnim.None) S.Controller.Oled(e.Oled);
        float hold = 0.75f;
        switch (e.Kind)
        {
            case BattleEventKind.EnemyDamaged:
            {
                _enemyFlash = 1;
                _enemyShake = e.Critical ? 0.6f : 0.35f;
                _enemyKnock = e.Critical ? 1.4f : 1f;
                float size = Math.Clamp(EnemyHeightOnScreen, 220, 520);
                if (e.Element == Element.None) _fx.Slash(EnemyCenter, size, e.Critical);
                else _fx.Element(e.Element, EnemyCenter, size);
                _hitStop = e.Critical ? 0.14f : 0.07f;
                _popups.Add(new Art.Popup { Text = e.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), Pos = EnemyCenter + new Vector2(0, -40), Color = e.Critical ? Palette.Gold : Color.White, Size = e.Critical ? 104 : 80, Kind = e.Critical ? Art.PopupKind.Critical : Art.PopupKind.Damage });
                if (e.Critical) _shake = Math.Max(_shake, 0.4f);
                if (_battle?.Enemy.IsDead == true)
                {
                    _enemyDeath = 0;
                    _hitStop = 0.22f;
                    _shake = Math.Max(_shake, 0.5f);
                    _fx.Shatter(EnemyBounds, _art?.Average ?? Color.Gray, _battle.Enemy.IsBoss ? 320 : 170);
                }
                break;
            }
            case BattleEventKind.HeroDamaged:
                if (e.Amount > 0)
                {
                    _heroFlash = 1;
                    _heroPanelShake = 1;
                    _redVignette = Math.Min(1, 0.5f + (e.Amount / (float)Math.Max(1, _run.Hero.MaxHp)));
                    _fxTop.Sparks(new Vector2(300, 860), 14, new Color(255, 80, 60), 500);
                    _fxTop.Claw(new Vector2(960, 560), e.Cue == Cue.BigDamage);
                    _popups.Add(new Art.Popup { Text = e.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), Pos = new Vector2(330, 728), Color = Palette.Bad, Size = 70, Kind = Art.PopupKind.HeroDamage });
                }
                hold = string.IsNullOrEmpty(e.Text) ? 0.25f : 0.7f;
                break;
            case BattleEventKind.HeroHealed:
                if (e.Amount > 0)
                {
                    _popups.Add(new Art.Popup { Text = "+" + e.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), Pos = new Vector2(330, 728), Color = Palette.Good, Size = 64, Kind = Art.PopupKind.Heal });
                    _fxTop.Heal(new Vector2(300, 930), 480);
                }
                hold = 0.25f;
                break;
            case BattleEventKind.EnemyHealed:
                _popups.Add(new Art.Popup { Text = "+" + e.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), Pos = EnemyCenter, Color = Palette.Good, Size = 64, Kind = Art.PopupKind.Heal });
                _fx.Heal(EnemyFeet, 320);
                break;
            case BattleEventKind.EnemyAttack:
                _enemyAttackT = 0;
                hold = 0.25f;
                break;
            case BattleEventKind.Shake:
                _shake = e.Amount >= 2 ? 1f : 0.6f;
                hold = 0.05f;
                break;
            case BattleEventKind.Dodge:
                _popups.Add(new Art.Popup { Text = "MISS", Pos = new Vector2(330, 728), Color = Palette.Cursor, Size = 56, Kind = Art.PopupKind.Miss });
                break;
        }
        if (!string.IsNullOrEmpty(e.Text)) Say(e.Text);
        else hold = Math.Min(hold, 0.3f);
        return hold;
    }

    private void Say(string text)
    {
        _log.Add(text);
        if (_log.Count > 40) _log.RemoveAt(0);
        _typer.Set(text);
    }

    private WaitEvents Events(List<BattleEvent> list) => new(list);

    private WaitEvents Message(string text, Cue cue = Cue.None, LedColor led = LedColor.None) =>
        new([new BattleEvent(BattleEventKind.Message, text, 0, cue, led)]);

    private WaitChallenge Challenge(ChallengeKind kind, bool cancelable = false, string? lead = null)
    {
        var ctx = new ChallengeContext(S.Body.Mode, S.Body.Thresholds(S.Settings), _run.Rng.NextULong(),
            _battle?.ReviveTarget ?? 35);
        return new WaitChallenge(new ChallengeView(S, kind, ctx, cancelable, lead).WithActors(S.Assets.Hero(_run.Hero.Gender), _art));
    }

    // ------------------------------------------------------------------
    // 1 階の流れ
    // ------------------------------------------------------------------

    private IEnumerable<Wait> FloorFlow()
    {
        while (true)
        {
            // 階に着いた
            _battle = null;
            _art = null;
            _run.RollWeather();
            if (S.Controller.LatestWeather is { } w) _run.SetRealWeather(w);
            Autosave();
            S.Cue(Cue.Step, 0.7f);
            S.Controller.Led(_run.Biome.Led);
            // その場所の空気（粒）を、あらかじめ画面いっぱいに漂わせておく
            _fx.Clear();
            for (int i = 0; i < 60; i++)
            {
                _fx.Ambient(_run.Biome, 0.1f);
                _fx.Update(0.1f);
            }
            yield return new WaitPanel(PanelKind.FloorIntro, _run.Floor, 0.5f, EnemyFactory.IsBossFloor(_run.Floor) ? 2.8f : 1.9f);

            // 敵が現れる（ボスの階は、その前に警告）
            if (EnemyFactory.IsBossFloor(_run.Floor))
            {
                S.Cue(Cue.Alarm);
                S.Controller.Led(LedColor.Red);
                yield return new WaitPanel(PanelKind.Warning, _run.Floor, 0.6f, 2.0f);
            }
            var def = EnemyFactory.Choose(_run.Rng, _run.Floor, _run.Day);
            var enemy = EnemyFactory.Create(def, _run.Floor, _run.Difficulty);
            _battle = new BattleSession(_run, enemy);
            _art = S.Assets.Enemy(def.Sprite);
            _enemyAppear = 0;
            _encounter = 0;
            _enemyKnock = 0;
            _enemyDeath = -1;
            _enemyDisplayHp = enemy.Hp;
            _enemyTrail = enemy.Hp;
            _log.Clear();
            _battle.Begin();
            yield return Events(_battle.TakeEvents());

            // 戦い
            foreach (var wt in BattleFlow()) yield return wt;
            if (_gameOver) yield break;

            // 戦いのあと
            foreach (var wt in AfterBattle()) yield return wt;
            if (_ending) yield break;
            if (_run.Floor >= Progression.TopFloor)
            {
                yield break;
            }
            _run.Floor++;
        }
    }

    private bool _gameOver;
    private bool _ending;

    private IEnumerable<Wait> BattleFlow()
    {
        var b = _battle!;
        while (true)
        {
            // ---- こちらの番 ----
            bool canAct = true;
            if (b.ConditionChallenge != ChallengeKind.None)
            {
                var kind = b.ConditionChallenge;
                yield return Message(kind == ChallengeKind.Thaw ? "体が凍りついている！" : "体がしびれて動かない！", Cue.Buzzer);
                var ch = Challenge(kind);
                yield return ch;
                canAct = b.ResolveCondition(ch.View.Outcome);
                yield return Events(b.TakeEvents());
            }
            while (canAct)
            {
                _choice = null;
                yield return new WaitCommand();
                var c = _choice!;
                bool used = false;
                switch (c.Kind)
                {
                    case ChoiceKind.Attack:
                        if (_run.Hero.IsBerserk)
                        {
                            var ch = Challenge(ChallengeKind.Rage);
                            yield return ch;
                            _dashT = 0;
                            b.Attack(ch.View.Outcome);
                        }
                        else
                        {
                            _dashT = 0;
                            b.Attack();
                        }
                        used = true;
                        break;
                    case ChoiceKind.Skill:
                    {
                        var sk = SkillCatalog.Get(c.Skill);
                        ChallengeOutcome? o = null;
                        if (sk.Challenge != ChallengeKind.None)
                        {
                            var ch = Challenge(sk.Challenge, cancelable: true);
                            yield return ch;
                            if (ch.View.Cancelled) continue;
                            o = ch.View.Outcome;
                        }
                        if (b.CannotUse(sk) is null) yield return new WaitAction(ActionStyle.CutIn, sk.Name, sk.Element, Icons.For(sk));
                        used = b.UseSkill(c.Skill, o);
                        break;
                    }
                    case ChoiceKind.Item:
                    {
                        ChallengeOutcome? o = null;
                        if (c.Item!.Kind == ItemKind.Whetstone)
                        {
                            var ch = Challenge(ChallengeKind.Blacksmith, cancelable: true);
                            yield return ch;
                            if (ch.View.Cancelled) continue;
                            o = ch.View.Outcome;
                        }
                        yield return new WaitAction(ActionStyle.Item, c.Item!.Name, c.Item.Element, Icons.For(c.Item));
                        used = b.UseItem(c.Item, o);
                        break;
                    }
                }
                yield return Events(b.TakeEvents());
                if (used || b.Outcome != BattleOutcome.Continue) break;
            }

            if (b.Outcome == BattleOutcome.HeroDown)
            {
                foreach (var wt in Down()) yield return wt;
                if (_gameOver) yield break;
            }
            if (b.Outcome != BattleOutcome.Continue) yield break;

            // ---- 敵の番 ----
            yield return new WaitSeconds(0.25f, false);
            var intent = b.PlanEnemyTurn();
            ChallengeOutcome? eo = null;
            if (intent.Challenge != ChallengeKind.None)
            {
                yield return Message(intent.Announce, Cue.Alarm, LedColor.Red);
                var ch = Challenge(intent.Challenge, lead: intent.Announce);
                yield return ch;
                eo = ch.View.Outcome;
            }
            b.ResolveEnemyTurn(intent, eo);
            yield return Events(b.TakeEvents());

            if (b.Outcome == BattleOutcome.HeroDown)
            {
                foreach (var wt in Down()) yield return wt;
                if (_gameOver) yield break;
            }
            if (b.Outcome != BattleOutcome.Continue) yield break;
        }
    }

    /// <summary>HP が 0 になったとき：蘇生の儀式か、ゲームオーバー。</summary>
    private IEnumerable<Wait> Down()
    {
        var b = _battle!;
        S.Cue(Cue.Death);
        S.Controller.Led(LedColor.Red);
        _shake = 1;
        yield return Message("【心停止】HP が尽きた…");
        if (b.CanRevive)
        {
            yield return Message($"まだ助かる！ 蘇生のチャンス（のこり {_run.Hero.ReviveCharges} 回）");
            var ch = Challenge(ChallengeKind.Revive, lead: "心臓マッサージだ！");
            yield return ch;
            bool ok = b.ResolveRevive(ch.View.Outcome);
            yield return Events(b.TakeEvents());
            if (ok) yield break;
            yield return Message("蘇生できなかった…", Cue.Buzzer);
        }
        _gameOver = true;
        var rec = S.Records;
        rec.GameOvers++;
        rec.BestFloor = Math.Max(rec.BestFloor, _run.Floor);
        rec.TotalBattles += _run.BattlesWon - _recordedWins;
        _recordedWins = _run.BattlesWon;
        S.SaveRecords();
        S.Game.Scenes.Go(new GameOverScene(S, _run), 1.2f);
    }

    private IEnumerable<Wait> AfterBattle()
    {
        var b = _battle!;
        var h = _run.Hero;
        b.EndBattle();
        bool won = b.Outcome is BattleOutcome.Victory or BattleOutcome.Peace or BattleOutcome.Intimidated;
        if (won)
        {
            var reward = b.ClaimReward();
            string banner = b.Outcome switch
            {
                BattleOutcome.Peace => "和解成立",
                BattleOutcome.Intimidated => "威圧成功",
                _ => "VICTORY",
            };
            yield return new WaitSeconds(0.35f, false);
            S.Cue(Cue.Coin);
            yield return new WaitPanel(PanelKind.Victory, banner, 0.5f, 1.5f);
            if (reward.Exp > 0) yield return Message($"経験値 {reward.Exp} を手に入れた！", Cue.Coin);
            foreach (var it in reward.Drops) yield return Message($"{b.Enemy.Name} は {it.Name} を落としていった！", Cue.Coin);
            foreach (var it in reward.Lost) yield return Message($"{it.Name} を見つけたが、もう持てない…", Cue.Buzzer);
            foreach (var up in reward.LevelUps)
            {
                S.Cue(Cue.LevelUp);
                S.Controller.Led(LedColor.White);
                _fxTop.Pillar(new Vector2(960, 640), Palette.Gold);
                yield return new WaitPanel(PanelKind.LevelUp, up, 0.8f);
            }
        }
        else if (b.Outcome == BattleOutcome.Escaped)
        {
            yield return Message("なんとか次の階へ逃げ込んだ。");
        }
        else if (b.Outcome == BattleOutcome.EnemyFled)
        {
            yield return new WaitSeconds(0.4f, false);
        }

        // 最上…いや最深部
        if (b.Enemy.IsFinalBoss && b.Outcome == BattleOutcome.Victory)
        {
            _ending = true;
            _run.Cleared = true;
            var rec = S.Records;
            rec.Clears++;
            rec.BestFloor = Progression.TopFloor;
            rec.TotalBattles += _run.BattlesWon - _recordedWins;
            _recordedWins = _run.BattlesWon;
            if (rec.FastestClearSeconds <= 0 || _run.PlaySeconds < rec.FastestClearSeconds) rec.FastestClearSeconds = _run.PlaySeconds;
            S.SaveRecords();
            try { S.Store.DeleteSave(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            S.Game.Scenes.Go(new StoryScene(S, StoryScene.Epilogue, "final", "ending", () => new EndingScene(S, _run)), 1.5f);
            yield break;
        }

        var ev = FloorEvents.Roll(_run, b.Enemy.IsBoss && b.Outcome == BattleOutcome.Victory);
        switch (ev)
        {
            case FloorEvent.Rest:
                yield return Message("階段の脇に、清らかな泉が湧いている。", Cue.Heal, LedColor.Blue);
                FloorEvents.Rest(h);
                _fxTop.Heal(new Vector2(960, 700), 900);
                yield return Message("泉の水を飲んだ。HP と MP がすべて回復した！", Cue.Heal, LedColor.Green);
                break;
            case FloorEvent.Treasure:
            {
                S.Controller.Led(LedColor.Yellow);
                yield return Message("宝箱を見つけた！", Cue.Coin);
                var q = new WaitChoice("宝箱を開けますか？", "開ける", "そっとしておく");
                yield return q;
                if (q.Result != 0) break;
                var ch = Challenge(ChallengeKind.Lockpick);
                yield return ch;
                if (!ch.View.Outcome.Success)
                {
                    yield return Message("鍵穴が潰れて、もう開かない…", Cue.Buzzer);
                    break;
                }
                if (FloorEvents.IsTrapped(_run))
                {
                    yield return Message("開けた瞬間… 警報が鳴り響いた！ 罠だ！！", Cue.Alarm, LedColor.Red);
                    var run = Challenge(ChallengeKind.EscapeRun, lead: "天井が崩れてくる！");
                    yield return run;
                    if (run.View.Outcome.Success) yield return Message("ぜぇ…ぜぇ… なんとか逃げ切った。");
                    else
                    {
                        int d = h.TakeDamage(Math.Min(h.Hp - 1, FloorEvents.FallDamage(h, 0.25)));
                        _heroFlash = 1;
                        _shake = 1;
                        yield return Message($"瓦礫に巻き込まれて {d} のダメージ！", Cue.BigDamage);
                    }
                    break;
                }
                var item = FloorEvents.OpenTreasure(_run);
                _fx.Twinkle(new Vector2(960, 520), 50, Palette.Gold);
                if (h.Inventory.Add(item)) yield return Message($"宝箱から {item.Name} を手に入れた！", Cue.Unlock, LedColor.White);
                else yield return Message($"{item.Name} が入っていたが、もう持てない…", Cue.Buzzer);
                break;
            }
            case FloorEvent.Bridge:
            {
                yield return Message("古びた吊り橋が、行く手に揺れている…", Cue.None, LedColor.Yellow);
                var ch = Challenge(ChallengeKind.Bridge);
                yield return ch;
                if (ch.View.Outcome.Success)
                {
                    int exp = FloorEvents.BridgeBonusExp(_run.Floor);
                    var ups = h.GainExp(exp, _run.Rng);
                    yield return Message($"素晴らしいバランス感覚だ！ ボーナス経験値 {exp}", Cue.Coin);
                    foreach (var up in ups)
                    {
                        S.Cue(Cue.LevelUp);
                        _fxTop.Pillar(new Vector2(960, 640), Palette.Gold);
                yield return new WaitPanel(PanelKind.LevelUp, up, 0.8f);
                    }
                }
                else
                {
                    int d = h.TakeDamage(Math.Min(h.Hp - 1, FloorEvents.FallDamage(h, 0.18)));
                    _heroFlash = 1;
                    _shake = 0.8f;
                    yield return Message($"橋から落ちた！ {d} のダメージ…", Cue.Damage);
                }
                break;
            }
        }
        yield return new WaitSeconds(0.3f, false);
    }

    private void Autosave()
    {
        try { S.Store.Save(_run); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (_run.Floor > S.Records.BestFloor)
        {
            S.Records.BestFloor = _run.Floor;
            S.SaveRecords();
        }
    }

    private Vector2 EnemyFeet => new(960, 660);

    private Vector2 EnemyCenter
    {
        get
        {
            if (_art is null) return new Vector2(960, 420);
            float h = Art.EnemyHeight(_art, EnemyScale);
            return EnemyFeet - new Vector2(0, h / 2);
        }
    }

    private float EnemyHeightOnScreen => _art is null ? 300 : Art.EnemyHeight(_art, EnemyScale);

    private Rectangle EnemyBounds
    {
        get
        {
            if (_art is null) return new Rectangle(860, 300, 200, 300);
            float h = EnemyHeightOnScreen;
            float w = _art.FrameWidth(false) * _art.BaseScale * EnemyScale;
            return new Rectangle((int)(EnemyFeet.X - (w / 2)), (int)(EnemyFeet.Y - h), (int)w, (int)h);
        }
    }

    private float EnemyScale => _art is null ? 1 : Math.Min(1f, 540f / (_art.Idle.Height * _art.BaseScale));
}
