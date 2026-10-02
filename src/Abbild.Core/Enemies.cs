namespace Abbild.Core;

public enum EnemySkill
{
    None,
    Fire,     // 炎（1.5 倍）
    Ice,      // 氷（1.2 倍 + 凍結）
    Thunder,  // 雷（麻痺）
    Heal,     // 自分を回復
    Smash,    // 痛恨（2 倍、たまに外れる）
    Poison,   // MP を削る毒の息
    Curse,    // 呪い（1.5 倍）
    Drain,    // 吸血
    Wind,     // 2 回攻撃
    Glare,    // 凝視（心を静めないと大ダメージ）
    Blind,    // 目潰し（心眼で見切る）
    Iai,      // 居合い（反射神経でカウンター）
}

/// <summary>敵の動き方（WinForms 版の AnimType を引き継ぐ）。</summary>
public enum Motion { Stop, Breathe, Squish, Vibrate, Float, Flicker, Heavy, Panic }

public enum BiomeEffect { None, Burn, Slow, Miss, Poison, Sanctuary, Dark, Blessing, Curse, Void }

/// <summary>敵の見た目。Animated のときは rmt/{Id}_idle.png などのコマ帯を使う。</summary>
public sealed record SpriteRef(string Id, bool Animated = false, uint Tint = 0xFFFFFF, float Scale = 1f);

public sealed record Biome(
    int Index,
    string Name,
    string Background,
    LedColor Led,
    BiomeEffect Effect,
    Element WeakTo,
    Element Resists,
    string EffectText)
{
    public static readonly IReadOnlyList<Biome> All =
    [
        new(0, "平原", "plain", LedColor.Green, BiomeEffect.None, Element.None, Element.None, "穏やかな地下の森"),
        new(1, "火山", "volcano", LedColor.Red, BiomeEffect.Burn, Element.Ice, Element.Fire, "熱で毎ターン少しずつ HP が減る"),
        new(2, "氷河", "ice", LedColor.Blue, BiomeEffect.Slow, Element.Fire, Element.Ice, "寒さで体が重く、攻撃をかわしにくい"),
        new(3, "砂漠", "desert", LedColor.Yellow, BiomeEffect.Miss, Element.Ice, Element.Thunder, "砂ぼこりで攻撃が外れやすい"),
        new(4, "毒の沼", "poison", LedColor.Purple, BiomeEffect.Poison, Element.Fire, Element.Poison, "毒気で毎ターン少しずつ HP が減る"),
        new(5, "神殿", "temple", LedColor.White, BiomeEffect.Sanctuary, Element.Thunder, Element.Holy, "聖なる光で毎ターン少しずつ回復"),
        new(6, "暗黒の洞窟", "cave", LedColor.Orange, BiomeEffect.Dark, Element.Holy, Element.Poison, "暗くて敵の正体が見えにくい"),
        new(7, "神界", "divine", LedColor.White, BiomeEffect.Blessing, Element.Poison, Element.Holy, "祝福で経験値が 2 割多い"),
        new(8, "魔界", "demon", LedColor.Magenta, BiomeEffect.Curse, Element.Holy, Element.Fire, "呪いでスキルの MP が 2 多くかかる"),
        new(9, "終焉", "final", LedColor.Red, BiomeEffect.Void, Element.Holy, Element.None, "すべてが混ざり合う、迷宮の最深部"),
    ];

    /// <summary>1〜100 階のバイオーム（1〜10 階が平原、91〜100 階が終焉）。</summary>
    public static Biome ForFloor(int floor) => All[Math.Clamp((floor - 1) / 10, 0, All.Count - 1)];
}

public sealed record EnemyDef(
    string Id,
    string Name,
    SpriteRef Sprite,
    Motion Motion,
    float MotionSpeed,
    int MotionIntensity,
    double HpMul,
    double AtkMul,
    EnemySkill[] Skills,
    double DefMul = 1.0,
    double SpdMul = 1.0,
    double ExpMul = 1.0,
    double FleeChance = 0,
    string? Quote = null);

/// <summary>戦っている敵 1 体。</summary>
public sealed class Enemy
{
    public required EnemyDef Def { get; init; }
    public required string Name { get; init; }
    public required int MaxHp { get; init; }
    public int Hp { get; set; }
    public int Attack { get; set; }
    public required int Defense { get; init; }
    public required int Speed { get; init; }
    public required int ExpReward { get; init; }
    public required bool IsBoss { get; init; }
    public required bool IsFinalBoss { get; init; }
    public required double SkillRate { get; init; }

    /// <summary>次の自分の番を休む（クモの巣）。</summary>
    public bool Stopped { get; set; }

    public bool IsDead => Hp <= 0;

    public int TakeDamage(int amount)
    {
        int before = Hp;
        Hp = Math.Max(0, Hp - Math.Max(0, amount));
        return before - Hp;
    }

    public int Heal(int amount)
    {
        int before = Hp;
        Hp = Math.Min(MaxHp, Hp + Math.Max(0, amount));
        return Hp - before;
    }
}

public static class EnemyCatalog
{
    private static SpriteRef Img(string id, float scale = 1f) => new(id, false, 0xFFFFFF, scale);
    private static SpriteRef Rmt(string id, uint tint = 0xFFFFFF, float scale = 1f) => new(id, true, tint, scale);
    private static EnemySkill[] S(params EnemySkill[] s) => s;

    private static readonly EnemySkill[] NoSkill = [];

    /// <summary>バイオームごとの普通の敵。</summary>
    public static readonly IReadOnlyList<IReadOnlyList<EnemyDef>> Regulars =
    [
        // 0 平原
        [
            new("slime", "スライム", Rmt("slime_a"), Motion.Squish, 1.0f, 5, 0.7, 0.7, NoSkill),
            new("wild_dog", "野犬", Img("wild_dog"), Motion.Breathe, 2.0f, 3, 0.95, 1.15, S(EnemySkill.Smash), SpdMul: 1.3),
            new("kobold", "コボルト", Rmt("kobold_a"), Motion.Vibrate, 3.0f, 2, 0.8, 0.9, NoSkill),
            new("trainee", "見習い兵士", Rmt("soldier_a"), Motion.Stop, 0.5f, 1, 1.0, 1.0, S(EnemySkill.Iai)),
            new("goblin", "ゴブリン", Img("goblin"), Motion.Breathe, 1.2f, 3, 0.9, 1.0, NoSkill),
            new("chameleon", "カメレオン", Img("chameleon"), Motion.Stop, 0.1f, 1, 0.9, 0.95, S(EnemySkill.Blind)),
            new("bat", "コウモリ", Img("bat"), Motion.Float, 1.5f, 15, 0.6, 0.8, S(EnemySkill.Blind), SpdMul: 1.4),
        ],
        // 1 火山
        [
            new("red_slime", "レッドスライム", Rmt("slime_b"), Motion.Squish, 1.5f, 8, 0.8, 0.9, S(EnemySkill.Fire)),
            new("magma_slime", "マグマスライム", Rmt("slime_d1", 0xFF5A28), Motion.Flicker, 2.0f, 6, 1.0, 1.0, S(EnemySkill.Fire)),
            new("oilm", "オイルム", Rmt("slime_d1", 0x8C8C3C), Motion.Squish, 0.5f, 10, 0.75, 0.85, S(EnemySkill.Fire)),
            new("fire_bird", "ファイアバード", Rmt("bird_b"), Motion.Float, 0.8f, 10, 0.95, 1.1, S(EnemySkill.Fire), SpdMul: 1.3),
            new("fire_wizard", "ファイアウィザード", Rmt("wizard_a"), Motion.Float, 0.5f, 5, 0.8, 1.25, S(EnemySkill.Fire)),
            new("fire_wisp", "ファイアウィスプ", Img("fire_wisp", 0.8f), Motion.Flicker, 3.0f, 10, 0.75, 1.0, S(EnemySkill.Blind)),
            new("drone", "自爆ドローン", Img("kamikaze_drone"), Motion.Vibrate, 10.0f, 5, 0.5, 1.7, S(EnemySkill.Fire), SpdMul: 1.5),
            new("turret", "タレット", Img("turret"), Motion.Stop, 0f, 0, 1.2, 1.1, S(EnemySkill.Glare), DefMul: 1.5),
            new("berserker", "狂戦士", Img("berserker"), Motion.Panic, 2.0f, 4, 1.1, 1.3, S(EnemySkill.Smash)),
        ],
        // 2 氷河
        [
            new("ice_slime", "アイススライム", Rmt("slime_d1", 0x78E6FF), Motion.Stop, 0f, 0, 0.9, 0.9, S(EnemySkill.Ice)),
            new("ice_falcon", "アイスファルコン", Rmt("bird_d"), Motion.Float, 0.8f, 10, 0.95, 1.1, S(EnemySkill.Iai), SpdMul: 1.4),
            new("ice_wizard", "アイスウィザード", Rmt("wizard_b"), Motion.Float, 0.5f, 5, 0.8, 1.2, S(EnemySkill.Ice)),
            new("ice_crystal", "アイスクリスタル", Img("ice_crystal"), Motion.Stop, 0f, 0, 1.0, 0.95, S(EnemySkill.Ice), DefMul: 1.6),
            new("fishman", "半魚人", Img("fishman"), Motion.Breathe, 1.0f, 5, 0.95, 1.0, S(EnemySkill.Ice)),
            new("killer_crab", "キラークラブ", Img("killer_crab"), Motion.Vibrate, 0.5f, 2, 1.1, 1.05, S(EnemySkill.Smash), DefMul: 1.5),
            new("sea_serpent", "シーサーペント", Img("sea_serpent"), Motion.Float, 0.5f, 15, 1.3, 1.1, S(EnemySkill.Glare)),
            new("shark", "サメ", Img("shark"), Motion.Breathe, 2.0f, 3, 1.15, 1.25, S(EnemySkill.Smash)),
            new("anglerfish", "深海魚", Img("deep_sea_fish"), Motion.Float, 0.7f, 8, 0.85, 1.0, S(EnemySkill.Blind)),
            new("jellyfish", "電気クラゲ", Img("electric_jellyfish"), Motion.Float, 0.6f, 12, 0.8, 0.95, S(EnemySkill.Thunder)),
            new("siren", "セイレーン", Img("siren"), Motion.Float, 0.5f, 8, 0.9, 1.0, S(EnemySkill.Glare, EnemySkill.Heal)),
            new("giant_squid", "大王イカ", Img("giant_squid"), Motion.Squish, 0.6f, 6, 1.2, 1.05, S(EnemySkill.Blind)),
            new("kraken", "クラーケン", Img("kraken"), Motion.Heavy, 0.6f, 6, 1.4, 1.15, S(EnemySkill.Smash)),
        ],
        // 3 砂漠
        [
            new("bandit", "盗賊", Img("bandit"), Motion.Breathe, 1.5f, 2, 0.85, 1.0, S(EnemySkill.Blind), SpdMul: 1.3),
            new("hobgoblin", "ホブゴブリン", Img("hobgoblin"), Motion.Breathe, 1.0f, 3, 1.0, 1.05, S(EnemySkill.Smash)),
            new("orc", "オーク", Img("orc"), Motion.Heavy, 0.8f, 5, 1.25, 1.1, S(EnemySkill.Smash)),
            new("kobold_guard", "コボルトガード", Rmt("kobold_b"), Motion.Vibrate, 3.0f, 2, 1.0, 0.85, NoSkill, DefMul: 1.6),
            new("drill_mole", "ドリルモグラ", Img("drill_mole"), Motion.Vibrate, 8.0f, 5, 1.0, 1.05, S(EnemySkill.Smash)),
            new("wind_bird", "ウインドバード", Rmt("bird_c"), Motion.Float, 0.8f, 10, 0.95, 1.0, S(EnemySkill.Wind), SpdMul: 1.3),
            new("wind_wizard", "ウインドウィザード", Rmt("wizard_d"), Motion.Float, 0.5f, 5, 0.8, 1.15, S(EnemySkill.Wind)),
            new("wind_spirit", "ウィンドスピリット", Img("wind_spirit"), Motion.Float, 5.0f, 12, 0.85, 0.95, S(EnemySkill.Blind)),
            new("griffon", "グリフォン", Img("griffon"), Motion.Float, 1.0f, 8, 1.2, 1.15, S(EnemySkill.Wind)),
            new("mummy", "マミー", Img("mummy"), Motion.Breathe, 0.4f, 4, 1.1, 1.0, S(EnemySkill.Curse)),
            new("harpy", "ハーピー", Img("harpy"), Motion.Float, 1.2f, 10, 0.9, 1.0, S(EnemySkill.Wind), SpdMul: 1.3),
            new("giant_turtle", "巨大ガメ", Img("giant_turtle"), Motion.Heavy, 0.3f, 3, 1.5, 0.9, S(EnemySkill.Smash), DefMul: 2.0),
        ],
        // 4 毒の沼
        [
            new("poison_slime", "毒スライム", Rmt("slime_d1", 0xB45AE6), Motion.Squish, 0.8f, 4, 0.85, 0.9, S(EnemySkill.Poison)),
            new("zombie", "ゾンビ", Rmt("zombie_a"), Motion.Breathe, 0.2f, 5, 1.05, 1.0, S(EnemySkill.Poison)),
            new("poison_zombie", "ポイズンゾンビ", Rmt("zombie_b"), Motion.Breathe, 0.2f, 5, 1.15, 1.0, S(EnemySkill.Poison)),
            new("corpse", "腐った死体", Img("rotting_corpse"), Motion.Squish, 0.5f, 8, 1.0, 0.85, S(EnemySkill.Poison)),
            new("killer_bee", "キラービー", Img("killer_bee"), Motion.Vibrate, 10.0f, 5, 0.6, 1.2, S(EnemySkill.Iai), SpdMul: 1.6),
            new("great_serpent", "大蛇", Img("great_serpent"), Motion.Breathe, 0.8f, 5, 1.1, 1.05, S(EnemySkill.Glare)),
            new("giant_spider", "巨大グモ", Img("giant_spider"), Motion.Vibrate, 0.5f, 2, 0.9, 1.0, S(EnemySkill.Blind)),
            new("man_eater", "マンイーター", Rmt("plant_a"), Motion.Breathe, 0.5f, 10, 1.05, 1.0, S(EnemySkill.Drain)),
            new("poison_eater", "ポイズンイーター", Rmt("plant_b"), Motion.Breathe, 0.5f, 10, 1.1, 1.05, S(EnemySkill.Poison)),
            new("hell_plant", "ヘルプラント", Rmt("plant_c"), Motion.Breathe, 0.5f, 10, 1.15, 1.1, S(EnemySkill.Curse)),
        ],
        // 5 神殿
        [
            new("stone_slime", "ストーンスライム", Rmt("slime_d"), Motion.Vibrate, 8.0f, 3, 0.9, 0.9, S(EnemySkill.Smash), DefMul: 1.8),
            new("guard_robot", "警備ロボ", Img("guard_robot"), Motion.Vibrate, 2.0f, 2, 1.1, 1.0, S(EnemySkill.Glare)),
            new("golem", "ゴーレム", Img("golem"), Motion.Heavy, 0.2f, 3, 1.4, 1.1, S(EnemySkill.Smash), DefMul: 1.4, SpdMul: 0.6),
            new("iron_golem", "アイアンゴーレム", Img("iron_golem"), Motion.Vibrate, 0.5f, 2, 1.5, 1.15, S(EnemySkill.Smash), DefMul: 1.9, SpdMul: 0.6),
            new("earth_golem", "アースゴーレム", Img("earth_golem"), Motion.Heavy, 0.5f, 5, 1.25, 1.05, S(EnemySkill.Smash), SpdMul: 0.7),
            new("paladin", "聖騎士", Rmt("soldier_b"), Motion.Stop, 0.5f, 1, 1.15, 1.15, S(EnemySkill.Iai)),
            new("living_armor", "動く鎧", Img("living_armor"), Motion.Vibrate, 1.0f, 2, 1.05, 1.05, S(EnemySkill.Smash), DefMul: 1.6),
            new("cursed_sword", "呪いの剣", Img("cursed_sword"), Motion.Float, 0.2f, 5, 0.8, 1.3, S(EnemySkill.Iai), SpdMul: 1.3),
            new("tank", "暴走戦車", Img("runaway_tank"), Motion.Vibrate, 5.0f, 5, 1.2, 1.15, S(EnemySkill.Smash), DefMul: 1.6),
        ],
        // 6 暗黒の洞窟
        [
            new("dark_slime", "ダークスライム", Rmt("slime_d1", 0x5A4678), Motion.Squish, 1.0f, 5, 1.0, 1.0, S(EnemySkill.Curse)),
            new("vampire_bat", "吸血バット", Img("vampire_bat"), Motion.Float, 2.0f, 20, 0.75, 0.95, S(EnemySkill.Drain), SpdMul: 1.5),
            new("ghost", "ゴースト", Img("ghost"), Motion.Float, 1.0f, 20, 0.85, 0.95, S(EnemySkill.Curse)),
            new("hitodama", "人魂", Img("hitodama", 0.8f), Motion.Flicker, 1.5f, 6, 0.7, 1.0, S(EnemySkill.Curse)),
            new("shadow", "シャドウ", Img("shadow"), Motion.Squish, 2.0f, 10, 0.95, 1.1, S(EnemySkill.Blind)),
            new("floating_eye", "浮遊目玉", Img("floating_eye"), Motion.Float, 0.5f, 25, 0.9, 1.0, S(EnemySkill.Glare)),
            new("ghast", "ガースト", Img("ghast"), Motion.Float, 1.0f, 10, 1.1, 1.15, S(EnemySkill.Fire)),
            new("mimic", "ミミック", Img("mimic"), Motion.Panic, 5.0f, 8, 1.25, 1.2, S(EnemySkill.Drain), ExpMul: 1.5),
            new("skeleton", "スケルトン", Img("skeleton"), Motion.Vibrate, 5.0f, 1, 0.9, 1.05, S(EnemySkill.Iai)),
            new("skull", "スカル", Rmt("skull_a", scale: 1.2f), Motion.Flicker, 3.0f, 10, 1.1, 1.15, NoSkill),
        ],
        // 7 神界
        [
            new("golden_slime", "黄金のスライム", Rmt("slime_c"), Motion.Flicker, 3.0f, 10, 1.3, 0.6, S(EnemySkill.Heal), ExpMul: 1.6),
            new("rose_pixie", "ローズピクシー", Rmt("fairy_a"), Motion.Float, 1.0f, 10, 0.85, 0.95, S(EnemySkill.Heal)),
            new("heart_pixie", "ハートピクシー", Rmt("fairy_c"), Motion.Float, 1.0f, 10, 0.85, 0.95, S(EnemySkill.Heal)),
            new("golden_eater", "ゴールデンイーター", Rmt("plant_d"), Motion.Breathe, 0.5f, 10, 1.15, 1.05, S(EnemySkill.Heal)),
            new("gold_skull", "ゴールドスカル", Rmt("skull_c", scale: 1.2f), Motion.Flicker, 3.0f, 10, 1.15, 1.2, S(EnemySkill.Heal)),
            new("light_orb", "ライトオーブ", Img("light_orb", 0.8f), Motion.Flicker, 1.0f, 15, 0.9, 1.0, S(EnemySkill.Blind)),
            new("spaghetti", "空飛ぶスパゲッティ", Img("flying_spaghetti"), Motion.Float, 2.0f, 15, 1.1, 1.05, S(EnemySkill.Blind)),
            new("ufo", "UFO", Img("ufo"), Motion.Float, 1.5f, 18, 1.0, 1.1, S(EnemySkill.Thunder)),
        ],
        // 8 魔界
        [
            new("king_slime", "キングスライム", Rmt("slime_a", 0x3C8CFF, 1.4f), Motion.Heavy, 0.5f, 12, 1.4, 1.1, S(EnemySkill.Smash)),
            new("luna_pixie", "ルナピクシー", Rmt("fairy_b"), Motion.Float, 1.0f, 10, 0.85, 1.0, S(EnemySkill.Curse)),
            new("dark_pixie", "ダークピクシー", Rmt("fairy_d"), Motion.Float, 1.0f, 10, 0.85, 1.0, S(EnemySkill.Curse)),
            new("werewolf", "ウェアウルフ", Img("werewolf"), Motion.Breathe, 2.5f, 5, 1.1, 1.2, S(EnemySkill.Smash), SpdMul: 1.3),
            new("mad_bear", "マッドベア", Img("mad_bear"), Motion.Heavy, 0.8f, 5, 1.25, 1.15, S(EnemySkill.Smash)),
            new("black_knight", "黒騎士", Rmt("soldier_c"), Motion.Breathe, 0.8f, 2, 1.2, 1.2, S(EnemySkill.Iai)),
            new("thunderbird", "サンダーバード", Img("thunderbird"), Motion.Flicker, 5.0f, 8, 1.0, 1.15, S(EnemySkill.Thunder), SpdMul: 1.3),
            new("thunder_wizard", "サンダーウィザード", Rmt("wizard_c"), Motion.Float, 0.5f, 5, 0.85, 1.2, S(EnemySkill.Thunder)),
            new("thunder_ball", "サンダーボール", Img("thunder_ball", 0.8f), Motion.Flicker, 10.0f, 12, 0.75, 1.05, S(EnemySkill.Thunder)),
            new("mecha_goblin", "メカゴブリン", Img("mecha_goblin"), Motion.Vibrate, 4.0f, 3, 0.95, 1.05, S(EnemySkill.Thunder)),
            new("ai_core", "AIコア", Img("ai_core"), Motion.Flicker, 5.0f, 2, 1.3, 0.9, S(EnemySkill.Thunder), DefMul: 1.5),
            new("angry_wifi", "怒れるWi-Fi", Img("angry_wifi"), Motion.Flicker, 10.0f, 8, 0.6, 1.0, S(EnemySkill.Thunder), SpdMul: 1.5),
            new("error404", "エラー404", Img("error404"), Motion.Stop, 0f, 0, 1.0, 1.0, S(EnemySkill.Curse)),
            new("bug", "バグ", Img("bug"), Motion.Vibrate, 20.0f, 6, 0.7, 1.05, NoSkill, SpdMul: 1.6),
            new("death_skull", "デススカル", Rmt("skull_d", scale: 1.2f), Motion.Flicker, 3.0f, 10, 1.2, 1.25, S(EnemySkill.Curse)),
            new("reaper", "死神", Img("reaper"), Motion.Float, 0.5f, 10, 1.1, 1.25, S(EnemySkill.Iai, EnemySkill.Glare)),
            new("vampire", "ヴァンパイア", Img("vampire"), Motion.Breathe, 1.0f, 3, 1.2, 1.15, S(EnemySkill.Drain, EnemySkill.Glare)),
            new("dullahan", "デュラハン", Img("dullahan"), Motion.Heavy, 1.0f, 5, 1.25, 1.2, S(EnemySkill.Iai)),
            new("lich", "リッチ", Img("lich"), Motion.Flicker, 2.0f, 5, 1.0, 1.3, S(EnemySkill.Ice)),
            new("dark_dragon", "ダークドラゴン", Rmt("dragon_d", scale: 1.3f), Motion.Heavy, 0.5f, 8, 1.5, 1.25, S(EnemySkill.Glare)),
        ],
        // 9 終焉
        [
            new("fire_dragon", "ファイアドラゴン", Rmt("dragon_a", scale: 1.3f), Motion.Heavy, 0.5f, 8, 1.4, 1.2, S(EnemySkill.Fire)),
            new("ice_dragon", "アイスドラゴン", Rmt("dragon_b", scale: 1.3f), Motion.Heavy, 0.5f, 8, 1.45, 1.2, S(EnemySkill.Ice)),
            new("thunder_dragon", "サンダードラゴン", Rmt("dragon_c", scale: 1.3f), Motion.Heavy, 0.5f, 8, 1.5, 1.2, S(EnemySkill.Thunder)),
            new("phantom", "魔王の幻影", Img("demon_lord_phantom"), Motion.Float, 1.5f, 10, 1.35, 1.25, S(EnemySkill.Fire, EnemySkill.Curse)),
            new("destroyer", "破壊神", Img("god_of_destruction"), Motion.Panic, 3.0f, 8, 1.6, 1.3, S(EnemySkill.Smash, EnemySkill.Fire)),
            new("death_knight", "黒騎士・改", Rmt("soldier_d"), Motion.Breathe, 0.8f, 2, 1.3, 1.25, S(EnemySkill.Iai, EnemySkill.Smash)),
        ],
    ];

    /// <summary>10 階ごとのボス（10, 20, …, 100 階）。</summary>
    public static readonly IReadOnlyList<EnemyDef> Bosses =
    [
        new("boss_boar", "暴れイノシシ", Img("wild_boar", 1.2f), Motion.Vibrate, 3.0f, 8, 1.0, 1.0, S(EnemySkill.Smash), Quote: "フゴォォォ！！"),
        new("boss_chimera", "キマイラ", Img("chimera", 1.2f), Motion.Vibrate, 2.0f, 5, 1.0, 1.0, S(EnemySkill.Fire, EnemySkill.Glare), Quote: "三つの頭がこちらをにらんでいる…"),
        new("boss_leviathan", "リヴァイアサン", Img("leviathan", 1.25f), Motion.Float, 0.4f, 10, 1.0, 1.0, S(EnemySkill.Ice, EnemySkill.Smash), Quote: "凍てつく海の底から、巨体がせり上がる！"),
        new("boss_wyvern", "ワイバーン", Img("wyvern", 1.25f), Motion.Float, 0.5f, 8, 1.0, 1.0, S(EnemySkill.Poison, EnemySkill.Glare, EnemySkill.Wind), Quote: "砂嵐を裂いて、翼竜が舞い降りた！"),
        new("boss_hydra", "ヒュドラ", Img("hydra", 1.25f), Motion.Squish, 1.0f, 8, 1.0, 1.0, S(EnemySkill.Poison, EnemySkill.Smash, EnemySkill.Heal), Quote: "首を落としても、また生えてくるという…"),
        new("boss_ancient", "古代兵器", Img("ancient_weapon", 1.25f), Motion.Heavy, 0.1f, 5, 1.0, 1.0, S(EnemySkill.Glare, EnemySkill.Thunder), Quote: "ゴゴゴ… 侵入者ヲ 排除スル"),
        new("boss_blood_skull", "ブラッドスカル", Rmt("skull_b", scale: 1.8f), Motion.Flicker, 3.0f, 12, 1.0, 1.0, S(EnemySkill.Drain, EnemySkill.Curse, EnemySkill.Blind), Quote: "闇の中で、赤い炎が笑っている"),
        new("boss_phoenix", "フェニックス", Img("phoenix", 1.25f), Motion.Float, 0.6f, 10, 1.0, 1.0, S(EnemySkill.Fire, EnemySkill.Heal, EnemySkill.Glare), Quote: "不死鳥が、まばゆい炎をまとって現れた！"),
        new("boss_behemoth", "ベヒーモス", Img("behemoth", 1.3f), Motion.Heavy, 0.2f, 5, 1.0, 1.0, S(EnemySkill.Smash, EnemySkill.Glare, EnemySkill.Iai), Quote: "大地が揺れる。魔界の主が目を覚ました"),
        new("boss_demon_lord", "真・魔王", Img("demon_lord", 1.35f), Motion.Heavy, 0.8f, 12, 1.0, 1.0,
            S(EnemySkill.Curse, EnemySkill.Glare, EnemySkill.Iai, EnemySkill.Blind, EnemySkill.Fire), Quote: "よくぞ来た、人間よ。その魂、もらい受ける"),
    ];

    /// <summary>どの階でもまれに出る敵。</summary>
    public static readonly IReadOnlyList<EnemyDef> Rares =
    [
        new("metal_slime", "メタルスライム", Rmt("slime_d1", 0xC8C8D2), Motion.Vibrate, 8.0f, 3, 0.18, 0.6, NoSkill, DefMul: 12, SpdMul: 2.0, ExpMul: 6.0, FleeChance: 0.3),
        new("lost_cat", "迷子の猫", Img("lost_cat"), Motion.Breathe, 3.0f, 2, 0.35, 0.3, S(EnemySkill.Heal), ExpMul: 1.5, FleeChance: 0.15, Quote: "にゃーん"),
        new("empty_can", "空き缶", Img("empty_can", 0.7f), Motion.Stop, 0f, 0, 0.2, 0, NoSkill, ExpMul: 0.5),
        new("bento", "伝説のコンビニ弁当", Img("legendary_bento", 0.8f), Motion.Flicker, 2.0f, 5, 0.3, 0.3, S(EnemySkill.Heal), ExpMul: 3.0, FleeChance: 0.2),
    ];

    /// <summary>91〜99 階でまれに出る、ふざけた強敵。</summary>
    public static readonly IReadOnlyList<EnemyDef> Jokes =
    [
        new("dev_grudge", "開発者の怨念", Img("dev_grudge"), Motion.Panic, 5.0f, 10, 1.3, 1.25, S(EnemySkill.Curse, EnemySkill.Blind), ExpMul: 2.0, Quote: "仕様です"),
        new("deadline", "締め切り", Img("deadline"), Motion.Panic, 8.0f, 14, 1.4, 1.35, S(EnemySkill.Curse, EnemySkill.Iai), ExpMul: 2.0, Quote: "あと 3 時間"),
        new("mom", "お母さん", Img("mom"), Motion.Heavy, 1.0f, 5, 2.0, 1.4, S(EnemySkill.Smash, EnemySkill.Glare), ExpMul: 3.0, Quote: "いつまでゲームしてるの！"),
    ];

    /// <summary>曜日限定の敵。</summary>
    public static readonly EnemyDef FridaySlime = new("friday_slime", "花金スライム", Rmt("slime_c", 0xFFD700), Motion.Flicker, 5.0f, 10, 0.4, 0.3, S(EnemySkill.Heal), ExpMul: 3.0, FleeChance: 0.2, Quote: "今日は金曜日！");
    public static readonly EnemyDef SundayDevil = new("sunday_devil", "サンデー・デビル", Img("reaper"), Motion.Heavy, 1.0f, 5, 2.2, 1.35, S(EnemySkill.Curse), ExpMul: 3.0, Quote: "明日は月曜日だぞ…");
    public static readonly EnemyDef BlueMonday = new("blue_monday", "ブルーマンデー", Img("ghost"), Motion.Float, 0.2f, 2, 0.9, 0.9, S(EnemySkill.Drain), ExpMul: 1.3, Quote: "会社 行きたくない…");

    public static IEnumerable<EnemyDef> Everything =>
        Regulars.SelectMany(x => x).Concat(Bosses).Concat(Rares).Concat(Jokes).Append(FridaySlime).Append(SundayDevil).Append(BlueMonday);
}

public static class EnemyFactory
{
    public static bool IsBossFloor(int floor) => floor % 10 == 0;

    /// <summary>floor 階の敵を決める。</summary>
    public static EnemyDef Choose(GameRandom rng, int floor, DayOfWeek day)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var biome = Biome.ForFloor(floor);
        if (IsBossFloor(floor)) return EnemyCatalog.Bosses[biome.Index];

        // 曜日限定（WinForms 版と同じ確率）
        if (day == DayOfWeek.Friday && rng.Percent(12)) return EnemyCatalog.FridaySlime;
        if (day == DayOfWeek.Sunday && floor >= 5 && rng.Percent(6)) return EnemyCatalog.SundayDevil;
        if (day == DayOfWeek.Monday && rng.Percent(10)) return EnemyCatalog.BlueMonday;

        if (rng.Percent(4)) return rng.Pick(EnemyCatalog.Rares);
        if (biome.Index == 9 && rng.Percent(10)) return rng.Pick(EnemyCatalog.Jokes);
        return rng.Pick(EnemyCatalog.Regulars[biome.Index]);
    }

    public static Enemy Create(EnemyDef def, int floor, Difficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(def);
        bool boss = IsBossFloor(floor) && EnemyCatalog.Bosses.Contains(def);
        bool final = boss && floor >= Progression.TopFloor;
        double hp = Progression.EnemyHpBase(floor) * def.HpMul * DifficultyRules.EnemyHp(difficulty);
        double atk = Progression.EnemyAttackBase(floor) * def.AtkMul * DifficultyRules.EnemyAttack(difficulty);
        double exp = Progression.ExpBase(floor) * def.ExpMul;
        if (boss)
        {
            hp *= final ? 9.0 : 5.0;
            atk *= final ? 1.35 : 1.18;
            exp *= 4.0;
        }
        int maxHp = Math.Max(1, (int)Math.Round(hp));
        return new Enemy
        {
            Def = def,
            Name = def.Name,
            MaxHp = maxHp,
            Hp = maxHp,
            Attack = (int)Math.Round(atk),
            Defense = (int)Math.Round(Progression.EnemyDefenseBase(floor) * def.DefMul),
            Speed = (int)Math.Round(Progression.EnemySpeedBase(floor) * def.SpdMul),
            ExpReward = Math.Max(1, (int)Math.Round(exp)),
            IsBoss = boss,
            IsFinalBoss = final,
            SkillRate = def.Skills.Length == 0 ? 0 : boss ? 0.5 : 0.38,
        };
    }
}
