namespace Abbild.Core;

public enum SkillId
{
    Fire,
    Heal,
    Charge,
    Blizzard,
    Negotiate,
    Meditation,
    Dowsing,
    Thunder,
    Alchemy,
    Fishing,
    Breath,
    Berserk,
    FinalStrike,
}

/// <summary>体を使うミニゲームの種類。</summary>
public enum ChallengeKind
{
    None,
    // キャラ作成
    HeartTrial,
    ShakeTrial,
    MashTrial,
    // スキル
    Charge,
    Alchemy,
    Meditation,
    Fishing,
    Negotiation,
    Dowsing,
    Breath,
    Rage,
    Blacksmith,
    // 敵の技・状態異常
    Glare,
    BlindDefense,
    Iai,
    ShakeFree,
    Thaw,
    // 階の出来事
    Revive,
    Lockpick,
    EscapeRun,
    Bridge,
}

public enum SkillKind { Magic, Heal, Physical, Challenge }

public sealed record SkillDef(
    SkillId Id,
    string Name,
    int MpCost,
    int Power,
    Element Element,
    SkillKind Kind,
    ChallengeKind Challenge,
    int LearnLevel,
    bool ConsumesTurn,
    string Description);

public static class SkillCatalog
{
    public static readonly IReadOnlyList<SkillDef> All =
    [
        new(SkillId.Fire, "ファイア", 4, 28, Element.Fire, SkillKind.Magic, ChallengeKind.None, 1, true, "炎で攻撃。晴れだと威力アップ、雨だと半減"),
        new(SkillId.Heal, "ヒール", 6, 45, Element.None, SkillKind.Heal, ChallengeKind.None, 1, true, "HP を回復する"),
        new(SkillId.Charge, "気合ため", 0, 0, Element.None, SkillKind.Challenge, ChallengeKind.Charge, 1, true, "振って力をためる。次の攻撃の威力が上がる"),
        new(SkillId.Blizzard, "ブリザド", 4, 28, Element.Ice, SkillKind.Magic, ChallengeKind.None, 3, true, "氷で攻撃。雨だと威力アップ"),
        new(SkillId.Negotiate, "交渉", 0, 0, Element.None, SkillKind.Challenge, ChallengeKind.Negotiation, 3, true, "落ち着けば和解、興奮すれば威圧。中途半端だと決裂"),
        new(SkillId.Meditation, "瞑想", 5, 0, Element.None, SkillKind.Challenge, ChallengeKind.Meditation, 5, true, "心を静めるほど大きく回復"),
        new(SkillId.Dowsing, "第六感", 0, 0, Element.None, SkillKind.Challenge, ChallengeKind.Dowsing, 5, false, "傾けて隠し財宝を探す（1 階で 3 回まで・手番を使わない）"),
        new(SkillId.Thunder, "サンダー", 7, 42, Element.Thunder, SkillKind.Magic, ChallengeKind.None, 7, true, "雷で攻撃"),
        new(SkillId.Alchemy, "即席錬金", 8, 0, Element.None, SkillKind.Challenge, ChallengeKind.Alchemy, 7, true, "お題どおりに振って薬を作る"),
        new(SkillId.Fishing, "釣り", 5, 0, Element.None, SkillKind.Challenge, ChallengeKind.Fishing, 10, true, "魚を釣って HP と MP を回復"),
        new(SkillId.Breath, "息吹", 3, 30, Element.Poison, SkillKind.Challenge, ChallengeKind.Breath, 10, true, "息を吹きかけて毒の霧で攻撃"),
        new(SkillId.Berserk, "バーサーク", 12, 0, Element.None, SkillKind.Challenge, ChallengeKind.None, 13, true, "興奮状態になる。高ぶっている間は打撃が 2 倍"),
        new(SkillId.FinalStrike, "必殺剣", 18, 300, Element.None, SkillKind.Physical, ChallengeKind.None, 16, true, "攻撃力の 3 倍で斬る"),
    ];

    private static readonly Dictionary<SkillId, SkillDef> Map = All.ToDictionary(s => s.Id);

    public static SkillDef Get(SkillId id) => Map[id];

    public static IEnumerable<SkillDef> LearnedAt(int level) => All.Where(s => s.LearnLevel <= level);

    public static IEnumerable<SkillDef> NewAt(int level) => All.Where(s => s.LearnLevel == level);
}
