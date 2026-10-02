namespace Abbild.Core;

/// <summary>ミニゲームを作るときの条件。</summary>
public sealed record ChallengeContext(BodyMode Mode, HeartThresholds Heart, ulong Seed, int ReviveTarget = 35);

/// <summary>
/// 体を使うミニゲーム。入力（BodyFrame）を毎フレーム受け取り、終わったら Outcome を持つ。
/// 画面の描き方は持たず、表示に使う値（ランプの色・ゲージ・メッセージ）だけを公開する。
/// </summary>
public abstract class Challenge
{
    private readonly List<Cue> _cues = [];

    protected Challenge(ChallengeKind kind, ChallengeContext ctx, double duration)
    {
        Kind = kind;
        Context = ctx ?? throw new ArgumentNullException(nameof(ctx));
        Duration = duration;
        Rng = new GameRandom(ctx.Seed);
    }

    public ChallengeKind Kind { get; }
    public ChallengeContext Context { get; }
    public BodyMode Mode => Context.Mode;
    protected GameRandom Rng { get; }

    /// <summary>制限時間（0 なら時間で終わらない）。</summary>
    public double Duration { get; protected set; }
    public double Elapsed { get; private set; }
    public double TimeLeft => Duration <= 0 ? 0 : Math.Max(0, Duration - Elapsed);

    public bool Finished { get; private set; }
    public ChallengeOutcome Outcome { get; private set; } = ChallengeOutcome.Fail();

    public abstract string Title { get; }

    /// <summary>始める前の説明（操作方法は Mode で変わる）。</summary>
    public abstract IReadOnlyList<string> Instructions { get; }

    /// <summary>大きく出す今の状況（「振れ！ 12/20」など）。</summary>
    public string Status { get; protected set; } = "";

    /// <summary>コントローラーの LED（画面にも同じ色のランプを出す）。</summary>
    public LedColor Lamp { get; protected set; } = LedColor.None;

    /// <summary>0〜1 のゲージ（使わなければ負）。</summary>
    public double Gauge { get; protected set; } = -1;
    public string GaugeLabel { get; protected set; } = "";

    /// <summary>傾きの表示（使わなければ NaN）。-1〜1。</summary>
    public double TiltMarker { get; protected set; } = double.NaN;

    /// <summary>傾きで狙う範囲（中心と半幅。使わなければ NaN）。</summary>
    public double TiltZoneCenter { get; protected set; } = double.NaN;
    public double TiltZoneHalf { get; protected set; }

    /// <summary>呼吸のガイドを出すか（キーボードで心拍を扱うとき）。</summary>
    public virtual bool WantsBreathGuide => false;

    /// <summary>心拍を表示するか。</summary>
    public virtual bool ShowsHeart => false;

    /// <summary>画面を暗くする（心眼）。</summary>
    public virtual bool DarkScreen => false;

    /// <summary>画面を一瞬白く光らせる（居合いの合図）。</summary>
    public bool Flash { get; protected set; }

    /// <summary>この結果を出したときのひとこと。</summary>
    public string ResultText { get; protected set; } = "";

    public IReadOnlyList<Cue> TakeCues()
    {
        var c = _cues.ToArray();
        _cues.Clear();
        return c;
    }

    protected void Play(Cue c) => _cues.Add(c);

    public void Update(in BodyFrame f)
    {
        if (Finished) return;
        Elapsed += f.Dt;
        Step(f);
        if (!Finished && Duration > 0 && Elapsed >= Duration) TimeUp();
    }

    protected abstract void Step(in BodyFrame f);

    /// <summary>時間切れのとき。</summary>
    protected abstract void TimeUp();

    protected void Finish(ChallengeOutcome outcome, string text)
    {
        if (Finished) return;
        Finished = true;
        Outcome = outcome;
        ResultText = text;
        Play(outcome.Success ? Cue.Success : Cue.Buzzer);
    }

    protected string ShakeVerb => Mode == BodyMode.Sensor ? "コントローラーを振る" : "← → を交互に押す";

    protected string ShakeShort => Mode == BodyMode.Sensor ? "振れ" : "← → 連打";

    protected string ConfirmName => Mode == BodyMode.Sensor ? "決定ボタン" : "決定ボタン（Z / Enter / パッドの A）";

    protected string BreathVerb => Mode == BodyMode.Sensor ? "センサーに息を吹きかける" : "息ボタン（C / パッドの Y）を押し続ける";

    public static Challenge Create(ChallengeKind kind, ChallengeContext ctx) => kind switch
    {
        ChallengeKind.HeartTrial => new HeartTrial(ctx),
        ChallengeKind.ShakeTrial => new CountChallenge(kind, ctx, 10, 0, "技の測定（素早さ）", "10 秒間、とにかく{0}！", true),
        ChallengeKind.MashTrial => new CountChallenge(kind, ctx, 10, 0, "力の測定（攻撃力）", "10 秒間、{1}を連打！", false),
        ChallengeKind.Charge => new CountChallenge(kind, ctx, 3, 0, "気合ため", "3 秒間、全力で{0}！ 回数が多いほど次の攻撃が強くなる", true),
        ChallengeKind.ShakeFree => new CountChallenge(kind, ctx, 5, 20, "しびれを振り払え", "5 秒以内に {2} 回{0}！", true),
        ChallengeKind.EscapeRun => new CountChallenge(kind, ctx, 10, ctx.Mode == BodyMode.Sensor ? 40 : 50, "崩落！ 走り抜けろ", "10 秒以内に {2} 回{0}！", true),
        ChallengeKind.Revive => new CountChallenge(kind, ctx, 10, ctx.ReviveTarget, "蘇生せよ！", "10 秒以内に{1}を {2} 回連打！", false),
        ChallengeKind.Alchemy => new AlchemyChallenge(ctx),
        ChallengeKind.Meditation => new CalmChallenge(kind, ctx, 10, ctx.Mode == BodyMode.Keys ? 0.65 : 0.72, "瞑想", "心を静めるほど大きく回復する"),
        ChallengeKind.Glare => new CalmChallenge(kind, ctx, 5, ctx.Mode == BodyMode.Keys ? 0.55 : 0.6, "動くな！ 心拍検知", "敵が心臓の音を探っている。5 秒間、心を静めて気配を消せ"),
        ChallengeKind.Negotiation => new NegotiationChallenge(ctx),
        ChallengeKind.Rage => new RageChallenge(ctx),
        ChallengeKind.Dowsing => new DowsingChallenge(ctx),
        ChallengeKind.Bridge => new BridgeChallenge(ctx),
        ChallengeKind.Fishing => new FishingChallenge(ctx),
        ChallengeKind.BlindDefense => new BlindDefenseChallenge(ctx),
        ChallengeKind.Iai => new IaiChallenge(ctx),
        ChallengeKind.Blacksmith => new BlacksmithChallenge(ctx),
        ChallengeKind.Lockpick => new LockpickChallenge(ctx),
        ChallengeKind.Thaw => new BreathChallenge(kind, ctx, 6, "氷を溶かせ", true),
        ChallengeKind.Breath => new BreathChallenge(kind, ctx, 4, "息吹", false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "ミニゲームのない種類です。"),
    };
}

/// <summary>回数を数えるミニゲーム（振る・連打）。target が 0 なら回数そのものが結果。</summary>
public sealed class CountChallenge : Challenge
{
    private readonly int _target;
    private readonly bool _shake;
    private readonly string _title;
    private readonly string _howTo;

    public CountChallenge(ChallengeKind kind, ChallengeContext ctx, double seconds, int target, string title, string howTo, bool shake)
        : base(kind, ctx, seconds)
    {
        _target = target;
        _shake = shake;
        _title = title;
        _howTo = howTo;
        Lamp = kind == ChallengeKind.Revive || kind == ChallengeKind.EscapeRun ? LedColor.Red : LedColor.Cyan;
        Status = Verb;
    }

    public int Count { get; private set; }

    public int Target => _target;

    private string Verb => _shake ? ShakeShort : "連打";

    public override string Title => _title;

    public override IReadOnlyList<string> Instructions =>
    [
        string.Format(System.Globalization.CultureInfo.InvariantCulture, _howTo, ShakeVerb, ConfirmName, _target),
        _shake && Mode == BodyMode.Keys ? "（ゲームパッドならスティックを左右にはじく）" : "",
    ];

    protected override void Step(in BodyFrame f)
    {
        int n = _shake ? f.Shakes : (f.ConfirmPressed ? 1 : 0);
        if (n > 0)
        {
            Count += n;
            Play(Cue.Tick);
        }
        Status = _target > 0 ? $"{Verb}！ {Count} / {_target}" : $"{Verb}！ {Count} 回";
        Gauge = _target > 0 ? Math.Min(1, Count / (double)_target) : Math.Min(1, Count / (_shake ? 40.0 : 80.0));
        GaugeLabel = _target > 0 ? "" : $"{Count}";
        if (_target > 0 && Count >= _target)
        {
            Finish(new ChallengeOutcome(true, 1, Count), Kind switch
            {
                ChallengeKind.Revive => "蘇生成功！",
                ChallengeKind.EscapeRun => "なんとか逃げ切った！",
                _ => "振り払った！",
            });
        }
    }

    protected override void TimeUp()
    {
        if (_target > 0)
        {
            Finish(new ChallengeOutcome(false, Count / (double)_target, Count), Kind switch
            {
                ChallengeKind.Revive => "蘇生失敗…",
                ChallengeKind.EscapeRun => "間に合わなかった…",
                _ => "振り払えなかった…",
            });
        }
        else
        {
            var o = new ChallengeOutcome(true, Count, Count);
            Finish(o, $"{Count} 回！");
        }
    }
}

/// <summary>キャラ作成の「心」の測定：心拍（コントローラー）か、鼓動のリズム（キーボード）。</summary>
public sealed class HeartTrial : Challenge
{
    private readonly TapTempo _tempo = new();
    private double _sum;
    private double _w;

    public HeartTrial(ChallengeContext ctx) : base(ChallengeKind.HeartTrial, ctx, 10)
    {
        Lamp = LedColor.White;
    }

    public override string Title => "心の測定（体力）";

    public override bool ShowsHeart => Mode == BodyMode.Sensor;

    public override IReadOnlyList<string> Instructions => Mode == BodyMode.Sensor
        ?
        [
            "心拍センサーに指をそっと置いて、10 秒間じっとしていてください。",
            "落ち着いた心拍は、このあとの「瞑想」や「隠密」の基準にもなります。",
        ]
        :
        [
            "自分の鼓動を感じながら、そのリズムで決定ボタンを叩いてください（10 秒間）。",
            "胸に手を当てるとわかりやすい。速いリズムほど体力が高くなる。",
        ];

    public double Bpm => Mode == BodyMode.Sensor ? (_w > 1 ? _sum / _w : double.NaN) : _tempo.Bpm;

    protected override void Step(in BodyFrame f)
    {
        if (Mode == BodyMode.Sensor)
        {
            if (double.IsFinite(f.HeartBpm) && f.HeartBpm is > 35 and < 220)
            {
                _sum += f.HeartBpm * f.Dt;
                _w += f.Dt;
            }
            Status = double.IsFinite(f.HeartBpm) && f.HeartBpm > 0 ? $"♥ {f.HeartBpm:0}" : "♥ --（指を置いてください）";
        }
        else
        {
            if (f.ConfirmPressed)
            {
                _tempo.Tap(Elapsed);
                Play(Cue.Heartbeat);
            }
            double b = _tempo.Bpm;
            Status = double.IsFinite(b) ? $"♥ {b:0}" : "♥ …（叩いてください）";
        }
        GaugeLabel = Mode == BodyMode.Keys ? $"{_tempo.Count} 回" : "";
    }

    protected override void TimeUp()
    {
        double b = Bpm;
        bool ok = double.IsFinite(b);
        if (!ok) b = 72;
        Finish(new ChallengeOutcome(true, b, (int)Math.Round(b)), ok ? $"平均 {b:0}" : "うまく測れなかったので、平均的な値にしました");
    }
}

/// <summary>即席錬金：お題（優しく／激しく）どおりに振る。</summary>
public sealed class AlchemyChallenge : Challenge
{
    private readonly bool _gentle;

    public AlchemyChallenge(ChallengeContext ctx) : base(ChallengeKind.Alchemy, ctx, 5)
    {
        _gentle = Rng.Next(2) == 0;
        Lamp = LedColor.Cyan;
    }

    public int Count { get; private set; }

    private (int Min, int Max) Range => _gentle ? (5, 15) : (Mode == BodyMode.Sensor ? 25 : 30, 999);

    public override string Title => _gentle ? "即席錬金【優しく】" : "即席錬金【激しく】";

    public override IReadOnlyList<string> Instructions =>
    [
        _gentle
            ? $"ゆっくり、ていねいに混ぜる。5 秒間で {Range.Min}〜{Range.Max} 回{ShakeVerb}。"
            : $"全力で混ぜる！ 5 秒間で {Range.Min} 回以上{ShakeVerb}！",
        "振りすぎても、足りなくても失敗する。",
    ];

    protected override void Step(in BodyFrame f)
    {
        if (f.Shakes > 0)
        {
            Count += f.Shakes;
            Play(Cue.Tick);
        }
        Status = $"{Count} 回（目標 {(_gentle ? $"{Range.Min}〜{Range.Max}" : $"{Range.Min}〜")}）";
        Gauge = Math.Min(1, Count / (double)(_gentle ? 20 : Range.Min));
        Lamp = _gentle && Count > Range.Max ? LedColor.Red : Count >= Range.Min ? LedColor.Green : LedColor.Cyan;
    }

    protected override void TimeUp()
    {
        bool ok = Count >= Range.Min && Count <= Range.Max;
        Finish(new ChallengeOutcome(ok, ok ? 1 : 0, Count), ok ? "上質なポーションができた！" : "謎の液体ができた…");
    }
}

/// <summary>心を静めるミニゲーム（瞑想・凝視）。</summary>
public sealed class CalmChallenge : Challenge
{
    private readonly double _need;
    private readonly string _title;
    private readonly string _lead;
    private double _calmTime;
    private double _badTime;

    public CalmChallenge(ChallengeKind kind, ChallengeContext ctx, double seconds, double needRatio, string title, string lead)
        : base(kind, ctx, seconds)
    {
        _need = needRatio;
        _title = title;
        _lead = lead;
        Lamp = LedColor.Blue;
    }

    public override string Title => _title;

    public override bool WantsBreathGuide => Mode == BodyMode.Keys;

    public override bool ShowsHeart => true;

    public override IReadOnlyList<string> Instructions => Mode == BodyMode.Sensor
        ? [_lead + "。", "センサーに指を置き、深呼吸。コントローラーは動かさないこと。"]
        : [_lead + "。", "輪が広がる間は決定ボタンを押し続け（吸う）、縮む間は離す（吐く）。ほかのボタンは押さないこと。"];

    public double CalmRatio => Elapsed <= 0 ? 0 : _calmTime / Elapsed;

    protected override void Step(in BodyFrame f)
    {
        bool calm = double.IsFinite(f.HeartBpm) && f.HeartBpm <= Context.Heart.Calm;
        bool disturbed = Mode == BodyMode.Sensor ? f.Moving || f.Shakes > 0 : f.LeftPressed || f.RightPressed || f.Shakes > 0;
        if (calm && !disturbed) _calmTime += f.Dt;
        else _badTime += f.Dt;
        Lamp = calm && !disturbed ? LedColor.Blue : LedColor.Red;
        Status = double.IsFinite(f.HeartBpm) ? $"♥ {f.HeartBpm:0}" : "♥ --";
        Gauge = CalmRatio;
        GaugeLabel = "静けさ";
        if (Kind == ChallengeKind.Glare && _badTime > Duration * (1 - _need) + 0.01)
        {
            Finish(new ChallengeOutcome(false, CalmRatio), "心音を聞かれた！");
        }
    }

    protected override void TimeUp()
    {
        bool ok = CalmRatio >= _need;
        string text = Kind == ChallengeKind.Glare
            ? ok ? "気配を消してやり過ごした！" : "心音を聞かれた！"
            : ok ? "精神統一、完了。" : "雑念が混じってしまった…";
        Finish(new ChallengeOutcome(ok, CalmRatio), text);
    }
}

/// <summary>交渉：落ち着けば和解、高ぶれば威圧、中途半端なら決裂。</summary>
public sealed class NegotiationChallenge : Challenge
{
    private double _sum;
    private double _t;

    public NegotiationChallenge(ChallengeContext ctx) : base(ChallengeKind.Negotiation, ctx, 6)
    {
        Lamp = LedColor.Cyan;
    }

    public override string Title => "交渉";

    public override bool WantsBreathGuide => Mode == BodyMode.Keys;

    public override bool ShowsHeart => true;

    public override IReadOnlyList<string> Instructions =>
    [
        "6 秒間、心を整えて敵と向き合う。",
        Mode == BodyMode.Sensor
            ? "落ち着く（深呼吸）→ 和解して経験値。高ぶる（激しく振る）→ 威圧してアイテム。どっちつかずは決裂。"
            : "呼吸のガイドに合わせる → 和解して経験値。決定ボタンを連打して高ぶる → 威圧してアイテム。どっちつかずは決裂。",
    ];

    public double Average => _t > 0 ? _sum / _t : double.NaN;

    protected override void Step(in BodyFrame f)
    {
        // 最後の 3 秒の平均で決める（前半で気持ちを作れるように）
        if (double.IsFinite(f.HeartBpm) && Elapsed >= Duration - 3)
        {
            _sum += f.HeartBpm * f.Dt;
            _t += f.Dt;
        }
        var h = Context.Heart;
        Lamp = !double.IsFinite(f.HeartBpm) ? LedColor.Cyan : f.HeartBpm <= h.Calm ? LedColor.Blue : f.HeartBpm >= h.Excite ? LedColor.Red : LedColor.Yellow;
        Status = double.IsFinite(f.HeartBpm) ? $"♥ {f.HeartBpm:0}" : "♥ --";
        Gauge = double.IsFinite(f.HeartBpm) ? Math.Clamp((f.HeartBpm - (h.Rest - 15)) / 45.0, 0, 1) : -1;
        GaugeLabel = "冷静 ← → 高ぶり";
    }

    protected override void TimeUp()
    {
        double avg = Average;
        var h = Context.Heart;
        var mood = !double.IsFinite(avg) ? Mood.Neutral : avg <= h.Calm ? Mood.Calm : avg >= h.Excite ? Mood.Excited : Mood.Neutral;
        Finish(new ChallengeOutcome(mood != Mood.Neutral, 0, 0, mood), mood switch
        {
            Mood.Calm => "冷静に語りかけた",
            Mood.Excited => "鬼の形相でにらみつけた！",
            _ => "どっちつかずの態度…",
        });
    }
}

/// <summary>バーサーク中の攻撃：高ぶりを保てているか。</summary>
public sealed class RageChallenge : Challenge
{
    private int _shakes;
    private double _last = double.NaN;

    public RageChallenge(ChallengeContext ctx) : base(ChallengeKind.Rage, ctx, 2.5)
    {
        Lamp = LedColor.Red;
    }

    public override string Title => "高ぶれ！";

    public override bool ShowsHeart => true;

    public override IReadOnlyList<string> Instructions =>
    [
        Mode == BodyMode.Sensor ? "2.5 秒間、激しく振って心拍を上げろ！" : "2.5 秒間、決定ボタンを連打して心拍を上げろ！",
        "高ぶったままなら威力 2 倍。冷めると反動で動けない。",
    ];

    protected override void Step(in BodyFrame f)
    {
        _shakes += f.Shakes;
        if (double.IsFinite(f.HeartBpm)) _last = f.HeartBpm;
        Status = double.IsFinite(_last) ? $"♥ {_last:0}" : "♥ --";
        Gauge = double.IsFinite(_last) ? Math.Clamp((_last - Context.Heart.Rest) / (Context.Heart.Excite - Context.Heart.Rest + 8), 0, 1) : -1;
        GaugeLabel = "高ぶり";
    }

    protected override void TimeUp()
    {
        bool ok = (double.IsFinite(_last) && _last >= Context.Heart.Excite) || (Mode == BodyMode.Sensor && _shakes >= 6);
        Finish(new ChallengeOutcome(ok), ok ? "怒りが燃え上がる！" : "冷静になってしまった…");
    }
}

/// <summary>第六感：傾けて、反応がいちばん強い角度で 1 秒キープ。</summary>
public sealed class DowsingChallenge : Challenge
{
    private readonly double _target;
    private double _keep;
    private double _beep;

    public DowsingChallenge(ChallengeContext ctx) : base(ChallengeKind.Dowsing, ctx, 20)
    {
        double t;
        do { t = Rng.Range(-0.85, 0.85); } while (Math.Abs(t) < 0.2);
        _target = t;
        Lamp = LedColor.Cyan;
    }

    public override string Title => "第六感";

    public override IReadOnlyList<string> Instructions =>
    [
        Mode == BodyMode.Sensor ? "コントローラーをゆっくり左右に傾けて、財宝の気配を探す。" : "← → で感覚の針をゆっくり動かして、財宝の気配を探す。",
        "近づくとランプが黄色、真上で白く光る。白のまま 1 秒キープで発見！（20 秒）",
    ];

    protected override void Step(in BodyFrame f)
    {
        double d = Math.Abs(f.Tilt - _target);
        TiltMarker = f.Tilt;
        _beep -= f.Dt;
        if (d < 0.08)
        {
            Lamp = LedColor.White;
            Status = "そこだ！！ キープしろ！";
            _keep += f.Dt;
            if (_beep <= 0) { Play(Cue.Tick); _beep = 0.15; }
        }
        else
        {
            _keep = 0;
            if (d < 0.25)
            {
                Lamp = LedColor.Yellow;
                Status = "近いぞ…！";
                if (_beep <= 0) { Play(Cue.Tick); _beep = 0.45; }
            }
            else
            {
                Lamp = LedColor.Blue;
                Status = "反応なし…";
            }
        }
        Gauge = Math.Clamp(1 - (d / 1.2), 0, 1);
        GaugeLabel = "気配";
        if (_keep >= 1.0) Finish(ChallengeOutcome.Pass(), "隠された財宝を見つけ出した！");
    }

    protected override void TimeUp() => Finish(ChallengeOutcome.Fail(), "集中力が切れてしまった…");
}

/// <summary>吊り橋：揺れに合わせて水平を保つ。</summary>
public sealed class BridgeChallenge : Challenge
{
    private readonly double _phase1;
    private readonly double _phase2;
    private double _unstable;

    public BridgeChallenge(ChallengeContext ctx) : base(ChallengeKind.Bridge, ctx, 10)
    {
        _phase1 = Rng.Range(0, Math.Tau);
        _phase2 = Rng.Range(0, Math.Tau);
        Lamp = LedColor.Yellow;
        TiltZoneCenter = 0;
        TiltZoneHalf = 0.35;
    }

    public override string Title => "吊り橋";

    public override IReadOnlyList<string> Instructions =>
    [
        "古びた吊り橋を渡る。風で足もとが揺れる！",
        Mode == BodyMode.Sensor
            ? "揺れと反対にコントローラーを傾けて、水平を保て（10 秒）。合計 2 秒傾いたら落ちる。"
            : "揺れと反対に ← → を押して、水平を保て（10 秒）。合計 2 秒傾いたら落ちる。",
    ];

    public double Wobble(double t) => (0.45 * Math.Sin((t * 1.3) + _phase1)) + (0.25 * Math.Sin((t * 2.9) + _phase2));

    protected override void Step(in BodyFrame f)
    {
        double eff = Math.Clamp(f.Tilt + Wobble(Elapsed), -1, 1);
        TiltMarker = eff;
        bool bad = Math.Abs(eff) > TiltZoneHalf;
        if (bad) _unstable += f.Dt;
        Lamp = bad ? LedColor.Red : LedColor.Yellow;
        Status = bad ? "傾いている！！ 戻せ！" : "安定中…";
        Gauge = Math.Max(0, 1 - (_unstable / 2.0));
        GaugeLabel = "足場";
        if (_unstable >= 2.0) Finish(ChallengeOutcome.Fail(), "バランスを崩して落ちてしまった！");
    }

    protected override void TimeUp() => Finish(ChallengeOutcome.Pass(), "無事に渡り切った！");
}

/// <summary>釣り：投げて、待って、緑で巻き赤で止める。</summary>
public sealed class FishingChallenge : Challenge
{
    private enum Stage { Cast, Wait, Fight }

    private Stage _stage = Stage.Cast;
    private double _stageTime;
    private bool _pulling;
    private double _switchAt;
    private int _reel;
    private int _tension;

    public FishingChallenge(ChallengeContext ctx) : base(ChallengeKind.Fishing, ctx, 0)
    {
        Lamp = LedColor.Cyan;
        Status = "竿を振って投げろ！";
    }

    public override string Title => "釣り";

    public override IReadOnlyList<string> Instructions =>
    [
        $"1. {ShakeVerb}と竿を投げる　2. かかるまで動かない",
        $"3. ランプが緑のあいだ{ShakeVerb}と巻ける。赤のときに巻くと糸が切れる！",
    ];

    private const int ReelTarget = 16;
    private const int TensionLimit = 6;

    protected override void Step(in BodyFrame f)
    {
        _stageTime += f.Dt;
        switch (_stage)
        {
            case Stage.Cast:
                if (f.Shakes > 0 || f.ConfirmPressed)
                {
                    _stage = Stage.Wait;
                    _stageTime = 0;
                    Lamp = LedColor.Blue;
                    Status = "じっと待て…";
                    Play(Cue.Splash);
                }
                else if (_stageTime > 6)
                {
                    Finish(ChallengeOutcome.Fail(), "投げられなかった…");
                }
                break;
            case Stage.Wait:
                if (_stageTime > 2.2)
                {
                    _stage = Stage.Fight;
                    _stageTime = 0;
                    Play(Cue.Alarm);
                    NextSignal();
                }
                break;
            case Stage.Fight:
                if (_stageTime >= _switchAt) NextSignal();
                if (f.Shakes > 0)
                {
                    if (_pulling) { _tension += f.Shakes; Play(Cue.Buzzer); }
                    else { _reel += f.Shakes; Play(Cue.Tick); }
                }
                Gauge = Math.Min(1, _reel / (double)ReelTarget);
                GaugeLabel = $"糸の張り {_tension}/{TensionLimit}";
                if (_tension >= TensionLimit) Finish(ChallengeOutcome.Fail(), "ブチッ！ 糸が切れてしまった…");
                else if (_reel >= ReelTarget) Finish(ChallengeOutcome.Pass(), "釣り上げた！ 大物だ！");
                else if (_stageTime > 10) Finish(ChallengeOutcome.Fail(), "逃げられてしまった…");
                break;
        }
    }

    private void NextSignal()
    {
        _pulling = Rng.Chance(0.3);
        _switchAt = _stageTime + Rng.Range(0.6, 1.2);
        Lamp = _pulling ? LedColor.Red : LedColor.Green;
        Status = _pulling ? "止まれ！！ 糸が切れそうだ！" : "今だ！ 巻け！！";
    }

    protected override void TimeUp() { }
}

/// <summary>心眼：暗闇の中、光と音の合図で左右にかわす。</summary>
public sealed class BlindDefenseChallenge : Challenge
{
    private int _round;
    private int _dodged;
    private double _t;
    private int _dir;          // -1 左 / +1 右 / 0 合図待ち
    private bool _answered;

    public BlindDefenseChallenge(ChallengeContext ctx) : base(ChallengeKind.BlindDefense, ctx, 0)
    {
        Status = "気配を読め…";
    }

    public override string Title => "心眼";

    public override bool DarkScreen => true;

    public override IReadOnlyList<string> Instructions =>
    [
        "視界が奪われた！ 3 回の攻撃が来る。2 回かわせば無傷。",
        Mode == BodyMode.Sensor
            ? "青い光・高い音 → 左に傾ける／赤い光・低い音 → 右に傾ける（1 秒以内）"
            : "青い光・高い音 → ← ／ 赤い光・低い音 → → （1 秒以内）",
    ];

    public int Round => _round;

    protected override void Step(in BodyFrame f)
    {
        _t += f.Dt;
        if (_dir == 0)
        {
            Lamp = LedColor.None;
            if (_t >= 1.1)
            {
                _dir = Rng.Next(2) == 0 ? -1 : 1;
                _t = 0;
                _answered = false;
                Lamp = _dir < 0 ? LedColor.Blue : LedColor.Red;
                Play(_dir < 0 ? Cue.Freeze : Cue.Explosion);
                Status = $"{_round + 1} 撃目！";
            }
            return;
        }
        bool left = f.LeftPressed || (Mode == BodyMode.Sensor && f.Tilt < -0.45);
        bool right = f.RightPressed || (Mode == BodyMode.Sensor && f.Tilt > 0.45);
        if (!_answered && (left || right))
        {
            _answered = true;
            bool ok = (_dir < 0 && left && !right) || (_dir > 0 && right && !left);
            if (ok) { _dodged++; Play(Cue.Miss); Status = "かわした！"; }
            else { Play(Cue.Damage); Status = "逆だ！ 直撃！"; }
            NextRound();
        }
        else if (_t > 1.0)
        {
            Play(Cue.Damage);
            Status = "遅い！ 直撃！";
            NextRound();
        }
    }

    private void NextRound()
    {
        _round++;
        _dir = 0;
        _t = 0;
        Gauge = _round / 3.0;
        GaugeLabel = $"かわした {_dodged} / {_round}";
        if (_round >= 3)
        {
            bool ok = _dodged >= 2;
            Finish(new ChallengeOutcome(ok, _dodged / 3.0, _dodged), ok ? "すべて見切った！" : "見切れなかった…");
        }
    }

    protected override void TimeUp() { }
}

/// <summary>居合い：合図の瞬間に反応。早すぎても遅すぎても斬られる。</summary>
public sealed class IaiChallenge : Challenge
{
    private readonly double _signalAt;
    private bool _signaled;
    private double _sinceSignal;

    public IaiChallenge(ChallengeContext ctx) : base(ChallengeKind.Iai, ctx, 0)
    {
        _signalAt = Rng.Range(2.0, 5.0);
        Lamp = LedColor.Blue;
        Status = "集中せよ…";
    }

    public override string Title => "居合い";

    public override IReadOnlyList<string> Instructions =>
    [
        "敵が刀に手をかけた。一撃必殺の技が来る！",
        Mode == BodyMode.Sensor
            ? "「キィン！」と画面が光った瞬間に、コントローラーを振れ（0.5 秒以内）。それまでは動くな。"
            : "「キィン！」と画面が光った瞬間に、決定ボタンを押せ（0.5 秒以内）。それまでは何も押すな。",
    ];

    public double ReactionTime { get; private set; } = double.NaN;

    protected override void Step(in BodyFrame f)
    {
        bool act = Mode == BodyMode.Sensor ? f.Shakes > 0 || f.ConfirmPressed : f.ConfirmPressed || f.LeftPressed || f.RightPressed;
        if (!_signaled)
        {
            if (act || (Mode == BodyMode.Sensor && f.Moving))
            {
                Finish(ChallengeOutcome.Fail(), "早すぎる！！ 隙だらけだ！");
                return;
            }
            if (Elapsed >= _signalAt)
            {
                _signaled = true;
                Flash = true;
                Lamp = LedColor.White;
                Status = "今だ！！";
                Play(Cue.Swing);
            }
            return;
        }
        _sinceSignal += f.Dt;
        if (_sinceSignal > 0.12) Flash = false;
        if (act)
        {
            ReactionTime = _sinceSignal;
            Finish(new ChallengeOutcome(true, 1 - (_sinceSignal / 0.5)), $"一閃！（{_sinceSignal:0.00} 秒）");
        }
        else if (_sinceSignal > 0.5)
        {
            Finish(ChallengeOutcome.Fail(), "遅い！！ 斬られた…");
        }
    }

    protected override void TimeUp() { }
}

/// <summary>鍛冶：ピッ・ピッ・ピッ・ポーン（振る）を 3 回。2 回成功で OK。</summary>
public sealed class BlacksmithChallenge : Challenge
{
    private const double Beat = 0.5;
    private int _round;
    private int _hits;
    private double _t = -0.8;     // 少し間をあけてから始める
    private int _ticksPlayed;
    private bool _roundDone;

    public BlacksmithChallenge(ChallengeContext ctx) : base(ChallengeKind.Blacksmith, ctx, 0)
    {
        Lamp = LedColor.Blue;
        Status = "リズムに乗れ…";
    }

    public override string Title => "リズム鍛冶";

    public override IReadOnlyList<string> Instructions =>
    [
        "「ピッ、ピッ、ピッ、カーン！」の「カーン」に合わせて打つ。3 回中 2 回成功で攻撃力アップ。",
        Mode == BodyMode.Sensor ? "打つ ＝ コントローラーを振る（または決定ボタン）" : "打つ ＝ 決定ボタン",
    ];

    protected override void Step(in BodyFrame f)
    {
        _t += f.Dt;
        bool hit = f.ConfirmPressed || f.Shakes > 0;
        double strike = Beat * 3;
        while (_ticksPlayed < 3 && _t >= _ticksPlayed * Beat)
        {
            Play(Cue.Tick);
            Lamp = LedColor.Blue;
            Status = new string('・', _ticksPlayed + 1);
            _ticksPlayed++;
        }
        if (_ticksPlayed == 3 && _t >= strike && Lamp != LedColor.Red && !_roundDone)
        {
            Lamp = LedColor.Red;
            Play(Cue.Lock);
            Status = "カーン！";
        }
        if (!_roundDone && hit)
        {
            double off = _t - strike;
            bool good = off is >= -0.12 and <= 0.3;
            if (good) { _hits++; Play(Cue.Critical); Status = "Good!"; }
            else { Play(Cue.Buzzer); Status = off < 0 ? "早すぎ…" : "Miss…"; }
            _roundDone = true;
        }
        if (!_roundDone && _t > strike + 0.3)
        {
            Status = "Miss…";
            Play(Cue.Buzzer);
            _roundDone = true;
        }
        if (_roundDone && _t > strike + 0.8)
        {
            _round++;
            Gauge = _round / 3.0;
            GaugeLabel = $"成功 {_hits} / {_round}";
            if (_round >= 3)
            {
                bool ok = _hits >= 2;
                Finish(new ChallengeOutcome(ok, _hits / 3.0, _hits), ok ? "見事な槌さばき！" : "うまく研げなかった…");
                return;
            }
            _t = -0.4;
            _ticksPlayed = 0;
            _roundDone = false;
            Lamp = LedColor.Blue;
        }
    }

    protected override void TimeUp() { }
}

/// <summary>解錠：色がめまぐるしく変わるランプが白の瞬間にボタン。</summary>
public sealed class LockpickChallenge : Challenge
{
    private static readonly LedColor[] Colors = [LedColor.Red, LedColor.Blue, LedColor.Yellow, LedColor.Purple, LedColor.Cyan, LedColor.White, LedColor.Green];
    private double _next;

    public LockpickChallenge(ChallengeContext ctx) : base(ChallengeKind.Lockpick, ctx, 15)
    {
        Lamp = LedColor.Red;
        Status = "白で押せ！";
    }

    public override string Title => "宝箱の鍵";

    public override IReadOnlyList<string> Instructions =>
    [
        "鍵がかかっている。ランプの色がめまぐるしく変わる。",
        $"【白】に光った瞬間に{ConfirmName}！ ほかの色で押すと鍵が壊れる。",
    ];

    protected override void Step(in BodyFrame f)
    {
        if (f.ConfirmPressed)
        {
            bool ok = Lamp == LedColor.White;
            Finish(new ChallengeOutcome(ok), ok ? "カチャッ… 鍵が開いた！" : "ガチッ… 鍵が壊れてしまった");
            return;
        }
        if (Elapsed >= _next)
        {
            var c = Lamp;
            while (c == Lamp) c = Colors[Rng.Next(Colors.Length)];
            Lamp = c;
            _next = Elapsed + Rng.Range(0.22, 0.34);
            Play(Cue.Cursor);
        }
    }

    protected override void TimeUp() => Finish(ChallengeOutcome.Fail(), "手間取って、あきらめた…");
}

/// <summary>息を吹きかける（凍結を溶かす・息吹）。</summary>
public sealed class BreathChallenge : Challenge
{
    private readonly bool _thaw;
    private readonly string _title;
    private double _breath;
    private int _shakes;

    public BreathChallenge(ChallengeKind kind, ChallengeContext ctx, double seconds, string title, bool thaw)
        : base(kind, ctx, seconds)
    {
        _thaw = thaw;
        _title = title;
        Lamp = thaw ? LedColor.Cyan : LedColor.Purple;
    }

    private const double Need = 1.2;
    private const double Full = 2.5;

    public override string Title => _title;

    public override IReadOnlyList<string> Instructions => _thaw
        ?
        [
            "体が凍りついた！ 温かい息で氷を溶かせ。",
            Mode == BodyMode.Sensor
                ? $"{Duration:0} 秒以内に{BreathVerb}（合計 {Need:0.0} 秒）。20 回振っても溶ける。"
                : $"{Duration:0} 秒以内に{BreathVerb}（合計 {Need:0.0} 秒）。",
        ]
        :
        [
            "大きく息を吸って、毒の霧を吹きかける！",
            $"{Duration:0} 秒以内に、できるだけ長く{BreathVerb}。長いほど威力が上がる。",
        ];

    protected override void Step(in BodyFrame f)
    {
        if (f.Breathing)
        {
            _breath += f.Dt;
            Lamp = _thaw ? LedColor.Orange : LedColor.Purple;
        }
        else
        {
            Lamp = _thaw ? LedColor.Cyan : LedColor.Magenta;
        }
        _shakes += f.Shakes;
        Gauge = Math.Min(1, _breath / (_thaw ? Need : Full));
        GaugeLabel = _thaw ? "溶けぐあい" : "息の長さ";
        Status = f.Breathing ? "フーーーッ！" : "息を吹きかけろ！";
        if (_thaw && (_breath >= Need || (Mode == BodyMode.Sensor && _shakes >= 20)))
        {
            Finish(ChallengeOutcome.Pass(), "氷が溶けた！");
        }
        if (!_thaw && _breath >= Full)
        {
            Finish(ChallengeOutcome.Pass(1), "最大の息吹！");
        }
    }

    protected override void TimeUp()
    {
        double score = Math.Min(1, _breath / Full);
        if (_thaw) Finish(ChallengeOutcome.Fail(), "氷が溶けきらなかった…");
        else Finish(new ChallengeOutcome(score > 0.05, score), score > 0.05 ? "毒の霧を吹きかけた！" : "息が続かなかった…");
    }
}
