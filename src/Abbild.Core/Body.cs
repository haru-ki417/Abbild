namespace Abbild.Core;

/// <summary>体の入力をどこから取るか。</summary>
public enum BodyMode
{
    /// <summary>キーボード・ゲームパッド（体の動きは操作で置きかえる）。</summary>
    Keys,

    /// <summary>自作コントローラー（心拍・加速度・湿度センサー）。</summary>
    Sensor,
}

/// <summary>1 フレームぶんの「体の入力」。キーボードでもコントローラーでも同じ形にそろえる。</summary>
public struct BodyFrame
{
    public double Dt;

    /// <summary>決定ボタンを押した瞬間（連打の 1 回）。</summary>
    public bool ConfirmPressed;
    public bool ConfirmHeld;
    public bool LeftPressed;
    public bool RightPressed;

    /// <summary>このフレームで「振った」回数。</summary>
    public int Shakes;

    /// <summary>傾き（-1 = 左いっぱい, +1 = 右いっぱい）。</summary>
    public double Tilt;

    /// <summary>大きく動いている（隠密・居合いの「動くな」判定）。</summary>
    public bool Moving;

    /// <summary>心拍（bpm）。わからなければ NaN。</summary>
    public double HeartBpm;

    /// <summary>息を吹きかけている。</summary>
    public bool Breathing;

    /// <summary>何か操作をした（居合いの「お手つき」判定）。</summary>
    public readonly bool AnyAction => ConfirmPressed || LeftPressed || RightPressed || Shakes > 0 || Moving;
}

/// <summary>心拍のしきい値。</summary>
public readonly record struct HeartThresholds(double Rest, double Calm, double Excite)
{
    public static HeartThresholds ForKeys => new(72, 79, 90);

    public static HeartThresholds FromSettings(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(s.RestingBpm, s.RestingBpm + s.CalmMargin, s.RestingBpm + s.ExciteMargin);
    }
}

/// <summary>
/// コントローラーがないときの「心拍」。連打や振りで上がり、
/// 呼吸のガイドに合わせてボタンを押す・離すと下がる。
/// </summary>
public sealed class SimulatedHeart
{
    private double _activity;   // 最近の動きの多さ（回/秒）
    private double _calm;       // 最近の呼吸の合い具合（0〜1）

    public SimulatedHeart(double rest = 72) => Rest = rest;

    public double Rest { get; }

    public double Bpm { get; private set; } = 80;

    /// <summary>呼吸のガイドの周期（吸う 4 秒・吐く 4 秒）。</summary>
    public const double BreathCycle = 8.0;

    public static bool IsInhale(double t) => (t % BreathCycle) < BreathCycle / 2;

    /// <param name="actions">このフレームの連打・振りの回数。</param>
    /// <param name="guideActive">呼吸のガイドを出しているか。</param>
    /// <param name="guideTime">ガイドの経過時間。</param>
    /// <param name="holding">決定ボタンを押し続けているか（吸う＝押す、吐く＝離す）。</param>
    public void Update(double dt, int actions, bool guideActive, double guideTime, bool holding)
    {
        if (dt <= 0) return;
        _activity += (actions / dt - _activity) * Math.Min(1, dt / 0.8);
        if (guideActive)
        {
            bool inhale = IsInhale(guideTime);
            double match = inhale == holding ? 1 : 0;
            _calm += (match - _calm) * Math.Min(1, dt / 1.5);
        }
        else
        {
            _calm += (0 - _calm) * Math.Min(1, dt / 4.0);
        }
        double target = Rest + 8 + Math.Min(40, _activity * 3.2) - (16 * _calm);
        Bpm += (target - Bpm) * Math.Min(1, dt / 1.6);
    }

    /// <summary>落ち着いた状態から始める（瞑想などの前）。</summary>
    public void Reset(double bpm)
    {
        Bpm = bpm;
        _activity = 0;
        _calm = 0;
    }
}

/// <summary>
/// 加速度の大きさから「振った」回数を数える（しきい値を超えて、いったん下がったら 1 回）。
/// </summary>
public sealed class ShakeCounter(double threshold = 0.55, double release = 0.3, double refractory = 0.12)
{
    private bool _high;
    private double _since = 99;

    /// <summary>重力を除いた加速度の大きさ（g）を入れる。振った瞬間なら 1 を返す。</summary>
    public int Feed(double dynamicG, double dt)
    {
        _since += dt;
        if (!_high && dynamicG > threshold && _since >= refractory)
        {
            _high = true;
            _since = 0;
            return 1;
        }
        if (_high && dynamicG < release) _high = false;
        return 0;
    }
}

/// <summary>キーボードで左右を交互に押した回数を「振り」として数える。</summary>
public sealed class AlternationCounter
{
    private int _last; // -1 = 左, +1 = 右, 0 = まだ

    public int Feed(bool left, bool right)
    {
        int n = 0;
        if (left && _last != -1) { if (_last == 1) n++; _last = -1; }
        if (right && _last != 1) { if (_last == -1) n++; _last = 1; }
        return n;
    }

    public void Reset() => _last = 0;
}

/// <summary>ボタンを叩いた間隔から、リズム（bpm）を出す。</summary>
public sealed class TapTempo
{
    private readonly List<double> _times = [];

    public void Tap(double t) => _times.Add(t);

    public int Count => _times.Count;

    public double Bpm
    {
        get
        {
            if (_times.Count < 3) return double.NaN;
            var gaps = new List<double>();
            for (int i = 1; i < _times.Count; i++)
            {
                double g = _times[i] - _times[i - 1];
                if (g is > 0.25 and < 2.0) gaps.Add(g);
            }
            if (gaps.Count < 2) return double.NaN;
            gaps.Sort();
            double median = gaps[gaps.Count / 2];
            return 60.0 / median;
        }
    }
}
