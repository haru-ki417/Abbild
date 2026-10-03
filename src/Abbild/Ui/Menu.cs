using Abbild.Core;
using Abbild.Engine;
using Microsoft.Xna.Framework;

namespace Abbild.Ui;

public sealed record MenuItem(string Label, bool Enabled = true, string Right = "", string Description = "", object? Tag = null, string? Icon = null);

/// <summary>縦に並んだ選択肢。キー・パッド・マウスで選べる。</summary>
public sealed class Menu
{
    private List<MenuItem> _items = [];
    private int _scroll;

    public int Index { get; set; }
    public float RowHeight { get; set; } = 64;
    public float FontSize { get; set; } = 40;
    public int VisibleRows { get; set; } = 10;
    public bool Wrap { get; set; } = true;

    /// <summary>出てからの時間（秒）。項目が上から順にすべり込む。大きな値なら全部出ている。</summary>
    public float Reveal { get; set; } = 99;

    // 選択の帯は、行から行へなめらかに動く
    private float _bandRow = -1;
    private float _lastTime;

    /// <summary>左右キーで値を変える項目（設定画面用）。</summary>
    public Action<int, int>? OnAdjust { get; set; }

    public IReadOnlyList<MenuItem> Items => _items;

    public MenuItem? Selected => Index >= 0 && Index < _items.Count ? _items[Index] : null;

    public void SetItems(IEnumerable<MenuItem> items, bool keepIndex = true)
    {
        _items = items.ToList();
        if (!keepIndex) Index = 0;
        Index = Math.Clamp(Index, 0, Math.Max(0, _items.Count - 1));
        if (_items.Count > 0 && !_items[Index].Enabled)
        {
            int i = _items.FindIndex(x => x.Enabled);
            if (i >= 0 && !keepIndex) Index = i;
        }
    }

    /// <summary>操作を受けて、決定されたら選ばれた番号を返す（なければ -1）。</summary>
    public int Update(Services s, Rectangle area)
    {
        var input = s.Input;
        if (_items.Count == 0) return -1;
        int before = Index;
        if (input.Repeat(Act.Up)) Move(-1);
        if (input.Repeat(Act.Down)) Move(1);
        if (OnAdjust is not null)
        {
            bool enabled = _items[Index].Enabled;
            if (input.Repeat(Act.Left) && enabled) { OnAdjust(Index, -1); s.Cue(Cue.Cursor); }
            if (input.Repeat(Act.Right) && enabled) { OnAdjust(Index, 1); s.Cue(Cue.Cursor); }
        }
        if (input.Wheel != 0) Move(-input.Wheel);

        int hovered = -1;
        for (int row = 0; row < Math.Min(VisibleRows, _items.Count - _scroll); row++)
        {
            var r = RowRect(area, row);
            if (r.Contains(input.Mouse)) hovered = row + _scroll;
        }
        if (hovered >= 0 && input.MouseMoved) Index = hovered;
        if (Index != before) s.Cue(Cue.Cursor);
        EnsureVisible();

        bool click = input.MouseClicked && hovered >= 0 && hovered == Index;
        if (input.Pressed(Act.Confirm) || click)
        {
            if (_items[Index].Enabled)
            {
                s.Cue(Cue.Confirm);
                return Index;
            }
            s.Cue(Cue.Buzzer);
        }
        return -1;
    }

    private void Move(int d)
    {
        if (_items.Count == 0) return;
        int n = _items.Count;
        int i = Index;
        for (int k = 0; k < n; k++)
        {
            i += d;
            if (Wrap) i = ((i % n) + n) % n;
            else i = Math.Clamp(i, 0, n - 1);
            if (_items[i].Enabled || OnAdjust is not null) break;
        }
        Index = i;
    }

    private void EnsureVisible()
    {
        if (Index < _scroll) _scroll = Index;
        if (Index >= _scroll + VisibleRows) _scroll = Index - VisibleRows + 1;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _items.Count - VisibleRows));
    }

    private Rectangle RowRect(Rectangle area, int row) =>
        new(area.X, (int)(area.Y + (row * RowHeight)), area.Width, (int)RowHeight);

    public void Draw(Gfx g, Rectangle area, float time, bool active = true)
    {
        float dt = Math.Clamp(time - _lastTime, 0, 0.1f);
        _lastTime = time;
        float target = Index - _scroll;
        if (_bandRow < 0 || MathF.Abs(_bandRow - target) > VisibleRows) _bandRow = target;
        _bandRow += (target - _bandRow) * Math.Min(1, dt * 20);

        int shown = Math.Min(VisibleRows, _items.Count - _scroll);
        // 選択の帯（文字より先に、なめらかな位置に描く）
        if (active && Index >= _scroll && Index < _scroll + shown && RowK(Index - _scroll) > 0.5f)
        {
            float pulse = 0.85f + (0.15f * MathF.Sin(time * 6));
            var r = new Rectangle(area.X, (int)(area.Y + (_bandRow * RowHeight)), area.Width, (int)RowHeight);
            var band = new Rectangle(r.X, r.Y + 4, r.Width, r.Height - 8);
            const int steps = 12;
            for (int k = 0; k < steps; k++)
            {
                int x0 = band.X + (band.Width * k / steps);
                int x1 = band.X + (band.Width * (k + 1) / steps);
                g.Rect(new Rectangle(x0, band.Y, x1 - x0, band.Height), Palette.Gold * (0.32f * (1 - (k / (float)steps)) * pulse));
            }
            g.Rect(new Rectangle(band.X, band.Y, 4, band.Height), Palette.Gold * pulse);
            DrawCursor(g, new Vector2(r.X + 20, r.Center.Y), time);
        }
        for (int row = 0; row < shown; row++)
        {
            int i = row + _scroll;
            var it = _items[i];
            var r = RowRect(area, row);
            float rk = RowK(row);
            if (rk <= 0) continue;
            r.Offset((int)((1 - Ease.OutCubic(rk)) * 40), 0);
            bool sel = i == Index && active;
            var color = (!it.Enabled ? Palette.Disabled : sel ? Color.White : Palette.Text * 0.92f) * rk;
            float ty = r.Y + ((r.Height - FontSize) / 2) - 2;
            float lx = r.X + 44;
            if (it.Icon is not null)
            {
                float isz = Math.Min(40, r.Height - 14);
                Icons.Draw(g, it.Icon, new Vector2(lx, r.Center.Y - (isz / 2)), isz, (it.Enabled ? 1f : 0.4f) * rk);
                lx += isz + 12;
            }
            g.Text(it.Label, new Vector2(lx, ty), FontSize, color);
            if (!string.IsNullOrEmpty(it.Right))
            {
                g.TextRight(it.Right, new Vector2(r.Right - 16, ty), FontSize, (it.Enabled ? (sel ? Palette.Gold : Palette.Dim) : Palette.Disabled) * rk);
            }
        }
        if (_scroll > 0) g.TextCentered("▲", new Vector2(area.Center.X, area.Y - 14), 24, Palette.Dim);
        if (_scroll + VisibleRows < _items.Count) g.TextCentered("▼", new Vector2(area.Center.X, area.Y + (VisibleRows * RowHeight) + 10), 24, Palette.Dim);
    }

    private float RowK(int row) => Math.Clamp((Reveal - (row * 0.05f)) / 0.22f, 0, 1);

    public static void DrawCursor(Gfx g, Vector2 at, float time)
    {
        float dx = MathF.Sin(time * 8) * 3;
        var p = at + new Vector2(dx, 0);
        // 右向きの三角
        for (int i = 0; i < 12; i++)
        {
            g.Rect(p.X - 8 + i, p.Y - 12 + i, 1, 24 - (2 * i), Palette.Gold);
        }
    }
}

/// <summary>1 文字ずつ出す文章。</summary>
public sealed class Typewriter
{
    private string _text = "";
    private float _shown;

    public string Text => _text;

    public bool Done => _shown >= _text.Length;

    public string Visible => _text[..Math.Min(_text.Length, (int)_shown)];

    /// <summary>出ている文字数（小数。最後の文字をふわっと出すのに使う）。</summary>
    public float Shown => _shown;

    public void Set(string text)
    {
        _text = text;
        _shown = 0;
    }

    public void Update(float dt, float cps) => _shown = Math.Min(_text.Length, _shown + (dt * cps));

    public void Finish() => _shown = _text.Length;
}

/// <summary>画面下の操作ガイド。</summary>
public static class Hints
{
    public static void Draw(Gfx g, Input input, params (string Key, string What)[] items) => DrawAt(g, input, Gfx.Height - 52, items);

    public static void DrawAt(Gfx g, Input input, float y, params (string Key, string What)[] items)
    {
        float x = Gfx.Width - 40;
        for (int i = items.Length - 1; i >= 0; i--)
        {
            var (key, what) = items[i];
            string label = what;
            var mw = g.Measure(label, 26).X;
            x -= mw;
            g.Text(label, new Vector2(x, y), 26, Palette.Dim);
            var kw = Math.Max(36, g.Measure(key, 24).X + 18);
            x -= kw + 10;
            var kr = new Rectangle((int)x, (int)y - 2, (int)kw, 36);
            g.Rect(kr, new Color(40, 44, 64) * 0.95f);
            g.Outline(kr, Palette.Dim, 2);
            g.TextCentered(key, new Vector2(kr.Center.X, kr.Center.Y), 24, Color.White);
            x -= 32;
        }
        _ = input;
    }

    public static (string, string) Confirm(Input i, string what = "決定") => (i.ConfirmLabel, what);

    public static (string, string) Cancel(Input i, string what = "もどる") => (i.CancelLabel, what);
}
