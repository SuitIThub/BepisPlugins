---
name: build-bepisplugins
description: Build BepisPlugins BepInEx plugins using dotnet build. Use when the user asks to build, compile, or test a plugin. Supports all game variants (AI, EC, HC, HS, HS2, KK, KKS, PH, SVS) and plugin types.
---

# Build BepisPlugins

## Tool Usage

Always use the AskQuestion tool when gathering information or confirming choices with the user. This provides a better user experience than conversational questions.

## When to Use

Apply this skill when:
- User asks to "build", "compile", or "create" a plugin
- After code changes that need to be tested
- When working with any BepisPlugins project
- When not prompted by the user, ask first if the project should be built
- When the user specifies to run in the background, do not ask for feedback during the build and deployment process

## Project Structure

BepisPlugins is a multi-project solution organized by game prefix and plugin type:

### Game Prefixes

| Prefix | Game |
|--------|------|
| AI | AI Shoujo |
| EC | Emotion Creators |
| HC | HoneyCome |
| HS | Honey Select (original) |
| HS2 | Honey Select 2 |
| KK | Koikatsu |
| KKS | Koikatsu Sunshine |
| PH | PlayHome |
| SVS | Summer Vacation Scramble |

### Plugin Types

| Type | Description |
|------|-------------|
| BGMLoader | Background music loader |
| ConfigurationManager | Configuration UI wrapper |
| ColorCorrector | Post-processing color correction |
| ExtensibleSaveFormat | Extended save data support |
| ExtensibleSaveFormat_Patcher | Patcher for ExtensibleSaveFormat |
| InputUnlocker | Input field character limit removal |
| Screencap | Screenshot and render capture |
| Sideloader | Mod loading system |
| SliderUnlocker | Slider limit removal |

### Available Projects

The solution contains the following buildable projects:

**AI Shoujo:**
- AI_BGMLoader, AI_ConfigurationManager, AI_ExtensibleSaveFormat, AI_ExtensibleSaveFormat_Patcher
- AI_InputUnlocker, AI_Screencap, AI_Sideloader, AI_SliderUnlocker

**Emotion Creators:**
- EC_BGMLoader, EC_ConfigurationManager, EC_ColorCorrector, EC_ExtensibleSaveFormat
- EC_ExtensibleSaveFormat_Patcher, EC_InputUnlocker, EC_MessageCenter, EC_Screencap
- EC_Sideloader, EC_SliderUnlocker

**HoneyCome:**
- HC_BGMLoader, HC_ConfigurationManager

**Honey Select (original):**
- HS_ConfigurationManager, HS_SliderUnlocker

**Honey Select 2:**
- HS2_BGMLoader, HS2_ConfigurationManager, HS2_ExtensibleSaveFormat, HS2_ExtensibleSaveFormat_Patcher
- HS2_InputUnlocker, HS2_Screencap, HS2_Sideloader, HS2_SliderUnlocker

**Koikatsu:**
- KK_BGMLoader, KK_ColorCorrector, KK_ConfigurationManager, KK_ExtensibleSaveFormat
- KK_ExtensibleSaveFormat_Patcher, KK_InputUnlocker, KK_Screencap, KK_Sideloader, KK_SliderUnlocker

**Koikatsu Sunshine:**
- KKS_BGMLoader, KKS_ColorCorrector, KKS_ConfigurationManager, KKS_ExtensibleSaveFormat
- KKS_ExtensibleSaveFormat_Patcher, KKS_InputUnlocker, KKS_Screencap, KKS_Sideloader, KKS_SliderUnlocker

**PlayHome:**
- PH_ConfigurationManager, PH_ExtensibleSaveFormat, PH_SliderUnlocker

**Summer Vacation Scramble:**
- SVS_BGMLoader, SVS_ConfigurationManager

## Build Process

### Step 1: Determine Target Project

Try to determine the target project from context:

1. **Check recent file edits** - If user recently edited `src/HS2_Screencap/*.cs`, target is likely `HS2_Screencap`
2. **Check conversation context** - Look for mentions of specific games or plugins
3. **Check git status** - Modified files indicate which project(s) were changed

If the target cannot be determined from context, use AskQuestion to prompt the user:

**First, ask for the game:**

```
Which game platform are you targeting?
- AI (AI Shoujo)
- EC (Emotion Creators)
- HC (HoneyCome)
- HS (Honey Select original)
- HS2 (Honey Select 2)
- KK (Koikatsu)
- KKS (Koikatsu Sunshine)
- PH (PlayHome)
- SVS (Summer Vacation Scramble)
```

**Then, ask for the plugin type based on available plugins for that game:**

For example, if HS2 was selected:
```
Which HS2 plugin do you want to build?
- HS2_BGMLoader
- HS2_ConfigurationManager
- HS2_ExtensibleSaveFormat
- HS2_InputUnlocker
- HS2_Screencap
- HS2_Sideloader
- HS2_SliderUnlocker
```

### Step 2: Run the Build

Execute the build command for the specific project:

```powershell
dotnet build src/{ProjectName}/{ProjectName}.csproj -c Release
```

Or build the entire solution:

```powershell
dotnet build BepisPlugins.sln -c Release
```

### Step 3: Verify Output

After a successful build, the output DLL is located at:
- `bin/{GamePrefix}_{PluginType}/` (output directory configured in project)

Check the project's `.csproj` file for the exact output path configuration.

## Deployment

### Preset Deployment Configuration (Honey Select 2 / StudioNeoV2)

| Setting | Value |
|---------|-------|
| Deployment target | `D:\Honey Select\BepInEx\plugins\HS2_BepisPlugins` |
| Target application | `D:/Honey Select/StudioNEOV2.exe` |
| Process name | `StudioNeoV2.exe` |
| Log file path | `D:/Honey Select/output_log.txt` |
| Success indicator | `[Info   :Advanced Map Search] Background scan finished` |

### Deployment Steps

1. **Check for running process**
   - Before copying, check if the target game process is running
   - If running, close it and wait for termination

2. **Handle existing files**
   - When running in background mode: backup original DLL automatically
   - When running interactively: use AskQuestion to ask if existing DLL should be overwritten directly or deactivated (rename to `.dl_`)
   - If a deactivated file exists, offer to rename with incremental number or overwrite

3. **Copy the DLL**
   - Copy from build output to deployment target

4. **Launch application** (if requested)
   - Start the target game executable

5. **Monitor log** (if requested)
   - Monitor the game's output log
   - Report any errors related to the deployed plugin

### Custom Deployment Paths

For games other than Honey Select 2, ask the user for deployment paths or use these common patterns:

| Game | Typical Install Path Pattern |
|------|------------------------------|
| AI Shoujo | `{GameRoot}/BepInEx/plugins/{PluginName}/` |
| Emotion Creators | `{GameRoot}/BepInEx/plugins/{PluginName}/` |
| HoneyCome | `{GameRoot}/BepInEx/plugins/{PluginName}/` |
| Honey Select | `{GameRoot}/BepInEx/plugins/{PluginName}/` |
| Honey Select 2 | `D:/Honey Select/BepInEx/plugins/{PluginName}/` |
| Koikatsu | `{GameRoot}/BepInEx/plugins/{PluginName}/` |
| Koikatsu Sunshine | `{GameRoot}/BepInEx/plugins/{PluginName}/` |
| PlayHome | `{GameRoot}/BepInEx/plugins/{PluginName}/` |
| Summer Vacation Scramble | `{GameRoot}/BepInEx/plugins/{PluginName}/` |

## Troubleshooting

**Build fails with missing references:**
- Check if referenced assembly paths in .csproj are correct
- Ensure required DLLs exist in the referenced locations
- Run `dotnet restore` before building

**Build fails with SDK not found:**
- Ensure .NET SDK is installed
- Run `dotnet --list-sdks` to verify

**Build fails with shared project errors:**
- This solution uses shared projects (.shproj) for common code
- Ensure all shared project references are correct

## Quick Reference

### Build Single Project
```powershell
dotnet build src/{Prefix}_{PluginType}/{Prefix}_{PluginType}.csproj -c Release
```

### Build All Projects
```powershell
dotnet build BepisPlugins.sln -c Release
```

### Build All Projects for One Game
```powershell
dotnet build BepisPlugins.sln -c Release /p:BuildForGame={Prefix}
```

### Common Build Examples
```powershell
# Build HS2_Screencap
dotnet build src/HS2_Screencap/HS2_Screencap.csproj -c Release

# Build AI_Sideloader
dotnet build src/AI_Sideloader/AI_Sideloader.csproj -c Release

# Build KK_ConfigurationManager
dotnet build src/KK_ConfigurationManager/KK_ConfigurationManager.csproj -c Release
```
