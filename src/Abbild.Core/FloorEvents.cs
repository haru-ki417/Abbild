namespace Abbild.Core;

public enum FloorEvent { None, Treasure, Bridge, Rest }

/// <summary>戦いのあとの出来事（宝箱・吊り橋・休憩所）。</summary>
public static class FloorEvents
{
    public static FloorEvent Roll(RunState run, bool bossDefeated)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (bossDefeated) return FloorEvent.Rest;
        double treasure = run.Hero.Talent == Talent.Fortune ? 12 : 6;
        double r = run.Rng.NextDouble() * 100;
        if (r < treasure) return FloorEvent.Treasure;
        if (r < treasure + 8) return FloorEvent.Bridge;
        return FloorEvent.None;
    }

    /// <summary>解錠に成功したあと、罠だったか。</summary>
    public static bool IsTrapped(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Rng.Percent(8);
    }

    public static Item OpenTreasure(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.TreasuresOpened++;
        return ItemGenerator.Generate(run.Rng, run.Floor, 1.5);
    }

    /// <summary>吊り橋を渡りきったときのおまけの経験値。</summary>
    public static int BridgeBonusExp(int floor) => (int)Math.Round(Progression.ExpBase(floor) * 1.2);

    /// <summary>落ちた・逃げ遅れたときのダメージ。</summary>
    public static int FallDamage(Hero hero, double ratio)
    {
        ArgumentNullException.ThrowIfNull(hero);
        return Math.Max(1, (int)Math.Round(hero.MaxHp * ratio));
    }

    /// <summary>休憩所：HP・MP がすべて戻る。</summary>
    public static void Rest(Hero hero)
    {
        ArgumentNullException.ThrowIfNull(hero);
        hero.Hp = hero.MaxHp;
        hero.Mp = hero.MaxMp;
        hero.Condition = Condition.Normal;
    }
}
