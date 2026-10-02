using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Abbild.Engine;

public enum Act { Up, Down, Left, Right, Confirm, Cancel, Menu, Breath }

/// <summary>キーボード・ゲームパッド・マウスをまとめて「操作」に直す。</summary>
public sealed class Input
{
    private KeyboardState _kb, _pkb;
    private GamePadState _gp, _pgp;
    private MouseState _ms, _pms;
    private readonly Dictionary<Act, float> _held = [];
    private readonly HashSet<Act> _repeat = [];
    private readonly System.Text.StringBuilder _typed = new();
    private Rectangle _viewport = new(0, 0, Gfx.Width, Gfx.Height);
    private readonly HashSet<Act> _inject = [];
    private readonly HashSet<Act> _injectPrev = [];
    private readonly HashSet<Act> _injectNow = [];

    /// <summary>名前入力中。文字キー（Z・X・C・WASD）を操作として扱わない。</summary>
    public bool TextMode { get; set; }

    /// <summary>自動操作（動作確認用）：このフレームで押しているものとして扱う。</summary>
    public void Inject(params Act[] acts)
    {
        foreach (var a in acts) _inject.Add(a);
    }

    public void InjectText(string text) => _typed.Append(text);

    /// <summary>最後に使ったのがゲームパッドか（ボタンの表示を変える）。</summary>
    public bool UsingGamepad { get; private set; }

    public Vector2 Mouse { get; private set; }
    public bool MouseMoved { get; private set; }
    public bool MouseClicked { get; private set; }
    public bool MouseRightClicked { get; private set; }
    public int Wheel { get; private set; }

    /// <summary>このフレームで打たれた文字（名前の入力用）。</summary>
    public string Typed { get; private set; } = "";

    public float StickX => _gp.IsConnected ? _gp.ThumbSticks.Left.X : 0;

    public void OnTextInput(char c)
    {
        if (!char.IsControl(c)) _typed.Append(c);
    }

    public void SetViewport(Rectangle r) => _viewport = r;

    public void Update(float dt)
    {
        _pkb = _kb;
        _pgp = _gp;
        _pms = _ms;
        _injectPrev.Clear();
        foreach (var a in _injectNow) _injectPrev.Add(a);
        _injectNow.Clear();
        foreach (var a in _inject) _injectNow.Add(a);
        _inject.Clear();
        _kb = Keyboard.GetState();
        _gp = GamePad.GetState(PlayerIndex.One);
        _ms = Microsoft.Xna.Framework.Input.Mouse.GetState();

        if (_kb.GetPressedKeyCount() > 0 && _pkb.GetPressedKeyCount() == 0) UsingGamepad = false;
        if (_gp.IsConnected && _gp.PacketNumber != _pgp.PacketNumber && AnyGamepadInput(_gp)) UsingGamepad = true;

        float sx = _viewport.Width / (float)Gfx.Width;
        float sy = _viewport.Height / (float)Gfx.Height;
        var m = new Vector2((_ms.X - _viewport.X) / sx, (_ms.Y - _viewport.Y) / sy);
        MouseMoved = (_ms.X != _pms.X || _ms.Y != _pms.Y) && _pms.X != 0;
        Mouse = m;
        MouseClicked = _ms.LeftButton == ButtonState.Pressed && _pms.LeftButton == ButtonState.Released;
        MouseRightClicked = _ms.RightButton == ButtonState.Pressed && _pms.RightButton == ButtonState.Released;
        Wheel = Math.Sign(_ms.ScrollWheelValue - _pms.ScrollWheelValue);

        Typed = _typed.ToString();
        _typed.Clear();

        _repeat.Clear();
        foreach (var a in Enum.GetValues<Act>())
        {
            if (Held(a))
            {
                float t = _held.GetValueOrDefault(a) + dt;
                float before = _held.GetValueOrDefault(a);
                _held[a] = t;
                if (before == 0) _repeat.Add(a);
                else if (t > 0.38f && ((int)((t - 0.38f) / 0.075f)) != ((int)((before - 0.38f) / 0.075f))) _repeat.Add(a);
            }
            else
            {
                _held[a] = 0;
            }
        }
    }

    private static bool AnyGamepadInput(GamePadState g) =>
        g.Buttons.A == ButtonState.Pressed || g.Buttons.B == ButtonState.Pressed || g.Buttons.X == ButtonState.Pressed
        || g.Buttons.Y == ButtonState.Pressed || g.Buttons.Start == ButtonState.Pressed || g.DPad.Up == ButtonState.Pressed
        || g.DPad.Down == ButtonState.Pressed || g.DPad.Left == ButtonState.Pressed || g.DPad.Right == ButtonState.Pressed
        || g.ThumbSticks.Left.Length() > 0.5f;

    private bool Down(KeyboardState k, GamePadState g, Act a, bool text) => a switch
    {
        Act.Up => k.IsKeyDown(Keys.Up) || (!text && k.IsKeyDown(Keys.W)) || g.DPad.Up == ButtonState.Pressed || g.ThumbSticks.Left.Y > 0.55f,
        Act.Down => k.IsKeyDown(Keys.Down) || (!text && k.IsKeyDown(Keys.S)) || g.DPad.Down == ButtonState.Pressed || g.ThumbSticks.Left.Y < -0.55f,
        Act.Left => k.IsKeyDown(Keys.Left) || (!text && k.IsKeyDown(Keys.A)) || g.DPad.Left == ButtonState.Pressed || g.ThumbSticks.Left.X < -0.55f || g.Buttons.LeftShoulder == ButtonState.Pressed,
        Act.Right => k.IsKeyDown(Keys.Right) || (!text && k.IsKeyDown(Keys.D)) || g.DPad.Right == ButtonState.Pressed || g.ThumbSticks.Left.X > 0.55f || g.Buttons.RightShoulder == ButtonState.Pressed,
        Act.Confirm => (!text && k.IsKeyDown(Keys.Z)) || k.IsKeyDown(Keys.Enter) || (!text && k.IsKeyDown(Keys.Space)) || g.Buttons.A == ButtonState.Pressed,
        Act.Cancel => (!text && k.IsKeyDown(Keys.X)) || (!text && k.IsKeyDown(Keys.Back)) || k.IsKeyDown(Keys.Escape) || g.Buttons.B == ButtonState.Pressed,
        Act.Menu => k.IsKeyDown(Keys.Escape) || k.IsKeyDown(Keys.Tab) || g.Buttons.Start == ButtonState.Pressed,
        Act.Breath => (!text && k.IsKeyDown(Keys.C)) || g.Buttons.Y == ButtonState.Pressed,
        _ => false,
    };

    public bool Held(Act a) => Down(_kb, _gp, a, TextMode) || _injectNow.Contains(a);

    public bool Pressed(Act a) => (Down(_kb, _gp, a, TextMode) && !Down(_pkb, _pgp, a, TextMode)) || (_injectNow.Contains(a) && !_injectPrev.Contains(a));

    /// <summary>押した瞬間と、押し続けたときの繰り返し（メニューのカーソル用）。</summary>
    public bool Repeat(Act a) => _repeat.Contains(a);

    public bool KeyPressed(Keys k) => _kb.IsKeyDown(k) && !_pkb.IsKeyDown(k);

    public bool KeyHeld(Keys k) => _kb.IsKeyDown(k);

    /// <summary>名前入力などで、文字キーを操作として扱わないときに使う。</summary>
    public bool ToggleFullscreen => (_kb.IsKeyDown(Keys.LeftAlt) && KeyPressed(Keys.Enter)) || KeyPressed(Keys.F11);

    public bool BackspacePressed => KeyPressed(Keys.Back);

    public string ConfirmLabel => UsingGamepad ? "A" : "Z";

    public string CancelLabel => UsingGamepad ? "B" : "X";

    public string BreathLabel => UsingGamepad ? "Y" : "C";

    public string MenuLabel => UsingGamepad ? "START" : "Esc";
}
