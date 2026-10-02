namespace Abbild.Core;

/// <summary>キャラ作成で決まる最初の能力。</summary>
public sealed record StartingStats(int MaxHp, int Attack, int Speed, Talent Talent)
{
    /// <summary>
    /// 3 つの測定結果から能力を決める。どれも上限と下限があり、極端な値にはならない。
    /// </summary>
    /// <param name="heartBpm">平均の心拍（または鼓動のリズム）。</param>
    /// <param name="shakes">10 秒で振った回数。</param>
    /// <param name="mashes">10 秒で連打した回数。</param>
    public static StartingStats FromTrials(double heartBpm, int shakes, int mashes)
    {
        double bpm = double.IsFinite(heartBpm) && heartBpm > 0 ? heartBpm : 72;
        int hp = (int)Math.Round(Math.Clamp(60 + bpm * 0.75, 95, 150));
        int spd = (int)Math.Round(Math.Clamp(8 + shakes * 0.3, 8, 20));
        int atk = (int)Math.Round(Math.Clamp(10 + mashes * 0.16, 10, 20));
        return new StartingStats(hp, atk, spd, DecideTalent(hp, atk, spd));
    }

    /// <summary>測定をしないときの、平均的な能力。</summary>
    public static StartingStats Default => new(118, 14, 13, Talent.Power);

    /// <summary>一番とがっている能力から才能を決める（WinForms 版の考え方を引き継ぐ）。</summary>
    public static Talent DecideTalent(int hp, int atk, int spd)
    {
        double h = (hp - 95) / 55.0;   // 0〜1
        double a = (atk - 10) / 10.0;
        double s = (spd - 8) / 12.0;
        if (a >= s && a >= h && a >= 0.3) return Talent.Power;
        if (s > a && s >= h && s >= 0.3) return Talent.Speed;
        if (h > a && h > s && h >= 0.6) return Talent.Fortune;
        return Talent.Magic;
    }
}

public sealed class Hero
{
    public string Name { get; set; } = "アルト";
    public Gender Gender { get; set; }
    public Talent Talent { get; set; }
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public int MaxHp { get; set; }
    public int Hp { get; set; }
    public int MaxMp { get; set; }
    public int Mp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Speed { get; set; }
    public Condition Condition { get; set; }
    public Inventory Inventory { get; } = new();

    /// <summary>気合ためで上がった、次の攻撃の倍率。</summary>
    public double ChargeMultiplier { get; set; } = 1.0;

    public bool IsBerserk { get; set; }

    /// <summary>残りの蘇生のチャンス。</summary>
    public int ReviveCharges { get; set; }

    public int RevivesUsed { get; set; }

    public bool IsDead => Hp <= 0;

    public int ExpToNext => Progression.ExpToNext(Level);

    public static Hero Create(string name, Gender gender, StartingStats s, Difficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(s);
        var h = new Hero
        {
            Name = string.IsNullOrWhiteSpace(name) ? "アルト" : name.Trim(),
            Gender = gender,
            Talent = s.Talent,
            MaxHp = s.MaxHp,
            Attack = s.Attack,
            Speed = s.Speed,
            Defense = 6,
            MaxMp = s.Talent == Talent.Magic ? 40 : 30,
            ReviveCharges = DifficultyRules.ReviveCharges(difficulty),
        };
        h.Hp = h.MaxHp;
        h.Mp = h.MaxMp;
        h.Inventory.Add(new Item("potion"), 3);
        h.Inventory.Add(new Item("energy"), 1);
        return h;
    }

    public IEnumerable<SkillDef> Skills => SkillCatalog.LearnedAt(Level);

    public bool Knows(SkillId id) => SkillCatalog.Get(id).LearnLevel <= Level;

    public int HealHp(int amount)
    {
        int before = Hp;
        Hp = Math.Clamp(Hp + Math.Max(0, amount), 0, MaxHp);
        return Hp - before;
    }

    public int HealMp(int amount)
    {
        int before = Mp;
        Mp = Math.Clamp(Mp + Math.Max(0, amount), 0, MaxMp);
        return Mp - before;
    }

    public int TakeDamage(int amount)
    {
        int before = Hp;
        Hp = Math.Max(0, Hp - Math.Max(0, amount));
        return before - Hp;
    }

    /// <summary>経験値を得て、上がったレベルごとの結果を返す。</summary>
    public List<LevelUpResult> GainExp(int amount, GameRandom rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var ups = new List<LevelUpResult>();
        Exp += Math.Max(0, amount);
        while (Exp >= ExpToNext && Level < Progression.MaxLevel)
        {
            Exp -= ExpToNext;
            ups.Add(LevelUp(rng));
        }
        if (Level >= Progression.MaxLevel) Exp = 0;
        return ups;
    }

    private LevelUpResult LevelUp(GameRandom rng)
    {
        Level++;
        int hp = 11 + rng.Next(0, 5);
        int mp = Talent == Talent.Magic ? 4 : 3;
        int atk = 2 + (Talent == Talent.Power ? 1 : 0) + (rng.Chance(0.3) ? 1 : 0);
        int def = 1 + (Level % 2 == 0 ? 1 : 0);
        int spd = rng.Chance(Talent == Talent.Speed ? 0.8 : 0.5) ? 1 : 0;
        MaxHp += hp; MaxMp += mp; Attack += atk; Defense += def; Speed += spd;
        Hp = MaxHp;
        Mp = MaxMp;
        Condition = Condition.Normal;
        return new LevelUpResult(Level, hp, mp, atk, def, spd, SkillCatalog.NewAt(Level).ToList());
    }
}

public sealed record LevelUpResult(int Level, int Hp, int Mp, int Attack, int Defense, int Speed, IReadOnlyList<SkillDef> NewSkills);

public static class Progression
{
    public const int MaxLevel = 99;
    public const int TopFloor = 100;

    public static int ExpToNext(int level) => (int)Math.Round(14 * Math.Pow(level, 1.38) + 6);

    /// <summary>floor 階の普通の敵が落とす経験値の基準。</summary>
    public static double ExpBase(int floor) => 0.42 * ExpToNext((int)Math.Round(1 + (floor * 0.46)));

    public static double EnemyHpBase(int floor) => 34 + (3.1 * floor);

    public static double EnemyAttackBase(int floor) => 9 + (0.82 * floor);

    public static double EnemyDefenseBase(int floor) => 0.42 * floor;

    public static double EnemySpeedBase(int floor) => 8 + (0.45 * floor);
}
