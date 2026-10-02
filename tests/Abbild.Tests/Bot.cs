namespace Abbild.Tests;

/// <summary>バランス確認用の自動プレイヤー。ミニゲームは確率 skill で成功するものとする。</summary>
internal sealed class Bot(double skill)
{
    public sealed record Result(bool Cleared, int Floor, int Level, int RevivesUsed, int[] LevelAt, int Turns);

    public Result Play(ulong seed, Difficulty difficulty, StartingStats? stats = null)
    {
        var hero = Hero.Create("ボット", Gender.Male, stats ?? StartingStats.Default, difficulty);
        var run = RunState.Start(hero, difficulty, seed);
        var luck = new GameRandom(seed ^ 0xABCDEFUL);
        var levelAt = new int[11];
        int turns = 0;
        for (run.Floor = 1; run.Floor <= Progression.TopFloor; run.Floor++)
        {
            if (run.Floor % 10 == 1) levelAt[run.Floor / 10] = hero.Level;
            var def = EnemyFactory.Choose(run.Rng, run.Floor, DayOfWeek.Wednesday);
            var enemy = EnemyFactory.Create(def, run.Floor, difficulty);
            var b = new BattleSession(run, enemy);
            b.Begin();
            while (b.Outcome == BattleOutcome.Continue)
            {
                turns++;
                if (turns > 200000) throw new InvalidOperationException("終わらない");
                bool acted = true;
                if (b.ConditionChallenge != ChallengeKind.None)
                {
                    acted = b.ResolveCondition(Roll(luck));
                }
                if (acted) PlayerTurn(b, luck);
                if (b.Outcome == BattleOutcome.Continue)
                {
                    var intent = b.PlanEnemyTurn();
                    b.ResolveEnemyTurn(intent, intent.Challenge == ChallengeKind.None ? null : Roll(luck, 0.85));
                }
                if (b.Outcome == BattleOutcome.HeroDown)
                {
                    if (!b.CanRevive || !b.ResolveRevive(Roll(luck, 1.1)))
                    {
                        return new Result(false, run.Floor, hero.Level, hero.RevivesUsed, levelAt, turns);
                    }
                }
                b.TakeEvents();
            }
            b.ClaimReward();
            var ev = FloorEvents.Roll(run, enemy.IsBoss);
            switch (ev)
            {
                case FloorEvent.Rest:
                    FloorEvents.Rest(hero);
                    break;
                case FloorEvent.Treasure:
                    if (Roll(luck).Success)
                    {
                        if (FloorEvents.IsTrapped(run))
                        {
                            if (!Roll(luck).Success) hero.TakeDamage(FloorEvents.FallDamage(hero, 0.25));
                        }
                        else
                        {
                            hero.Inventory.Add(FloorEvents.OpenTreasure(run));
                        }
                    }
                    break;
                case FloorEvent.Bridge:
                    if (Roll(luck).Success) hero.GainExp(FloorEvents.BridgeBonusExp(run.Floor), run.Rng);
                    else hero.TakeDamage(FloorEvents.FallDamage(hero, 0.18));
                    break;
            }
            if (hero.Hp <= 0) hero.Hp = 1;
        }
        levelAt[10] = hero.Level;
        return new Result(true, 100, hero.Level, hero.RevivesUsed, levelAt, turns);
    }

    private ChallengeOutcome Roll(GameRandom r, double factor = 1.0)
    {
        bool ok = r.Chance(Math.Min(0.98, skill * factor));
        return new ChallengeOutcome(ok, ok ? 1 : 0, ok ? 30 : 5, ok ? Mood.Calm : Mood.Neutral);
    }

    private void PlayerTurn(BattleSession b, GameRandom luck)
    {
        var h = b.Hero;
        double hpRatio = h.Hp / (double)h.MaxHp;
        if (hpRatio < 0.4)
        {
            var heal = h.Inventory.Stacks.Where(s => s.Item.Kind == ItemKind.HealHp && s.Item.Effect is not PrefixEffect.Rotten and not PrefixEffect.Cursed)
                .OrderBy(s => Math.Abs(s.Item.Power - (h.MaxHp - h.Hp))).FirstOrDefault();
            if (heal is not null) { b.UseItem(heal.Item); return; }
            var hs = SkillCatalog.Get(SkillId.Meditation);
            if (b.CannotUse(hs) is null && luck.Chance(0.5)) { b.UseSkill(SkillId.Meditation, Roll(luck)); return; }
            var hl = SkillCatalog.Get(SkillId.Heal);
            if (b.CannotUse(hl) is null) { b.UseSkill(SkillId.Heal); return; }
        }
        if (h.Mp < h.MaxMp * 0.3)
        {
            var mp = h.Inventory.Stacks.FirstOrDefault(s => s.Item.Kind == ItemKind.HealMp && s.Item.Effect == PrefixEffect.None);
            if (mp is not null && luck.Chance(0.5)) { b.UseItem(mp.Item); return; }
        }
        // 弱点の魔法
        foreach (var id in new[] { SkillId.Thunder, SkillId.Fire, SkillId.Blizzard })
        {
            var s = SkillCatalog.Get(id);
            if (b.CannotUse(s) is null && b.Affinity(s.Element) > 1 && h.Mp > h.MaxMp * 0.35) { b.UseSkill(id); return; }
        }
        if (b.Enemy.IsBoss)
        {
            var fs = SkillCatalog.Get(SkillId.FinalStrike);
            if (b.CannotUse(fs) is null && h.Mp > h.MaxMp * 0.5) { b.UseSkill(SkillId.FinalStrike); return; }
            var dmg = h.Inventory.Stacks.Where(s => s.Item.Kind == ItemKind.Damage && s.Item.Effect == PrefixEffect.None).OrderByDescending(s => s.Item.Power).FirstOrDefault();
            if (dmg is not null) { b.UseItem(dmg.Item); return; }
        }
        b.Attack();
    }
}
