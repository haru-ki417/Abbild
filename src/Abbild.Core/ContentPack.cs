using System.Text;

namespace Abbild.Core;

/// <summary>
/// BGM をまとめて 1 つのファイル（bgm.dat）にしまう入れ物。
/// 配布元（OpenTracks）の規約で「遊ぶ人が音声ファイルとしてかんたんに取り出せる状態」での利用が
/// 禁止されているため、Ogg をそのまま置かず、名前ごとの鍵でかき混ぜて 1 つにまとめる。
/// 形式：「ABPK」・版（uint32）・数（uint32）、つづいて [名前の長さ（uint16）・名前（UTF-8）・位置（uint32）・長さ（uint32）] × 数、そのあとに中身。
/// tools/pack_bgm.py と同じ形式・同じかき混ぜ方。
/// </summary>
public static class ContentPack
{
    private static readonly byte[] Magic = "ABPK"u8.ToArray();
    private const uint Version = 1;

    /// <summary>名前から鍵を作り、バイト列をかき混ぜる（もう一度かけると元に戻る）。</summary>
    public static void Scramble(Span<byte> data, string name)
    {
        uint x = Seed(name);
        for (int i = 0; i < data.Length; i++)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            data[i] ^= (byte)(x & 0xFF);
        }
    }

    private static uint Seed(string name)
    {
        // FNV-1a に固定の値を混ぜる（0 にならないように）
        uint h = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(name))
        {
            h ^= b;
            h *= 16777619;
        }
        h ^= 0x5A17C0DEu;
        return h == 0 ? 0x1234567u : h;
    }

    /// <summary>まとめて書き出す。</summary>
    public static void Write(Stream output, IEnumerable<(string Name, byte[] Data)> entries)
    {
        ArgumentNullException.ThrowIfNull(output);
        var list = entries.ToList();
        using var w = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write((uint)list.Count);
        long headerSize = 12 + list.Sum(e => 2 + Encoding.UTF8.GetByteCount(e.Name) + 8);
        long offset = headerSize;
        foreach (var (name, data) in list)
        {
            var nb = Encoding.UTF8.GetBytes(name);
            w.Write((ushort)nb.Length);
            w.Write(nb);
            w.Write((uint)offset);
            w.Write((uint)data.Length);
            offset += data.Length;
        }
        foreach (var (name, data) in list)
        {
            var copy = (byte[])data.Clone();
            Scramble(copy, name);
            w.Write(copy);
        }
    }

    /// <summary>目次を読む（名前 → 位置と長さ）。形式が違えば InvalidDataException。</summary>
    public static Dictionary<string, (long Offset, int Length)> ReadIndex(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Position = 0;
        using var r = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        var magic = r.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("BGM のまとめファイルではありません。");
        uint version = r.ReadUInt32();
        if (version != Version) throw new InvalidDataException($"対応していない版です（{version}）。");
        uint count = r.ReadUInt32();
        if (count > 1000) throw new InvalidDataException("目次が壊れています。");
        var index = new Dictionary<string, (long, int)>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int len = r.ReadUInt16();
            string name = Encoding.UTF8.GetString(r.ReadBytes(len));
            long offset = r.ReadUInt32();
            int length = checked((int)r.ReadUInt32());
            if (offset + length > input.Length) throw new InvalidDataException("目次が壊れています。");
            index[name] = (offset, length);
        }
        return index;
    }

    /// <summary>1 つ取り出す（かき混ぜを戻した中身）。なければ null。</summary>
    public static byte[]? Load(Stream input, string name)
    {
        ArgumentNullException.ThrowIfNull(input);
        var index = ReadIndex(input);
        if (!index.TryGetValue(name, out var e)) return null;
        var data = new byte[e.Length];
        input.Position = e.Offset;
        input.ReadExactly(data);
        Scramble(data, name);
        return data;
    }

    public static byte[]? Load(string path, string name)
    {
        using var fs = File.OpenRead(path);
        return Load(fs, name);
    }
}
