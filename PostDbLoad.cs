using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

namespace QuestConditionOverhaulFinal;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 100)]
public class PostDbLoad(
    DatabaseServer databaseServer,
    JsonUtil jsonUtil,
    FileUtil fileUtil,
    ModHelper modHelper,
    ISptLogger<PostDbLoad> logger
) : IOnLoad
{
    private static readonly string[] UsecDogtagTemplateIds =
    [
        "59f32c3b86f77472a31742f0",
        "6662ea05f6259762c56f3189",
        "6662e9f37fa79a6d83730fa0",
        "6764207f2fa5e32733055c4a",
        "6764202ae307804338014c1a",
        "68418091b5b0c9e4c60f0e7a"
    ];

    private static readonly string[] BearDogtagTemplateIds =
    [
        "59f32bb586f774757e1e8442",
        "6662e9cda7e0b43baa3d5f76",
        "6662e9aca7e0b43baa3d5f74",
        "684181208d035f60230f63f9",
        "684180bc51bf8645f7067bc8",
        "675dcb0545b1a2d108011b2b",
        "675dc9d37ae1a8792107ca96"
    ];

    private const string RoublesTemplateId = "5449016a4bdc2d6f028b456f";
    private const string EurosTemplateId = "569668774bdc2da2298b4568";
    private const string DollarsTemplateId = "5696686a4bdc2da3298b456a";

    private readonly string _modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

    public async Task OnLoad()
    {
        var configPath = System.IO.Path.Combine(_modPath, "config.jsonc");
        var raw = await fileUtil.ReadFileAsync(configPath);
        var config = jsonUtil.Deserialize<OverhaulConfig>(raw) ?? new OverhaulConfig();

        if (!config.Enabled)
        {
            logger.Info("[kazusa-QuestConditionOverhaul] Disabled by config.");
            return;
        }

        dynamic tables = databaseServer.GetTables();
        List<Quest> quests = ((IEnumerable<Quest>)tables.Templates.Quests.Values).ToList();
        dynamic locales = tables.Locales.Global;

        var stats = new RewriteStats(0, 0, 0, 0, 0, 0, 0, 0, 0);
        var traderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var conditionPlans = new Dictionary<string, GeneratedConditionPlan>(StringComparer.OrdinalIgnoreCase);

        foreach (Quest quest in quests)
        {
            string questId = quest.Id.ToString();
            string traderId = quest.TraderId.ToString();

            if (!ShouldProcessQuest(questId, traderId, config))
            {
                continue;
            }

            GeneratedConditionPlan plan = BuildPlan(questId, config);
            quest.Type = plan.QuestType;

            if (config.SetLocationToAny)
            {
                quest.Location = "any";
            }

            QuestCondition finishCondition = CreateFinishCondition(questId, plan, config);
            quest.Conditions.AvailableForFinish = new List<QuestCondition> { finishCondition };
            conditionPlans[finishCondition.Id.ToString()] = plan;

            stats = Accumulate(stats, plan);
            traderCounts[traderId] = traderCounts.TryGetValue(traderId, out int current) ? current + 1 : 1;
        }

        // Register a transformer for each locale to apply all additions at once
        var localesDict = (Dictionary<string, LazyLoad<Dictionary<string, string>>>)locales;
        foreach (var kvp in localesDict)
        {
            string langCode = kvp.Key;
            LazyLoad<Dictionary<string, string>> lazyLoad = kvp.Value;
            lazyLoad.AddTransformer(new Func<Dictionary<string, string>?, Dictionary<string, string>?>(dict =>
            {
                if (dict == null) return null;
                foreach (var pair in conditionPlans)
                {
                    dict[pair.Key] = BuildConditionLocaleText(pair.Value, langCode, config);
                }
                return dict;
            }));
        }

        logger.Success($"[kazusa-QuestConditionOverhaul] Finish-only generated pass completed. Replaced AvailableForFinish on {stats.ProcessedQuests} quests.");
    }

    private static bool ShouldProcessQuest(string questId, string traderId, OverhaulConfig config)
    {
        if (config.ExcludedQuestIds.Contains(questId))
        {
            return false;
        }

        if (config.ExcludedTraderIds.Contains(traderId))
        {
            return false;
        }

        return config.ProcessAllQuests || config.TargetTraderIds.Contains(traderId);
    }

    private static GeneratedConditionPlan BuildPlan(string questId, OverhaulConfig config)
    {
        int roll = GetDeterministicPercent(questId, config.Seed, "category");
        int totalWeight = Math.Max(1, config.Weights.Kills + config.Weights.Dogtags + config.Weights.Money);
        int killThreshold = config.Weights.Kills * 100 / totalWeight;
        int dogtagThreshold = (config.Weights.Kills + config.Weights.Dogtags) * 100 / totalWeight;

        if (roll < killThreshold)
        {
            bool pmc = GetDeterministicPercent(questId, config.Seed, "killTarget") < config.Kills.PmcChance;
            int count = GetSteppedRange(questId, config.Seed, "killCount", config.Kills.MinCount, config.Kills.MaxCount, 1);

            if (pmc)
            {
                if (config.Kills.DistinguishFactions)
                {
                    bool usec = GetDeterministicPercent(questId, config.Seed, "killFaction") < config.Kills.UsecChance;
                    return new GeneratedConditionPlan(GeneratedConditionKind.Kill, count, QuestTypeEnum.Elimination, usec ? "Usec" : "Bear");
                }
                else
                {
                    return new GeneratedConditionPlan(GeneratedConditionKind.Kill, count, QuestTypeEnum.Elimination, "AnyPmc");
                }
            }
            else
            {
                return new GeneratedConditionPlan(GeneratedConditionKind.Kill, count, QuestTypeEnum.Elimination, "Savage");
            }
        }

        if (roll < dogtagThreshold)
        {
            int count = GetSteppedRange(questId, config.Seed, "dogtagCount", config.Dogtags.MinCount, config.Dogtags.MaxCount, 1);
            if (config.Dogtags.DistinguishFactions)
            {
                bool usec = GetDeterministicPercent(questId, config.Seed, "dogtagFaction") < config.Dogtags.UsecChance;
                return new GeneratedConditionPlan(GeneratedConditionKind.Dogtag, count, QuestTypeEnum.PickUp, null, usec ? DogtagFaction.Usec : DogtagFaction.Bear);
            }
            else
            {
                return new GeneratedConditionPlan(GeneratedConditionKind.Dogtag, count, QuestTypeEnum.PickUp, null, null);
            }
        }

        int moneyRoll = GetDeterministicPercent(questId, config.Seed, "moneyType");
        int totalCurrencyWeight = Math.Max(1, config.Money.RoublesChance + config.Money.EurosChance + config.Money.DollarsChance);
        int roublesThreshold = config.Money.RoublesChance * 100 / totalCurrencyWeight;
        int eurosThreshold = (config.Money.RoublesChance + config.Money.EurosChance) * 100 / totalCurrencyWeight;

        CurrencyKind currency = moneyRoll < roublesThreshold
            ? CurrencyKind.Roubles
            : moneyRoll < eurosThreshold
                ? CurrencyKind.Euros
                : CurrencyKind.Dollars;

        int amount = currency switch
        {
            CurrencyKind.Roubles => GetSteppedRange(questId, config.Seed, "roublesCount", config.Money.RoublesMin, config.Money.RoublesMax, config.Money.RoublesStep),
            CurrencyKind.Euros => GetSteppedRange(questId, config.Seed, "eurosCount", config.Money.EurosMin, config.Money.EurosMax, config.Money.EurosStep),
            _ => GetSteppedRange(questId, config.Seed, "dollarsCount", config.Money.DollarsMin, config.Money.DollarsMax, config.Money.DollarsStep)
        };

        return new GeneratedConditionPlan(GeneratedConditionKind.Money, amount, QuestTypeEnum.PickUp, null, null, currency);
    }

    private static QuestCondition CreateFinishCondition(string questId, GeneratedConditionPlan plan, OverhaulConfig config)
    {
        return plan.Kind switch
        {
            GeneratedConditionKind.Kill => CreateKillCondition(questId, plan),
            GeneratedConditionKind.Dogtag => CreateDogtagCondition(questId, plan, config),
            _ => CreateMoneyCondition(questId, plan)
        };
    }

    private static QuestCondition CreateKillCondition(string questId, GeneratedConditionPlan plan)
    {
        return new QuestCondition
        {
            CompleteInSeconds = 0,
            ConditionType = "CounterCreator",
            Counter = new QuestConditionCounter
            {
                Id = GenerateId(questId, "kill-counter"),
                Conditions =
                [
                    new QuestConditionCounterCondition
                    {
                        Id = new MongoId(GenerateId(questId, "kill-inner")),
                        CompareMethod = ">=",
                        ConditionType = "Kills",
                        ResetOnSessionEnd = false,
                        Target = new ListOrT<string>(null!, plan.KillTarget!),
                        Value = 1,
                        BodyPart = [],
                        Daytime = new DaytimeCounter { From = 0, To = 0 },
                        Distance = new CounterConditionDistance { CompareMethod = ">=", Value = 0 },
                        DynamicLocale = false,
                        EnemyEquipmentExclusive = [],
                        EnemyEquipmentInclusive = [],
                        EnemyHealthEffects = [],
                        SavageRole = [],
                        Weapon = [],
                        WeaponCaliber = [],
                        WeaponModsExclusive = [],
                        WeaponModsInclusive = []
                    }
                ]
            },
            DoNotResetIfCounterCompleted = false,
            DynamicLocale = false,
            GlobalQuestCounterId = string.Empty,
            Id = new MongoId(GenerateId(questId, "finish-kill")),
            Index = 0,
            IsNecessary = true,
            IsResetOnConditionFailed = false,
            OneSessionOnly = false,
            ParentId = string.Empty,
            Type = "Elimination",
            Value = plan.Count,
            VisibilityConditions = []
        };
    }

    private static QuestCondition CreateDogtagCondition(string questId, GeneratedConditionPlan plan, OverhaulConfig config)
    {
        return new QuestCondition
        {
            ConditionType = "HandoverItem",
            DogtagLevel = 0,
            DynamicLocale = false,
            GlobalQuestCounterId = string.Empty,
            Id = new MongoId(GenerateId(questId, "finish-dogtag")),
            Index = 0,
            IsEncoded = false,
            MaxDurability = 100,
            MinDurability = 0,
            OnlyFoundInRaid = config.Dogtags.OnlyFoundInRaid,
            ParentId = string.Empty,
            Target = new ListOrT<string>(
                (plan.DogtagFaction == null
                    ? CombinedDogtagTemplateIds(config)
                    : (plan.DogtagFaction == DogtagFaction.Usec 
                        ? (config.Dogtags.UsecTemplateIds.Count > 0 ? config.Dogtags.UsecTemplateIds : UsecDogtagTemplateIds.ToList())
                        : (config.Dogtags.BearTemplateIds.Count > 0 ? config.Dogtags.BearTemplateIds : BearDogtagTemplateIds.ToList()))),
                null!),
            Value = plan.Count,
            VisibilityConditions = []
        };
    }

    private static List<string> CombinedDogtagTemplateIds(OverhaulConfig config)
    {
        var usecList = config.Dogtags.UsecTemplateIds.Count > 0 ? config.Dogtags.UsecTemplateIds : UsecDogtagTemplateIds.ToList();
        var bearList = config.Dogtags.BearTemplateIds.Count > 0 ? config.Dogtags.BearTemplateIds : BearDogtagTemplateIds.ToList();
        return usecList.Concat(bearList).ToList();
    }

    private static QuestCondition CreateMoneyCondition(string questId, GeneratedConditionPlan plan)
    {
        string currencyTemplateId = plan.Currency switch
        {
            CurrencyKind.Roubles => RoublesTemplateId,
            CurrencyKind.Euros => EurosTemplateId,
            _ => DollarsTemplateId
        };

        return new QuestCondition
        {
            ConditionType = "HandoverItem",
            DogtagLevel = 0,
            DynamicLocale = false,
            GlobalQuestCounterId = string.Empty,
            Id = new MongoId(GenerateId(questId, "finish-money")),
            Index = 0,
            IsEncoded = false,
            MaxDurability = 100,
            MinDurability = 0,
            OnlyFoundInRaid = false,
            ParentId = string.Empty,
            Target = new ListOrT<string>(new List<string> { currencyTemplateId }, null!),
            Value = plan.Count,
            VisibilityConditions = []
        };
    }

    private static string BuildConditionLocaleText(GeneratedConditionPlan plan, string langCode, OverhaulConfig config)
    {
        if (config.Locales.TryGetValue(langCode, out var templates))
        {
            return FormatTemplate(plan, templates);
        }

        if (config.Locales.TryGetValue("en", out var enTemplates))
        {
            return FormatTemplate(plan, enTemplates);
        }

        return FormatTemplate(plan, GetHardcodedDefaultEnglishTemplates());
    }

    private static string FormatTemplate(GeneratedConditionPlan plan, LocaleTemplates templates)
    {
        if (plan.Kind == GeneratedConditionKind.Kill)
        {
            string targetKey = plan.KillTarget ?? "AnyPmc";
            string targetName = templates.Targets.TryGetValue(targetKey, out var val) ? val : targetKey;
            return templates.Kill
                .Replace("{count}", plan.Count.ToString())
                .Replace("{target}", targetName);
        }

        if (plan.Kind == GeneratedConditionKind.Dogtag)
        {
            string factionKey = plan.DogtagFaction?.ToString() ?? "AnyPmc";
            string factionName = templates.Factions.TryGetValue(factionKey, out var val) ? val : factionKey;
            return templates.Dogtag
                .Replace("{count}", plan.Count.ToString())
                .Replace("{faction}", factionName);
        }

        string currencyTemplate = plan.Currency switch
        {
            CurrencyKind.Roubles => templates.Roubles,
            CurrencyKind.Euros => templates.Euros,
            _ => templates.Dollars
        };

        return currencyTemplate.Replace("{count}", plan.Count.ToString("N0"));
    }

    private static LocaleTemplates GetHardcodedDefaultEnglishTemplates()
    {
        return new LocaleTemplates
        {
            Kill = "Eliminate {count} {target}",
            Dogtag = "Hand over {count} {faction} dogtags",
            Roubles = "Hand over {count} Roubles",
            Euros = "Hand over {count} Euros",
            Dollars = "Hand over {count} Dollars",
            Targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Usec", "USEC" },
                { "Bear", "BEAR" },
                { "Savage", "SCAV" },
                { "AnyPmc", "PMC" }
            },
            Factions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Usec", "USEC" },
                { "Bear", "BEAR" },
                { "AnyPmc", "PMC" }
            }
        };
    }

    private static RewriteStats Accumulate(RewriteStats stats, GeneratedConditionPlan plan)
    {
        return plan.Kind switch
        {
            GeneratedConditionKind.Kill when plan.KillTarget == "Usec" => stats with { ProcessedQuests = stats.ProcessedQuests + 1, KillUsec = stats.KillUsec + 1 },
            GeneratedConditionKind.Kill when plan.KillTarget == "Bear" => stats with { ProcessedQuests = stats.ProcessedQuests + 1, KillBear = stats.KillBear + 1 },
            GeneratedConditionKind.Kill when plan.KillTarget == "Savage" => stats with { ProcessedQuests = stats.ProcessedQuests + 1, KillScav = stats.KillScav + 1 },
            GeneratedConditionKind.Kill => stats with { ProcessedQuests = stats.ProcessedQuests + 1, KillUsec = stats.KillUsec + 1, KillBear = stats.KillBear + 1 },
            GeneratedConditionKind.Dogtag when plan.DogtagFaction == DogtagFaction.Usec => stats with { ProcessedQuests = stats.ProcessedQuests + 1, DogtagUsec = stats.DogtagUsec + 1 },
            GeneratedConditionKind.Dogtag when plan.DogtagFaction == DogtagFaction.Bear => stats with { ProcessedQuests = stats.ProcessedQuests + 1, DogtagBear = stats.DogtagBear + 1 },
            GeneratedConditionKind.Dogtag => stats with { ProcessedQuests = stats.ProcessedQuests + 1, DogtagUsec = stats.DogtagUsec + 1, DogtagBear = stats.DogtagBear + 1 },
            GeneratedConditionKind.Money when plan.Currency == CurrencyKind.Roubles => stats with { ProcessedQuests = stats.ProcessedQuests + 1, MoneyRoubles = stats.MoneyRoubles + 1 },
            GeneratedConditionKind.Money when plan.Currency == CurrencyKind.Euros => stats with { ProcessedQuests = stats.ProcessedQuests + 1, MoneyEuros = stats.MoneyEuros + 1 },
            _ => stats with { ProcessedQuests = stats.ProcessedQuests + 1, MoneyDollars = stats.MoneyDollars + 1 }
        };
    }

    private static int GetDeterministicPercent(string questId, string seed, string salt)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{questId}:{salt}"));
        return hash[0] % 100;
    }

    private static int GetSteppedRange(string questId, string seed, string salt, int min, int max, int step)
    {
        if (max <= min)
        {
            return min;
        }

        step = Math.Max(1, step);
        int span = ((max - min) / step) + 1;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{questId}:{salt}"));
        int value = BitConverter.ToInt32(hash, 0) & int.MaxValue;
        return min + ((value % span) * step);
    }

    private static string GenerateId(string questId, string salt)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes($"{questId}:{salt}"));
        return Convert.ToHexString(hash).ToLowerInvariant()[..24];
    }
}
