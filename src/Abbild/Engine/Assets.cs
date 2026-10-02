using System.Text.Json;
using Abbild.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Abbild.Engine;

/// <summary>敵の見た目（一枚絵、またはコマ帯のアニメーション）。</summary>
public sealed class EnemyArt
{
    public required Texture2D Idle { get; init; }
    public Texture2D? Attack { get; init; }
    public int IdleFrames { get; init; } = 1;
    public int AttackFrames { get; init; } = 1;

    /// <summary>ドット絵（点で拡大する）か。</summary>
    public bool Pixel { get; init; }

    /// <summary>画面での基本の大きさ（倍率）。</summary>
    public float BaseScale { get; init; } = 1f;

    public Color Tint { get; init; } = Color.White;

    public int FrameWidth(bool attack) => (attack && Attack is not null ? Attack.Width / AttackFrames : Idle.Width / IdleFrames);

    public int FrameHeight(bool attack) => attack && Attack is not null ? Attack.Height : Idle.Height;
}

/// <summary>画像を必要になったときに読み込んで、使い回す。</summary>
public sealed class Assets(GraphicsDevice device, string contentRoot)
{
    private readonly Dictionary<string, Texture2D> _textures = [];
    private Dictionary<string, int>? _frames;

    public string Root { get; } = contentRoot;

    public Texture2D Texture(string relative)
    {
        if (_textures.TryGetValue(relative, out var t)) return t;
        string path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        using var fs = File.OpenRead(path);
        t = Texture2D.FromStream(device, fs, DefaultColorProcessors.PremultiplyAlpha);
        _textures[relative] = t;
        return t;
    }

    public Texture2D Background(string id) => Texture($"bg/{id}.jpg");

    public Texture2D Hero(Gender g) => Texture(g == Gender.Female ? "hero/hero_female.png" : "hero/hero_male.png");

    private int FramesOf(string name)
    {
        _frames ??= JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(Root, "rmt", "frames.json"))) ?? [];
        return _frames.TryGetValue(name, out int n) ? n : 1;
    }

    public EnemyArt Enemy(SpriteRef s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var tint = new Color((byte)((s.Tint >> 16) & 0xFF), (byte)((s.Tint >> 8) & 0xFF), (byte)(s.Tint & 0xFF));
        if (!s.Animated)
        {
            var t = Texture($"enemies/{s.Id}.png");
            // 一枚絵は長辺がおよそ 420px。画面ではおよそ 1.15 倍で出す
            return new EnemyArt { Idle = t, BaseScale = 1.15f * s.Scale, Tint = tint };
        }
        string idle = $"{s.Id}_idle";
        string atk = $"{s.Id}_attack";
        var idleTex = Texture($"rmt/{idle}.png");
        Texture2D? atkTex = File.Exists(Path.Combine(Root, "rmt", atk + ".png")) ? Texture($"rmt/{atk}.png") : null;
        int frameH = idleTex.Height;
        // 64px 前後のドット絵は 5 倍、大きめのもの（ドラゴンなど）は 3 倍
        float scale = (frameH <= 90 ? 5f : 3.2f) * s.Scale;
        return new EnemyArt
        {
            Idle = idleTex,
            Attack = atkTex,
            IdleFrames = FramesOf(idle),
            AttackFrames = atkTex is null ? 1 : FramesOf(atk),
            Pixel = true,
            BaseScale = scale,
            Tint = tint,
        };
    }

    /// <summary>起動時に、よく使う画像を先に読み込んでおく（最初の戦闘で引っかからないように）。</summary>
    public void Warm()
    {
        foreach (var b in Biome.All) Background(b.Background);
        Background("dungeon");
        Background("corridor");
        Hero(Gender.Male);
        Hero(Gender.Female);
    }
}
