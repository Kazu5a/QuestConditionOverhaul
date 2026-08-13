using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

namespace QuestConditionOverhaulFinal;

[Injectable(TypePriority = int.MaxValue)]
public class PostDbLoad(
    TemplateTable templateTable,
    LocaleTable localeTable,
    LocaleService localeService,
    JsonUtil jsonUtil,
    FileUtil fileUtil,
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

    // location key -> quest-level location MongoId (from each map's base.json _Id)
    private static readonly Dictionary<string, string> QuestLocationIds = new(StringComparer.OrdinalIgnoreCase)
    {
        { "bigmap", "56f40101d2720b2a4d8b45d6" },
        { "factory", "55f2d3fd4bdc2d5f408b4567" },
        { "interchange", "5714dbc024597771384a510d" },
        { "laboratory", "5b0fc42d86f7744a585f9105" },
        { "lighthouse", "5704e4dad2720bb55b8b4567" },
        { "rezervbase", "5704e5fad2720bc05b8b4567" },
        { "shoreline", "5704e554d2720bac5b8b456e" },
        { "tarkovstreets", "5714dc692459777137212e12" },
        { "woods", "5704e3c2d2720bac5b8b4567" },
        { "sandbox", "653e6760052c01c1c805532f" },
        { "labyrinth", "6733700029c367a3d40b02af" }
    };

    private readonly string _modPath = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var configPath = System.IO.Path.Combine(_modPath, "config.jsonc");
        var raw = await fileUtil.ReadFileAsync(configPath, cancellationToken);
        var config = jsonUtil.Deserialize<OverhaulConfig>(raw) ?? new OverhaulConfig();

        if (!config.Enabled)
        {
            logger.Info("[kazusa-QuestConditionOverhaul] Disabled by config.");
            return;
        }

        VerifyHardcodedItemIds();

        List<Quest> quests = templateTable.Quests.Values.ToList();

        var stats = new RewriteStats(0, 0, 0, 0, 0, 0, 0, 0, 0);
        var traderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var conditionPlans = new Dictionary<string, GeneratedConditionPlan>(StringComparer.OrdinalIgnoreCase);

        var targetTraderIdsCaseInsensitive = new Dictionary<string, bool>(config.TargetTraderIds, StringComparer.OrdinalIgnoreCase);
        var excludedTraderIdsCaseInsensitive = new Dictionary<string, bool>(config.ExcludedTraderIds, StringComparer.OrdinalIgnoreCase);

        foreach (Quest quest in quests)
        {
            string questId = quest.Id.ToString();
            string traderId = quest.TraderId.ToString();

            if (!ShouldProcessQuest(questId, traderId, config, targetTraderIdsCaseInsensitive, excludedTraderIdsCaseInsensitive))
            {
                continue;
            }

            GeneratedConditionPlan plan = BuildPlan(questId, config);
            quest.Type = plan.QuestType;
            quest.Location = GetQuestLocation(plan, config);

            QuestCondition finishCondition = CreateFinishCondition(questId, plan, config);
            quest.Conditions.AvailableForFinish = new List<QuestCondition> { finishCondition };
            conditionPlans[finishCondition.Id.ToString()] = plan;

            stats = Accumulate(stats, plan);
            traderCounts[traderId] = traderCounts.TryGetValue(traderId, out int current) ? current + 1 : 1;
        }

        // Register a transformer for each locale to apply all additions at once
        foreach (var kvp in localeTable.Global)
        {
            string langCode = kvp.Key;
            LazyLoad<GlobalLocaleDictionary> lazyLoad = kvp.Value;
            lazyLoad.AddTransformer(dict =>
            {
                if (dict == null) return null;
                foreach (var pair in conditionPlans)
                {
                    dict[pair.Key] = BuildConditionLocaleText(pair.Value, langCode, config);
                }
                return dict;
            });
        }

        logger.Success($"[kazusa-QuestConditionOverhaul] Finish-only generated pass completed. Replaced AvailableForFinish on {stats.ProcessedQuests} quests.");

        // Check if there are any new trader IDs to append to the config file
        var newTraderIds = new List<string>();
        foreach (var traderId in traderCounts.Keys)
        {
            if (!targetTraderIdsCaseInsensitive.ContainsKey(traderId))
            {
                newTraderIds.Add(traderId);
            }
        }

        if (newTraderIds.Count > 0)
        {
            try
            {
                string updatedText = InsertNewTraderIds(raw, "targetTraderIds", newTraderIds, localeService);
                updatedText = InsertNewTraderIds(updatedText, "excludedTraderIds", newTraderIds, localeService);

                await fileUtil.WriteFileAsync(configPath, updatedText, cancellationToken);
                logger.Info($"[kazusa-QuestConditionOverhaul] Dynamically added {newTraderIds.Count} new modded trader IDs to config.jsonc.");
            }
            catch (Exception ex)
            {
                logger.Error($"[kazusa-QuestConditionOverhaul] Failed to write back config.jsonc: {ex.Message}");
            }
        }
    }

    private void VerifyHardcodedItemIds()
    {
        var missingIds = new List<string>();
        missingIds.AddRange(UsecDogtagTemplateIds.Where(id => !templateTable.Items.ContainsKey(new MongoId(id))));
        missingIds.AddRange(BearDogtagTemplateIds.Where(id => !templateTable.Items.ContainsKey(new MongoId(id))));

        if (!templateTable.Items.ContainsKey(new MongoId(RoublesTemplateId)))
        {
            missingIds.Add(RoublesTemplateId);
        }

        if (!templateTable.Items.ContainsKey(new MongoId(EurosTemplateId)))
        {
            missingIds.Add(EurosTemplateId);
        }

        if (!templateTable.Items.ContainsKey(new MongoId(DollarsTemplateId)))
        {
            missingIds.Add(DollarsTemplateId);
        }

        if (missingIds.Count > 0)
        {
            logger.Warning(
                $"[kazusa-QuestConditionOverhaul] The following hardcoded item IDs are NOT present in this SPT 4.1 database build, some quests may be uncompletable: {string.Join(", ", missingIds)}"
            );
        }
    }

    private static bool ShouldProcessQuest(string questId, string traderId, OverhaulConfig config, Dictionary<string, bool> targetTraders, Dictionary<string, bool> excludedTraders)
    {
        if (config.ExcludedQuestIds.Contains(questId))
        {
            return false;
        }

        if (excludedTraders.TryGetValue(traderId, out bool isExcluded) && isExcluded)
        {
            return false;
        }

        if (config.ProcessAllQuests)
        {
            return true;
        }

        return targetTraders.TryGetValue(traderId, out bool isTarget) && isTarget;
    }

    private static string GetTraderNickname(string traderId, LocaleService localeService)
    {
        foreach (string lang in new[] { "en", "ch" })
        {
            Dictionary<string, string> locale = localeService.GetLocaleDb(lang);
            string key = $"{traderId} Nickname";
            if (locale.TryGetValue(key, out string? nickname) && !string.IsNullOrWhiteSpace(nickname))
            {
                return nickname;
            }
        }

        return traderId;
    }

    private static string InsertNewTraderIds(string jsoncText, string blockName, List<string> newTraderIds, LocaleService localeService)
    {
        string lineEnding = jsoncText.Contains("\r\n") ? "\r\n" : "\n";
        string[] lines = jsoncText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var outputLines = new List<string>();
        bool insideBlock = false;
        var blockEntries = new List<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            outputLines.Add(line);

            if (line.Contains($"\"{blockName}\":"))
            {
                insideBlock = true;
                continue;
            }

            if (insideBlock)
            {
                if (line.Trim().StartsWith("}"))
                {
                    var idsToAdd = new List<string>();
                    foreach (var id in newTraderIds)
                    {
                        bool alreadyExists = false;
                        foreach (int idx in blockEntries)
                        {
                            if (outputLines[idx].Contains($"\"{id}\""))
                            {
                                alreadyExists = true;
                                break;
                            }
                        }
                        if (!alreadyExists)
                        {
                            idsToAdd.Add(id);
                        }
                    }

                    if (idsToAdd.Count > 0)
                    {
                        if (blockEntries.Count > 0)
                        {
                            int lastIdx = blockEntries[blockEntries.Count - 1];
                            string lastLine = outputLines[lastIdx];
                            int commentIndex = lastLine.IndexOf("//");
                            string codePart = commentIndex != -1 ? lastLine.Substring(0, commentIndex) : lastLine;
                            string commentPart = commentIndex != -1 ? lastLine.Substring(commentIndex) : "";

                            if (!codePart.TrimEnd().EndsWith(","))
                            {
                                string trimmedCode = codePart.TrimEnd();
                                outputLines[lastIdx] = trimmedCode + "," + (commentPart.Length > 0 ? " " + commentPart : "");
                            }
                        }

                        for (int k = 0; k < idsToAdd.Count; k++)
                        {
                            string id = idsToAdd[k];
                            string nickname = GetTraderNickname(id, localeService);
                            bool isLast = (k == idsToAdd.Count - 1);
                            string comma = isLast ? "" : ",";
                            outputLines.Insert(outputLines.Count - 1, $"    \"{id}\": false{comma} // {nickname}");
                        }
                    }

                    insideBlock = false;
                    blockEntries.Clear();
                }
                else if (line.Contains(":"))
                {
                    blockEntries.Add(outputLines.Count - 1);
                }
            }
        }

        return string.Join(lineEnding, outputLines);
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
            string? location = config.Kills.DistinguishLocation ? PickWeightedLocation(questId, config) : null;

            if (pmc)
            {
                if (config.Kills.DistinguishFactions)
                {
                    bool usec = GetDeterministicPercent(questId, config.Seed, "killFaction") < config.Kills.UsecChance;
                    return new GeneratedConditionPlan(GeneratedConditionKind.Kill, count, QuestTypeEnum.Elimination, usec ? "Usec" : "Bear", null, null, location);
                }
                else
                {
                    return new GeneratedConditionPlan(GeneratedConditionKind.Kill, count, QuestTypeEnum.Elimination, "AnyPmc", null, null, location);
                }
            }
            else
            {
                return new GeneratedConditionPlan(GeneratedConditionKind.Kill, count, QuestTypeEnum.Elimination, "Savage", null, null, location);
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
        var counterConditions = new List<QuestConditionCounterCondition>
        {
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
        };

        if (!string.IsNullOrEmpty(plan.Location))
        {
            counterConditions.Add(
                new QuestConditionCounterCondition
                {
                    Id = new MongoId(GenerateId(questId, "kill-location")),
                    ConditionType = "Location",
                    DynamicLocale = false,
                    Target = new ListOrT<string>(GetLocationTargetIds(plan.Location), null!)
                }
            );
        }

        return new QuestCondition
        {
            CompleteInSeconds = 0,
            ConditionType = "CounterCreator",
            Counter = new QuestConditionCounter
            {
                Id = GenerateId(questId, "kill-counter"),
                Conditions = counterConditions
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
            string mapName = GetMapName(plan, templates);
            return templates.Kill
                .Replace("{count}", plan.Count.ToString())
                .Replace("{target}", targetName)
                .Replace("{map}", mapName);
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

    private static string GetMapName(GeneratedConditionPlan plan, LocaleTemplates templates)
    {
        if (string.IsNullOrEmpty(plan.Location))
        {
            return templates.Maps.TryGetValue("any", out var anyVal) ? anyVal : "any map";
        }

        return templates.Maps.TryGetValue(plan.Location, out var val) ? val : plan.Location;
    }

    private static LocaleTemplates GetHardcodedDefaultEnglishTemplates()
    {
        return new LocaleTemplates
        {
            Kill = "Eliminate {count} {target} on {map}",
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
            },
            Maps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "any", "any map" },
                { "bigmap", "Customs" },
                { "factory", "Factory" },
                { "interchange", "Interchange" },
                { "laboratory", "The Lab" },
                { "lighthouse", "Lighthouse" },
                { "rezervbase", "Reserve" },
                { "shoreline", "Shoreline" },
                { "tarkovstreets", "Streets of Tarkov" },
                { "woods", "Woods" },
                { "sandbox", "Ground Zero" },
                { "labyrinth", "The Labyrinth" }
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

    private static int GetDeterministicInt(string questId, string seed, string salt, int maxExclusive)
    {
        if (maxExclusive <= 1)
        {
            return 0;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{questId}:{salt}"));
        int value = BitConverter.ToInt32(hash, 0) & int.MaxValue;
        return value % maxExclusive;
    }

    private static string? PickWeightedLocation(string questId, OverhaulConfig config)
    {
        var weighted = config.Kills.LocationWeights
            .Where(kvp => kvp.Value > 0)
            .ToList();

        if (weighted.Count == 0)
        {
            return null;
        }

        int totalWeight = weighted.Sum(kvp => kvp.Value);
        int roll = GetDeterministicInt(questId, config.Seed, "killLocation", totalWeight);

        foreach (var kvp in weighted)
        {
            roll -= kvp.Value;
            if (roll < 0)
            {
                return kvp.Key;
            }
        }

        return weighted[^1].Key;
    }

    private static List<string> GetLocationTargetIds(string location)
    {
        // These values are EFT quest-condition location IDs, whose casing does not
        // consistently match the lowercase SPT database folder names.
        return location.ToLowerInvariant() switch
        {
            "bigmap" => ["bigmap"],
            "factory" => ["factory4_day", "factory4_night"],
            "interchange" => ["Interchange"],
            "laboratory" => ["laboratory"],
            "lighthouse" => ["Lighthouse"],
            "rezervbase" => ["RezervBase"],
            "shoreline" => ["Shoreline"],
            "tarkovstreets" => ["TarkovStreets"],
            "woods" => ["Woods"],
            "sandbox" => ["Sandbox", "Sandbox_high"],
            "labyrinth" => ["Labyrinth"],
            _ => [location]
        };
    }

    private static string GetQuestLocation(GeneratedConditionPlan plan, OverhaulConfig config)
    {
        if (plan.Kind != GeneratedConditionKind.Kill || !config.Kills.DistinguishLocation || string.IsNullOrEmpty(plan.Location))
        {
            return "any";
        }

        return QuestLocationIds.TryGetValue(plan.Location, out var id) ? id : "any";
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
