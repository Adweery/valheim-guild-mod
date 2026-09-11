# Valheim Guild

An in-game quest journal, Renown progression, inventory overview and nearby chest tracker for Valheim, with Windows and macOS installers.

Originally built for the RavensOath community. Public documentation, releases and installer prompts are in English.

## Language

Since **1.3.3**, the mod automatically follows the language selected in Valheim:

| Game language | Mod language |
| --- | --- |
| Slovak (SK) | Slovak |
| English (EN) | English |
| Any unsupported or unavailable language | English |

The setting is read when the UI renders, so changing the game language also changes the journal without a mod restart. This covers journal controls, tracker/status messages, completion notifications, supplies labels and the bundled quest titles, chapters and descriptions, including Swamp preparation. No separate mod language setting is needed.

Native item names continue to use the game's own item localization. Player names and custom administrator-written quests are preserved; custom text needs a matching entry in `src/ValheimGuildTelemetry/translations.json` to have a second language. Language selection only changes presentation, not saved progress, quest IDs, XP or account linking. The Discord bot retains its existing language because one shared Discord channel has no single game-language setting.

Download a **fresh installer ZIP** for English installer prompts. An older installer can still update the mod, but its own prompts and bundled README remain unchanged.

The quest journal requires a compatible dedicated-server plugin and the existing Valheim Guild Discord bot with its quest feed. This repository includes integration modules, **not a complete standalone Discord bot distribution**. Another world needs its own server configuration and account linking; installing the client alone does not set up a quest service.

## Installation

A GitHub account is not required. Close Valheim before installing or updating.

### Windows

1. Download the [Windows installer](https://github.com/Adweery/valheim-guild-mod/releases/latest/download/Valheim-Guild-Installer-Windows.zip) and extract the entire ZIP.
2. Run `Install-Valheim.cmd`, keeping `Install-Valheim.ps1` beside it.
3. When installation finishes (`Done`), launch Valheim through Steam.

### macOS

1. Download the [Mac installer](https://github.com/Adweery/valheim-guild-mod/releases/latest/download/Valheim-Guild-Installer-Mac.zip) and extract it.
2. With Valheim closed, try opening `Install-Valheim.command`. macOS may block the first launch because the installer is unsigned.
3. If blocked, open **Apple menu > System Settings > Privacy & Security**, scroll to **Security**, then click **Open Anyway** next to the blocked installer. Confirm the prompt and authenticate if requested. Only approve the installer you downloaded from this repository.
4. After installation finishes, launch the modded game using `Start-Valheim-Guild-Mac.command` in the game directory, or an already configured Valheim Guild launcher. Apple Silicon requires Rosetta.

If Open Anyway is missing, try opening the script once more and return to Privacy & Security. See [Apple's instructions for opening an app from an unknown developer](https://support.apple.com/en-ca/guide/mac-help/mh40616/mac).

These installers are not commercially code-signed. If your operating system blocks a script, report the exact message in a [GitHub issue](https://github.com/Adweery/valheim-guild-mod/issues). Do not disable system protection. The Windows launcher uses a temporary PowerShell execution policy for its own process, without changing the system policy.

After joining your configured server, link your game account through Discord `/linkgame`. Use `/gameprogress` to check the connection.

## Controls

| Key | Action |
| --- | --- |
| **J** | Open or close the journal |
| **Escape** | Close the journal |
| **F8** | Alternative journal shortcut |
| **F9** | Show or hide the compact quest tracker |

Opening the journal is designed to release the cursor and block character and camera input. Closing it with Escape consumes that press so it does not also open the game menu.

J is ignored while typing in game chat, the console or the supplies search field. Use Escape or F8 to close the journal while searching. Function keys may require Fn on a Mac.

Settings are stored in `BepInEx/config/adwery.valheim.guildtelemetry.cfg`. Change `Quests.JournalKey` if J conflicts with your bindings. Version 1.3.2 migrates the old default F8 to J once, while preserving other custom keys. Tracker size and shortcuts are configurable.

Keyboard logic tests and compilation pass. Live verification of keyboard, cursor and clicking behavior remains pending.

## Quest journal and automatic catch-up

The journal shows personal and team quests, chapters, recorded progress, Renown, completed quests and Swamp preparation. It uses the same progression data as the Discord bot.

- The bot reconciles saved world state and recorded telemetry before exporting the first journal snapshot.
- Account linking applies already recorded progress without duplicate rewards or historical completion messages.
- Automatic tracking selects up to five unfinished quests, prioritizing partial progress and the current chapter. Completed quests are replaced with the next objectives.
- Manually pinning a quest switches to a custom selection. Re-enable automatic selection in the journal or through `Quests.AutoTrackNext`.
- Each quest indicates whether its progress is supported by world state, recorded counters or manual confirmation.
- Crafting and kills from before telemetry installation are not inferred from equipment. Unverifiable building and organization tasks remain manual and use Discord `/complete`.

Opening or refreshing the journal does not award XP or post Discord messages. Live completions can show a short in-game notification; old completions are not replayed when joining. Updates normally arrive within approximately 15 seconds. Disconnected or outdated data is marked as cached.

The journal needs server plugin 1.2.0 and a configured bot export. This release does not provide map arrows or waypoints. See the [server integration guide](server/README.md).

## Inventory and nearby chests

Open the journal's supplies and equipment tab to view carried items, equipped item quality and observed chest contents. Carried amounts and storage amounts are shown separately. Each chest record includes game coordinates and its last observation time.

Accessible, loaded chests are scanned within **30 metres** by default. Set `Supplies.ScanRadius` between 5 and 100 metres. The scanner reads state already synchronized by the game, so changes by another player do not require reopening a chest, but still depend on normal network replication.

The scanner respects personal-chest access and wards. It does not claim ownership or add network ports. It processes 32 chests every two seconds, up to the nearest 100. At the full limit, a scan round takes approximately eight seconds plus game network delay. Distant records remain explicitly historical. Known access loss or destruction removes the corresponding cached contents.

Use the minus, plus and plus-five buttons to set personal packing targets. Stored items do not satisfy a carried-item target. Targets remain saved when inventories become empty. This is a manual preparation list; it does not award crafting XP or automatically change quest completion.

The local cache is separated by character and world in `BepInEx/config/ValheimGuildSupplies`, with limits of 100 chests, 4,000 item records and 512 packing targets. It is not shared between players or between Windows and Mac. It is not a global scan of every chest in the world. Automatic build, food and potion recommendations are not implemented.

Nearby-scanning tests cover additions, removals, empty chests, access changes, wards, range and batching. A live test with two connected players and interactive supplies-tab verification remains pending. Client versions 1.3.x do not require a server update beyond 1.2.0 for this feature.

## Updates and recovery

Close the game and run the installer again to download the latest mod. Download a fresh installer ZIP when the installer itself changes; rerunning an older script does not update that script.

The installer:

- Finds Steam libraries and asks for the game directory if detection is missing or ambiguous.
- Checks downloaded files against the release manifest's SHA-256 hashes.
- Stops if Valheim is running, without closing or launching it.
- Backs up replaced files in `ValheimGuildBackups` inside the game directory and attempts rollback after write failures. Backups remain available for manual recovery.
- Preserves worlds, characters, account linking, XP, unrelated mods and existing configuration.
- Downloads BepInExPack Valheim 5.4.2350 directly from its publisher when the loader is absent. It stops on an unrecognized or incomplete existing loader rather than overwriting it.

Manifest hashes check download consistency; they are not an independent publisher signature. Update trust depends on access to this repository.

Dependencies: [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) and [BepInEx](https://github.com/AzumattDev/BepInEx). The BepInEx archive and game assemblies are not redistributed here.

## Development

Mod source is in `src/ValheimGuildTelemetry`. Use .NET SDK 8 and reference assemblies from your legally installed copy of the game. Obtain BepInEx and 0Harmony references from `BepInEx/core`.

The build uses dedicated-server `assembly_valheim`, `assembly_utils`, `UnityEngine` and `UnityEngine.CoreModule`, plus matching client `UnityEngine.IMGUIModule`, `UnityEngine.TextRenderingModule`, `UnityEngine.InputLegacyModule` and `assembly_guiutils`. Do not include game assemblies in a release.

```sh
dotnet build src/ValheimGuildTelemetry -c Release \
  -p:GameReferences=/path/to/references \
  -p:LoaderReferences=/path/to/BepInEx/core

dotnet run --project src/StateTests
dotnet run --project src/NearbyTests
python3 -m unittest discover -s tests -v
```

Tests use isolated state, stubs or temporary folders. Windows installer tests run on a Windows GitHub Actions runner. Passing these checks does not establish live gameplay compatibility. Build, model tests and dedicated-server plugin loading have been checked; interactive Mac/Windows UI testing remains pending. Release notes record version-specific validation and known issues.

## Publishing

Write public documentation, release titles, release notes, issues, pull requests and commit messages in English. Keep verification claims specific and distinguish automated checks from live gameplay tests.

Prepare the four release assets using a compiled, verified DLL:

```sh
python build_release.py --version 1.3.3 \
  --dll /path/to/ValheimGuildTelemetry.dll \
  --loader /path/to/BepInExPack.zip \
  --output /path/to/new-release-directory
```

The script creates `latest.json`, `ValheimGuildTelemetry.zip`, `Valheim-Guild-Installer-Windows.zip` and `Valheim-Guild-Installer-Mac.zip`. It does not publish them.

Create a draft release, upload all four assets and publish only when the complete set is ready. The latest manifest points to an immutable tagged mod ZIP. Always create new tags for changed packages; never replace assets under a published tag.

Installer, client mod and Discord bot versions are separate concerns. Bot-only fixes do not require a client update. Coordinate protocol changes with the dedicated server before marking a client release as latest.
