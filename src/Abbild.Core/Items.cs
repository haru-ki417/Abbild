namespace Abbild.Core;

public enum ItemKind
{
    HealHp,
    HealMp,
    Damage,
    BuffAttack,
    BuffDefense,
    Whetstone,
    Escape,
    Weaken,
    Stop,
}

/// <summary>接頭辞の特殊効果。</summary>
public enum PrefixEffect { None, Rotten, Cursed, Blessed, Random }

public sealed record ItemBase(string Id, string Name, ItemKind Kind, int Power, Element Element, int MinFloor, double Weight, string Description);

public sealed record ItemPrefix(string Id, string Label, double Multiplier, PrefixEffect Effect, Element Element, bool HealOnly, bool DamageOnly, int MinFloor, double Weight);

/// <summary>アイテム（ベース + 接頭辞）。保存は ID の組で行う。</summary>
public sealed record Item(string BaseId, string? PrefixId = null)
{
    public ItemBase Base => ItemCatalog.Base(BaseId);

    public ItemPrefix? Prefix => PrefixId is null ? null : ItemCatalog.Prefix(PrefixId);

    public string Name => (Prefix?.Label ?? "") + Base.Name;

    public ItemKind Kind => Base.Kind;

    public PrefixEffect Effect => Prefix?.Effect ?? PrefixEffect.None;

    public Element Element
    {
        get
        {
            var p = Prefix;
            if (p is not null && p.Element != Element.None && Base.Kind == ItemKind.Damage) return p.Element;
            if (p?.Effect == PrefixEffect.Blessed && Base.Kind == ItemKind.Damage) return Element.Holy;
            return Base.Element;
        }
    }

    /// <summary>効果量（ランダム系の接頭辞は使ったときに決まるので、ここでは目安）。</summary>
    public int Power => (int)Math.Round(Base.Power * (Prefix?.Multiplier ?? 1.0));

    public string Description
    {
        get
        {
            var b = Base;
            string main = b.Kind switch
            {
                ItemKind.HealHp when b.Power >= 9999 => "HP をすべて回復",
                ItemKind.HealHp => $"HP を {Power} 回復",
                ItemKind.HealMp => $"MP を {Power} 回復",
                ItemKind.Damage => $"敵に {Power} の{(Element == Element.None ? "" : Names.Of(Element) + "属性の")}ダメージ",
                _ => b.Description,
            };
            string extra = Effect switch
            {
                PrefixEffect.Rotten => b.Kind is ItemKind.HealHp or ItemKind.HealMp ? "（お腹を壊すかも…）" : "（威力が落ちている）",
                PrefixEffect.Cursed => "（使うと自分も傷つく）",
                PrefixEffect.Blessed => b.Kind == ItemKind.HealHp ? "（状態異常も治る）" : "",
                PrefixEffect.Random => "（効き目は使うまでわからない）",
                _ => "",
            };
            return main + extra;
        }
    }
}

public sealed class ItemStack(Item item, int count)
{
    public Item Item { get; } = item;

    public int Count { get; set; } = count;
}

public sealed class Inventory
{
    public const int MaxKinds = 24;
    public const int MaxStack = 99;

    private readonly List<ItemStack> _stacks = [];

    public IReadOnlyList<ItemStack> Stacks => _stacks;

    public int TotalCount => _stacks.Sum(s => s.Count);

    public bool IsEmpty => _stacks.Count == 0;

    /// <summary>追加する。いっぱいで入らなければ false。</summary>
    public bool Add(Item item, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(item);
        var s = _stacks.FirstOrDefault(x => x.Item == item);
        if (s is not null)
        {
            if (s.Count >= MaxStack) return false;
            s.Count = Math.Min(MaxStack, s.Count + count);
            return true;
        }
        if (_stacks.Count >= MaxKinds) return false;
        _stacks.Add(new ItemStack(item, Math.Min(MaxStack, count)));
        _stacks.Sort((a, b) => Order(a.Item).CompareTo(Order(b.Item)));
        return true;
    }

    public bool Remove(Item item)
    {
        var s = _stacks.FirstOrDefault(x => x.Item == item);
        if (s is null) return false;
        s.Count--;
        if (s.Count <= 0) _stacks.Remove(s);
        return true;
    }

    public int CountOf(Item item) => _stacks.FirstOrDefault(x => x.Item == item)?.Count ?? 0;

    public void Clear() => _stacks.Clear();

    private static int Order(Item i) => ((int)i.Kind * 1000) + Array.IndexOf(ItemCatalog.BaseIds, i.BaseId);
}

public static class ItemCatalog
{
    public static readonly IReadOnlyList<ItemBase> Bases =
    [
        new("potion", "ポーション", ItemKind.HealHp, 60, Element.None, 1, 14, ""),
        new("herb", "薬草", ItemKind.HealHp, 35, Element.None, 1, 10, ""),
        new("onigiri", "おにぎり", ItemKind.HealHp, 45, Element.None, 1, 8, ""),
        new("remedy", "特効薬", ItemKind.HealHp, 150, Element.None, 22, 9, ""),
        new("steak", "ステーキ", ItemKind.HealHp, 240, Element.None, 45, 7, ""),
        new("elixir", "エリクサー", ItemKind.HealHp, 9999, Element.None, 55, 1.5, ""),
        new("energy", "エナジードリンク", ItemKind.HealMp, 18, Element.None, 1, 7, ""),
        new("magicwater", "魔法の水", ItemKind.HealMp, 45, Element.None, 18, 5, ""),
        new("stone", "石ころ", ItemKind.Damage, 18, Element.None, 1, 6, ""),
        new("shuriken", "手裏剣", ItemKind.Damage, 40, Element.None, 1, 6, ""),
        new("molotov", "火炎ビン", ItemKind.Damage, 70, Element.Fire, 5, 5, ""),
        new("icecrystal", "氷の結晶", ItemKind.Damage, 70, Element.Ice, 5, 5, ""),
        new("thunderorb", "稲妻の玉", ItemKind.Damage, 70, Element.Thunder, 5, 5, ""),
        new("jackbox", "びっくり箱", ItemKind.Damage, 95, Element.Thunder, 12, 3, ""),
        new("holywater", "聖水", ItemKind.Damage, 120, Element.Holy, 15, 4, ""),
        new("grenade", "手榴弾", ItemKind.Damage, 150, Element.Fire, 25, 4, ""),
        new("dynamite", "ダイナマイト", ItemKind.Damage, 210, Element.Fire, 40, 3, ""),
        new("powerseed", "力の種", ItemKind.BuffAttack, 3, Element.None, 8, 1.6, "攻撃力がずっと 3 上がる"),
        new("guardseed", "守りの種", ItemKind.BuffDefense, 3, Element.None, 8, 1.6, "防御力がずっと 3 上がる"),
        new("whetstone", "研石", ItemKind.Whetstone, 0, Element.None, 3, 3, "リズムよく研ぐと攻撃力がずっと上がる"),
        new("smokeball", "煙玉", ItemKind.Escape, 0, Element.None, 3, 2.5, "戦いから逃げて次の階へ（ボスには効かない）"),
        new("sand", "砂", ItemKind.Weaken, 0, Element.None, 1, 3, "敵の攻撃力を 2 割下げる"),
        new("web", "クモの巣", ItemKind.Stop, 0, Element.None, 6, 2.5, "敵の動きを 1 回止める"),
    ];

    public static readonly IReadOnlyList<ItemPrefix> Prefixes =
    [
        new("poor", "粗悪な", 0.5, PrefixEffect.None, Element.None, false, false, 1, 8),
        new("old", "古い", 0.8, PrefixEffect.None, Element.None, false, false, 1, 10),
        new("plain", "普通の", 1.0, PrefixEffect.None, Element.None, false, false, 1, 14),
        new("good", "良質な", 1.2, PrefixEffect.None, Element.None, false, false, 1, 10),
        new("fine", "高級な", 1.5, PrefixEffect.None, Element.None, false, false, 10, 6),
        new("best", "最高級の", 2.0, PrefixEffect.None, Element.None, false, false, 25, 3),
        new("royal", "王室の", 3.0, PrefixEffect.None, Element.None, false, false, 40, 1.4),
        new("legend", "伝説の", 5.0, PrefixEffect.None, Element.None, false, false, 55, 0.6),
        new("divine", "神々の", 10.0, PrefixEffect.None, Element.None, false, false, 70, 0.15),
        new("dev", "開発者用の", 100.0, PrefixEffect.None, Element.None, false, false, 1, 0.02),
        new("half", "半額の", 1.0, PrefixEffect.None, Element.None, false, false, 1, 3),
        new("konbini", "コンビニの", 0.9, PrefixEffect.None, Element.None, false, false, 1, 4),
        new("rotten", "腐った", 0.5, PrefixEffect.Rotten, Element.None, false, false, 1, 3),
        new("shady", "怪しい", 1.5, PrefixEffect.Random, Element.None, false, false, 1, 2.5),
        new("glowing", "光る", 1.2, PrefixEffect.None, Element.None, false, false, 1, 3),
        new("heavy", "重い", 1.5, PrefixEffect.None, Element.None, false, false, 5, 2.5),
        new("cursed", "呪われた", 3.0, PrefixEffect.Cursed, Element.None, false, false, 10, 2),
        new("blessed", "祝福された", 2.0, PrefixEffect.Blessed, Element.None, false, false, 10, 2),
        new("burning", "燃える", 1.2, PrefixEffect.None, Element.Fire, false, true, 1, 3),
        new("frozen", "凍った", 1.2, PrefixEffect.None, Element.Ice, false, true, 1, 3),
        new("charged", "帯電した", 1.2, PrefixEffect.None, Element.Thunder, false, true, 1, 3),
        new("toxic", "猛毒の", 1.1, PrefixEffect.None, Element.Poison, false, true, 1, 3),
        new("moms", "お母さんの", 2.0, PrefixEffect.Blessed, Element.None, true, false, 1, 1.5),
        new("expired", "賞味期限切れの", 0.1, PrefixEffect.None, Element.None, true, false, 1, 2),
        new("golden", "金ピカの", 1.0, PrefixEffect.None, Element.None, false, false, 1, 2),
        new("gift", "もらい物の", 1.0, PrefixEffect.None, Element.None, false, false, 1, 2),
        new("recycled", "リサイクル", 0.8, PrefixEffect.None, Element.None, false, false, 1, 2),
        new("mystery", "謎の", 2.0, PrefixEffect.Random, Element.None, false, false, 1, 2),
    ];

    internal static readonly string[] BaseIds = Bases.Select(b => b.Id).ToArray();

    private static readonly Dictionary<string, ItemBase> BaseMap = Bases.ToDictionary(b => b.Id);
    private static readonly Dictionary<string, ItemPrefix> PrefixMap = Prefixes.ToDictionary(p => p.Id);

    public static ItemBase Base(string id) =>
        BaseMap.TryGetValue(id, out var b) ? b : throw new KeyNotFoundException($"不明なアイテム: {id}");

    public static ItemPrefix Prefix(string id) =>
        PrefixMap.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"不明な接頭辞: {id}");

    public static bool IsKnown(string baseId, string? prefixId) =>
        BaseMap.ContainsKey(baseId) && (prefixId is null || PrefixMap.ContainsKey(prefixId));

    /// <summary>接頭辞がつくのは回復・攻撃アイテムだけ。</summary>
    public static bool TakesPrefix(ItemBase b) => b.Kind is ItemKind.HealHp or ItemKind.HealMp or ItemKind.Damage;

    public static bool PrefixFits(ItemPrefix p, ItemBase b)
    {
        if (!TakesPrefix(b)) return false;
        bool heal = b.Kind is ItemKind.HealHp or ItemKind.HealMp;
        if (p.HealOnly && !heal) return false;
        if (p.DamageOnly && b.Kind != ItemKind.Damage) return false;
        if (b.Id == "elixir" && p.Multiplier != 1.0 && p.Effect == PrefixEffect.None) return false;
        return true;
    }
}

public static class ItemGenerator
{
    /// <summary>
    /// floor 階で見つかるアイテムを 1 つ作る。luck が高いほど良い接頭辞が出やすい（0 = 普通）。
    /// </summary>
    public static Item Generate(GameRandom rng, int floor, double luck = 0)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var bases = ItemCatalog.Bases.Where(b => b.MinFloor <= floor).ToList();
        var b = rng.PickWeighted(bases, x => x.Weight);
        if (!ItemCatalog.TakesPrefix(b) || rng.Chance(0.35)) return new Item(b.Id);
        var prefixes = ItemCatalog.Prefixes.Where(p => p.MinFloor <= floor && ItemCatalog.PrefixFits(p, b)).ToList();
        if (prefixes.Count == 0) return new Item(b.Id);
        var p = rng.PickWeighted(prefixes, x => x.Weight * (x.Multiplier > 1.4 ? 1 + luck : 1));
        return new Item(b.Id, p.Id);
    }
}
