namespace Abbild.Core;

public enum BattleEventKind
{
    Message,
    EnemyDamaged,
    HeroDamaged,
    HeroHealed,
    EnemyHealed,
    Dodge,
    Status,
    EnemyAttack,   // 敵の攻撃の動き
    Shake,         // 画面の揺れ
}

/// <summary>画面に順番に流す出来事。</summary>
public sealed record BattleEvent(
    BattleEventKind Kind,
    string Text,
    int Amount = 0,
    Cue Cue = Cue.None,
    LedColor Led = LedColor.None,
    OledAnim Oled = OledAnim.None,
    bool Critical = false,
    Element Element = Element.None);

public enum BattleOutcome
{
    Continue,
    Victory,
    EnemyFled,
    Escaped,
    Peace,       // 交渉で和解
    Intimidated, // 交渉で威圧
    HeroDown,    // HP が 0（蘇生できるかは別）
}

/// <summary>敵の番に何をするか。ミニゲームが要るときは Challenge が入る。</summary>
public sealed record EnemyIntent(EnemySkill Skill, ChallengeKind Challenge, string Announce);

public sealed record VictoryReward(int Exp, IReadOnlyList<LevelUpResult> LevelUps, IReadOnlyList<Item> Drops, IReadOnlyList<Item> Lost);

/// <summary>1 階ぶんの戦い。ルールだけを持ち、画面や入力には依存しない。</summary>
public sealed class BattleSession
{
    private readonly List<BattleEvent> _events = [];

    public BattleSession(RunState run, Enemy enemy)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
        Enemy = enemy ?? throw new ArgumentNullException(nameof(enemy));
    }

    public RunState Run { get; }
    public Enemy Enemy { get; }
    public Hero Hero => Run.Hero;
    public GameRandom Rng => Run.Rng;
    public Biome Biome => Run.Biome;
    public int Floor => Run.Floor;
    public BattleOutcome Outcome { get; private set; } = BattleOutcome.Continue;
    public int DowsingLeft { get; private set; } = 3;
    public int Turn { get; private set; } = 1;

    /// <summary>洞窟では敵の名前が見えにくい。</summary>
    public string EnemyDisplayName => Biome.Effect == BiomeEffect.Dark && Turn <= 1 && !Enemy.IsBoss ? "？？？" : Enemy.Name;

    /// <summary>ここまでにたまった出来事を取り出す。</summary>
    public List<BattleEvent> TakeEvents()
    {
        var list = new List<BattleEvent>(_events);
        _events.Clear();
        return list;
    }

    private void Say(string text, Cue cue = Cue.None, LedColor led = LedColor.None, OledAnim oled = OledAnim.None) =>
        _events.Add(new BattleEvent(BattleEventKind.Message, text, 0, cue, led, oled));

    private void Emit(BattleEvent e) => _events.Add(e);

    // ------------------------------------------------------------------
    // 開始
    // ------------------------------------------------------------------

    public void Begin()
    {
        string name = EnemyDisplayName;
        if (Enemy.IsBoss)
        {
            Say($"地下 {Floor} 階の主、{Enemy.Name} が立ちはだかった！", Cue.BossEncounter, Biome.Led, OledAnim.Encounter);
        }
        else
        {
            Say($"{name} が現れた！", Cue.Encounter, Biome.Led, OledAnim.Encounter);
        }
        if (Enemy.Def.Quote is { } q) Say($"「{q}」");
    }

    // ------------------------------------------------------------------
    // こちらの番
    // ------------------------------------------------------------------

    /// <summary>番のはじめに状態異常があれば、解くためのミニゲームを返す。</summary>
    public ChallengeKind ConditionChallenge => Hero.Condition switch
    {
        Condition.Frozen => ChallengeKind.Thaw,
        Condition.Paralyzed => ChallengeKind.ShakeFree,
        _ => ChallengeKind.None,
    };

    /// <summary>状態異常を解けたか。false なら、この番は動けない。</summary>
    public bool ResolveCondition(ChallengeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var c = Hero.Condition;
        if (c == Condition.Normal) return true;
        if (outcome.Success)
        {
            Hero.Condition = Condition.Normal;
            Say(c == Condition.Frozen ? "温かい息で氷を溶かした！" : "体を振って、しびれを振り払った！", Cue.Success, LedColor.Cyan);
            return true;
        }
        Say(c == Condition.Frozen ? "体が凍りついて動けない…" : "しびれて動けない…", Cue.Buzzer);
        return false;
    }

    /// <summary>通常攻撃。バーサーク中なら rage（高ぶりの判定）を渡す。</summary>
    public void Attack(ChallengeOutcome? rage = null, bool swingBonus = false)
    {
        double mul = 1.0;
        if (Hero.IsBerserk)
        {
            if (rage is { Success: true })
            {
                mul *= 2.0;
                Say("【狂戦士】怒りの一撃！！（威力 2 倍）", Cue.Charge, LedColor.Red);
            }
            else
            {
                Hero.IsBerserk = false;
                Say("冷静になってしまった… 反動で動けない！", Cue.Buzzer, LedColor.Blue);
                return;
            }
        }
        if (swingBonus) mul *= 1.2;
        if (Hero.ChargeMultiplier > 1.0)
        {
            mul *= Hero.ChargeMultiplier;
            Say("ためた力を解き放つ！", Cue.Charge);
            Hero.ChargeMultiplier = 1.0;
        }

        if (HeroMisses())
        {
            Say(Biome.Effect == BiomeEffect.Miss ? "砂ぼこりで手元がくるった！ 攻撃は外れた" : "攻撃は外れた！", Cue.Miss, oled: OledAnim.Attack);
            return;
        }

        bool crit = Rng.Percent(CritChance);
        double atk = Hero.Attack * (Hero.Talent == Talent.Power ? 1.2 : 1.0) * mul;
        int dmg = crit ? Damage(atk * 1.6, 0) : Damage(atk, Enemy.Defense);
        Say(crit ? "会心の一撃！！" : $"{Hero.Name} の攻撃！", crit ? Cue.Critical : Cue.Hit, oled: OledAnim.Attack);
        HitEnemy(dmg, crit, Element.None);
    }

    /// <summary>スキルが今つかえるか（使えない理由を返す。使えれば null）。</summary>
    public string? CannotUse(SkillDef s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!Hero.Knows(s.Id)) return "まだ覚えていない";
        if (Hero.Mp < MpCost(s)) return "MP が足りない";
        if (s.Id == SkillId.Dowsing && DowsingLeft <= 0) return "この階ではもう集中できない";
        if (s.Id == SkillId.Negotiate && Enemy.IsBoss) return "ボスは聞く耳を持たない";
        if (s.Id == SkillId.Berserk && Hero.IsBerserk) return "すでに興奮している";
        return null;
    }

    public int MpCost(SkillDef s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.MpCost == 0 ? 0 : s.MpCost + (Biome.Effect == BiomeEffect.Curse ? 2 : 0);
    }

    /// <summary>
    /// スキルを使う。ミニゲームが要るスキルは、先に画面側でミニゲームをして結果を渡す。
    /// 戻り値は手番を使ったか。
    /// </summary>
    public bool UseSkill(SkillId id, ChallengeOutcome? outcome = null)
    {
        var s = SkillCatalog.Get(id);
        if (CannotUse(s) is { } why)
        {
            Say(why, Cue.Buzzer);
            return false;
        }
        Hero.Mp -= MpCost(s);
        double level = 1 + (0.08 * (Hero.Level - 1));
        double magic = Hero.Talent == Talent.Magic ? 1.3 : 1.0;

        switch (id)
        {
            case SkillId.Fire:
            case SkillId.Blizzard:
            case SkillId.Thunder:
            {
                Say($"{s.Name} を唱えた！", s.Element switch
                {
                    Element.Fire => Cue.Fire,
                    Element.Ice => Cue.Ice,
                    _ => Cue.Thunder,
                }, s.Element switch { Element.Fire => LedColor.Red, Element.Ice => LedColor.Cyan, _ => LedColor.Yellow }, OledAnim.Magic);
                double power = s.Power * level * magic * WeatherFactor(s.Element, true);
                if (Hero.ChargeMultiplier > 1.0)
                {
                    power *= Hero.ChargeMultiplier;
                    Hero.ChargeMultiplier = 1.0;
                    Say("ためた力で威力アップ！");
                }
                HitEnemy(MagicDamage(power, s.Element), false, s.Element);
                return true;
            }
            case SkillId.Heal:
            {
                int amount = (int)Math.Round(s.Power * (1 + (0.1 * (Hero.Level - 1))) * magic);
                int healed = Hero.HealHp(amount);
                Say($"ヒール！ HP が {healed} 回復した", Cue.Heal, LedColor.Green, OledAnim.Magic);
                Emit(new BattleEvent(BattleEventKind.HeroHealed, "", healed));
                return true;
            }
            case SkillId.FinalStrike:
            {
                Say("必殺剣！！", Cue.Critical, LedColor.White, OledAnim.Attack);
                double atk = Hero.Attack * 3.0 * (Hero.Talent == Talent.Power ? 1.2 : 1.0) * Hero.ChargeMultiplier;
                Hero.ChargeMultiplier = 1.0;
                HitEnemy(Damage(atk, Enemy.Defense), true, Element.None);
                return true;
            }
            case SkillId.Berserk:
                Hero.IsBerserk = true;
                Say("ウォォォォ！！！ 興奮状態になった！", Cue.Charge, LedColor.Red);
                Say("※攻撃のたびに、高ぶりを保て！");
                return true;
            case SkillId.Charge:
            {
                var o = outcome ?? ChallengeOutcome.Fail();
                double mult = Math.Clamp(1.0 + (o.Count / 20.0), 1.0, 2.5);
                Hero.ChargeMultiplier = Math.Max(Hero.ChargeMultiplier, mult);
                Say($"力をためた！ 次の攻撃の威力 {Hero.ChargeMultiplier:0.0} 倍", Cue.Charge, LedColor.Cyan);
                return true;
            }
            case SkillId.Alchemy:
            {
                var o = outcome ?? ChallengeOutcome.Fail();
                int amount = (int)Math.Round(Hero.MaxHp * (o.Success ? 0.45 : 0.08));
                int healed = Hero.HealHp(amount);
                Say(o.Success ? $"上質なポーションができた！ HP が {healed} 回復" : $"謎の液体ができた… HP が {healed} 回復",
                    o.Success ? Cue.Heal : Cue.Buzzer, o.Success ? LedColor.Green : LedColor.None);
                Emit(new BattleEvent(BattleEventKind.HeroHealed, "", healed));
                return true;
            }
            case SkillId.Meditation:
            {
                var o = outcome ?? ChallengeOutcome.Fail();
                int healed = Hero.HealHp((int)Math.Round(Hero.MaxHp * (o.Success ? 0.7 : 0.12)));
                int mp = o.Success ? Hero.HealMp((int)Math.Round(Hero.MaxMp * 0.1)) : 0;
                Say(o.Success ? $"精神統一。HP が {healed} 回復した" + (mp > 0 ? $"（MP +{mp}）" : "") : $"雑念が混じった… HP が {healed} 回復",
                    o.Success ? Cue.Heal : Cue.Buzzer, o.Success ? LedColor.Blue : LedColor.None);
                Emit(new BattleEvent(BattleEventKind.HeroHealed, "", healed));
                return true;
            }
            case SkillId.Fishing:
            {
                var o = outcome ?? ChallengeOutcome.Fail();
                if (o.Success)
                {
                    int hp = Hero.HealHp((int)Math.Round(Hero.MaxHp * 0.35));
                    int mp = Hero.HealMp((int)Math.Round(Hero.MaxMp * 0.3));
                    Say($"新鮮な魚を食べた！ HP +{hp}、MP +{mp}", Cue.Heal, LedColor.Green);
                    Emit(new BattleEvent(BattleEventKind.HeroHealed, "", hp));
                }
                else
                {
                    Say("逃げられてしまった…", Cue.Buzzer);
                }
                return true;
            }
            case SkillId.Breath:
            {
                var o = outcome ?? ChallengeOutcome.Fail();
                if (o.Score <= 0.05)
                {
                    Say("息が続かなかった…", Cue.Buzzer);
                    return true;
                }
                Say("毒の霧を吹きかけた！", Cue.Poison, LedColor.Purple, OledAnim.Magic);
                double power = s.Power * level * magic * (0.4 + (0.8 * Math.Clamp(o.Score, 0, 1)));
                HitEnemy(MagicDamage(power, Element.Poison), false, Element.Poison);
                return true;
            }
            case SkillId.Negotiate:
                return ResolveNegotiation(outcome ?? ChallengeOutcome.Fail());
            case SkillId.Dowsing:
            {
                DowsingLeft--;
                var o = outcome ?? ChallengeOutcome.Fail();
                if (o.Success)
                {
                    var item = ItemGenerator.Generate(Rng, Floor, 1.0);
                    if (Hero.Inventory.Add(item)) Say($"壁の中から {item.Name} を見つけた！", Cue.Coin, LedColor.White);
                    else Say($"{item.Name} を見つけたが、もう持てない…", Cue.Buzzer);
                }
                else
                {
                    Say("何も感じ取れなかった…", Cue.Buzzer);
                }
                Say($"（この階で探せるのは あと {DowsingLeft} 回）");
                return false;
            }
        }
        return true;
    }

    private bool ResolveNegotiation(ChallengeOutcome o)
    {
        switch (o.Mood)
        {
            case Mood.Calm:
                Say("説得成功！ 敵は満足して帰っていった", Cue.Success, LedColor.Blue);
                Outcome = BattleOutcome.Peace;
                return true;
            case Mood.Excited:
                Say("威圧成功！ 敵は怯えて逃げ出した", Cue.Success, LedColor.Red);
                Outcome = BattleOutcome.Intimidated;
                return true;
            default:
                Enemy.Attack = (int)Math.Round(Enemy.Attack * 1.1);
                Say("交渉決裂！ 敵が怒り出した（攻撃力アップ）", Cue.Buzzer, LedColor.Red);
                return true;
        }
    }

    /// <summary>アイテムを使う。研石は先にミニゲームをして結果を渡す。戻り値は手番を使ったか。</summary>
    public bool UseItem(Item item, ChallengeOutcome? outcome = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Hero.Inventory.CountOf(item) <= 0) return false;
        if (item.Kind == ItemKind.Escape && Enemy.IsBoss)
        {
            Say("ボスからは逃げられない！", Cue.Buzzer);
            return false;
        }
        Hero.Inventory.Remove(item);
        Say($"{item.Name} を使った！", Cue.Confirm);

        double mul = item.Effect == PrefixEffect.Random ? Rng.Range(0.2, 3.0) : 1.0;
        int power = (int)Math.Round(item.Power * mul);
        if (item.Effect == PrefixEffect.Random) Say(mul >= 1.5 ? "…なんだかすごく効いた！" : mul < 0.7 ? "…あまり効かなかった" : "…普通に効いた");

        switch (item.Kind)
        {
            case ItemKind.HealHp:
                if (item.Effect == PrefixEffect.Rotten)
                {
                    int d = Hero.TakeDamage(Math.Max(1, power / 2));
                    Say($"お腹を壊した… {d} のダメージ", Cue.Damage);
                    Emit(new BattleEvent(BattleEventKind.HeroDamaged, "", d));
                    break;
                }
                {
                    int healed = Hero.HealHp(item.Base.Power >= 9999 ? Hero.MaxHp : power);
                    Say($"HP が {healed} 回復した", Cue.Heal, LedColor.Green);
                    Emit(new BattleEvent(BattleEventKind.HeroHealed, "", healed));
                    if (item.Effect == PrefixEffect.Blessed && Hero.Condition != Condition.Normal)
                    {
                        Hero.Condition = Condition.Normal;
                        Say("体が軽くなった！（状態異常が治った）");
                    }
                }
                break;
            case ItemKind.HealMp:
                if (item.Effect == PrefixEffect.Rotten)
                {
                    int lost = Math.Min(Hero.Mp, Math.Max(1, power / 2));
                    Hero.Mp -= lost;
                    Say($"気分が悪くなった… MP が {lost} 減った", Cue.Buzzer);
                    break;
                }
                Say($"MP が {Hero.HealMp(power)} 回復した", Cue.Heal, LedColor.Blue);
                break;
            case ItemKind.Damage:
            {
                double scaled = power * (1 + (Floor / 60.0));
                if (item.Effect == PrefixEffect.Rotten) scaled *= 0.5;
                var cue = item.Element switch
                {
                    Element.Fire => Cue.Fire,
                    Element.Ice => Cue.Ice,
                    Element.Thunder => Cue.Thunder,
                    Element.Poison => Cue.Poison,
                    Element.Holy => Cue.Holy,
                    _ => Cue.Hit,
                };
                Emit(new BattleEvent(BattleEventKind.Message, "", 0, cue, LedColor.Orange, OledAnim.Attack));
                HitEnemy(MagicDamage(scaled, item.Element), false, item.Element);
                break;
            }
            case ItemKind.BuffAttack:
                Hero.Attack += item.Power;
                Say($"力がみなぎる！ 攻撃力が {item.Power} 上がった", Cue.LevelUp, LedColor.Red);
                break;
            case ItemKind.BuffDefense:
                Hero.Defense += item.Power;
                Say($"体が頑丈になった！ 防御力が {item.Power} 上がった", Cue.LevelUp, LedColor.Blue);
                break;
            case ItemKind.Whetstone:
            {
                var o = outcome ?? ChallengeOutcome.Fail();
                if (o.Success)
                {
                    int up = 2 + (o.Count >= 3 ? 2 : 1);
                    Hero.Attack += up;
                    Say($"武器がキンキンに研ぎ澄まされた！ 攻撃力 +{up}", Cue.Success, LedColor.White);
                }
                else
                {
                    Say("うまく研げなかった…", Cue.Buzzer);
                }
                break;
            }
            case ItemKind.Escape:
                Say("煙にまぎれて逃げ出した！", Cue.Swing);
                Outcome = BattleOutcome.Escaped;
                break;
            case ItemKind.Weaken:
                Enemy.Attack = Math.Max(1, (int)Math.Round(Enemy.Attack * 0.8));
                Say("敵の目に砂をかけた！ 敵の攻撃力が下がった", Cue.Success);
                break;
            case ItemKind.Stop:
                Enemy.Stopped = true;
                Say("クモの巣で敵を絡め取った！ 敵は動けない", Cue.Success);
                break;
        }

        if (item.Effect == PrefixEffect.Cursed && Outcome == BattleOutcome.Continue)
        {
            int d = Hero.TakeDamage(Math.Max(1, Hero.MaxHp * 15 / 100));
            Say($"呪いが体をむしばむ… {d} のダメージ", Cue.Damage, LedColor.Purple);
            Emit(new BattleEvent(BattleEventKind.HeroDamaged, "", d));
            if (Hero.IsDead) Outcome = BattleOutcome.HeroDown;
        }
        if (Hero.IsDead && Outcome == BattleOutcome.Continue) Outcome = BattleOutcome.HeroDown;
        return true;
    }

    // ------------------------------------------------------------------
    // 敵の番
    // ------------------------------------------------------------------

    /// <summary>敵が何をするかを決める。ミニゲームが要る技なら Challenge が入る。</summary>
    public EnemyIntent PlanEnemyTurn()
    {
        if (Enemy.Stopped || Enemy.Attack <= 0 || Enemy.Def.FleeChance > 0) return new EnemyIntent(EnemySkill.None, ChallengeKind.None, "");
        var skills = Enemy.Def.Skills;
        if (skills.Length > 0 && Rng.Chance(Enemy.SkillRate))
        {
            var sk = Rng.Pick(skills);
            if (sk == EnemySkill.Heal && Enemy.Hp > Enemy.MaxHp * 0.7)
            {
                var others = skills.Where(x => x != EnemySkill.Heal).ToArray();
                sk = others.Length > 0 ? Rng.Pick(others) : EnemySkill.None;
            }
            return sk switch
            {
                EnemySkill.Glare => new EnemyIntent(sk, ChallengeKind.Glare, $"{Enemy.Name} がこちらを凝視している… 殺気を探られている！"),
                EnemySkill.Blind => new EnemyIntent(sk, ChallengeKind.BlindDefense, $"{Enemy.Name} が目潰しを使った！ 視界が奪われる！"),
                EnemySkill.Iai => new EnemyIntent(sk, ChallengeKind.Iai, $"{Enemy.Name} が構えた… 一撃必殺の気配！"),
                _ => new EnemyIntent(sk, ChallengeKind.None, ""),
            };
        }
        return new EnemyIntent(EnemySkill.None, ChallengeKind.None, "");
    }

    /// <summary>敵の番を進める。ミニゲームの技なら結果を渡す。</summary>
    public void ResolveEnemyTurn(EnemyIntent intent, ChallengeOutcome? outcome = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (Outcome != BattleOutcome.Continue) return;

        if (Enemy.Stopped)
        {
            Enemy.Stopped = false;
            Say($"{Enemy.Name} はクモの巣にからまって動けない！");
            EndRound();
            return;
        }
        if (Enemy.Def.FleeChance > 0 && Rng.Chance(Enemy.Def.FleeChance))
        {
            Say($"{Enemy.Name} は逃げ出した！", Cue.Swing);
            Outcome = BattleOutcome.EnemyFled;
            return;
        }
        if (Enemy.Attack <= 0)
        {
            Say($"{Enemy.Name} はころがっている…");
            EndRound();
            return;
        }

        int baseDmg = Damage(Enemy.Attack, Hero.Defense);
        bool boss = Enemy.IsBoss;
        Emit(new BattleEvent(BattleEventKind.EnemyAttack, ""));

        switch (intent.Skill)
        {
            case EnemySkill.None:
                Say($"{Enemy.Name} の攻撃！", Cue.Damage);
                if (!HeroDodges()) HurtHero(baseDmg);
                break;
            case EnemySkill.Fire:
                Say($"{Enemy.Name} のファイアブレス！", Cue.Fire, LedColor.Red, OledAnim.Damage);
                if (!HeroDodges()) HurtHero((int)Math.Round(baseDmg * 1.5));
                break;
            case EnemySkill.Ice:
                Say($"{Enemy.Name} のブリザド！", Cue.Ice, LedColor.Cyan, OledAnim.Damage);
                if (!HeroDodges())
                {
                    HurtHero((int)Math.Round(baseDmg * 1.2));
                    if (!Hero.IsDead && Hero.Condition == Condition.Normal && Rng.Chance(boss ? 0.25 : 0.35))
                    {
                        Hero.Condition = Condition.Frozen;
                        Emit(new BattleEvent(BattleEventKind.Status, "体が凍りついた！", 0, Cue.Freeze, LedColor.Cyan));
                    }
                }
                break;
            case EnemySkill.Thunder:
                Say($"{Enemy.Name} のサンダー！", Cue.Thunder, LedColor.Yellow, OledAnim.Damage);
                if (!HeroDodges())
                {
                    HurtHero(baseDmg);
                    if (!Hero.IsDead && Hero.Condition == Condition.Normal && Rng.Chance(boss ? 0.25 : 0.35))
                    {
                        Hero.Condition = Condition.Paralyzed;
                        Emit(new BattleEvent(BattleEventKind.Status, "体がしびれた！", 0, Cue.Thunder, LedColor.Yellow));
                    }
                }
                break;
            case EnemySkill.Heal:
            {
                int healed = Enemy.Heal((int)Math.Round(Enemy.MaxHp * (boss ? 0.12 : 0.22)));
                Say($"{Enemy.Name} は傷を回復した（+{healed}）", Cue.Heal, LedColor.Green);
                Emit(new BattleEvent(BattleEventKind.EnemyHealed, "", healed));
                break;
            }
            case EnemySkill.Smash:
                if (Rng.Chance(0.15))
                {
                    Say($"{Enemy.Name} の痛恨の一撃！ …しかし空振りした", Cue.Miss);
                    break;
                }
                Say($"{Enemy.Name} の痛恨の一撃！", Cue.BigDamage, LedColor.Red, OledAnim.Damage);
                if (!HeroDodges()) HurtHero(baseDmg * 2, big: true);
                break;
            case EnemySkill.Poison:
            {
                Say($"{Enemy.Name} のポイズンブレス！", Cue.Poison, LedColor.Purple, OledAnim.Damage);
                int mpLoss = Math.Min(Hero.Mp, 6 + (Floor / 6));
                Hero.Mp -= mpLoss;
                if (mpLoss > 0) Emit(new BattleEvent(BattleEventKind.Status, $"MP が {mpLoss} 減った！", 0, Cue.Poison));
                HurtHero(Math.Max(1, baseDmg / 2));
                break;
            }
            case EnemySkill.Curse:
                Say($"{Enemy.Name} は呪いの言葉を吐いた！", Cue.Damage, LedColor.Purple, OledAnim.Damage);
                if (!HeroDodges()) HurtHero((int)Math.Round(baseDmg * 1.5));
                break;
            case EnemySkill.Drain:
            {
                Say($"{Enemy.Name} は血を吸おうとしてきた！", Cue.Damage, LedColor.Magenta, OledAnim.Damage);
                if (HeroDodges()) break;
                int d = HurtHero(baseDmg);
                int healed = Enemy.Heal(d);
                if (healed > 0) Emit(new BattleEvent(BattleEventKind.EnemyHealed, $"{Enemy.Name} は HP を {healed} 吸い取った", healed));
                break;
            }
            case EnemySkill.Wind:
                Say($"{Enemy.Name} のカマイタチ！", Cue.Swing, LedColor.Green, OledAnim.Damage);
                for (int i = 0; i < 2 && !Hero.IsDead; i++)
                {
                    if (!HeroDodges()) HurtHero((int)Math.Round(baseDmg * 0.6));
                }
                break;
            case EnemySkill.Glare:
            {
                var o = outcome ?? ChallengeOutcome.Fail();
                if (o.Success)
                {
                    Say("気配を消してやり過ごした！", Cue.Success, LedColor.Blue);
                }
                else
                {
                    Say("心音を聞かれた！！", Cue.BigDamage, LedColor.Red, OledAnim.Damage);
                    HurtHero((int)Math.Round(Hero.MaxHp * (boss ? 0.6 : 0.45) * DifficultyRules.EnemyAttack(Run.Difficulty)) + (baseDmg / 2), big: true);
                }
                break;
            }
            case EnemySkill.Blind:
            {
                var o = outcome ?? ChallengeOutcome.Fail();
                if (o.Success)
                {
                    Say("心眼で見切った！（ダメージ無効）", Cue.Success, LedColor.White);
                }
                else
                {
                    Say("見切れなかった… 直撃！", Cue.BigDamage, LedColor.Red, OledAnim.Damage);
                    HurtHero((int)Math.Round(baseDmg * 1.5), big: true);
                }
                break;
            }
            case EnemySkill.Iai:
            {
                var o = outcome ?? ChallengeOutcome.Fail();
                if (o.Success)
                {
                    Say("見切った！！ カウンター一閃！", Cue.Critical, LedColor.White, OledAnim.Attack);
                    double atk = Hero.Attack * 2.5 * (Hero.Talent == Talent.Power ? 1.2 : 1.0);
                    HitEnemy(Damage(atk, 0), true, Element.None);
                    if (Outcome != BattleOutcome.Continue) return;
                }
                else
                {
                    Say("斬り捨てられた！！", Cue.BigDamage, LedColor.Red, OledAnim.Damage);
                    HurtHero((int)Math.Round(baseDmg * 2.3), big: true);
                }
                break;
            }
        }

        if (Hero.IsDead)
        {
            Outcome = BattleOutcome.HeroDown;
            return;
        }
        EndRound();
    }

    /// <summary>1 往復の終わり（場所の効果）。</summary>
    private void EndRound()
    {
        Turn++;
        int pct = Math.Max(1, Hero.MaxHp * 3 / 100);
        switch (Biome.Effect)
        {
            case BiomeEffect.Burn:
                HurtHero(pct, quiet: true);
                Say($"火山の熱で {pct} のダメージ", Cue.None, LedColor.Red);
                break;
            case BiomeEffect.Poison:
                HurtHero(pct, quiet: true);
                Say($"毒気で {pct} のダメージ", Cue.None, LedColor.Purple);
                break;
            case BiomeEffect.Sanctuary:
                if (Hero.Hp < Hero.MaxHp)
                {
                    int h = Hero.HealHp(pct);
                    Say($"聖なる光で HP が {h} 回復した", Cue.None, LedColor.White);
                    Emit(new BattleEvent(BattleEventKind.HeroHealed, "", h));
                }
                break;
            case BiomeEffect.Void when Turn % 2 == 0:
                HurtHero(pct, quiet: true);
                Say($"虚無が体を削る… {pct} のダメージ");
                break;
        }
        if (Hero.IsDead) Outcome = BattleOutcome.HeroDown;
    }

    // ------------------------------------------------------------------
    // 勝利・蘇生
    // ------------------------------------------------------------------

    /// <summary>勝ったとき（和解・威圧も含む）の報酬を受け取る。</summary>
    public VictoryReward ClaimReward()
    {
        double expMul = Biome.Effect == BiomeEffect.Blessing ? 1.2 : 1.0;
        int exp = Outcome switch
        {
            BattleOutcome.Victory => (int)Math.Round(Enemy.ExpReward * expMul),
            BattleOutcome.Peace => (int)Math.Round(Enemy.ExpReward * expMul * 0.7),
            _ => 0,
        };
        var drops = new List<Item>();
        var lost = new List<Item>();
        double dropChance = Hero.Talent == Talent.Fortune ? 0.6 : 0.45;
        if (Outcome is BattleOutcome.Victory or BattleOutcome.Intimidated)
        {
            int count = Enemy.IsBoss ? 2 : Rng.Chance(dropChance) ? 1 : 0;
            if (Outcome == BattleOutcome.Intimidated) count = Math.Max(count, 1);
            for (int i = 0; i < count; i++)
            {
                var item = ItemGenerator.Generate(Rng, Floor, Enemy.IsBoss ? 1.5 : Hero.Talent == Talent.Fortune ? 0.6 : 0);
                if (Hero.Inventory.Add(item)) drops.Add(item);
                else lost.Add(item);
            }
        }
        if (Outcome is BattleOutcome.Victory or BattleOutcome.Peace or BattleOutcome.Intimidated) Run.BattlesWon++;
        var ups = Hero.GainExp(exp, Rng);
        Hero.IsBerserk = false;
        Hero.ChargeMultiplier = 1.0;
        return new VictoryReward(exp, ups, drops, lost);
    }

    /// <summary>戦いが終わったとき（勝ち・逃げ・逃げられた すべて）に、戦い中だけの状態を戻す。</summary>
    public void EndBattle()
    {
        Hero.IsBerserk = false;
        Hero.ChargeMultiplier = 1.0;
    }

    /// <summary>蘇生の儀式に挑めるか。</summary>
    public bool CanRevive => Hero.ReviveCharges > 0;

    /// <summary>蘇生で求める連打の回数。使うたびに増える。</summary>
    public int ReviveTarget => 35 + (10 * Hero.RevivesUsed);

    public bool ResolveRevive(ChallengeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        Hero.ReviveCharges = Math.Max(0, Hero.ReviveCharges - 1);
        Hero.RevivesUsed++;
        if (!outcome.Success) return false;
        Hero.Hp = Math.Max(1, Hero.MaxHp * 3 / 10);
        Hero.Condition = Condition.Normal;
        Outcome = BattleOutcome.Continue;
        Say("奇跡的に息を吹き返した！", Cue.Revive, LedColor.White);
        Emit(new BattleEvent(BattleEventKind.HeroHealed, "", Hero.Hp));
        return true;
    }

    // ------------------------------------------------------------------
    // 計算
    // ------------------------------------------------------------------

    public double CritChance => 5 + (Hero.Talent == Talent.Fortune ? 7 : 0);

    public double HeroEvasion
    {
        get
        {
            double e = 4 + ((Hero.Speed - Enemy.Speed) * 0.6);
            if (Hero.Talent == Talent.Speed) e += 6;
            if (Biome.Effect == BiomeEffect.Slow) e -= 4;
            return Math.Clamp(e, 0, 25);
        }
    }

    private bool HeroMisses()
    {
        double miss = Biome.Effect switch { BiomeEffect.Miss => 12, BiomeEffect.Void => 5, _ => 2 };
        return Rng.Percent(miss);
    }

    private bool HeroDodges()
    {
        if (!Rng.Percent(HeroEvasion)) return false;
        Emit(new BattleEvent(BattleEventKind.Dodge, $"{Hero.Name} はひらりとかわした！", 0, Cue.Miss));
        return true;
    }

    private int Damage(double attack, int defense)
    {
        double v = attack * Rng.Range(0.9, 1.1) * 100.0 / (100.0 + Math.Max(0, defense));
        return Math.Max(1, (int)Math.Round(v));
    }

    private int MagicDamage(double power, Element element)
    {
        double v = power * Rng.Range(0.92, 1.08) * Affinity(element) * 100.0 / (100.0 + (Enemy.Defense / 2.0));
        return Math.Max(1, (int)Math.Round(v));
    }

    public double Affinity(Element e)
    {
        if (e == Element.None) return 1.0;
        if (e == Biome.WeakTo) return 1.5;
        if (e == Biome.Resists) return 0.5;
        return 1.0;
    }

    public double WeatherFactor(Element e, bool announce)
    {
        double f = (Run.Weather, e) switch
        {
            (Weather.Clear, Element.Fire) => 1.5,
            (Weather.Rain, Element.Ice) => 1.5,
            (Weather.Rain, Element.Fire) => 0.5,
            (Weather.Clouds, Element.Thunder) => 1.3,
            _ => 1.0,
        };
        if (announce && f > 1) Say($"{Names.Of(Run.Weather)}の空が{Names.Of(e)}の魔力を高める！（威力 {f:0.#} 倍）");
        if (announce && f < 1) Say("雨で炎がかき消された…（威力半減）");
        return f;
    }

    private void HitEnemy(int damage, bool crit, Element element)
    {
        int dealt = Enemy.TakeDamage(Math.Min(damage, 9999));
        double aff = Affinity(element);
        Emit(new BattleEvent(BattleEventKind.EnemyDamaged, $"{Enemy.Name} に {dealt} のダメージ！", dealt, crit ? Cue.Critical : Cue.Hit, Critical: crit, Element: element));
        if (aff > 1) Say("効果は ばつぐんだ！");
        else if (aff < 1) Say("あまり効いていないようだ…");
        if (Enemy.IsDead)
        {
            Outcome = BattleOutcome.Victory;
            Say(Enemy.IsFinalBoss ? $"{Enemy.Name} の体が、光とともに崩れ去っていく…" : $"{Enemy.Name} を倒した！", Cue.Victory, LedColor.White, OledAnim.Win);
        }
    }

    private int HurtHero(int damage, bool big = false, bool quiet = false)
    {
        int d = Hero.TakeDamage(damage);
        if (!quiet)
        {
            Emit(new BattleEvent(BattleEventKind.HeroDamaged, $"{d} のダメージを受けた！", d, big ? Cue.BigDamage : Cue.Damage, LedColor.Red, OledAnim.Damage));
            if (big || d >= Hero.MaxHp / 5) Emit(new BattleEvent(BattleEventKind.Shake, "", big ? 2 : 1));
        }
        else
        {
            Emit(new BattleEvent(BattleEventKind.HeroDamaged, "", d));
        }
        return d;
    }
}
