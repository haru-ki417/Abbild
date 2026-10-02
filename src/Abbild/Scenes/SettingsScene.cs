using Abbild.Controller;
using Abbild.Core;
using Abbild.Engine;
using Abbild.Ui;
using Microsoft.Xna.Framework;

namespace Abbild.Scenes;

public sealed class SettingsScene : Scene
{
    private readonly Func<Scene> _back;
    private readonly Menu _menu = new() { RowHeight = 62, FontSize = 36, VisibleRows = 12, Wrap = true };
    private string[] _ports = [];
    private bool _mainDirty;
    private bool _weatherDirty;

    public SettingsScene(Services s, Func<Scene> back) : base(s)
    {
        _back = back;
        _menu.OnAdjust = Adjust;
    }

    private enum Row { Bgm, Se, Screen, TextSpeed, UseController, Port, ControllerSound, Excite, WeatherPort, Reconnect, Sensors, Back }

    public override void Enter()
    {
        _ports = SerialLink.Ports();
        Refresh(keep: false);
    }

    private void Refresh(bool keep = true)
    {
        var st = S.Settings;
        string Vol(int v) => v == 0 ? "なし" : new string('■', v) + new string('□', 10 - v);
        _menu.SetItems(
        [
            new("BGM の音量", true, Vol(st.BgmVolume), "← → で調整"),
            new("効果音の音量", true, Vol(st.SeVolume), "← → で調整"),
            new("画面", true, S.Game.IsFullscreen ? "フルスクリーン" : "ウィンドウ", "F11 でもいつでも切り替えられます"),
            new("文字の速さ", true, st.TextSpeed switch { 0 => "ゆっくり", 2 => "はやい", _ => "ふつう" }, "戦いのメッセージが流れる速さ"),
            new("自作コントローラー", true, st.UseController ? "使う" : "使わない", "Arduino で作ったコントローラーを使うか"),
            new("　ポート", st.UseController, st.ControllerPort ?? "自動で探す", "← → で選んで、決定で接続します"),
            new("　コントローラーの音", st.UseController, st.ControllerSound ? "鳴らす" : "鳴らさない", "ブザーの音（LED はいつも光ります）"),
            new("　高ぶりの判定", st.UseController, $"安静時 +{st.ExciteMargin}", $"安静時の心拍 {st.RestingBpm:0}。バーサークや威圧で「高ぶっている」とみなす上がり幅"),
            new("天気モジュール（ESP）", true, st.WeatherPort ?? "使わない", "外の天気をゲームに反映する（任意）。← → で選んで、決定で接続します"),
            new("接続しなおす", st.UseController, "", S.Controller.Link.Message),
            new("センサーの確認", true, "", "いま体の入力がどう読まれているかを見る"),
            new("もどる"),
        ], keep);
    }

    private void Adjust(int row, int d)
    {
        var st = S.Settings;
        switch ((Row)row)
        {
            case Row.Bgm: st.BgmVolume = Math.Clamp(st.BgmVolume + d, 0, 10); break;
            case Row.Se: st.SeVolume = Math.Clamp(st.SeVolume + d, 0, 10); S.Audio.SeVolume = st.SeVolume / 10f; break;
            case Row.Screen: Toggle(Row.Screen); return;
            case Row.TextSpeed: st.TextSpeed = Math.Clamp(st.TextSpeed + d, 0, 2); break;
            case Row.UseController: Toggle(Row.UseController); return;
            case Row.ControllerSound: st.ControllerSound = !st.ControllerSound; break;
            case Row.Excite: st.ExciteMargin = Math.Clamp(st.ExciteMargin + (d * 2), 4, 40); break;
            case Row.Port: CyclePort(d); return;
            case Row.WeatherPort: CycleWeather(d); return;
        }
        S.ApplyAudioSettings();
        S.SaveSettings();
        Refresh();
    }

    private void CyclePort(int d)
    {
        var options = new List<string?> { null };
        options.AddRange(_ports);
        int i = options.IndexOf(S.Settings.ControllerPort);
        S.Settings.ControllerPort = options[((i + d) % options.Count + options.Count) % options.Count];
        _mainDirty = true;
        S.SaveSettings();
        Refresh();
    }

    private void CycleWeather(int d)
    {
        var options = new List<string?> { null };
        options.AddRange(_ports);
        int i = options.IndexOf(S.Settings.WeatherPort);
        S.Settings.WeatherPort = options[((i + d) % options.Count + options.Count) % options.Count];
        _weatherDirty = true;
        S.SaveSettings();
        Refresh();
    }

    private void Toggle(Row row)
    {
        var st = S.Settings;
        switch (row)
        {
            case Row.Screen:
                S.Game.SetFullscreen(!S.Game.IsFullscreen);
                st.Fullscreen = S.Game.IsFullscreen;
                break;
            case Row.UseController:
                st.UseController = !st.UseController;
                S.Controller.ConnectMain(st);
                _mainDirty = false;
                break;
        }
        S.SaveSettings();
        Refresh();
    }

    protected override void Update(float dt)
    {
        int i = _menu.Update(S, MenuRect);
        if (i >= 0)
        {
            switch ((Row)i)
            {
                case Row.Screen: Toggle(Row.Screen); break;
                case Row.UseController: Toggle(Row.UseController); break;
                case Row.ControllerSound: Adjust(i, 1); break;
                case Row.Port:
                case Row.Reconnect:
                    _ports = SerialLink.Ports();
                    S.Controller.ConnectMain(S.Settings);
                    _mainDirty = false;
                    break;
                case Row.WeatherPort:
                    S.Controller.ConnectWeather(S.Settings);
                    _weatherDirty = false;
                    break;
                case Row.Sensors: S.Game.Scenes.Go(new SensorTestScene(S, () => this)); break;
                case Row.Back: Back(); break;
                case Row.TextSpeed: Adjust(i, 1); break;
            }
        }
        if (In.Pressed(Act.Cancel)) Back();
        // 接続の状態が変わったら説明を書きかえる
        if ((int)(Time * 2) != (int)((Time - dt) * 2)) Refresh();
    }

    private void Back()
    {
        S.Cue(Cue.Cancel);
        S.SaveSettings();
        if (_mainDirty) S.Controller.ConnectMain(S.Settings);
        if (_weatherDirty) S.Controller.ConnectWeather(S.Settings);
        S.Game.Scenes.Go(_back());
    }

    private static Rectangle MenuRect => new(380, 150, 1160, 62 * 12);

    public override void Draw()
    {
        var g = G;
        g.Batch.Begin();
        Art.Cover(g, S.Assets.Background("dungeon"), 1.05f, default, new Color(70, 70, 85));
        g.TextCentered("せってい", new Vector2(Gfx.Width / 2f, 76), 56, Palette.Gold, bold: true);
        var mr = MenuRect;
        g.Window(new Rectangle(mr.X - 30, mr.Y - 24, mr.Width + 60, mr.Height + 48), 0.95f);
        _menu.Draw(g, mr, Time);
        var desc = _menu.Selected?.Description ?? "";
        g.TextCentered(desc, new Vector2(Gfx.Width / 2f, 975), 30, Palette.Dim);
        var link = S.Controller.Link;
        g.TextCentered($"コントローラー：{link.Message}", new Vector2(Gfx.Width / 2f, 1020), 26, S.Controller.Active ? Palette.Good : Palette.Dim);
        Hints.Draw(g, In, ("←→", "変更"), Hints.Confirm(In), Hints.Cancel(In));
        g.Batch.End();
    }
}

/// <summary>センサーの値を見る画面（自作コントローラーの調整用）。</summary>
public sealed class SensorTestScene(Services s, Func<Scene> back) : Scene(s)
{
    private int _shakes;
    private int _presses;
    private BodyFrame _f;
    private readonly List<float> _bpmHistory = [];

    protected override void Update(float dt)
    {
        _f = S.Body.Read(dt, false, Time);
        _shakes += _f.Shakes;
        if (_f.ConfirmPressed) _presses++;
        if (double.IsFinite(_f.HeartBpm))
        {
            _bpmHistory.Add((float)_f.HeartBpm);
            if (_bpmHistory.Count > 300) _bpmHistory.RemoveAt(0);
        }
        S.Controller.Led(_f.Breathing ? LedColor.Orange : _f.Moving ? LedColor.Red : LedColor.Blue);
        if (In.Pressed(Act.Cancel))
        {
            S.Cue(Cue.Cancel);
            S.Game.Scenes.Go(back());
        }
    }

    public override void Draw()
    {
        var g = G;
        g.Batch.Begin();
        g.Rect(new Rectangle(0, 0, Gfx.Width, Gfx.Height), new Color(12, 14, 26));
        g.TextCentered("センサーの確認", new Vector2(Gfx.Width / 2f, 70), 52, Palette.Gold, bold: true);
        var hub = S.Controller;
        bool sensor = S.Body.Mode == BodyMode.Sensor;
        g.TextCentered(sensor ? $"♥ 自作コントローラー（{hub.Link.PortName}）の値を使っています" : "キーボード・ゲームパッドで代わりの値を作っています",
            new Vector2(Gfx.Width / 2f, 140), 32, sensor ? Palette.Good : Palette.Dim);
        var r = new Rectangle(200, 200, 1520, 720);
        g.Window(r, 0.95f);
        float x = r.X + 60, y = r.Y + 50;
        void Row(string label, string value, Color c)
        {
            g.Text(label, new Vector2(x, y), 36, Palette.Dim);
            g.Text(value, new Vector2(x + 380, y), 36, c);
            y += 64;
        }
        Row("心拍", double.IsFinite(_f.HeartBpm) ? $"{_f.HeartBpm:0} bpm" : "--（指を置いてください）", Palette.Hp);
        Row("振った回数", $"{_shakes}", Color.White);
        Row("決定ボタン", $"{_presses} 回" + (_f.ConfirmHeld ? "（押している）" : ""), Color.White);
        Row("動き", _f.Moving ? "動いている" : "静止", _f.Moving ? Palette.Bad : Palette.Good);
        Row("息", _f.Breathing ? "吹きかけている！" : "--", _f.Breathing ? Palette.Gold : Palette.Dim);
        var sensors = hub.Sensors;
        Row("湿度 / 気温", double.IsFinite(sensors.Humidity) ? $"{sensors.Humidity:0}% / {sensors.Temperature:0}℃" : "--", Color.White);
        Row("天気", hub.LatestWeather is { } w ? Names.Of(w) : "--", Color.White);
        Row("最後の受信", sensors.Last is null ? "--" : $"v{sensors.Last.Version}　{sensors.SecondsSinceSample:0.0} 秒前", Palette.Dim);

        // 傾き
        var track = new Rectangle(r.X + 440, r.Bottom - 120, 900, 30);
        g.Text("傾き", new Vector2(x, track.Y - 4), 36, Palette.Dim);
        g.Rect(track, new Color(30, 30, 44));
        g.Outline(track, Color.White, 2);
        float mx = track.Center.X + ((float)_f.Tilt * track.Width / 2);
        g.Rect(mx - 6, track.Y - 14, 12, track.Height + 28, Palette.Cursor);

        // 心拍のグラフ
        var gr = new Rectangle(r.Right - 520, r.Y + 50, 460, 220);
        g.Rect(gr, new Color(20, 22, 36));
        g.Outline(gr, Palette.BorderInner, 2);
        for (int i = 1; i < _bpmHistory.Count; i++)
        {
            float x0 = gr.X + (gr.Width * (i - 1) / 300f), x1 = gr.X + (gr.Width * i / 300f);
            float y0 = gr.Bottom - ((_bpmHistory[i - 1] - 40) / 120f * gr.Height), y1 = gr.Bottom - ((_bpmHistory[i] - 40) / 120f * gr.Height);
            g.Line(new Vector2(x0, Math.Clamp(y0, gr.Y, gr.Bottom)), new Vector2(x1, Math.Clamp(y1, gr.Y, gr.Bottom)), Palette.Hp, 3);
        }
        g.Text($"安静時 {S.Settings.RestingBpm:0} / 高ぶり +{S.Settings.ExciteMargin}", new Vector2(gr.X, gr.Bottom + 10), 26, Palette.Dim);
        Hints.Draw(g, In, Hints.Cancel(In));
        g.Batch.End();
    }
}
