using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestConditionOverhaulFinal;

public record ModMetadata : SPTarkov.Server.Core.Models.Spt.Mod.AbstractModMetadata
{
    public override string ModGuid { get; init; } = "custom-static-kazusa-QuestConditionOverhaul";
    public override string Name { get; init; } = "kazusa-QuestConditionOverhaul";
    public override string Author { get; init; } = "kazusa";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "MIT";
}

public sealed class OverhaulConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("processAllQuests")]
    public bool ProcessAllQuests { get; set; } = true;

    [JsonPropertyName("targetTraderIds")]
    public List<string> TargetTraderIds { get; set; } = [];

    [JsonPropertyName("excludedQuestIds")]
    public List<string> ExcludedQuestIds { get; set; } = [];

    [JsonPropertyName("excludedTraderIds")]
    public List<string> ExcludedTraderIds { get; set; } = [];

    [JsonPropertyName("seed")]
    public string Seed { get; set; } = "quest-condition-overhaul-v1";

    [JsonPropertyName("weights")]
    public ConditionWeights Weights { get; set; } = new();

    [JsonPropertyName("kills")]
    public KillConfig Kills { get; set; } = new();

    [JsonPropertyName("dogtags")]
    public DogtagConfig Dogtags { get; set; } = new();

    [JsonPropertyName("money")]
    public MoneyConfig Money { get; set; } = new();

    [JsonPropertyName("locales")]
    public Dictionary<string, LocaleTemplates> Locales { get; set; } = [];
}

public sealed class ConditionWeights
{
    [JsonPropertyName("kills")]
    public int Kills { get; set; } = 40;

    [JsonPropertyName("dogtags")]
    public int Dogtags { get; set; } = 40;

    [JsonPropertyName("money")]
    public int Money { get; set; } = 20;
}

public sealed class KillConfig
{
    [JsonPropertyName("pmcChance")]
    public int PmcChance { get; set; } = 70;

    [JsonPropertyName("usecChance")]
    public int UsecChance { get; set; } = 50;

    [JsonPropertyName("minCount")]
    public int MinCount { get; set; } = 10;

    [JsonPropertyName("maxCount")]
    public int MaxCount { get; set; } = 50;

    [JsonPropertyName("distinguishFactions")]
    public bool DistinguishFactions { get; set; } = true;
}

public sealed class DogtagConfig
{
    [JsonPropertyName("usecChance")]
    public int UsecChance { get; set; } = 50;

    [JsonPropertyName("minCount")]
    public int MinCount { get; set; } = 5;

    [JsonPropertyName("maxCount")]
    public int MaxCount { get; set; } = 20;

    [JsonPropertyName("onlyFoundInRaid")]
    public bool OnlyFoundInRaid { get; set; } = true;

    [JsonPropertyName("distinguishFactions")]
    public bool DistinguishFactions { get; set; } = true;

    [JsonPropertyName("usecTemplateIds")]
    public List<string> UsecTemplateIds { get; set; } = [];

    [JsonPropertyName("bearTemplateIds")]
    public List<string> BearTemplateIds { get; set; } = [];
}

public sealed class MoneyConfig
{
    [JsonPropertyName("roublesChance")]
    public int RoublesChance { get; set; } = 60;

    [JsonPropertyName("eurosChance")]
    public int EurosChance { get; set; } = 20;

    [JsonPropertyName("dollarsChance")]
    public int DollarsChance { get; set; } = 20;

    [JsonPropertyName("roublesMin")]
    public int RoublesMin { get; set; } = 500000;

    [JsonPropertyName("roublesMax")]
    public int RoublesMax { get; set; } = 5000000;

    [JsonPropertyName("roublesStep")]
    public int RoublesStep { get; set; } = 10000;

    [JsonPropertyName("eurosMin")]
    public int EurosMin { get; set; } = 2000;

    [JsonPropertyName("eurosMax")]
    public int EurosMax { get; set; } = 20000;

    [JsonPropertyName("eurosStep")]
    public int EurosStep { get; set; } = 100;

    [JsonPropertyName("dollarsMin")]
    public int DollarsMin { get; set; } = 2000;

    [JsonPropertyName("dollarsMax")]
    public int DollarsMax { get; set; } = 20000;

    [JsonPropertyName("dollarsStep")]
    public int DollarsStep { get; set; } = 100;
}

public enum GeneratedConditionKind
{
    Kill,
    Dogtag,
    Money
}

public enum DogtagFaction
{
    Usec,
    Bear
}

public enum CurrencyKind
{
    Roubles,
    Euros,
    Dollars
}

public sealed record GeneratedConditionPlan(
    GeneratedConditionKind Kind,
    int Count,
    QuestTypeEnum QuestType,
    string? KillTarget = null,
    DogtagFaction? DogtagFaction = null,
    CurrencyKind? Currency = null);

public sealed record RewriteStats(
    int ProcessedQuests,
    int KillUsec,
    int KillBear,
    int KillScav,
    int DogtagUsec,
    int DogtagBear,
    int MoneyRoubles,
    int MoneyEuros,
    int MoneyDollars);

public sealed class LocaleTemplates
{
    [JsonPropertyName("kill")]
    public string Kill { get; set; } = "Eliminate {count} {target} on any map";

    [JsonPropertyName("dogtag")]
    public string Dogtag { get; set; } = "Hand over {count} {faction} dogtags";

    [JsonPropertyName("roubles")]
    public string Roubles { get; set; } = "Hand over {count} Roubles";

    [JsonPropertyName("euros")]
    public string Euros { get; set; } = "Hand over {count} Euros";

    [JsonPropertyName("dollars")]
    public string Dollars { get; set; } = "Hand over {count} Dollars";

    [JsonPropertyName("targets")]
    public Dictionary<string, string> Targets { get; set; } = [];

    [JsonPropertyName("factions")]
    public Dictionary<string, string> Factions { get; set; } = [];
}


