using System.Collections.Concurrent;
using System.IO.Ports;
using Abbild.Core;

namespace Abbild.Controller;

public enum LinkState { Off, Searching, Connected, Lost }

/// <summary>
/// 自作コントローラー（Arduino）とのシリアル通信。読み取りは別スレッドで行い、
/// 受け取った行はゲームのスレッドで取り出す。つながらなくてもゲームは続けられる。
/// </summary>
public sealed class SerialLink : IDisposable
{
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly ConcurrentQueue<string> _out = new();
    private SerialPort? _port;
    private Thread? _thread;
    private volatile bool _stop;
    private volatile LinkState _state = LinkState.Off;

    public LinkState State => _state;
    public string? PortName { get; private set; }
    public string Message { get; private set; } = "未接続";

    /// <summary>このパソコンにあるシリアルポート。</summary>
    public static string[] Ports()
    {
        try { return SerialPort.GetPortNames().Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException) { return []; }
    }

    /// <summary>接続を始める。port が null なら、データを送ってくるポートを順に探す。</summary>
    public void Start(string? port, int baud, Func<string, bool> looksValid)
    {
        Stop();
        _stop = false;
        _state = LinkState.Searching;
        Message = port is null ? "コントローラーを探しています…" : $"{port} に接続しています…";
        _thread = new Thread(() => Run(port, baud, looksValid)) { IsBackground = true, Name = "SerialLink" };
        _thread.Start();
    }

    private void Run(string? fixedPort, int baud, Func<string, bool> looksValid)
    {
        var candidates = fixedPort is null ? Ports() : [fixedPort];
        foreach (var name in candidates)
        {
            if (_stop) return;
            if (TryOpen(name, baud, looksValid, fixedPort is not null)) break;
        }
        if (_port is null)
        {
            _state = LinkState.Off;
            Message = candidates.Length == 0 ? "シリアルポートが見つかりません" : "コントローラーが見つかりません";
            return;
        }
        ReadLoop();
    }

    private bool TryOpen(string name, int baud, Func<string, bool> looksValid, bool trust)
    {
        SerialPort? p = null;
        try
        {
            p = new SerialPort(name, baud)
            {
                NewLine = "\n",
                ReadTimeout = 400,
                WriteTimeout = 300,
                DtrEnable = true,
                RtsEnable = true,
                Encoding = System.Text.Encoding.ASCII,
            };
            p.Open();
            // Arduino は接続した瞬間にリセットされるので、少し待ちながら有効な行を探す
            var until = DateTime.UtcNow.AddSeconds(trust ? 4 : 3);
            while (DateTime.UtcNow < until && !_stop)
            {
                string line;
                try { line = p.ReadLine(); }
                catch (TimeoutException) { continue; }
                if (looksValid(line))
                {
                    _port = p;
                    PortName = name;
                    _state = LinkState.Connected;
                    Message = $"{name} で接続中";
                    _lines.Enqueue(line);
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // 使えないポートは次へ
        }
        try { p?.Close(); } catch (IOException) { }
        p?.Dispose();
        return false;
    }

    private void ReadLoop()
    {
        var port = _port!;
        DateTime lastData = DateTime.UtcNow;
        while (!_stop)
        {
            try
            {
                while (_out.TryDequeue(out var cmd)) port.WriteLine(cmd);
                string line = port.ReadLine();
                _lines.Enqueue(line);
                lastData = DateTime.UtcNow;
                if (_state != LinkState.Connected)
                {
                    _state = LinkState.Connected;
                    Message = $"{PortName} で接続中";
                }
            }
            catch (TimeoutException)
            {
                if ((DateTime.UtcNow - lastData).TotalSeconds > 3 && _state == LinkState.Connected)
                {
                    _state = LinkState.Lost;
                    Message = "コントローラーからの信号が途切れています";
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _state = LinkState.Lost;
                Message = "コントローラーが外れました";
                break;
            }
        }
        try { port.Close(); } catch (IOException) { }
        port.Dispose();
        if (ReferenceEquals(_port, port)) _port = null;
    }

    public bool TryReadLine(out string line) => _lines.TryDequeue(out line!);

    public void Send(string? command)
    {
        if (command is null || _state != LinkState.Connected) return;
        if (_out.Count < 32) _out.Enqueue(command);
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(800);
        _thread = null;
        try { _port?.Close(); } catch (IOException) { }
        _port?.Dispose();
        _port = null;
        _state = LinkState.Off;
        Message = "未接続";
        while (_lines.TryDequeue(out _)) { }
        while (_out.TryDequeue(out _)) { }
    }

    public void Dispose() => Stop();
}

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
        ArgumentNullException.ThrowIfNull(s);
        SoundEnabled = s.ControllerSound;
        if (s.UseController) _main.Start(s.ControllerPort, s.ControllerBaud, l => ControllerProtocol.TryParse(l, out _));
        else _main.Stop();
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
        while (_weather.TryReadLine(out var line))
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
