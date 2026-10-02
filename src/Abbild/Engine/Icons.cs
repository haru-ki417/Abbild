using Abbild.Core;
using Microsoft.Xna.Framework;

namespace Abbild.Engine;

/// <summary>10×10 のドットで描く小さなアイコン（素材を増やさずに、見た目を引き締める）。</summary>
public static class Icons
{
    private static readonly Dictionary<char, Color> Ink = new()
    {
        ['k'] = new Color(16, 12, 20),
        ['w'] = new Color(250, 250, 245),
        ['y'] = new Color(255, 214, 72),
        ['o'] = new Color(255, 140, 40),
        ['r'] = new Color(226, 52, 52),
        ['R'] = new Color(140, 20, 30),
        ['b'] = new Color(70, 120, 240),
        ['B'] = new Color(30, 50, 140),
        ['c'] = new Color(120, 230, 255),
        ['g'] = new Color(90, 220, 110),
        ['G'] = new Color(30, 120, 60),
        ['p'] = new Color(180, 90, 240),
        ['s'] = new Color(200, 210, 225),
        ['S'] = new Color(110, 120, 140),
        ['n'] = new Color(150, 96, 50),
        ['N'] = new Color(90, 54, 26),
    };

    private static readonly Dictionary<string, string[]> Shapes = new()
    {
        ["sword"] =
        [
            "........kk",
            ".......kwk",
            "......kwsk",
            ".....kwsk.",
            ".k..kwsk..",
            ".kkkwsk...",
            "..kysk....",
            ".kkyykk...",
            "kNk..k....",
            "kk........",
        ],
        ["shield"] =
        [
            ".kkkkkkkk.",
            "kbbbbbbbbk",
            "kbsyyyysbk",
            "kbsyBByybk",
            "kbsyBByybk",
            "kbbyyyyybk",
            ".kbbyyybk.",
            "..kbbbbk..",
            "...kbbk...",
            "....kk....",
        ],
        ["boot"] =
        [
            "..kkkkk...",
            "..knnnk...",
            "..knnnk...",
            "..knnnk...",
            "..knnnk...",
            "..knnnkkk.",
            "..knnnnnnk",
            ".kNNNNNNNk",
            ".kkkkkkkkk",
            "..........",
        ],
        ["heart"] =
        [
            "..........",
            ".kkk..kkk.",
            "kwrrkkrrrk",
            "krrrrrrrrk",
            "krrrrrrrrk",
            ".krrrrrrk.",
            "..kRrrRk..",
            "...kRRk...",
            "....kk....",
            "..........",
        ],
        ["drop"] =
        [
            "....kk....",
            "...kbbk...",
            "...kcbk...",
            "..kcbbbk..",
            ".kcbbbbbk.",
            ".kcbbbbbk.",
            ".kbbbbbBk.",
            "..kbbbBk..",
            "...kkkk...",
            "..........",
        ],
        ["star"] =
        [
            "....kk....",
            "...kyyk...",
            "kkkkyykkkk",
            "kyyyyyyyyk",
            ".kyywyyyk.",
            "..kyyyyk..",
            ".kyykkyyk.",
            ".kyk..kyk.",
            ".kk....kk.",
            "..........",
        ],
        ["flame"] =
        [
            "....k.....",
            "...kok....",
            "...kook...",
            "..koyok.k.",
            ".koyyookok",
            ".koywyyook",
            "koyywwyyok",
            "koyywwyyok",
            ".kooyyook.",
            "..kkkkkk..",
        ],
        ["snow"] =
        [
            "....kk....",
            ".k.kcck.k.",
            "kckkwwkkck",
            ".kcwwwwck.",
            "kkwwccwwkk",
            "kkwwccwwkk",
            ".kcwwwwck.",
            "kckkwwkkck",
            ".k.kcck.k.",
            "....kk....",
        ],
        ["bolt"] =
        [
            ".....kkkk.",
            "....kyyyk.",
            "...kyyyk..",
            "..kyyyk...",
            ".kyyyykkk.",
            ".kkkkyyyk.",
            "....kyyk..",
            "...kyyk...",
            "..kyyk....",
            "..kkk.....",
        ],
        ["poison"] =
        [
            "..kkkkkk..",
            ".kppppppk.",
            "kppwpppwpk",
            "kpwkpppkpk",
            "kppppppppk",
            ".kppkkppk.",
            "..kpppppk.",
            "..kpkpkpk.",
            "...kkkkk..",
            "..........",
        ],
        ["holy"] =
        [
            "....kk....",
            "....kyk...",
            "..k.kyk.k.",
            "...kwwwk..",
            "kkkwyyywkk",
            "kyywyyywyk",
            "kkkwyyywkk",
            "...kwwwk..",
            "..k.kyk.k.",
            "....kk....",
        ],
        ["potion"] =
        [
            "...kkkk...",
            "...knnk...",
            "...kssk...",
            "..kssssk..",
            ".kswrrrsk.",
            "kswrrrrrsk",
            "ksrrrrrrsk",
            "ksrrrrrRsk",
            ".ksRRRRsk.",
            "..kkkkkk..",
        ],
        ["ether"] =
        [
            "...kkkk...",
            "...knnk...",
            "...kssk...",
            "..kssssk..",
            ".kswbbbsk.",
            "kswbbbbbsk",
            "ksbbbbbbsk",
            "ksbbbbbBsk",
            ".ksBBBBsk.",
            "..kkkkkk..",
        ],
        ["bomb"] =
        [
            "......kok.",
            ".....k.k..",
            "....kk....",
            "..kkkkkk..",
            ".kSSSSSSk.",
            "kSwSSSSSSk",
            "kSSSSSSSSk",
            "kSSSSSSSSk",
            ".kSSSSSSk.",
            "..kkkkkk..",
        ],
        ["seed"] =
        [
            ".....kk...",
            "....kggk..",
            "...kgGk...",
            "..kkkkkk..",
            ".knnnnnnk.",
            "knnwnnnnnk",
            "knnnnnnnnk",
            ".knnnnnNk.",
            "..kNNNNk..",
            "...kkkk...",
        ],
        ["tool"] =
        [
            "..........",
            ".kkk......",
            "kssskk....",
            "ksssssk...",
            ".kksssskk.",
            "...kssssnk",
            "....kksnnk",
            "......knNk",
            ".......kNk",
            "........kk",
        ],
        ["eye"] =
        [
            "..........",
            "...kkkk...",
            ".kkwwwwkk.",
            "kwwwbbwwwk",
            "kwwbkkbwwk",
            "kwwbkkbwwk",
            ".kkwbbwkk.",
            "...kkkk...",
            "..........",
            "..........",
        ],
    };

    /// <summary>アイコンを描く。size はおよそのピクセル幅。</summary>
    public static void Draw(Gfx g, string name, Vector2 topLeft, float size = 32, float alpha = 1f)
    {
        if (!Shapes.TryGetValue(name, out var rows)) return;
        float px = MathF.Max(1, MathF.Floor(size / 10));
        for (int y = 0; y < rows.Length; y++)
        {
            var row = rows[y];
            for (int x = 0; x < row.Length; x++)
            {
                if (Ink.TryGetValue(row[x], out var c)) g.Rect(topLeft.X + (x * px), topLeft.Y + (y * px), px, px, c * alpha);
            }
        }
    }

    public static string For(Element e) => e switch
    {
        Element.Fire => "flame",
        Element.Ice => "snow",
        Element.Thunder => "bolt",
        Element.Poison => "poison",
        Element.Holy => "holy",
        _ => "sword",
    };

    public static string For(SkillDef s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Id switch
        {
            SkillId.Heal or SkillId.Meditation => "heart",
            SkillId.Charge or SkillId.Berserk => "star",
            SkillId.Alchemy => "potion",
            SkillId.Fishing => "drop",
            SkillId.Negotiate => "eye",
            SkillId.Dowsing => "eye",
            SkillId.Breath => "poison",
            SkillId.FinalStrike => "sword",
            _ => For(s.Element),
        };
    }

    public static string For(Item i)
    {
        ArgumentNullException.ThrowIfNull(i);
        return i.Kind switch
        {
            ItemKind.HealHp => "potion",
            ItemKind.HealMp => "ether",
            ItemKind.Damage => i.Element == Element.None ? "bomb" : For(i.Element),
            ItemKind.BuffAttack or ItemKind.BuffDefense => "seed",
            ItemKind.Whetstone => "tool",
            _ => "bomb",
        };
    }
}
