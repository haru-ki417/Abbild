using System.Collections.Concurrent;
using System.IO.Ports;
using Abbild.Core;

namespace Abbild.Controller;

public enum LinkState { Off, Searching, Connected, Lost }

/// <summary>
/// 自作コントローラー（Arduino）とのシリアル通信。読み取りは別スレッドで行い、
/// 受け取った行はゲームのスレッドで取り出す。つながらなくても、途中で抜けても、ゲームは止めない。
/// 接続しなおすたびに新しい「セッション」を作り、古いものは待たずに打ち切る（ゲームが固まらないように）。
/// </summary>
public sealed class SerialLink : IDisposable
{
    private sealed class Session
    {
        public readonly CancellationTokenSource Cancel = new();
        public readonly ConcurrentQueue<string> Lines = new();
        public readonly ConcurrentQueue<string> Out = new();
        public volatile LinkState State = LinkState.Searching;
        public volatile string Message = "";
        public volatile string? PortName;
        public SerialPort? Port;
    }

    private Session? _session;

    public LinkState State => _session?.State ?? LinkState.Off;
    public string? PortName => _session?.PortName;
    public string Message => _session?.Message is { Length: > 0 } m ? m : "未接続";

    /// <summary>このパソコンにあるシリアルポート。</summary>
    public static string[] Ports()
    {
        try { return SerialPort.GetPortNames().Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException or System.ComponentModel.Win32Exception) { return []; }
    }

    /// <summary>接続を始める。port が null なら、データを送ってくるポートを順に探す。</summary>
    public void Start(string? port, int baud, Func<string, bool> looksValid)
    {
        Stop();
        var s = new Session { Message = port is null ? "コントローラーを探しています…" : $"{port} に接続しています…" };
        _session = s;
        var thread = new Thread(() => Run(s, port, baud, looksValid)) { IsBackground = true, Name = "SerialLink" };
        thread.Start();
    }

    private static void Run(Session s, string? fixedPort, int baud, Func<string, bool> looksValid)
    {
        try
        {
            var candidates = fixedPort is null ? Ports() : [fixedPort];
            foreach (var name in candidates)
            {
                if (s.Cancel.IsCancellationRequested) return;
                if (TryOpen(s, name, baud, looksValid, fixedPort is not null)) break;
            }
            if (s.Port is null)
            {
                s.State = LinkState.Off;
                s.Message = candidates.Length == 0 ? "シリアルポートが見つかりません" : "コントローラーが見つかりません";
                return;
            }
            ReadLoop(s);
        }
        catch (Exception)
        {
            // 別スレッドの例外でゲームごと落ちないように、ここで必ず受け止める
            s.State = LinkState.Lost;
            s.Message = "コントローラーとの通信でエラーが起きました";
        }
        finally
        {
            Close(s.Port);
            s.Port = null;
        }
    }

    private static bool TryOpen(Session s, string name, int baud, Func<string, bool> looksValid, bool trust)
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
            // USB を抜いたときに後片付けの段階で落ちる既知の問題を避ける
            GC.SuppressFinalize(p.BaseStream);
            // Arduino は接続した瞬間にリセットされるので、少し待ちながら有効な行を探す
            var until = DateTime.UtcNow.AddSeconds(trust ? 4 : 3);
            while (DateTime.UtcNow < until && !s.Cancel.IsCancellationRequested)
            {
                string line;
                try { line = p.ReadLine(); }
                catch (TimeoutException) { continue; }
                if (looksValid(line))
                {
                    s.Port = p;
                    s.PortName = name;
                    s.State = LinkState.Connected;
                    s.Message = $"{name} で接続中";
                    s.Lines.Enqueue(line);
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            // 使えないポートは次へ
        }
        Close(p);
        return false;
    }

    private static void ReadLoop(Session s)
    {
        var port = s.Port!;
        DateTime lastData = DateTime.UtcNow;
        while (!s.Cancel.IsCancellationRequested)
        {
            try
            {
                while (s.Out.TryDequeue(out var cmd)) port.WriteLine(cmd);
                string line = port.ReadLine();
                s.Lines.Enqueue(line);
                while (s.Lines.Count > 500) s.Lines.TryDequeue(out _);
                lastData = DateTime.UtcNow;
                if (s.State != LinkState.Connected)
                {
                    s.State = LinkState.Connected;
                    s.Message = $"{s.PortName} で接続中";
                }
            }
            catch (TimeoutException)
            {
                if ((DateTime.UtcNow - lastData).TotalSeconds > 3 && s.State == LinkState.Connected)
                {
                    s.State = LinkState.Lost;
                    s.Message = "コントローラーからの信号が途切れています";
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                s.State = LinkState.Lost;
                s.Message = "コントローラーが外れました（つなぎなおしたら「接続しなおす」）";
                break;
            }
        }
    }

    private static void Close(SerialPort? p)
    {
        if (p is null) return;
        try { p.Close(); }
        catch (Exception) { }
        try { p.Dispose(); }
        catch (Exception) { }
    }

    public bool TryReadLine(out string line)
    {
        var s = _session;
        if (s is not null && s.Lines.TryDequeue(out var l))
        {
            line = l;
            return true;
        }
        line = "";
        return false;
    }

    public void Send(string? command)
    {
        var s = _session;
        if (command is null || s is null || s.State != LinkState.Connected) return;
        if (s.Out.Count < 32) s.Out.Enqueue(command);
    }

    /// <summary>今のセッションを打ち切る（待たない）。</summary>
    public void Stop()
    {
        var s = _session;
        _session = null;
        if (s is null) return;
        s.Cancel.Cancel();
        // 読み取り中の ReadLine を止めるため、ポートも閉じる（読み取りスレッドの後片付けと重なっても大丈夫）
        Close(s.Port);
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
