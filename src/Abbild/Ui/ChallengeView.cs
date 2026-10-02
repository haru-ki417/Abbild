using Abbild.Core;
using Abbild.Engine;
using Microsoft.Xna.Framework;

namespace Abbild.Ui;

/// <summary>
/// ミニゲームを画面に出して進める部品。説明 → カウントダウン → 本番 → 結果 の順に進む。
/// </summary>
public sealed class ChallengeView
{
    private enum Phase { Intro, Countdown, Running, Result, Done }

    private readonly Services _s;
    private readonly Challenge _c;
    private readonly bool _cancelable;
    private readonly string? _lead;
    private Phase _phase = Phase.Intro;
    private float _t;
    private float _runTime;
    private int _lastCount = -1;
    private float _heartPhase;
    private float _lastBpm = 72;

    public ChallengeView(Services s, ChallengeKind kind, ChallengeContext ctx, bool cancelable = false, string? lead = null)
    {
        _s = s;
        _c = Challenge.Create(kind, ctx);
        _cancelable = cancelable;
        _lead = lead;
    }

    public Challenge Challenge => _c;

    public bool Finished => _phase == Phase.Done;

    /// <summary>説明画面でやめた。</summary>
    public bool Cancelled { get; private set; }

    public ChallengeOutcome Outcome => _c.Outcome;

    public void Update(float dt)
    {
        var input = _s.Input;
        _t += dt;
        switch (_phase)
        {
            case Phase.Intro:
                if (input.Pressed(Act.Confirm) || input.MouseClicked || _s.Controller.Sensors.TakeEvents().A)
                {
                    _s.Cue(Cue.Confirm);
                    _phase = Phase.Countdown;
                    _t = 0;
                    _lastCount = -1;
                }
                else if (_cancelable && input.Pressed(Act.Cancel))
                {
                    _s.Cue(Cue.Cancel);
                    Cancelled = true;
                    _phase = Phase.Done;
                }
                break;
            case Phase.Countdown:
            {
                int count = 3 - (int)(_t / 0.7f);
                if (count != _lastCount && count > 0)
                {
                    _lastCount = count;
                    _s.Cue(Cue.Tick);
                }
                if (_t >= 2.1f)
                {
                    _phase = Phase.Running;
                    _t = 0;
                    _runTime = 0;
                    _s.Body.Reset(_c.WantsBreathGuide);
                    _s.Cue(Cue.Charge, 0.5f);
                }
                // カウントダウン中の操作は捨てる（居合いのお手つき判定に入れない）
                _s.Body.Read(dt, false, 0);
                break;
            }
            case Phase.Running:
            {
                _runTime += dt;
                var f = _s.Body.Read(dt, _c.WantsBreathGuide, _runTime);
                _c.Update(f);
                foreach (var cue in _c.TakeCues()) _s.Cue(cue);
                _s.Controller.Led(_c.Lamp);
                if (double.IsFinite(f.HeartBpm)) _lastBpm = (float)f.HeartBpm;
                _heartPhase += dt * (_lastBpm / 60f);
                if (_c.Finished)
                {
                    _phase = Phase.Result;
                    _t = 0;
                }
                break;
            }
            case Phase.Result:
                foreach (var cue in _c.TakeCues()) _s.Cue(cue);
                if (_t > 1.5f || (_t > 0.4f && input.Pressed(Act.Confirm)))
                {
                    _phase = Phase.Done;
                }
                break;
        }
    }

    public void Draw(Gfx g, float time)
    {
        var b = g.Batch;
        b.Begin();
        bool dark = _c.DarkScreen && _phase is Phase.Running or Phase.Countdown;
        g.Rect(new Rectangle(0, 0, Gfx.Width, Gfx.Height), Color.Black * (dark ? 0.97f : _phase == Phase.Intro ? 0.6f : 0.84f));

        if (_phase == Phase.Intro) DrawIntro(g, time);
        else if (_phase == Phase.Countdown) DrawCountdown(g);
        else DrawRunning(g, time, dark);

        if (_c.Flash) g.Rect(new Rectangle(0, 0, Gfx.Width, Gfx.Height), Color.White * 0.92f);
        b.End();
    }

    private void DrawIntro(Gfx g, float time)
    {
        var r = new Rectangle(310, 170, 1300, 700);
        g.Window(r);
        g.TextCentered(_c.Title, new Vector2(r.Center.X, r.Y + 80), 64, Palette.Gold, bold: true);
        float y = r.Y + 150;
        if (_lead is not null)
        {
            y += g.Paragraph(_lead, new Vector2(r.X + 80, y), 34, r.Width - 160, Palette.Bad) + 14;
        }
        foreach (var line in _c.Instructions)
        {
            if (string.IsNullOrEmpty(line)) continue;
            y += g.Paragraph(line, new Vector2(r.X + 80, y), 36, r.Width - 160, Palette.Text) + 16;
        }
        // どの入力で遊ぶか
        bool sensor = _c.Mode == BodyMode.Sensor;
        string tag = sensor ? "♥ 自作コントローラーで判定します" : "キーボード・ゲームパッドで判定します";
        g.TextCentered(tag, new Vector2(r.Center.X, r.Bottom - 150), 30, sensor ? Palette.Good : Palette.Dim);
        float blink = 0.6f + (0.4f * MathF.Sin(time * 5));
        g.TextCentered($"準備ができたら 決定（{_s.Input.ConfirmLabel}）", new Vector2(r.Center.X, r.Bottom - 80), 40, Color.White * blink);
        if (_cancelable) g.TextCentered($"{_s.Input.CancelLabel}：やめる", new Vector2(r.Right - 140, r.Bottom - 34), 26, Palette.Dim);
    }

    private void DrawCountdown(Gfx g)
    {
        int count = Math.Max(1, 3 - (int)(_t / 0.7f));
        float k = (_t % 0.7f) / 0.7f;
        float size = 260 - (k * 60);
        g.TextCentered(_c.Title, new Vector2(Gfx.Width / 2f, 220), 56, Palette.Gold, bold: true);
        g.TextCentered(count.ToString(System.Globalization.CultureInfo.InvariantCulture), new Vector2(Gfx.Width / 2f, Gfx.Height / 2f), size, Color.White * (1 - (k * 0.5f)), bold: true);
        if (_c.WantsBreathGuide) g.TextCentered("ゆっくり呼吸をととのえて…", new Vector2(Gfx.Width / 2f, 820), 36, Palette.Dim);
    }

    private void DrawRunning(Gfx g, float time, bool dark)
    {
        var center = new Vector2(Gfx.Width / 2f, 470);
        if (!dark)
        {
            g.TextCentered(_c.Title, new Vector2(Gfx.Width / 2f, 110), 52, Palette.Gold, bold: true);
        }

        // ランプ（コントローラーの LED と同じ色）
        var lampColor = Palette.Of(_c.Lamp);
        var lampPos = dark ? center : new Vector2(Gfx.Width / 2f, 300);
        float lampR = dark ? 160 : 90;
        if (_c.Lamp != LedColor.None)
        {
            g.Glow(lampPos, lampR * 2.6f, lampColor * 0.55f);
            g.Circle(lampPos, lampR, lampColor);
            g.Circle(lampPos - new Vector2(lampR * 0.3f, lampR * 0.3f), lampR * 0.28f, Color.White * 0.35f);
        }
        else
        {
            g.Circle(lampPos, lampR, new Color(30, 30, 40));
        }

        // 呼吸のガイド
        if (_c.WantsBreathGuide && _phase == Phase.Running)
        {
            float cyc = (float)(_runTime % SimulatedHeart.BreathCycle);
            bool inhale = SimulatedHeart.IsInhale(_runTime);
            float k = inhale ? cyc / 4f : 1 - ((cyc - 4f) / 4f);
            float rr = 80 + (Ease.InOutSine(k) * 110);
            var gp = new Vector2(Gfx.Width / 2f - 480, 470);
            g.Glow(gp, rr * 1.4f, new Color(90, 160, 255) * 0.25f);
            g.Ring(gp, rr, 6, new Color(140, 200, 255));
            bool holding = _s.Input.Held(Act.Confirm);
            g.Circle(gp, 26, holding == inhale ? Palette.Good : Palette.Bad);
            g.TextCentered(inhale ? "吸って…（押す）" : "吐いて…（離す）", new Vector2(gp.X, gp.Y + 250), 36, Color.White);
        }

        // 心拍
        if (_c.ShowsHeart && _phase == Phase.Running)
        {
            float beat = _heartPhase % 1f;
            float pop = beat < 0.15f ? 1 + ((0.15f - beat) * 2.4f) : 1;
            var hp = new Vector2(Gfx.Width / 2f + 480, 430);
            g.TextCentered("♥", hp, 150 * pop, Palette.Hp, bold: true);
            g.TextCentered($"{_lastBpm:0}", new Vector2(hp.X, hp.Y + 130), 54, Color.White, bold: true);
            g.TextCentered("bpm", new Vector2(hp.X, hp.Y + 180), 28, Palette.Dim);
        }

        // 大きな状況の文字
        bool heartText = _c.ShowsHeart && _c.Status.StartsWith('♥');
        if (!string.IsNullOrEmpty(_c.Status) && _phase == Phase.Running && !heartText)
        {
            g.TextCentered(_c.Status, new Vector2(Gfx.Width / 2f, dark ? 760 : 520), dark ? 48 : 64, Color.White, bold: true);
        }

        // 傾きのメーター
        if (double.IsFinite(_c.TiltMarker))
        {
            var track = new Rectangle(460, 650, 1000, 36);
            g.Rect(track, new Color(20, 22, 34));
            if (double.IsFinite(_c.TiltZoneCenter))
            {
                float zc = track.Center.X + ((float)_c.TiltZoneCenter * track.Width / 2);
                float zw = (float)_c.TiltZoneHalf * track.Width;
                g.Rect(zc - (zw / 2), track.Y, zw, track.Height, Palette.Good * 0.45f);
            }
            g.Outline(track, Color.White, 2);
            float mx = track.Center.X + ((float)Math.Clamp(_c.TiltMarker, -1, 1) * track.Width / 2);
            g.Rect(mx - 6, track.Y - 16, 12, track.Height + 32, lampColor == new Color(40, 40, 50) ? Color.White : lampColor);
            g.Text("左", new Vector2(track.X - 50, track.Y - 2), 32, Palette.Dim);
            g.Text("右", new Vector2(track.Right + 18, track.Y - 2), 32, Palette.Dim);
        }

        // ゲージと残り時間
        if (_c.Gauge >= 0 && !dark)
        {
            var gr = new Rectangle(560, 790, 800, 34);
            g.Bar(gr, (float)_c.Gauge, Palette.Gold, Palette.HpDark);
            if (!string.IsNullOrEmpty(_c.GaugeLabel)) g.TextCentered(_c.GaugeLabel, new Vector2(gr.Center.X, gr.Bottom + 28), 28, Palette.Dim);
        }
        if (_c.Duration > 0 && _phase == Phase.Running)
        {
            var tr = new Rectangle(560, 905, 800, 14);
            g.Bar(tr, (float)(_c.TimeLeft / _c.Duration), dark ? Color.Gray : Palette.Mp, Palette.MpDark, Color.White * 0.4f);
            g.TextCentered($"のこり {_c.TimeLeft:0.0} 秒", new Vector2(tr.Center.X, tr.Bottom + 26), 28, Palette.Dim);
        }

        if (_phase is Phase.Result or Phase.Done)
        {
            var rr = new Rectangle(460, 440, 1000, 150);
            g.Window(rr, 0.95f, border: _c.Outcome.Success ? Palette.Good : Palette.Bad);
            g.TextCentered(_c.ResultText, new Vector2(rr.Center.X, rr.Center.Y), 52, _c.Outcome.Success ? Color.White : Palette.Bad);
        }
        _ = time;
    }
}
