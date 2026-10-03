using System.Text.Json;
using System.Text.Json.Serialization;

namespace Abbild.Core;

public sealed class ItemData
{
    public string BaseId { get; set; } = "";
    public string? PrefixId { get; set; }
    public int Count { get; set; }
}

public sealed class HeroData
{
    public string Name { get; set; } = "";
    public Gender Gender { get; set; }
    public Talent Talent { get; set; }
    public int Level { get; set; }
    public int Exp { get; set; }
    public int MaxHp { get; set; }
    public int Hp { get; set; }
    public int MaxMp { get; set; }
    public int Mp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Speed { get; set; }
    public Condition Condition { get; set; }
    public int ReviveCharges { get; set; }
    public int RevivesUsed { get; set; }
    public List<ItemData> Items { get; set; } = [];
}

/// <summary>冒険の書（セーブデータ）。</summary>
public sealed class SaveData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DateTime SavedAt { get; set; }
    public HeroData Hero { get; set; } = new();
    public Difficulty Difficulty { get; set; }
    public int Floor { get; set; } = 1;
    public ulong[] Rng { get; set; } = [];
    public Weather Weather { get; set; }
    public int BattlesWon { get; set; }
    public int TreasuresOpened { get; set; }
    public double PlaySeconds { get; set; }

    public static SaveData From(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var h = run.Hero;
        return new SaveData
        {
            SavedAt = DateTime.Now,
            Difficulty = run.Difficulty,
            Floor = run.Floor,
            Rng = run.Rng.SaveState(),
            Weather = run.Weather,
            BattlesWon = run.BattlesWon,
            TreasuresOpened = run.TreasuresOpened,
            PlaySeconds = run.PlaySeconds,
            Hero = new HeroData
            {
                Name = h.Name, Gender = h.Gender, Talent = h.Talent, Level = h.Level, Exp = h.Exp,
                MaxHp = h.MaxHp, Hp = h.Hp, MaxMp = h.MaxMp, Mp = h.Mp,
                Attack = h.Attack, Defense = h.Defense, Speed = h.Speed, Condition = h.Condition,
                ReviveCharges = h.ReviveCharges, RevivesUsed = h.RevivesUsed,
                Items = h.Inventory.Stacks.Select(s => new ItemData { BaseId = s.Item.BaseId, PrefixId = s.Item.PrefixId, Count = s.Count }).ToList(),
            },
        };
    }

    /// <summary>冒険を復元する。壊れた値は安全な範囲に直す。</summary>
    public RunState ToRun()
    {
        var d = Hero ?? new HeroData();
        var h = new Hero
        {
            Name = string.IsNullOrWhiteSpace(d.Name) ? "アルト" : d.Name.Trim()[..Math.Min(d.Name.Trim().Length, 8)],
            Gender = d.Gender,
            Talent = d.Talent,
            Level = Math.Clamp(d.Level, 1, Progression.MaxLevel),
            Exp = Math.Max(0, d.Exp),
            MaxHp = Math.Max(1, d.MaxHp),
            MaxMp = Math.Max(0, d.MaxMp),
            Attack = Math.Max(1, d.Attack),
            Defense = Math.Max(0, d.Defense),
            Speed = Math.Max(1, d.Speed),
            Condition = d.Condition,
            ReviveCharges = Math.Max(0, d.ReviveCharges),
            RevivesUsed = Math.Max(0, d.RevivesUsed),
        };
        h.Hp = Math.Clamp(d.Hp, 1, h.MaxHp);
        h.Mp = Math.Clamp(d.Mp, 0, h.MaxMp);
        foreach (var it in d.Items ?? [])
        {
            if (it.Count > 0 && ItemCatalog.IsKnown(it.BaseId, it.PrefixId)) h.Inventory.Add(new Item(it.BaseId, it.PrefixId), it.Count);
        }
        var rng = Rng is { Length: 4 } ? GameRandom.FromState(Rng) : new GameRandom((ulong)DateTime.Now.Ticks);
        return new RunState
        {
            Hero = h,
            Difficulty = Difficulty,
            Rng = rng,
            Floor = Math.Clamp(Floor, 1, Progression.TopFloor),
            Weather = Weather,
            BattlesWon = Math.Max(0, BattlesWon),
            TreasuresOpened = Math.Max(0, TreasuresOpened),
            PlaySeconds = Math.Max(0, PlaySeconds),
        };
    }
}

public sealed class Settings
{
    public int BgmVolume { get; set; } = 7;
    public int SeVolume { get; set; } = 8;
    public bool Fullscreen { get; set; } = true;
    public int TextSpeed { get; set; } = 1;

    /// <summary>自作コントローラーのポート（null なら自動で探す）。</summary>
    public string? ControllerPort { get; set; }
    public int ControllerBaud { get; set; } = 9600;
    public bool UseController { get; set; } = true;

    /// <summary>コントローラーのブザーを鳴らすか。</summary>
    public bool ControllerSound { get; set; } = true;

    /// <summary>天気を送ってくる ESP のポート（なければ使わない）。</summary>
    public string? WeatherPort { get; set; }
    public int WeatherBaud { get; set; } = 115200;

    /// <summary>安静時の心拍（キャラ作成で測ったもの）。</summary>
    public double RestingBpm { get; set; } = 72;

    /// <summary>「高ぶっている」とみなす、安静時からの上がり幅。</summary>
    public int ExciteMargin { get; set; } = 12;

    /// <summary>「落ち着いている」とみなす、安静時からの上がり幅の上限。</summary>
    public int CalmMargin { get; set; } = 6;

    public void Normalize()
    {
        BgmVolume = Math.Clamp(BgmVolume, 0, 10);
        SeVolume = Math.Clamp(SeVolume, 0, 10);
        TextSpeed = Math.Clamp(TextSpeed, 0, 2);
        ControllerBaud = ControllerBaud is 9600 or 19200 or 38400 or 57600 or 115200 ? ControllerBaud : 9600;
        WeatherBaud = WeatherBaud is 9600 or 19200 or 38400 or 57600 or 115200 ? WeatherBaud : 115200;
        RestingBpm = double.IsFinite(RestingBpm) ? Math.Clamp(RestingBpm, 40, 130) : 72;
        ExciteMargin = Math.Clamp(ExciteMargin, 4, 40);
        CalmMargin = Math.Clamp(CalmMargin, 0, 30);
    }
}

public sealed class Records
{
    public int BestFloor { get; set; }
    public int Clears { get; set; }
    public int GameOvers { get; set; }
    public int TotalBattles { get; set; }
    public double FastestClearSeconds { get; set; }
}

/// <summary>セーブ・設定・記録をフォルダーに読み書きする。</summary>
public sealed class SaveStore(string directory)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Directory { get; } = directory;

    private string PathOf(string name) => Path.Combine(Directory, name);

    public static string DefaultDirectory()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(root)) root = AppContext.BaseDirectory;
        return Path.Combine(root, "Abbild");
    }

    /// <summary>冒険の書の数。</summary>
    public const int SlotCount = 3;

    private static string SlotFile(int slot) => $"adventure{slot}.json";

    private static void CheckSlot(int slot)
    {
        if (slot < 1 || slot > SlotCount) throw new ArgumentOutOfRangeException(nameof(slot), slot, $"冒険の書は 1〜{SlotCount} です。");
    }

    /// <summary>前の版（冒険の書が 1 つだけ）の adventure.json を、1 冊目に移す。</summary>
    private void MigrateLegacy()
    {
        try
        {
            string old = PathOf("adventure.json");
            if (!File.Exists(old)) return;
            string first = PathOf(SlotFile(1));
            if (File.Exists(first)) return;
            File.Move(old, first);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public bool HasSave(int slot)
    {
        CheckSlot(slot);
        MigrateLegacy();
        try { return File.Exists(PathOf(SlotFile(slot))); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>どれか 1 冊でも記録があるか。</summary>
    public bool HasAnySave => Enumerable.Range(1, SlotCount).Any(HasSave);

    public SaveData? LoadSave(int slot)
    {
        CheckSlot(slot);
        MigrateLegacy();
        return Read<SaveData>(SlotFile(slot));
    }

    /// <summary>冒険を読み出す（どの冊かも覚えておく）。なければ null。</summary>
    public RunState? LoadRun(int slot)
    {
        var data = LoadSave(slot);
        if (data is null) return null;
        var run = data.ToRun();
        run.Slot = slot;
        return run;
    }

    /// <summary>全部の冊を読む（[0] が 1 冊目。ない冊は null）。</summary>
    public IReadOnlyList<SaveData?> LoadAll() => Enumerable.Range(1, SlotCount).Select(LoadSave).ToList();

    /// <summary>いちばん新しく記録した冊（なければ 0）。</summary>
    public int LatestSlot()
    {
        var all = LoadAll();
        int best = 0;
        DateTime t = DateTime.MinValue;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is { } d && d.SavedAt >= t)
            {
                t = d.SavedAt;
                best = i + 1;
            }
        }
        return best;
    }

    public void Save(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        CheckSlot(run.Slot);
        Write(SlotFile(run.Slot), SaveData.From(run));
    }

    public void DeleteSave(int slot)
    {
        CheckSlot(slot);
        MigrateLegacy();
        string p = PathOf(SlotFile(slot));
        if (File.Exists(p)) File.Delete(p);
        string broken = p + ".broken";
        if (File.Exists(broken)) File.Delete(broken);
    }

    /// <summary>記録（最高到達・踏破回数など）を消す。</summary>
    public void ResetRecords() => Write("records.json", new Records());

    public Settings LoadSettings()
    {
        var s = Read<Settings>("settings.json") ?? new Settings();
        s.Normalize();
        return s;
    }

    public void SaveSettings(Settings s) => Write("settings.json", s);

    public Records LoadRecords() => Read<Records>("records.json") ?? new Records();

    public void SaveRecords(Records r) => Write("records.json", r);

    private T? Read<T>(string name) where T : class
    {
        string p = PathOf(name);
        try
        {
            if (!File.Exists(p)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(p), Json);
        }
        catch (JsonException)
        {
            // 壊れたファイルは脇へよけて、最初からにする
            try { File.Copy(p, p + ".broken", overwrite: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Write<T>(string name, T value)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string p = PathOf(name);
        string tmp = p + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, p, overwrite: true);
    }
}
