namespace Abbild.Core;

/// <summary>
/// 保存・再現できる乱数（xoshiro256**）。セーブデータに状態を書き出せるので、
/// ロードし直しても同じ流れになる（リセットで結果を引き直すことはできない）。
/// </summary>
public sealed class GameRandom
{
    private ulong _s0, _s1, _s2, _s3;

    public GameRandom(ulong seed)
    {
        ulong x = seed;
        _s0 = SplitMix(ref x);
        _s1 = SplitMix(ref x);
        _s2 = SplitMix(ref x);
        _s3 = SplitMix(ref x);
    }

    private GameRandom(ulong[] state)
    {
        _s0 = state[0]; _s1 = state[1]; _s2 = state[2]; _s3 = state[3];
        if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
    }

    public static GameRandom FromState(ulong[] state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Length != 4) throw new ArgumentException("状態は 4 要素です。", nameof(state));
        return new GameRandom(state);
    }

    public ulong[] SaveState() => [_s0, _s1, _s2, _s3];

    private static ulong SplitMix(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextULong()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>[0, 1) の実数。</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>[0, max) の整数。</summary>
    public int Next(int max)
    {
        if (max <= 0) return 0;
        return (int)(NextDouble() * max);
    }

    /// <summary>[min, max) の整数。</summary>
    public int Next(int min, int max) => max <= min ? min : min + Next(max - min);

    /// <summary>確率 p (0〜1) で true。</summary>
    public bool Chance(double p) => NextDouble() < p;

    /// <summary>百分率 percent で true。</summary>
    public bool Percent(double percent) => NextDouble() * 100.0 < percent;

    public double Range(double min, double max) => min + (max - min) * NextDouble();

    public T Pick<T>(IReadOnlyList<T> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (list.Count == 0) throw new ArgumentException("空の一覧からは選べません。", nameof(list));
        return list[Next(list.Count)];
    }

    /// <summary>重み付きで 1 つ選ぶ。</summary>
    public T PickWeighted<T>(IReadOnlyList<T> list, Func<T, double> weight)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(weight);
        double total = 0;
        foreach (var x in list) total += Math.Max(0, weight(x));
        if (total <= 0) return Pick(list);
        double r = NextDouble() * total;
        foreach (var x in list)
        {
            r -= Math.Max(0, weight(x));
            if (r < 0) return x;
        }
        return list[^1];
    }
}
