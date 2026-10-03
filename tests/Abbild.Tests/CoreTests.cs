namespace Abbild.Tests;

public class RandomTests
{
    [Fact]
    public void 同じ種からは同じ並びになり_状態を保存して再開できる()
    {
        var a = new GameRandom(123);
        var b = new GameRandom(123);
        for (int i = 0; i < 10; i++) Assert.Equal(a.NextULong(), b.NextULong());
        var state = a.SaveState();
        var c = GameRandom.FromState(state);
        for (int i = 0; i < 10; i++) Assert.Equal(a.NextULong(), c.NextULong());
    }

    [Fact]
    public void 範囲の外の値は出ない()
    {
        var r = new GameRandom(1);
        for (int i = 0; i < 10000; i++)
        {
            int v = r.Next(3, 9);
            Assert.InRange(v, 3, 8);
            Assert.InRange(r.NextDouble(), 0.0, 0.9999999999);
        }
    }
}

public class HeroTests
{
    [Fact]
    public void 測定の結果は上限と下限におさまる()
    {
        var low = StartingStats.FromTrials(0, 0, 0);
        var high = StartingStats.FromTrials(250, 500, 999);
        Assert.InRange(low.MaxHp, 95, 150);
        Assert.InRange(high.MaxHp, 95, 150);
        Assert.Equal(20, high.Attack);
        Assert.Equal(20, high.Speed);
        Assert.Equal(10, low.Attack);
    }

    [Theory]
    [InlineData(100, 20, 10, Talent.Power)]
    [InlineData(100, 11, 20, Talent.Speed)]
    [InlineData(150, 11, 9, Talent.Fortune)]
    [InlineData(100, 11, 9, Talent.Magic)]
    public void 才能はいちばん目立つ能力で決まる(int hp, int atk, int spd, Talent expected) =>
        Assert.Equal(expected, StartingStats.DecideTalent(hp, atk, spd));

    [Fact]
    public void レベルアップで能力が上がり_全回復し_スキルを覚える()
    {
        var h = Hero.Create("テスト", Gender.Female, StartingStats.Default, Difficulty.Normal);
        h.Hp = 1;
        var ups = h.GainExp(h.ExpToNext + Progression.ExpToNext(2), new GameRandom(5));
        Assert.Equal(2, ups.Count);
        Assert.Equal(3, h.Level);
        Assert.Equal(h.MaxHp, h.Hp);
        Assert.Contains(ups, u => u.NewSkills.Any(s => s.Id == SkillId.Blizzard));
        Assert.True(h.Knows(SkillId.Negotiate));
        Assert.False(h.Knows(SkillId.FinalStrike));
    }

    [Fact]
    public void 必要な経験値はレベルとともに増える()
    {
        for (int lv = 1; lv < 60; lv++) Assert.True(Progression.ExpToNext(lv + 1) > Progression.ExpToNext(lv));
    }
}

public class ItemTests
{
    [Fact]
    public void 同じアイテムは重なり_種類の上限を超えると入らない()
    {
        var inv = new Inventory();
        Assert.True(inv.Add(new Item("potion"), 3));
        Assert.True(inv.Add(new Item("potion")));
        Assert.Equal(4, inv.CountOf(new Item("potion")));
        Assert.Single(inv.Stacks);
        int added = 0;
        foreach (var b in ItemCatalog.Bases)
        {
            foreach (var p in ItemCatalog.Prefixes.Where(p => ItemCatalog.PrefixFits(p, b)))
            {
                if (inv.Add(new Item(b.Id, p.Id))) added++;
            }
        }
        Assert.Equal(Inventory.MaxKinds, inv.Stacks.Count);
        Assert.False(inv.Add(new Item("dynamite", "legend")) && inv.Stacks.Count > Inventory.MaxKinds);
    }

    [Fact]
    public void 接頭辞は合うアイテムにだけつき_名前と効果量が変わる()
    {
        var hot = new Item("molotov", "burning");
        Assert.Equal("燃える火炎ビン", hot.Name);
        Assert.Equal(Element.Fire, hot.Element);
        var holy = new Item("shuriken", "blessed");
        Assert.Equal(Element.Holy, holy.Element);
        Assert.Equal(80, holy.Power);
        Assert.False(ItemCatalog.PrefixFits(ItemCatalog.Prefix("burning"), ItemCatalog.Base("potion")));
        Assert.False(ItemCatalog.PrefixFits(ItemCatalog.Prefix("plain"), ItemCatalog.Base("smokeball")));
    }

    [Fact]
    public void 生成されるアイテムはいつも有効で_深い階ほど強いものが混ざる()
    {
        var r = new GameRandom(9);
        var shallow = Enumerable.Range(0, 2000).Select(_ => ItemGenerator.Generate(r, 1)).ToList();
        var deep = Enumerable.Range(0, 2000).Select(_ => ItemGenerator.Generate(r, 90)).ToList();
        Assert.All(shallow.Concat(deep), i => Assert.True(ItemCatalog.IsKnown(i.BaseId, i.PrefixId)));
        Assert.DoesNotContain(shallow, i => i.BaseId == "elixir");
        Assert.Contains(deep, i => i.BaseId is "elixir" or "steak" or "dynamite");
        Assert.True(deep.Average(i => i.Base.Power) > shallow.Average(i => i.Base.Power));
    }
}

public class EnemyTests
{
    [Fact]
    public void 十階ごとにボスが出て_最後は真魔王()
    {
        var r = new GameRandom(3);
        for (int f = 10; f <= 100; f += 10)
        {
            var def = EnemyFactory.Choose(r, f, DayOfWeek.Wednesday);
            Assert.Contains(def, EnemyCatalog.Bosses);
            var e = EnemyFactory.Create(def, f, Difficulty.Normal);
            Assert.True(e.IsBoss);
            Assert.Equal(f == 100, e.IsFinalBoss);
        }
        Assert.Equal("真・魔王", EnemyFactory.Choose(r, 100, DayOfWeek.Friday).Name);
    }

    [Fact]
    public void 普通の階ではそのバイオームの敵かまれな敵が出る()
    {
        var r = new GameRandom(4);
        for (int f = 1; f < 100; f++)
        {
            if (EnemyFactory.IsBossFloor(f)) continue;
            var def = EnemyFactory.Choose(r, f, DayOfWeek.Wednesday);
            var biome = Biome.ForFloor(f);
            bool ok = EnemyCatalog.Regulars[biome.Index].Contains(def) || EnemyCatalog.Rares.Contains(def) || EnemyCatalog.Jokes.Contains(def);
            Assert.True(ok, $"{f} 階の {def.Name}");
        }
    }

    [Fact]
    public void 金曜日には花金スライムが出ることがある()
    {
        var r = new GameRandom(8);
        var names = Enumerable.Range(0, 400).Select(_ => EnemyFactory.Choose(r, 3, DayOfWeek.Friday).Name).ToList();
        Assert.Contains("花金スライム", names);
        var wed = Enumerable.Range(0, 400).Select(_ => EnemyFactory.Choose(r, 3, DayOfWeek.Wednesday).Name).ToList();
        Assert.DoesNotContain("花金スライム", wed);
    }

    [Fact]
    public void 敵の強さは階が深いほど上がり_難しさで変わる()
    {
        var def = EnemyCatalog.Regulars[0][0];
        var e1 = EnemyFactory.Create(def, 1, Difficulty.Normal);
        var e9 = EnemyFactory.Create(def, 9, Difficulty.Normal);
        Assert.True(e9.MaxHp > e1.MaxHp && e9.Attack > e1.Attack);
        var easy = EnemyFactory.Create(def, 9, Difficulty.Easy);
        var hard = EnemyFactory.Create(def, 9, Difficulty.Hard);
        Assert.True(easy.MaxHp < e9.MaxHp && e9.MaxHp < hard.MaxHp);
    }

    [Fact]
    public void すべての敵の見た目の名前は重ならず空でない()
    {
        var all = EnemyCatalog.Everything.ToList();
        Assert.Equal(all.Count, all.Select(e => e.Id).Distinct().Count());
        Assert.All(all, e => Assert.False(string.IsNullOrWhiteSpace(e.Sprite.Id)));
    }
}

public class BattleTests
{
    private static (RunState Run, BattleSession Battle) Setup(string enemyId = "slime", int floor = 3, int level = 5)
    {
        var hero = Hero.Create("テスト", Gender.Male, StartingStats.Default, Difficulty.Normal);
        var rng = new GameRandom(1);
        while (hero.Level < level) hero.GainExp(hero.ExpToNext, rng);
        var run = RunState.Start(hero, Difficulty.Normal, 42);
        run.Floor = floor;
        var def = EnemyCatalog.Everything.First(e => e.Id == enemyId);
        var enemy = EnemyFactory.Create(def, floor, Difficulty.Normal);
        return (run, new BattleSession(run, enemy));
    }

    [Fact]
    public void 攻撃を続ければ敵を倒して経験値が入る()
    {
        var (run, b) = Setup();
        int before = run.Hero.Exp + (run.Hero.Level * 100000);
        for (int i = 0; i < 50 && b.Outcome == BattleOutcome.Continue; i++)
        {
            b.Attack();
            if (b.Outcome == BattleOutcome.Continue) b.ResolveEnemyTurn(b.PlanEnemyTurn());
        }
        Assert.Equal(BattleOutcome.Victory, b.Outcome);
        var reward = b.ClaimReward();
        Assert.True(reward.Exp > 0);
        Assert.True(run.Hero.Exp + (run.Hero.Level * 100000) > before);
        Assert.Equal(1, run.BattlesWon);
    }

    [Fact]
    public void ミニゲームの結果で敵の技の当たり方が変わる()
    {
        var (run, b) = Setup("reaper", 85, 40);
        var glare = new EnemyIntent(EnemySkill.Glare, ChallengeKind.Glare, "");
        int hp = run.Hero.Hp;
        b.ResolveEnemyTurn(glare, ChallengeOutcome.Pass());
        int afterPass = run.Hero.Hp;
        Assert.True(hp - afterPass <= run.Hero.MaxHp * 0.06, "成功ならダメージなし（場所の効果を除く）");
        b.ResolveEnemyTurn(glare, ChallengeOutcome.Fail());
        Assert.True(afterPass - run.Hero.Hp >= run.Hero.MaxHp * 0.4);
    }

    [Fact]
    public void 居合いを見切るとカウンターで敵が傷つく()
    {
        var (_, b) = Setup("trainee", 5, 8);
        int hp = b.Enemy.Hp;
        b.ResolveEnemyTurn(new EnemyIntent(EnemySkill.Iai, ChallengeKind.Iai, ""), ChallengeOutcome.Pass());
        Assert.True(b.Enemy.Hp < hp);
    }

    [Fact]
    public void 交渉は気持ちで結果が変わり_ボスには使えない()
    {
        var (_, calm) = Setup("goblin", 5, 5);
        calm.UseSkill(SkillId.Negotiate, new ChallengeOutcome(true, Mood: Mood.Calm));
        Assert.Equal(BattleOutcome.Peace, calm.Outcome);
        var (_, hot) = Setup("goblin", 5, 5);
        hot.UseSkill(SkillId.Negotiate, new ChallengeOutcome(true, Mood: Mood.Excited));
        Assert.Equal(BattleOutcome.Intimidated, hot.Outcome);
        var (_, boss) = Setup("boss_boar", 10, 5);
        Assert.NotNull(boss.CannotUse(SkillCatalog.Get(SkillId.Negotiate)));
        Assert.False(boss.UseSkill(SkillId.Negotiate, new ChallengeOutcome(true, Mood: Mood.Calm)));
        Assert.Equal(BattleOutcome.Continue, boss.Outcome);
    }

    [Fact]
    public void 第六感は手番を使わず_1階で3回まで()
    {
        var (_, b) = Setup("goblin", 5, 5);
        for (int i = 0; i < 3; i++) Assert.False(b.UseSkill(SkillId.Dowsing, ChallengeOutcome.Fail()));
        Assert.NotNull(b.CannotUse(SkillCatalog.Get(SkillId.Dowsing)));
    }

    [Fact]
    public void 凍結は息のミニゲームで解け_失敗すると動けない()
    {
        var (run, b) = Setup();
        run.Hero.Condition = Condition.Frozen;
        Assert.Equal(ChallengeKind.Thaw, b.ConditionChallenge);
        Assert.False(b.ResolveCondition(ChallengeOutcome.Fail()));
        Assert.Equal(Condition.Frozen, run.Hero.Condition);
        Assert.True(b.ResolveCondition(ChallengeOutcome.Pass()));
        Assert.Equal(Condition.Normal, run.Hero.Condition);
    }

    [Fact]
    public void 蘇生は回数が減り_必要な連打が増える()
    {
        var (run, b) = Setup();
        run.Hero.Hp = 0;
        int target = b.ReviveTarget;
        int charges = run.Hero.ReviveCharges;
        Assert.True(b.ResolveRevive(ChallengeOutcome.Pass()));
        Assert.True(run.Hero.Hp > 0);
        Assert.Equal(charges - 1, run.Hero.ReviveCharges);
        Assert.True(b.ReviveTarget > target);
    }

    [Fact]
    public void ボスからは煙玉で逃げられない()
    {
        var (run, b) = Setup("boss_boar", 10, 5);
        var smoke = new Item("smokeball");
        run.Hero.Inventory.Add(smoke);
        Assert.False(b.UseItem(smoke));
        Assert.Equal(1, run.Hero.Inventory.CountOf(smoke));
    }

    [Fact]
    public void 弱点の属性はよく効き_天気でも威力が変わる()
    {
        var (run, b) = Setup("red_slime", 13, 10);   // 火山：氷に弱く炎に強い
        Assert.Equal(1.5, b.Affinity(Element.Ice));
        Assert.Equal(0.5, b.Affinity(Element.Fire));
        run.Weather = Weather.Rain;
        Assert.Equal(0.5, b.WeatherFactor(Element.Fire, false));
        Assert.Equal(1.5, b.WeatherFactor(Element.Ice, false));
    }
}

public class SaveTests
{
    [Fact]
    public void 冒険の書を保存して読み直すと同じ状態になる()
    {
        string dir = Path.Combine(Path.GetTempPath(), "abbild-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore(dir);
            var hero = Hero.Create("セーブ", Gender.Female, StartingStats.FromTrials(90, 20, 50), Difficulty.Hard);
            hero.Inventory.Add(new Item("dynamite", "legend"), 2);
            hero.GainExp(500, new GameRandom(2));
            var run = RunState.Start(hero, Difficulty.Hard, 77);
            run.Floor = 37;
            run.PlaySeconds = 1234.5;
            run.Slot = 2;
            store.Save(run);
            Assert.True(store.HasSave(2));
            Assert.False(store.HasSave(1));
            var back = store.LoadRun(2)!;
            Assert.Equal(2, back.Slot);
            Assert.Equal(run.Floor, back.Floor);
            Assert.Equal(Difficulty.Hard, back.Difficulty);
            Assert.Equal(hero.Name, back.Hero.Name);
            Assert.Equal(hero.Level, back.Hero.Level);
            Assert.Equal(hero.MaxHp, back.Hero.MaxHp);
            Assert.Equal(2, back.Hero.Inventory.CountOf(new Item("dynamite", "legend")));
            Assert.Equal(run.Rng.NextULong(), back.Rng.NextULong());
            store.DeleteSave(2);
            Assert.False(store.HasSave(2));
            Assert.False(store.HasAnySave);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void 冒険の書は三冊べつべつに記録でき_一冊だけ消せる()
    {
        string dir = Path.Combine(Path.GetTempPath(), "abbild-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore(dir);
            for (int slot = 1; slot <= SaveStore.SlotCount; slot++)
            {
                var hero = Hero.Create($"勇者{slot}", Gender.Male, StartingStats.Default, Difficulty.Normal);
                var run = RunState.Start(hero, Difficulty.Normal, (ulong)slot);
                run.Floor = slot * 10;
                run.Slot = slot;
                store.Save(run);
            }
            var all = store.LoadAll();
            Assert.Equal(SaveStore.SlotCount, all.Count);
            Assert.Equal([10, 20, 30], all.Select(d => d!.Floor).ToArray());
            Assert.Equal("勇者2", all[1]!.Hero.Name);
            Assert.Equal(3, store.LatestSlot());

            store.DeleteSave(2);
            Assert.True(store.HasSave(1));
            Assert.False(store.HasSave(2));
            Assert.True(store.HasSave(3));
            Assert.Null(store.LoadAll()[1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => store.HasSave(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.HasSave(SaveStore.SlotCount + 1));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void 前の版の冒険の書は一冊目として読める()
    {
        string dir = Path.Combine(Path.GetTempPath(), "abbild-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var hero = Hero.Create("むかし", Gender.Female, StartingStats.Default, Difficulty.Easy);
            var run = RunState.Start(hero, Difficulty.Easy, 5);
            run.Floor = 42;
            var json = System.Text.Json.JsonSerializer.Serialize(SaveData.From(run), new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
            File.WriteAllText(Path.Combine(dir, "adventure.json"), json);
            var store = new SaveStore(dir);
            Assert.True(store.HasSave(1));
            Assert.Equal(42, store.LoadRun(1)!.Floor);
            Assert.False(File.Exists(Path.Combine(dir, "adventure.json")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void 記録を消すと最初の状態にもどる()
    {
        string dir = Path.Combine(Path.GetTempPath(), "abbild-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore(dir);
            store.SaveRecords(new Records { BestFloor = 55, Clears = 2, GameOvers = 9 });
            Assert.Equal(55, store.LoadRecords().BestFloor);
            store.ResetRecords();
            var r = store.LoadRecords();
            Assert.Equal(0, r.BestFloor);
            Assert.Equal(0, r.Clears);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void 壊れた値や知らないアイテムは安全に直して読む()
    {
        var data = new SaveData
        {
            Floor = 999,
            Hero = new HeroData { Name = "とても長い名前のゆうしゃさま", Level = 500, MaxHp = -5, Hp = 99999, Items = [new ItemData { BaseId = "ufo_part", Count = 3 }, new ItemData { BaseId = "potion", Count = 2 }] },
            Rng = [1, 2],
        };
        var run = data.ToRun();
        Assert.Equal(Progression.TopFloor, run.Floor);
        Assert.Equal(Progression.MaxLevel, run.Hero.Level);
        Assert.Equal(1, run.Hero.MaxHp);
        Assert.Equal(1, run.Hero.Hp);
        Assert.Equal(8, run.Hero.Name.Length);
        Assert.Single(run.Hero.Inventory.Stacks);
    }

    [Fact]
    public void 壊れた設定ファイルは脇へよけて最初の設定にする()
    {
        string dir = Path.Combine(Path.GetTempPath(), "abbild-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ これは JSON ではない");
            var s = new SaveStore(dir).LoadSettings();
            Assert.Equal(7, s.BgmVolume);
            Assert.True(File.Exists(Path.Combine(dir, "settings.json.broken")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
