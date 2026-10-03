namespace Abbild.Tests;

public class ContentPackTests
{
    [Fact]
    public void まとめて書いたものを名前で取り出すと元に戻る()
    {
        var a = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();
        var b = "OggS hello"u8.ToArray();
        using var ms = new MemoryStream();
        ContentPack.Write(ms, [("title", a), ("タイトル2", b)]);
        ms.Position = 0;
        Assert.Equal(a, ContentPack.Load(ms, "title"));
        Assert.Equal(b, ContentPack.Load(ms, "タイトル2"));
        Assert.Null(ContentPack.Load(ms, "none"));
    }

    [Fact]
    public void かき混ぜたあとは元の並びが見えない()
    {
        var data = "OggS"u8.ToArray().Concat(new byte[64]).ToArray();
        var copy = (byte[])data.Clone();
        ContentPack.Scramble(copy, "dungeon");
        Assert.NotEqual(data.Take(4), copy.Take(4));
        ContentPack.Scramble(copy, "dungeon");
        Assert.Equal(data, copy);
    }

    [Fact]
    public void 形式が違うファイルは読まない()
    {
        using var ms = new MemoryStream("OggS....not a pack"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => ContentPack.ReadIndex(ms));
    }

    /// <summary>リポジトリの bgm.dat（tools/pack_bgm.py で作ったもの）が、ゲームの読み方で 4 曲とも Ogg として戻る。</summary>
    [Fact]
    public void 同梱のBGMはPythonで作った形式のまま読めて_中身はOgg()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;
        while (dir is not null && path is null)
        {
            var p = Path.Combine(dir.FullName, "src", "Abbild", "Content", "bgm.dat");
            if (File.Exists(p)) path = p;
            dir = dir.Parent;
        }
        // bgm.dat は配布元の規約により公開リポジトリには入れていない。手元にあるときだけ確かめる
        Assert.SkipWhen(path is null, "src/Abbild/Content/bgm.dat がない（公開リポジトリには BGM を入れていない）");
        using var fs = File.OpenRead(path!);
        var index = ContentPack.ReadIndex(fs);
        Assert.Equal(["dungeon", "ending", "gameover", "title"], index.Keys.Order(StringComparer.Ordinal).ToArray());
        foreach (var name in index.Keys)
        {
            var data = ContentPack.Load(fs, name)!;
            Assert.Equal("OggS"u8.ToArray(), data.Take(4).ToArray());
        }
        // ファイルの中に、そのままの Ogg の印が出てこない（音声ファイルとして見つけられない）
        fs.Position = 0;
        var all = new byte[fs.Length];
        fs.ReadExactly(all);
        Assert.Equal(-1, all.AsSpan().IndexOf("OggS"u8));
    }
}
