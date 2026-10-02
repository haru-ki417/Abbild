using System.Globalization;

namespace Abbild.Core;

/// <summary>コントローラーから届いた 1 行ぶんのセンサー値。</summary>
public sealed record ControllerSample(
    int Version,
    double Bpm,          // 心拍（NaN = 不明）。v1 の生の波形値は RawPulse に入る
    double RawPulse,     // v1 の脈波センサーの生の値（NaN = なし）
    bool ButtonA,
    bool ButtonB,
    double AccelX,       // g
    double AccelY,
    double AccelZ,
    int Direction,       // 0 なし / 1 上 / 2 下 / 3 左 / 4 右
    double Humidity,     // % （NaN = なし）
    double Temperature); // ℃

/// <summary>
/// コントローラーとのやりとりの形式。
/// <para>v2（このリポジトリの Arduino スケッチ）: <c>AB2,bpm,buttons,ax_mg,ay_mg,az_mg,dir,hum,temp</c></para>
/// <para>v1（WinForms 版のころのスケッチ）: <c>pulse,sw,x,y,z,temp,hum</c>。x は加速度の生値（中心 330）、
/// z は方向キー（上/右 ≒ 500、下/左 ≒ 100、中立 ≒ 330）、sw は 1 = 決定・2 = 終了。</para>
/// <para>どちらも <c>W:Rain</c>（天気）と <c>T:Friday</c>（曜日）の行を受け付ける。</para>
/// </summary>
public static class ControllerProtocol
{
    public const double V1Center = 330;
    public const double V1PerG = 70;

    public static bool TryParse(string? line, out ControllerSample? sample)
    {
        sample = null;
        if (string.IsNullOrWhiteSpace(line)) return false;
        var p = line.Trim().Split(',');
        var ci = CultureInfo.InvariantCulture;
        if (p.Length >= 9 && p[0] == "AB2")
        {
            if (!double.TryParse(p[1], NumberStyles.Float, ci, out double bpm)) return false;
            if (!int.TryParse(p[2], NumberStyles.Integer, ci, out int btn)) return false;
            if (!double.TryParse(p[3], NumberStyles.Float, ci, out double ax)) return false;
            if (!double.TryParse(p[4], NumberStyles.Float, ci, out double ay)) return false;
            if (!double.TryParse(p[5], NumberStyles.Float, ci, out double az)) return false;
            if (!int.TryParse(p[6], NumberStyles.Integer, ci, out int dir)) return false;
            double hum = double.TryParse(p[7], NumberStyles.Float, ci, out double h) && h > 0 ? h : double.NaN;
            double temp = double.TryParse(p[8], NumberStyles.Float, ci, out double t) ? t : double.NaN;
            sample = new ControllerSample(2, bpm > 0 ? bpm : double.NaN, double.NaN, (btn & 1) != 0, (btn & 2) != 0,
                ax / 1000.0, ay / 1000.0, az / 1000.0, Math.Clamp(dir, 0, 4), hum, temp);
            return true;
        }
        if (p.Length >= 7)
        {
            var v = new double[7];
            for (int i = 0; i < 7; i++)
            {
                if (!double.TryParse(p[i], NumberStyles.Float, ci, out v[i])) return false;
            }
            double pulse = v[0];
            int sw = (int)v[1];
            double x = v[2], y = v[3], z = v[4];
            int dir = z > 450 ? 1 : z < 150 ? 2 : 0;
            bool isBpm = pulse is > 0 and <= 250;
            sample = new ControllerSample(1, isBpm ? pulse : double.NaN, isBpm ? double.NaN : pulse, sw == 1, sw == 2,
                (x - V1Center) / V1PerG, (y - V1Center) / V1PerG, 0, dir, v[6] > 0 ? v[6] : double.NaN, v[5]);
            return true;
        }
        return false;
    }

    public static bool TryParseWeather(string? line, out Weather weather)
    {
        weather = Weather.Clear;
        if (line is null) return false;
        string s = line.Trim();
        string? w = null;
        if (s.StartsWith("W:", StringComparison.Ordinal)) w = s[2..].Trim();
        else
        {
            // ESP 単体の形式: 天気,気温,湿度
            var p = s.Split(',');
            if (p.Length >= 3 && !double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) w = p[0].Trim();
        }
        if (w is null) return false;
        if (w.Contains("rain", StringComparison.OrdinalIgnoreCase) || w.Contains("drizzle", StringComparison.OrdinalIgnoreCase)
            || w.Contains("thunder", StringComparison.OrdinalIgnoreCase) || w.Contains("snow", StringComparison.OrdinalIgnoreCase))
        {
            weather = Weather.Rain;
        }
        else if (w.Contains("cloud", StringComparison.OrdinalIgnoreCase) || w.Contains("mist", StringComparison.OrdinalIgnoreCase)
            || w.Contains("fog", StringComparison.OrdinalIgnoreCase) || w.Contains("haze", StringComparison.OrdinalIgnoreCase))
        {
            weather = Weather.Clouds;
        }
        else
        {
            weather = Weather.Clear;
        }
        return true;
    }

    public static bool TryParseDay(string? line, out DayOfWeek day)
    {
        day = DayOfWeek.Sunday;
        if (line is null) return false;
        string s = line.Trim();
        return s.StartsWith("T:", StringComparison.Ordinal) && Enum.TryParse(s[2..].Trim(), true, out day);
    }

    /// <summary>LED の色をコントローラーへ送る 1 文字（WinForms 版と同じ）。</summary>
    public static string? LedCommand(LedColor c) => c switch
    {
        LedColor.Red => "r",
        LedColor.Green => "g",
        LedColor.Blue => "b",
        LedColor.Yellow => "y",
        LedColor.Purple => "p",
        LedColor.White => "w",
        LedColor.Orange => "o",
        LedColor.Magenta => "m",
        LedColor.Cyan => "c",
        _ => null,
    };

    /// <summary>ブザーの音のコマンド（WinForms 版と同じ文字。対応しない合図は null）。</summary>
    public static string? SoundCommand(Cue c) => c switch
    {
        Cue.Tick or Cue.Hit or Cue.Explosion or Cue.Buzzer or Cue.Swing or Cue.Heartbeat => "N",
        Cue.Success or Cue.Victory or Cue.LevelUp or Cue.Revive or Cue.Unlock or Cue.Coin => "S",
        Cue.Damage or Cue.BigDamage or Cue.Death => "D",
        Cue.Fire => "F",
        Cue.Ice or Cue.Freeze => "I",
        Cue.Thunder => "T",
        Cue.Heal or Cue.Holy => "H",
        Cue.Poison => "P",
        _ => null,
    };

    public static string? OledCommand(OledAnim a) => a switch
    {
        OledAnim.Encounter => "O:ENC",
        OledAnim.Attack => "O:ATK",
        OledAnim.Magic => "O:MAG",
        OledAnim.Damage => "O:DMG",
        OledAnim.Win => "O:WIN",
        _ => null,
    };
}

/// <summary>
/// センサーの値の流れから、振り・傾き・心拍・息・ボタンの押した瞬間を取り出す。
/// </summary>
public sealed class ControllerInterpreter
{
    private readonly ShakeCounter _shake = new(0.85, 0.5, 0.12);
    private readonly BeatDetector _beats = new();
    private bool _lastA;
    private bool _lastB;
    private int _lastDir;
    private double _humBase = double.NaN;
    private double _breathHold;

    // 前回の取り出し以降にたまった出来事
    private int _shakes;
    private bool _aPressed;
    private bool _bPressed;
    private bool _up, _down, _left, _right;

    public double Tilt { get; private set; }
    public bool Moving { get; private set; }
    public double Bpm { get; private set; } = double.NaN;
    public bool Breathing { get; private set; }
    public bool ButtonAHeld => _lastA;
    public double Humidity { get; private set; } = double.NaN;
    public double Temperature { get; private set; } = double.NaN;
    public ControllerSample? Last { get; private set; }
    public double SecondsSinceSample { get; private set; } = 999;

    public void Feed(ControllerSample s, double dt)
    {
        ArgumentNullException.ThrowIfNull(s);
        Last = s;
        SecondsSinceSample = 0;

        // 振り・動き
        double dyn = s.Version == 2
            ? Math.Abs(Math.Sqrt((s.AccelX * s.AccelX) + (s.AccelY * s.AccelY) + (s.AccelZ * s.AccelZ)) - 1.0)
            : Math.Abs(s.AccelX);
        _shakes += _shake.Feed(dyn, dt);
        Moving = dyn > (s.Version == 2 ? 0.25 : 0.36);
        Tilt = Math.Clamp(s.AccelX / 0.8, -1, 1);

        // 心拍
        if (double.IsFinite(s.Bpm)) Bpm = s.Bpm;
        else if (double.IsFinite(s.RawPulse)) Bpm = _beats.Feed(s.RawPulse, dt);
        else Bpm = double.NaN;

        // 息（湿度が急に上がる）
        if (double.IsFinite(s.Humidity))
        {
            Humidity = s.Humidity;
            if (double.IsNaN(_humBase)) _humBase = s.Humidity;
            bool rising = s.Humidity >= _humBase + 6 || s.Humidity >= 80;
            if (rising) _breathHold = 1.0;
            else _humBase += (s.Humidity - _humBase) * Math.Min(1, dt / 20.0);
        }
        _breathHold -= dt;
        Breathing = _breathHold > 0;
        if (double.IsFinite(s.Temperature)) Temperature = s.Temperature;

        // ボタン・方向の押した瞬間
        if (s.ButtonA && !_lastA) _aPressed = true;
        if (s.ButtonB && !_lastB) _bPressed = true;
        _lastA = s.ButtonA;
        _lastB = s.ButtonB;
        if (s.Direction != _lastDir)
        {
            switch (s.Direction)
            {
                case 1: _up = true; if (s.Version == 1) _right = true; break;
                case 2: _down = true; if (s.Version == 1) _left = true; break;
                case 3: _left = true; break;
                case 4: _right = true; break;
            }
            _lastDir = s.Direction;
        }
    }

    /// <summary>データが来ていない時間を進める。</summary>
    public void Tick(double dt)
    {
        SecondsSinceSample += dt;
        if (SecondsSinceSample > 1.5)
        {
            Moving = false;
            Breathing = false;
            Bpm = double.NaN;
        }
    }

    public bool Alive => SecondsSinceSample < 1.5;

    public (int Shakes, bool A, bool B, bool Up, bool Down, bool Left, bool Right) TakeEvents()
    {
        var r = (_shakes, _aPressed, _bPressed, _up, _down, _left, _right);
        _shakes = 0;
        _aPressed = _bPressed = _up = _down = _left = _right = false;
        return r;
    }
}

/// <summary>脈波センサーの生の値から拍を見つけて bpm を出す（v1 のスケッチ向け）。</summary>
public sealed class BeatDetector
{
    private double _avg = double.NaN;
    private double _amp = 40;
    private bool _above;
    private double _t;
    private double _lastBeat = double.NaN;
    private readonly Queue<double> _intervals = new();

    public double Bpm { get; private set; } = double.NaN;

    public double Feed(double raw, double dt)
    {
        _t += dt;
        if (double.IsNaN(_avg)) _avg = raw;
        _avg += (raw - _avg) * Math.Min(1, dt / 1.5);
        double dev = raw - _avg;
        _amp += (Math.Abs(dev) - _amp) * Math.Min(1, dt / 2.0);
        double th = Math.Max(8, _amp * 0.6);
        if (!_above && dev > th)
        {
            _above = true;
            if (!double.IsNaN(_lastBeat))
            {
                double iv = _t - _lastBeat;
                if (iv is > 0.3 and < 1.6)
                {
                    _intervals.Enqueue(iv);
                    while (_intervals.Count > 6) _intervals.Dequeue();
                    var sorted = _intervals.OrderBy(x => x).ToArray();
                    Bpm = 60.0 / sorted[sorted.Length / 2];
                }
            }
            _lastBeat = _t;
        }
        else if (_above && dev < th * 0.3)
        {
            _above = false;
        }
        if (!double.IsNaN(_lastBeat) && _t - _lastBeat > 3) Bpm = double.NaN;
        return Bpm;
    }
}
