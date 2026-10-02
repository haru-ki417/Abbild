using Abbild.Core;
using Abbild.Engine;

namespace Abbild.Controller;

/// <summary>
/// 毎フレームの「体の入力」を作る。コントローラーがあればセンサーの値を、
/// なければキーボード・ゲームパッドの操作を、同じ BodyFrame の形にそろえる。
/// </summary>
public sealed class BodyInput(Input input, ControllerHub hub)
{
    private readonly AlternationCounter _alt = new();
    private readonly SimulatedHeart _heart = new();
    private double _cursor;
    private int _stickSide;

    public BodyMode Mode => hub.Active ? BodyMode.Sensor : BodyMode.Keys;

    public double SimulatedBpm => _heart.Bpm;

    /// <summary>ミニゲームを始めるときに呼ぶ（傾きのカーソルと連打の数え方を戻す）。</summary>
    public void Reset(bool calmStart)
    {
        _cursor = 0;
        _alt.Reset();
        _heart.Reset(calmStart ? 82 : 84);
    }

    public BodyFrame Read(double dt, bool breathGuide, double guideTime)
    {
        var f = new BodyFrame { Dt = dt };
        bool left = input.Pressed(Act.Left);
        bool right = input.Pressed(Act.Right);

        // ゲームパッドのスティックを左右にはじいたら、左右を押したことにする
        float sx = input.StickX;
        int side = sx < -0.6f ? -1 : sx > 0.6f ? 1 : 0;
        if (side != 0 && side != _stickSide)
        {
            if (side < 0) left = true; else right = true;
        }
        if (Math.Abs(sx) < 0.3f) _stickSide = 0; else if (side != 0) _stickSide = side;

        f.ConfirmPressed = input.Pressed(Act.Confirm);
        f.ConfirmHeld = input.Held(Act.Confirm);
        f.LeftPressed = left;
        f.RightPressed = right;
        f.Breathing = input.Held(Act.Breath);

        if (Mode == BodyMode.Sensor)
        {
            var s = hub.Sensors;
            var ev = s.TakeEvents();
            f.ConfirmPressed |= ev.A;
            f.ConfirmHeld |= s.ButtonAHeld;
            f.LeftPressed |= ev.Left;
            f.RightPressed |= ev.Right;
            f.Shakes = ev.Shakes;
            f.Tilt = s.Tilt;
            f.Moving = s.Moving;
            f.HeartBpm = s.Bpm;
            f.Breathing |= s.Breathing;
        }
        else
        {
            hub.Sensors.TakeEvents();
            f.Shakes = _alt.Feed(left, right);
            // 傾き：← → を押している間だけ、ゆっくり動く（スティックならその向きに）
            double dir = (input.Held(Act.Right) ? 1 : 0) - (input.Held(Act.Left) ? 1 : 0);
            if (Math.Abs(sx) > 0.2f) dir = sx;
            _cursor = Math.Clamp(_cursor + (dir * dt * 0.75), -1, 1);
            f.Tilt = _cursor;
            int actions = f.Shakes + (f.ConfirmPressed ? 1 : 0);
            _heart.Update(dt, actions, breathGuide, guideTime, f.ConfirmHeld);
            f.HeartBpm = _heart.Bpm;
        }
        return f;
    }

    public HeartThresholds Thresholds(Settings s) => Mode == BodyMode.Sensor ? HeartThresholds.FromSettings(s) : HeartThresholds.ForKeys;
}
