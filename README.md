# kazusa-QuestConditionOverhaul - Project Build & Development Guide

This is an SPT (Single Player Tarkov) C# server mod designed to randomly overhaul quest completion conditions (eliminating enemies, handing over dogtags, and currency transactions) dynamically during server startup.

---

## 🛠 Prerequisites

To build and compile this project, you need:
- **.NET 9.0 SDK** (or higher) installed on your development machine.
- An IDE such as **VS Code**, **Rider**, or **Visual Studio**.

---

## 📦 Project Structure

- `PostDbLoad.cs`: Contains the main entry point (`IOnLoad`) that runs after the SPT database loads. It handles configuration parsing, dynamic quest overhaul planning, locales translation, and automatic modded trader discovery.
- `Models.cs`: Defines configuration models (`OverhaulConfig`, `ConditionWeights`, etc.) for JSONC deserialization.
- `config.jsonc`: The default user configuration file containing default weights, templates, and trader filters.
- `kazusa-QuestConditionOverhaul.csproj`: The MSBuild project file configured with custom targets for automated packing.

---

## 🚀 How to Build

Open your terminal in the repository root directory and run the following command to compile a production release:

```bash
dotnet build -c Release
```

### What happens during the build process:
1. **Restore & Compile**: Restores NuGet dependencies (including `SPTarkov.Server.Core`) and compiles `kazusa-QuestConditionOverhaul.dll`.
2. **Clean Output**: A custom MSBuild target (`CleanUnwantedFiles`) automatically deletes developer-only files like `.pdb`, `.deps.json`, and `.runtimeconfig.json` from the output directory to keep the mod package clean.
3. **Local Packaging**: A custom MSBuild target (`CopyBuildOutput`) structures the files into a deployment-ready directory under:
   ```text
   Build/SPT/user/mods/kazusa-QuestConditionOverhaul/
   ```
4. **Archive Generation**: Generates a deployment ZIP file under the `Release/` folder (e.g. `Release/kazusa-QuestConditionOverhaul-1.0.0.zip`).

---

## 🚚 Deployment

To install or update the mod:
1. Copy the structured directory `Build/SPT/user/mods/kazusa-QuestConditionOverhaul` to the `user/mods/` folder of your SPT installation directory.
   - Alternatively, extract the contents of the generated `.zip` archive from the `Release/` directory directly into your SPT server root.
2. Start the SPT Server.

---

## 🔄 Development & Auto-Discovery

When testing new modded traders, you do not need to look up their IDs manually. 
- Leave only the vanilla traders in your `config.jsonc`.
- Boot the SPT server once.
- The C# mod will dynamically scan all loaded quests, find modded trader IDs, resolve their localized names from locales, and automatically append them to `targetTraderIds` and `excludedTraderIds` in your `config.jsonc` (set to `false` by default, with comments) without stripping any existing comments.
