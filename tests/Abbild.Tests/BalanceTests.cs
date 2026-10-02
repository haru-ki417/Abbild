namespace Abbild.Tests;

public class BalanceTests(ITestOutputHelper output)
{
    private static (double ClearRate, double MedianFloor, double AvgRevives, double AvgLevel) Simulate(Difficulty d, double skill, int runs)
    {
        var bot = new Bot(skill);
        var results = Enumerable.Range(0, runs).Select(i => bot.Play((ulong)(i * 7919 + 13), d)).ToList();
        var floors = results.Select(r => r.Floor).OrderBy(x => x).ToList();
        return (results.Count(r => r.Cleared) / (double)runs, floors[runs / 2], results.Average(r => r.RevivesUsed), results.Average(r => r.Level));
    }

    [Fact]
    public void ふつうの腕前ならふつうで半分前後クリアできる()
    {
        var (rate, median, rev, lv) = Simulate(Difficulty.Normal, 0.7, 200);
        output.WriteLine($"Normal skill0.7: clear {rate:P0}, median floor {median}, revives {rev:0.0}, level {lv:0.0}");
        Assert.InRange(rate, 0.25, 0.85);
    }

    [Fact]
    public void 難しさの順に勝ちやすさが変わる()
    {
        var easy = Simulate(Difficulty.Easy, 0.6, 150);
        var normal = Simulate(Difficulty.Normal, 0.6, 150);
        var hard = Simulate(Difficulty.Hard, 0.6, 150);
        output.WriteLine($"skill0.6 easy {easy.ClearRate:P0} (floor {easy.MedianFloor}) / normal {normal.ClearRate:P0} (floor {normal.MedianFloor}) / hard {hard.ClearRate:P0} (floor {hard.MedianFloor})");
        Assert.True(easy.ClearRate >= normal.ClearRate);
        Assert.True(normal.ClearRate >= hard.ClearRate);
        Assert.True(easy.ClearRate >= 0.55, "やさしいは、ミニゲームが苦手でも多くの人がクリアできる");
    }

    [Fact]
    public void 上手な人はきびしいでもクリアの見込みがある()
    {
        var hard = Simulate(Difficulty.Hard, 0.9, 150);
        output.WriteLine($"Hard skill0.9: clear {hard.ClearRate:P0}, median floor {hard.MedianFloor}");
        Assert.InRange(hard.ClearRate, 0.1, 0.95);
    }

    [Fact]
    public void レベルは階の深さにほぼ比例して上がる()
    {
        var bot = new Bot(0.8);
        var levels = Enumerable.Range(0, 60).Select(i => bot.Play((ulong)(i + 1), Difficulty.Easy)).Where(r => r.Cleared).ToList();
        Assert.NotEmpty(levels);
        for (int k = 1; k <= 10; k++)
        {
            output.WriteLine($"B{k * 10 + 1}F: Lv {levels.Average(r => r.LevelAt[k == 10 ? 10 : k]):0.0}");
        }
        double at50 = levels.Average(r => r.LevelAt[5]);
        Assert.InRange(at50, 15, 35);
    }
}
