# kazusa-QuestConditionOverhaul

SPT 4.0 server mod that rewrites all loaded quests, including vanilla SPT quests and quests added by other mods.

It works on the merged quest database during `PostDBLoad`, so it does not care where a quest came from.

## What it changes

- preserves quest ids, rewards, trader ownership, and quest chain order
- preserves only `Quest` prerequisites in `AvailableForStart`
- replaces `AvailableForFinish` with one generated condition:
  - kill targets
  - hand over dogtags
  - hand over money
- optionally forces quest location to `any`
- rewrites quest locale text to match the new condition

## Build

```powershell
dotnet build .\QuestConditionOverhaulFinal\kazusa-QuestConditionOverhaul.csproj -c Release
```

Build output:

`Build/SPT/user/mods/kazusa-QuestConditionOverhaul`
