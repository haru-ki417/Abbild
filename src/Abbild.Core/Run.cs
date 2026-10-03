namespace Abbild.Core;

public enum Mood { Neutral, Calm, Excited }

/// <summary>ミニゲームの結果。</summary>
public sealed record ChallengeOutcome(bool Success, double Score = 0, int Count = 0, Mood Mood = Mood.Neutral)
{
    public static ChallengeOutcome Pass(double score = 1) => new(true, score);

    public static ChallengeOutcome Fail(double score = 0) => new(false, score);
}

/// <summary>1 回の冒険（1〜100 階）の状態。セーブの単位。</summary>
public sealed class RunState
{
    public required Hero Hero { get; init; }
    public required Difficulty Difficulty { get; init; }
    public required GameRandom Rng { get; set; }

    /// <summary>今いる階（1〜100）。</summary>
    public int Floor { get; set; } = 1;

    public Weather Weather { get; set; } = Weather.Clear;

    /// <summary>天気がコントローラー（ESP）から来ているか。</summary>
    public bool RealWeather { get; set; }

    public DayOfWeek Day { get; set; } = DateTime.Now.DayOfWeek;

    public int BattlesWon { get; set; }
    public int TreasuresOpened { get; set; }
    public double PlaySeconds { get; set; }
    public bool Cleared { get; set; }

    /// <summary>どの冒険の書（1〜SaveStore.SlotCount）に記録するか。</summary>
    public int Slot { get; set; } = 1;

    public static RunState Start(Hero hero, Difficulty difficulty, ulong seed)
    {
        var run = new RunState { Hero = hero, Difficulty = difficulty, Rng = new GameRandom(seed) };
        run.RollWeather();
        return run;
    }

    public Biome Biome => Biome.ForFloor(Floor);

    /// <summary>コントローラーから天気が来ていなければ、5 階ごとに迷宮の空模様が変わる。</summary>
    public void RollWeather()
    {
        if (RealWeather) return;
        var r = new GameRandom((ulong)(Floor / 5) * 7919UL + (ulong)Difficulty + 17UL);
        Weather = r.Next(10) switch { < 5 => Weather.Clear, < 8 => Weather.Clouds, _ => Weather.Rain };
    }

    public void SetRealWeather(Weather w)
    {
        RealWeather = true;
        Weather = w;
    }
}
