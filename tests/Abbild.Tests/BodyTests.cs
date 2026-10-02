namespace Abbild.Tests;

public class ProtocolTests
{
    [Fact]
    public void v2の行を読み取れる()
    {
        Assert.True(ControllerProtocol.TryParse("AB2,74,1,120,-30,980,4,55.0,23.5", out var s));
        Assert.Equal(2, s!.Version);
        Assert.Equal(74, s.Bpm);
        Assert.True(s.ButtonA);
        Assert.False(s.ButtonB);
        Assert.Equal(0.12, s.AccelX, 3);
        Assert.Equal(0.98, s.AccelZ, 3);
        Assert.Equal(4, s.Direction);
        Assert.Equal(55, s.Humidity);
    }

    [Fact]
    public void WinForms版のころのv1の行も読み取れる()
    {
        Assert.True(ControllerProtocol.TryParse("520,1,400,330,500,25,60", out var s));
        Assert.Equal(1, s!.Version);
        Assert.True(double.IsNaN(s.Bpm));
        Assert.Equal(520, s.RawPulse);
        Assert.True(s.ButtonA);
        Assert.Equal(1.0, s.AccelX, 3);
        Assert.Equal(1, s.Direction);

        Assert.True(ControllerProtocol.TryParse("72,0,330,330,100,25,60", out var bpm));
        Assert.Equal(72, bpm!.Bpm);
        Assert.Equal(2, bpm.Direction);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("AB2,x,1,2,3,4,5,6,7")]
    [InlineData("1,2,3")]
    public void 壊れた行は読まない(string line) => Assert.False(ControllerProtocol.TryParse(line, out _));

    [Theory]
    [InlineData("W:Rain", Weather.Rain)]
    [InlineData("W:Clear", Weather.Clear)]
    [InlineData("Clouds,22,40", Weather.Clouds)]
    [InlineData("Thunderstorm,18,90", Weather.Rain)]
    public void 天気の行を読み取れる(string line, Weather expected)
    {
        Assert.True(ControllerProtocol.TryParseWeather(line, out var w));
        Assert.Equal(expected, w);
    }

    [Fact]
    public void LED_ブザー_OLEDの命令はWinForms版と同じ文字()
    {
        Assert.Equal("r", ControllerProtocol.LedCommand(LedColor.Red));
        Assert.Equal("c", ControllerProtocol.LedCommand(LedColor.Cyan));
        Assert.Equal("S", ControllerProtocol.SoundCommand(Cue.Victory));
        Assert.Equal("F", ControllerProtocol.SoundCommand(Cue.Fire));
        Assert.Equal("O:ENC", ControllerProtocol.OledCommand(OledAnim.Encounter));
        Assert.Null(ControllerProtocol.SoundCommand(Cue.Cursor));
    }
}

public class InterpreterTests
{
    private static ControllerSample V2(double ax = 0, double az = 1, int btn = 0, int dir = 0, double hum = 50, double bpm = 70) =>
        new(2, bpm, double.NaN, (btn & 1) != 0, (btn & 2) != 0, ax, 0, az, dir, hum, 24);

    [Fact]
    public void 強く振ると1回ずつ数え_静止では数えない()
    {
        var it = new ControllerInterpreter();
        for (int i = 0; i < 40; i++) it.Feed(V2(), 0.05);
        Assert.Equal(0, it.TakeEvents().Shakes);
        for (int k = 0; k < 5; k++)
        {
            it.Feed(V2(ax: 1.6, az: 1.2), 0.05);
            it.Feed(V2(), 0.05);
            it.Feed(V2(), 0.05);
            it.Feed(V2(), 0.05);
        }
        Assert.Equal(5, it.TakeEvents().Shakes);
    }

    [Fact]
    public void ボタンと方向は押した瞬間だけを伝える()
    {
        var it = new ControllerInterpreter();
        it.Feed(V2(btn: 1, dir: 3), 0.05);
        it.Feed(V2(btn: 1, dir: 3), 0.05);
        var e = it.TakeEvents();
        Assert.True(e.A);
        Assert.True(e.Left);
        it.Feed(V2(btn: 1, dir: 3), 0.05);
        e = it.TakeEvents();
        Assert.False(e.A);
        Assert.False(e.Left);
    }

    [Fact]
    public void 湿度が急に上がると息を吹きかけたとみなす()
    {
        var it = new ControllerInterpreter();
        for (int i = 0; i < 100; i++) it.Feed(V2(hum: 48), 0.1);
        Assert.False(it.Breathing);
        it.Feed(V2(hum: 60), 0.1);
        Assert.True(it.Breathing);
        for (int i = 0; i < 20; i++) it.Feed(V2(hum: 49), 0.1);
        Assert.False(it.Breathing);
    }

    [Fact]
    public void 生の脈波から心拍を出せる()
    {
        // 72 bpm（0.833 秒ごと）の脈波を 50Hz で流す
        var det = new BeatDetector();
        double bpm = double.NaN;
        for (int i = 0; i < 50 * 12; i++)
        {
            double t = i / 50.0;
            double phase = (t % (60.0 / 72)) / (60.0 / 72);
            double raw = 500 + (phase < 0.12 ? 180 * Math.Sin(phase / 0.12 * Math.PI) : 0) + (5 * Math.Sin(t * 7));
            bpm = det.Feed(raw, 1 / 50.0);
        }
        Assert.InRange(bpm, 66, 78);
    }

    [Fact]
    public void データが途切れたら止まっている扱いにする()
    {
        var it = new ControllerInterpreter();
        it.Feed(V2(ax: 2), 0.05);
        Assert.True(it.Alive);
        it.Tick(2.0);
        Assert.False(it.Alive);
        Assert.False(it.Moving);
        Assert.True(double.IsNaN(it.Bpm));
    }
}

public class SimulatedBodyTests
{
    [Fact]
    public void 呼吸のガイドに合わせると心拍が下がり_連打すると上がる()
    {
        var calm = new SimulatedHeart();
        calm.Reset(76);
        for (double t = 0; t < 10; t += 1 / 60.0) calm.Update(1 / 60.0, 0, true, t, SimulatedHeart.IsInhale(t));
        Assert.True(calm.Bpm <= HeartThresholds.ForKeys.Calm, $"落ち着く {calm.Bpm}");

        var hot = new SimulatedHeart();
        hot.Reset(76);
        for (double t = 0; t < 4; t += 1 / 60.0) hot.Update(1 / 60.0, (int)(t * 60) % 8 == 0 ? 1 : 0, false, t, false);
        Assert.True(hot.Bpm >= HeartThresholds.ForKeys.Excite, $"高ぶる {hot.Bpm}");
    }

    [Fact]
    public void 左右の交互押しを振りとして数える()
    {
        var a = new AlternationCounter();
        Assert.Equal(0, a.Feed(true, false));
        Assert.Equal(1, a.Feed(false, true));
        Assert.Equal(0, a.Feed(false, true));
        Assert.Equal(1, a.Feed(true, false));
    }

    [Fact]
    public void 叩いたリズムから心拍の代わりを出す()
    {
        var t = new TapTempo();
        for (int i = 0; i < 8; i++) t.Tap(i * 0.75);
        Assert.Equal(80, t.Bpm, 1);
    }
}

public class ChallengeTests
{
    private static ChallengeContext Ctx(BodyMode mode = BodyMode.Keys) => new(mode, HeartThresholds.ForKeys, 1234, 35);

    private static void Run(Challenge c, Func<double, BodyFrame> frame, double max = 60)
    {
        for (double t = 0; t < max && !c.Finished; t += 1 / 60.0)
        {
            var f = frame(t);
            f.Dt = 1 / 60.0;
            c.Update(f);
        }
    }

    [Fact]
    public void 蘇生は目標の回数を連打すれば成功する()
    {
        var c = Challenge.Create(ChallengeKind.Revive, Ctx());
        Run(c, t => new BodyFrame { ConfirmPressed = ((int)(t * 60)) % 6 == 0 });
        Assert.True(c.Outcome.Success);
        var slow = Challenge.Create(ChallengeKind.Revive, Ctx());
        Run(slow, t => new BodyFrame { ConfirmPressed = ((int)(t * 60)) % 30 == 0 });
        Assert.False(slow.Outcome.Success);
    }

    [Fact]
    public void 居合いは合図の前に押すと失敗し_合図のあとすぐ押すと成功する()
    {
        var early = Challenge.Create(ChallengeKind.Iai, Ctx());
        Run(early, t => new BodyFrame { ConfirmPressed = t > 0.5 && t < 0.52 });
        Assert.False(early.Outcome.Success);

        var good = (IaiChallenge)Challenge.Create(ChallengeKind.Iai, Ctx());
        bool seen = false;
        double at = 0;
        Run(good, t =>
        {
            if (good.Flash && !seen) { seen = true; at = t; }
            return new BodyFrame { ConfirmPressed = seen && t - at > 0.15 && t - at < 0.17 };
        });
        Assert.True(good.Outcome.Success);
    }

    [Fact]
    public void 解錠は白で押すと成功する()
    {
        var c = Challenge.Create(ChallengeKind.Lockpick, Ctx());
        Run(c, _ => new BodyFrame { ConfirmPressed = c.Lamp == LedColor.White });
        Assert.True(c.Outcome.Success);
        var bad = Challenge.Create(ChallengeKind.Lockpick, Ctx());
        Run(bad, _ => new BodyFrame { ConfirmPressed = bad.Lamp == LedColor.Red });
        Assert.False(bad.Outcome.Success);
    }

    [Fact]
    public void 吊り橋は揺れと反対に傾ければ渡れる()
    {
        var c = (BridgeChallenge)Challenge.Create(ChallengeKind.Bridge, Ctx());
        Run(c, t => new BodyFrame { Tilt = -c.Wobble(t) });
        Assert.True(c.Outcome.Success);
        var lazy = Challenge.Create(ChallengeKind.Bridge, Ctx());
        Run(lazy, _ => new BodyFrame { Tilt = 0.9 });
        Assert.False(lazy.Outcome.Success);
    }

    [Fact]
    public void 心眼は合図の色の向きに動けばかわせる()
    {
        var c = Challenge.Create(ChallengeKind.BlindDefense, Ctx());
        LedColor last = LedColor.None;
        Run(c, _ =>
        {
            var f = new BodyFrame();
            if (c.Lamp != last && c.Lamp != LedColor.None)
            {
                f.LeftPressed = c.Lamp == LedColor.Blue;
                f.RightPressed = c.Lamp == LedColor.Red;
            }
            last = c.Lamp;
            return f;
        });
        Assert.True(c.Outcome.Success);
        Assert.Equal(3, c.Outcome.Count);
    }

    [Fact]
    public void 凝視は落ち着いていれば成功し_心拍が高いと失敗する()
    {
        var calm = Challenge.Create(ChallengeKind.Glare, Ctx());
        Run(calm, _ => new BodyFrame { HeartBpm = 70 });
        Assert.True(calm.Outcome.Success);
        var hot = Challenge.Create(ChallengeKind.Glare, Ctx());
        Run(hot, _ => new BodyFrame { HeartBpm = 100 });
        Assert.False(hot.Outcome.Success);
    }

    [Fact]
    public void 交渉は後半の心拍で気持ちが決まる()
    {
        var c = Challenge.Create(ChallengeKind.Negotiation, Ctx());
        Run(c, t => new BodyFrame { HeartBpm = t < 3 ? 100 : 70 });
        Assert.Equal(Mood.Calm, c.Outcome.Mood);
        var e = Challenge.Create(ChallengeKind.Negotiation, Ctx());
        Run(e, _ => new BodyFrame { HeartBpm = 110 });
        Assert.Equal(Mood.Excited, e.Outcome.Mood);
        var n = Challenge.Create(ChallengeKind.Negotiation, Ctx());
        Run(n, _ => new BodyFrame { HeartBpm = 84 });
        Assert.Equal(Mood.Neutral, n.Outcome.Mood);
    }

    [Fact]
    public void 鍛冶はカーンに合わせて打てば成功し_連打では失敗する()
    {
        var c = Challenge.Create(ChallengeKind.Blacksmith, Ctx());
        bool wasRed = false;
        Run(c, _ =>
        {
            bool red = c.Lamp == LedColor.Red;
            var f = new BodyFrame { ConfirmPressed = red && !wasRed };
            wasRed = red;
            return f;
        });
        Assert.True(c.Outcome.Success);
        var spam = Challenge.Create(ChallengeKind.Blacksmith, Ctx());
        Run(spam, t => new BodyFrame { ConfirmPressed = ((int)(t * 60)) % 5 == 0 });
        Assert.False(spam.Outcome.Success);
    }

    [Fact]
    public void 第六感は狙いの角度で止めれば見つかる()
    {
        var c = Challenge.Create(ChallengeKind.Dowsing, Ctx());
        // 左から右へゆっくり動かし、白くなったら止める
        double tilt = -1;
        Run(c, _ =>
        {
            if (c.Lamp != LedColor.White) tilt = Math.Min(1, tilt + 0.004);
            return new BodyFrame { Tilt = tilt };
        });
        Assert.True(c.Outcome.Success);
    }

    [Fact]
    public void 錬金はお題どおりの回数で成功する()
    {
        var c = (AlchemyChallenge)Challenge.Create(ChallengeKind.Alchemy, Ctx());
        bool gentle = c.Title.Contains("優しく", StringComparison.Ordinal);
        int every = gentle ? 30 : 8;
        Run(c, t => new BodyFrame { Shakes = ((int)(t * 60)) % every == 0 ? 1 : 0 });
        Assert.True(c.Outcome.Success, $"{c.Title} {c.Count}");
    }

    [Fact]
    public void 釣りは緑で巻き赤で止めれば釣れる()
    {
        var c = Challenge.Create(ChallengeKind.Fishing, Ctx());
        Run(c, t => new BodyFrame
        {
            Shakes = t < 0.1 ? 1 : (c.Lamp == LedColor.Green && ((int)(t * 60)) % 10 == 0 ? 1 : 0),
        });
        Assert.True(c.Outcome.Success, c.ResultText);
    }

    [Fact]
    public void 息は合計で決まった時間吹きかけると氷が溶ける()
    {
        var c = Challenge.Create(ChallengeKind.Thaw, Ctx());
        Run(c, t => new BodyFrame { Breathing = t > 1 && t < 3 });
        Assert.True(c.Outcome.Success);
        var none = Challenge.Create(ChallengeKind.Thaw, Ctx());
        Run(none, _ => new BodyFrame());
        Assert.False(none.Outcome.Success);
    }

    [Fact]
    public void すべての種類のミニゲームが作れて説明がある()
    {
        foreach (var k in Enum.GetValues<ChallengeKind>().Where(k => k != ChallengeKind.None))
        {
            foreach (var mode in new[] { BodyMode.Keys, BodyMode.Sensor })
            {
                var c = Challenge.Create(k, Ctx(mode));
                Assert.False(string.IsNullOrWhiteSpace(c.Title));
                Assert.Contains(c.Instructions, l => !string.IsNullOrWhiteSpace(l));
            }
        }
    }
}
