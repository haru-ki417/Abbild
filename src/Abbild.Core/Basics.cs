namespace Abbild.Core;

public enum Element { None, Fire, Ice, Thunder, Poison, Holy }

public enum Talent { None, Power, Speed, Magic, Fortune }

public enum Gender { Male, Female }

public enum Difficulty { Easy, Normal, Hard }

public enum Condition { Normal, Frozen, Paralyzed }

public enum Weather { Clear, Rain, Clouds }

/// <summary>演出のきっかけ。画面側で効果音に、コントローラー側でブザー・LED・OLED に変換する。</summary>
public enum Cue
{
    None,
    Cursor,
    Confirm,
    Cancel,
    Buzzer,      // 操作できない・失敗
    Tick,        // ピッ（カウントダウン・連打）
    Success,     // 成功
    Hit,         // こちらの攻撃
    Critical,
    Damage,      // こちらが受けるダメージ
    BigDamage,
    Miss,
    Fire,
    Ice,
    Thunder,
    Poison,
    Holy,
    Heal,
    Magic,
    Charge,
    LevelUp,
    Victory,
    Encounter,
    BossEncounter,
    Explosion,
    Lock,
    Unlock,
    Splash,
    Heartbeat,
    Swing,
    Alarm,
    Freeze,
    Breath,
    Death,
    Revive,
    Step,
    Coin,
}

/// <summary>コントローラーの LED の色。</summary>
public enum LedColor { None, Red, Green, Blue, Yellow, Purple, White, Orange, Magenta, Cyan }

/// <summary>コントローラーの OLED に出すアニメーション。</summary>
public enum OledAnim { None, Encounter, Attack, Magic, Damage, Win }

public static class Names
{
    public static string Of(Talent t) => t switch
    {
        Talent.Power => "ちから",
        Talent.Speed => "はやさ",
        Talent.Magic => "まりょく",
        Talent.Fortune => "うん",
        _ => "なし",
    };

    public static string Describe(Talent t) => t switch
    {
        Talent.Power => "打撃の威力が 2 割上がる",
        Talent.Speed => "攻撃をかわしやすく、先手を取りやすい",
        Talent.Magic => "スキルの威力・回復量が 3 割上がり、MP も多い",
        Talent.Fortune => "会心が出やすく、アイテムや宝箱が見つかりやすい",
        _ => "",
    };

    public static string Of(Difficulty d) => d switch
    {
        Difficulty.Easy => "やさしい",
        Difficulty.Hard => "きびしい",
        _ => "ふつう",
    };

    public static string Describe(Difficulty d) => d switch
    {
        Difficulty.Easy => "敵が弱め。蘇生のチャンスが 5 回。はじめての人に",
        Difficulty.Hard => "敵が強い。蘇生のチャンスは 1 回だけ",
        _ => "おすすめ。蘇生のチャンスが 3 回",
    };

    public static string Of(Condition c) => c switch
    {
        Condition.Frozen => "凍結",
        Condition.Paralyzed => "麻痺",
        _ => "",
    };

    public static string Of(Weather w) => w switch
    {
        Weather.Rain => "雨",
        Weather.Clouds => "くもり",
        _ => "晴れ",
    };

    public static string Of(DayOfWeek d) => d switch
    {
        DayOfWeek.Sunday => "日",
        DayOfWeek.Monday => "月",
        DayOfWeek.Tuesday => "火",
        DayOfWeek.Wednesday => "水",
        DayOfWeek.Thursday => "木",
        DayOfWeek.Friday => "金",
        _ => "土",
    };

    public static string Of(Element e) => e switch
    {
        Element.Fire => "炎",
        Element.Ice => "氷",
        Element.Thunder => "雷",
        Element.Poison => "毒",
        Element.Holy => "聖",
        _ => "",
    };
}

public static class DifficultyRules
{
    public static double EnemyHp(Difficulty d) => d switch { Difficulty.Easy => 0.8, Difficulty.Hard => 1.15, _ => 1.0 };

    public static double EnemyAttack(Difficulty d) => d switch { Difficulty.Easy => 0.7, Difficulty.Hard => 1.2, _ => 1.0 };

    public static int ReviveCharges(Difficulty d) => d switch { Difficulty.Easy => 5, Difficulty.Hard => 1, _ => 3 };
}
