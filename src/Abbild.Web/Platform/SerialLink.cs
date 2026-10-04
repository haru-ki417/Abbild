// ブラウザー版のための置きかえ（Windows 版の src/Abbild/Controller/SerialLink.cs）
using System.Collections.Concurrent;
using Abbild.Core;

namespace Abbild.Controller;

public enum LinkState { Off, Searching, Connected, Lost }

/// <summary>
/// ブラウザー版のシリアル通信（使えない）。ブラウザーからは COM ポートを開けないので、つながらない状態のまま。
/// 体の入力は、キーボード・ゲームパッド・画面のボタンで行う（Windows 版と同じ代わりの操作）。
/// </summary>
public sealed class SerialLink : IDisposable
{
    public LinkState State => LinkState.Off;

    public string? PortName => null;

    public string Message => "ブラウザー版では自作コントローラーは使えません";

    public static string[] Ports() => [];

    public void Start(string? port, int baud, Func<string, bool> looksValid)
    {
    }

    public bool TryReadLine(out string line)
    {
        line = "";
        return false;
    }

    public void Send(string? command)
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}

// ここから下は Windows 版（src/Abbild/Controller/SerialLink.cs）の ControllerHub と同じ

/// <summary>コントローラー本体と、天気モジュール（ESP）をまとめて扱う。</summary>
public sealed class ControllerHub : IDisposable
{
    private readonly SerialLink _main = new();
    private readonly SerialLink _weather = new();
    private LedColor _lastLed = LedColor.None;
    private double _sinceSample;

    public ControllerInterpreter Sensors { get; } = new();
    public SerialLink Link => _main;
    public SerialLink WeatherLink => _weather;

    public Weather? LatestWeather { get; private set; }
    public DayOfWeek? LatestDay { get; private set; }
    public bool SoundEnabled { get; set; } = true;

    /// <summary>コントローラーから体の入力が来ているか。</summary>
    public bool Active => _main.State == LinkState.Connected && Sensors.Alive;

    public void Connect(Settings s)
    {
        ConnectMain(s);
        ConnectWeather(s);
    }

    public void ConnectMain(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        SoundEnabled = s.ControllerSound;
        _lastLed = LedColor.None;
        if (s.UseController) _main.Start(s.ControllerPort, s.ControllerBaud, l => ControllerProtocol.TryParse(l, out _));
        else _main.Stop();
    }

    public void ConnectWeather(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!string.IsNullOrEmpty(s.WeatherPort)) _weather.Start(s.WeatherPort, s.WeatherBaud, l => ControllerProtocol.TryParseWeather(l, out _));
        else _weather.Stop();
    }

    public void Update(double dt)
    {
        Sensors.Tick(dt);
        var samples = new List<ControllerSample>();
        int n = 0;
        while (n++ < 200 && _main.TryReadLine(out var line))
        {
            if (ControllerProtocol.TryParse(line, out var sample) && sample is not null) samples.Add(sample);
            else if (ControllerProtocol.TryParseWeather(line, out var w)) LatestWeather = w;
            else if (ControllerProtocol.TryParseDay(line, out var d)) LatestDay = d;
        }
        // 前の行からの経過時間を、届いた行で等分して流す（20Hz でも 100Hz でも時間が合うように）
        _sinceSample += dt;
        if (samples.Count > 0)
        {
            double each = Math.Min(0.5, _sinceSample) / samples.Count;
            foreach (var s in samples) Sensors.Feed(s, each);
            _sinceSample = 0;
        }
        n = 0;
        while (n++ < 50 && _weather.TryReadLine(out var line))
        {
            if (ControllerProtocol.TryParseWeather(line, out var w)) LatestWeather = w;
        }
    }

    public void Led(LedColor c)
    {
        if (c == LedColor.None || c == _lastLed) return;
        _lastLed = c;
        _main.Send(ControllerProtocol.LedCommand(c));
    }

    public void Sound(Cue c)
    {
        if (SoundEnabled) _main.Send(ControllerProtocol.SoundCommand(c));
    }

    public void Oled(OledAnim a) => _main.Send(ControllerProtocol.OledCommand(a));

    public void Dispose()
    {
        _main.Dispose();
        _weather.Dispose();
    }
}
