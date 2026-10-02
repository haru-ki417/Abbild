using Abbild.Core;
using Microsoft.Xna.Framework.Audio;
using NVorbis;
using Cue = Abbild.Core.Cue;

namespace Abbild.Engine;

/// <summary>
/// BGM（Ogg Vorbis をその場で読み出して流す）と効果音（起動時に合成）。
/// 音の出る装置がなくてもゲームは止めない。
/// </summary>
public sealed class Audio : IDisposable
{
    private const int ChunkFrames = 4096;
    private readonly string _bgmDir;
    private readonly Dictionary<Cue, SoundEffect> _sfx = [];
    private DynamicSoundEffectInstance? _stream;
    private VorbisReader? _reader;
    private string? _current;
    private string? _pending;
    private float _fade = 1f;
    private float _fadeDir;
    private bool _loop = true;
    private float[] _floatBuf = [];
    private byte[] _byteBuf = [];

    public Audio(string contentRoot)
    {
        _bgmDir = Path.Combine(contentRoot, "bgm");
        try
        {
            foreach (Cue c in Enum.GetValues<Cue>())
            {
                if (c == Cue.None) continue;
                var pcm = Synth.Make(c);
                if (pcm.Length > 0) _sfx[c] = new SoundEffect(Synth.ToBytes(pcm), Synth.Rate, AudioChannels.Mono);
            }
            Enabled = true;
        }
        catch (Exception ex) when (ex is NoAudioHardwareException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            Enabled = false;
        }
    }

    public bool Enabled { get; private set; }

    /// <summary>0〜1。</summary>
    public float BgmVolume { get; set; } = 0.7f;
    public float SeVolume { get; set; } = 0.8f;

    public string? CurrentBgm => _pending ?? _current;

    public void Play(Cue cue, float volume = 1f, float pitch = 0f)
    {
        if (!Enabled || cue == Cue.None || SeVolume <= 0) return;
        if (_sfx.TryGetValue(cue, out var s))
        {
            try { s.Play(Math.Clamp(SeVolume * volume, 0, 1), Math.Clamp(pitch, -1, 1), 0); }
            catch (InvalidOperationException) { }
        }
    }

    /// <summary>BGM を切り替える（同じ曲なら何もしない）。null で止める。</summary>
    public void PlayBgm(string? name, bool loop = true)
    {
        if (!Enabled) return;
        if (name == CurrentBgm && _fadeDir >= 0) return;
        _pending = name;
        _loop = loop;
        _fadeDir = _current is null ? 0 : -1;
        if (_current is null) StartPending();
    }

    private void StartPending()
    {
        StopStream();
        _current = _pending;
        _pending = null;
        if (_current is null) return;
        string path = Path.Combine(_bgmDir, _current + ".ogg");
        if (!File.Exists(path)) { _current = null; return; }
        try
        {
            _reader = new VorbisReader(path);
            _stream = new DynamicSoundEffectInstance(_reader.SampleRate, _reader.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
            _floatBuf = new float[ChunkFrames * _reader.Channels];
            _byteBuf = new byte[ChunkFrames * _reader.Channels * 2];
            _fade = 0f;
            _fadeDir = 1;
            Fill();
            _stream.Volume = 0;
            _stream.Play();
        }
        catch (Exception ex) when (ex is NoAudioHardwareException or InvalidOperationException or IOException or ArgumentException)
        {
            StopStream();
            _current = null;
        }
    }

    private void Fill()
    {
        if (_stream is null || _reader is null) return;
        while (_stream.PendingBufferCount < 3)
        {
            int read = _reader.ReadSamples(_floatBuf, 0, _floatBuf.Length);
            if (read <= 0)
            {
                if (!_loop) return;
                _reader.SamplePosition = 0;
                read = _reader.ReadSamples(_floatBuf, 0, _floatBuf.Length);
                if (read <= 0) return;
            }
            for (int i = 0; i < read; i++)
            {
                short v = (short)Math.Clamp(_floatBuf[i] * 32767f, short.MinValue, short.MaxValue);
                _byteBuf[i * 2] = (byte)(v & 0xFF);
                _byteBuf[(i * 2) + 1] = (byte)((v >> 8) & 0xFF);
            }
            _stream.SubmitBuffer(_byteBuf, 0, read * 2);
        }
    }

    public void Update(float dt)
    {
        if (!Enabled) return;
        if (_fadeDir != 0)
        {
            _fade += _fadeDir * dt / 0.6f;
            if (_fade >= 1) { _fade = 1; _fadeDir = 0; }
            if (_fade <= 0)
            {
                _fade = 0;
                _fadeDir = 0;
                StartPending();
            }
        }
        if (_stream is not null)
        {
            try
            {
                Fill();
                _stream.Volume = Math.Clamp(BgmVolume * _fade * 0.85f, 0, 1);
            }
            catch (InvalidOperationException)
            {
                StopStream();
            }
        }
    }

    private void StopStream()
    {
        try
        {
            _stream?.Stop();
            _stream?.Dispose();
        }
        catch (InvalidOperationException) { }
        _stream = null;
        _reader?.Dispose();
        _reader = null;
    }

    public void Dispose()
    {
        StopStream();
        foreach (var s in _sfx.Values) s.Dispose();
        _sfx.Clear();
    }
}

/// <summary>ピコピコ系の効果音をその場で作る（素材の権利を気にしなくてよいように）。</summary>
public static class Synth
{
    public const int Rate = 44100;

    private enum Wave { Square, Triangle, Sine, Saw, Noise }

    private static readonly Random Rng = new(1234);

    public static byte[] ToBytes(float[] pcm)
    {
        var b = new byte[pcm.Length * 2];
        for (int i = 0; i < pcm.Length; i++)
        {
            short v = (short)Math.Clamp(pcm[i] * 32767f, short.MinValue, short.MaxValue);
            b[i * 2] = (byte)(v & 0xFF);
            b[(i * 2) + 1] = (byte)((v >> 8) & 0xFF);
        }
        return b;
    }

    private static float Osc(Wave w, double phase)
    {
        double p = phase - Math.Floor(phase);
        return w switch
        {
            Wave.Square => p < 0.5 ? 1f : -1f,
            Wave.Triangle => (float)(p < 0.5 ? (4 * p) - 1 : 3 - (4 * p)),
            Wave.Sine => (float)Math.Sin(p * Math.Tau),
            Wave.Saw => (float)((2 * p) - 1),
            _ => (float)((Rng.NextDouble() * 2) - 1),
        };
    }

    /// <summary>周波数を f0 から f1 へ動かしながら鳴らす。</summary>
    private static float[] Tone(Wave w, double f0, double f1, double dur, double vol = 0.5, double attack = 0.005, double release = 0.04, double vibrato = 0)
    {
        int n = (int)(dur * Rate);
        var buf = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double k = t / dur;
            double f = f0 * Math.Pow(f1 / f0, k);
            if (vibrato > 0) f *= 1 + (0.03 * Math.Sin(t * Math.Tau * vibrato));
            phase += f / Rate;
            float env = (float)Math.Min(1, Math.Min(t / attack, (dur - t) / release));
            buf[i] = Osc(w, phase) * (float)vol * Math.Max(0, env);
        }
        return buf;
    }

    private static float[] Noise(double dur, double vol = 0.4, double decayPow = 2, double lowpass = 1.0)
    {
        int n = (int)(dur * Rate);
        var buf = new float[n];
        float last = 0;
        for (int i = 0; i < n; i++)
        {
            double k = i / (double)n;
            float v = (float)((Rng.NextDouble() * 2) - 1);
            last += (v - last) * (float)lowpass;
            buf[i] = last * (float)vol * (float)Math.Pow(1 - k, decayPow);
        }
        return buf;
    }

    private static float[] Seq(params float[][] parts)
    {
        var list = new List<float>();
        foreach (var p in parts) list.AddRange(p);
        return [.. list];
    }

    private static float[] Mix(params float[][] parts)
    {
        int n = parts.Max(p => p.Length);
        var buf = new float[n];
        foreach (var p in parts)
        {
            for (int i = 0; i < p.Length; i++) buf[i] += p[i];
        }
        return buf;
    }

    private static float[] Silence(double dur) => new float[(int)(dur * Rate)];

    private static double Note(int semitoneFromA4) => 440 * Math.Pow(2, semitoneFromA4 / 12.0);

    private static float[] Arp(Wave w, double step, double vol, params int[] notes) =>
        Seq(notes.Select(n => Tone(w, Note(n), Note(n), step, vol, 0.003, 0.02)).ToArray());

    public static float[] Make(Cue c) => c switch
    {
        Cue.Cursor => Tone(Wave.Square, 880, 880, 0.035, 0.18),
        Cue.Confirm => Tone(Wave.Square, 660, 990, 0.08, 0.22),
        Cue.Cancel => Tone(Wave.Square, 520, 330, 0.08, 0.2),
        Cue.Buzzer => Tone(Wave.Square, 150, 120, 0.22, 0.25, vibrato: 30),
        Cue.Tick => Tone(Wave.Square, 1250, 1250, 0.045, 0.18),
        Cue.Success => Arp(Wave.Square, 0.06, 0.22, 3, 7, 10, 15),
        Cue.Hit => Mix(Noise(0.09, 0.5, 3, 0.6), Tone(Wave.Triangle, 160, 60, 0.12, 0.6)),
        Cue.Critical => Mix(Noise(0.16, 0.55, 2, 0.8), Tone(Wave.Saw, 500, 90, 0.18, 0.35), Seq(Silence(0.03), Tone(Wave.Square, 1760, 1760, 0.08, 0.15))),
        Cue.Damage => Mix(Tone(Wave.Square, 320, 70, 0.16, 0.3), Noise(0.12, 0.3, 2, 0.5)),
        Cue.BigDamage => Mix(Noise(0.35, 0.6, 1.5, 0.3), Tone(Wave.Saw, 160, 35, 0.35, 0.35)),
        Cue.Miss => Noise(0.16, 0.3, 1.2, 0.25),
        Cue.Fire => Mix(Noise(0.45, 0.45, 1.0, 0.35), Tone(Wave.Saw, 90, 180, 0.45, 0.2)),
        Cue.Ice => Seq(Tone(Wave.Triangle, 2100, 2100, 0.05, 0.25), Tone(Wave.Triangle, 2800, 2800, 0.05, 0.25), Tone(Wave.Triangle, 2400, 2400, 0.05, 0.25), Tone(Wave.Triangle, 3200, 3000, 0.15, 0.25)),
        Cue.Thunder => Mix(Noise(0.55, 0.6, 1.3, 0.9), Tone(Wave.Square, 60, 40, 0.5, 0.25)),
        Cue.Poison => Seq(Tone(Wave.Sine, 220, 380, 0.07, 0.4), Tone(Wave.Sine, 260, 420, 0.07, 0.4), Tone(Wave.Sine, 200, 340, 0.09, 0.4)),
        Cue.Holy => Mix(Tone(Wave.Sine, Note(3), Note(3), 0.6, 0.18, 0.05, 0.3), Tone(Wave.Sine, Note(7), Note(7), 0.6, 0.15, 0.08, 0.3), Tone(Wave.Sine, Note(10), Note(10), 0.6, 0.15, 0.12, 0.3)),
        Cue.Heal => Arp(Wave.Triangle, 0.075, 0.35, 3, 7, 10, 15, 19),
        Cue.Magic => Tone(Wave.Sine, 300, 1300, 0.28, 0.35, 0.01, 0.08),
        Cue.Charge => Tone(Wave.Saw, 110, 700, 0.42, 0.25, 0.02, 0.05),
        Cue.LevelUp => Seq(Arp(Wave.Square, 0.08, 0.22, 3, 7, 10), Tone(Wave.Square, Note(15), Note(15), 0.35, 0.22, 0.005, 0.15)),
        Cue.Victory => Seq(Arp(Wave.Square, 0.09, 0.22, 10, 10, 10), Tone(Wave.Square, Note(15), Note(15), 0.4, 0.24, 0.005, 0.2)),
        Cue.Encounter => Seq(Tone(Wave.Square, 880, 660, 0.08, 0.2), Tone(Wave.Square, 830, 620, 0.08, 0.2), Tone(Wave.Square, 780, 440, 0.18, 0.2, vibrato: 12)),
        Cue.BossEncounter => Mix(Tone(Wave.Saw, 55, 50, 0.9, 0.35, 0.2, 0.3), Noise(0.9, 0.25, 0.6, 0.08), Tone(Wave.Square, 110, 104, 0.9, 0.12, 0.3, 0.3)),
        Cue.Explosion => Mix(Noise(0.5, 0.7, 1.4, 0.2), Tone(Wave.Sine, 120, 40, 0.4, 0.5)),
        Cue.Lock => Mix(Noise(0.02, 0.4, 1, 1), Tone(Wave.Square, 2000, 1800, 0.03, 0.15)),
        Cue.Unlock => Seq(Mix(Noise(0.02, 0.4, 1, 1), Tone(Wave.Square, 2000, 1800, 0.03, 0.15)), Silence(0.05), Arp(Wave.Square, 0.06, 0.2, 10, 15, 22)),
        Cue.Splash => Noise(0.35, 0.5, 1.6, 0.15),
        Cue.Heartbeat => Seq(Tone(Wave.Sine, 70, 45, 0.09, 0.8), Silence(0.11), Tone(Wave.Sine, 62, 40, 0.1, 0.6)),
        Cue.Swing => Mix(Tone(Wave.Sine, 2600, 2500, 0.5, 0.3, 0.001, 0.45), Tone(Wave.Sine, 3900, 3800, 0.35, 0.12, 0.001, 0.3), Noise(0.05, 0.4, 2, 1)),
        Cue.Alarm => Seq(Tone(Wave.Square, 1000, 1000, 0.1, 0.2), Tone(Wave.Square, 800, 800, 0.1, 0.2), Tone(Wave.Square, 1000, 1000, 0.1, 0.2), Tone(Wave.Square, 800, 800, 0.1, 0.2)),
        Cue.Freeze => Tone(Wave.Square, 1700, 1700, 0.14, 0.24),
        Cue.Breath => Noise(0.45, 0.25, 0.8, 0.08),
        Cue.Death => Tone(Wave.Square, 440, 55, 1.1, 0.25, 0.01, 0.3, vibrato: 6),
        Cue.Revive => Seq(Arp(Wave.Triangle, 0.06, 0.35, -2, 3, 7, 10, 15, 19, 22), Tone(Wave.Sine, Note(27), Note(27), 0.4, 0.2, 0.01, 0.3)),
        Cue.Step => Noise(0.06, 0.25, 2, 0.15),
        Cue.Coin => Seq(Tone(Wave.Square, Note(14), Note(14), 0.07, 0.2), Tone(Wave.Square, Note(19), Note(19), 0.25, 0.2, 0.003, 0.2)),
        _ => [],
    };
}
