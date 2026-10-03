using Abbild.Controller;
using Abbild.Core;
using Microsoft.Xna.Framework;

namespace Abbild.Engine;

/// <summary>どの画面からも使う道具一式。</summary>
public sealed class Services
{
    public required AbbildGame Game { get; init; }
    public required Gfx Gfx { get; init; }
    public required Assets Assets { get; init; }
    public required Audio Audio { get; init; }
    public required Input Input { get; init; }
    public required ControllerHub Controller { get; init; }
    public required BodyInput Body { get; init; }
    public required SaveStore Store { get; init; }
    public required Settings Settings { get; set; }
    public required Records Records { get; set; }

    /// <summary>効果音を鳴らし、コントローラーのブザーにも送る。</summary>
    public void Cue(Cue c, float volume = 1f)
    {
        Audio.Play(c, volume);
        Controller.Sound(c);
    }

    public void ApplyAudioSettings()
    {
        Audio.BgmVolume = Settings.BgmVolume / 10f;
        Audio.SeVolume = Settings.SeVolume / 10f;
        Controller.SoundEnabled = Settings.ControllerSound;
    }

    public void SaveSettings()
    {
        try { Store.SaveSettings(Settings); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void SaveRecords()
    {
        try { Store.SaveRecords(Records); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>テキストの速さ（1 秒あたりの文字数）。</summary>
    public float TextCps => Settings.TextSpeed switch { 0 => 28f, 2 => 140f, _ => 60f };
}

public abstract class Scene(Services s)
{
    protected Services S { get; } = s;
    protected Gfx G => S.Gfx;
    protected Input In => S.Input;

    /// <summary>画面が始まってからの時間。</summary>
    public float Time { get; private set; }

    public virtual void Enter() { }

    public virtual void Leave() { }

    public void Tick(float dt)
    {
        Time += dt;
        Update(dt);
    }

    protected abstract void Update(float dt);

    public abstract void Draw();

    /// <summary>画面を揺らす量（ピクセル）。</summary>
    public virtual Vector2 Shake => Vector2.Zero;
}

/// <summary>画面の切り替え（暗転して入れ替える）。</summary>
public sealed class SceneManager
{
    private Scene? _current;
    private Scene? _next;
    private float _fade;      // 0 = 見える, 1 = 真っ黒
    private int _dir;         // 1 = 暗くしている, -1 = 明るくしている
    private float _speed = 3f;

    public Scene? Current => _current;

    public bool Transitioning => _dir != 0;

    public void Go(Scene next, float seconds = 0.35f)
    {
        _next = next;
        _speed = 1f / Math.Max(0.05f, seconds);
        if (_current is null)
        {
            Swap();
            _fade = 1;
            _dir = -1;
        }
        else
        {
            _dir = 1;
        }
    }

    private void Swap()
    {
        _current?.Leave();
        _current = _next;
        _next = null;
        _current?.Enter();
    }

    public void Update(float dt)
    {
        if (_dir == 1)
        {
            _fade += dt * _speed;
            if (_fade >= 1)
            {
                _fade = 1;
                Swap();
                _dir = -1;
            }
            return;
        }
        if (_dir == -1)
        {
            _fade -= dt * _speed;
            if (_fade <= 0) { _fade = 0; _dir = 0; }
        }
        _current?.Tick(dt);
    }

    public void Draw(Gfx g)
    {
        _current?.Draw();
        if (_fade > 0)
        {
            g.Batch.Begin();
            DrawWipe(g, _fade, _dir >= 0);
            g.Batch.End();
        }
    }

    /// <summary>
    /// 場面の切り替え：ひし形のタイルが斜めに広がって画面を覆い、次の場面では逆向きに消えていく。
    /// </summary>
    private static void DrawWipe(Gfx g, float f, bool closing)
    {
        const int tile = 96;
        int cols = (Gfx.Width / tile) + 2, rows = (Gfx.Height / tile) + 2;
        float span = cols + rows;
        // 最後は必ず真っ黒になるように、少し余裕を持たせる
        float p = f * 1.15f;
        var origin = new Vector2(0.5f, 0.5f);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                // 閉じるときは左上から、開くときは右下へ抜けていく
                float order = (c + r) / span;
                if (!closing) order = 1 - order;
                float k = Math.Clamp((p - (order * 0.6f)) / 0.4f, 0, 1);
                if (k <= 0) continue;
                float size = tile * 1.45f * k;
                g.Batch.Draw(g.Pixel, new Vector2(c * tile, r * tile), null, Color.Black, MathF.PI / 4, origin, size, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0);
            }
        }
    }
}

public static class Ease
{
    public static float OutCubic(float t) => 1 - MathF.Pow(1 - Math.Clamp(t, 0, 1), 3);

    public static float OutBack(float t)
    {
        t = Math.Clamp(t, 0, 1);
        const float c1 = 1.70158f, c3 = c1 + 1;
        return 1 + (c3 * MathF.Pow(t - 1, 3)) + (c1 * MathF.Pow(t - 1, 2));
    }

    public static float InOutSine(float t) => -(MathF.Cos(MathF.PI * Math.Clamp(t, 0, 1)) - 1) / 2;

    public static float InCubic(float t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * t;
    }

    public static float InOutCubic(float t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5f ? 4 * t * t * t : 1 - (MathF.Pow((-2 * t) + 2, 3) / 2);
    }

    public static float InBack(float t)
    {
        t = Math.Clamp(t, 0, 1);
        const float c1 = 1.70158f, c3 = c1 + 1;
        return (c3 * t * t * t) - (c1 * t * t);
    }

    /// <summary>落ちて、はねて、止まる。</summary>
    public static float OutBounce(float t)
    {
        t = Math.Clamp(t, 0, 1);
        const float n = 7.5625f, d = 2.75f;
        if (t < 1 / d) return n * t * t;
        if (t < 2 / d) { t -= 1.5f / d; return (n * t * t) + 0.75f; }
        if (t < 2.5f / d) { t -= 2.25f / d; return (n * t * t) + 0.9375f; }
        t -= 2.625f / d;
        return (n * t * t) + 0.984375f;
    }

    /// <summary>行き過ぎて、ゆれて、止まる。</summary>
    public static float OutElastic(float t)
    {
        t = Math.Clamp(t, 0, 1);
        if (t is 0 or 1) return t;
        const float c4 = MathF.Tau / 3;
        return (MathF.Pow(2, -10 * t) * MathF.Sin(((t * 10) - 0.75f) * c4)) + 1;
    }

    /// <summary>a〜b の区間での進み具合（0〜1）。</summary>
    public static float Span(float t, float a, float b) => Math.Clamp((t - a) / (b - a), 0, 1);
}
