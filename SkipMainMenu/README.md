# Skip Main Menu

Tested: 0.2.0 (Oct 5 2026): `DirectToRange` goes straight to The Range, one loading screen; the Start Game fallback is untested

Part of [Gunman Contracts Mods](../README.md). The game opens straight in The Range, with one loading screen instead of two. Only the launch is skipped; **Return to Main Menu** from the phone still shows the menu.

**How the game starts (checked in the disassembly, game 0.3.1.x; the scene order is confirmed from a real log).** The first scene is `GameLoader`. Its `ANBChangeMap.Awake` fills the `ANBStaticGameManager` statics and loads the save, `Start` starts a coroutine that shows the loading screen (`PreLoadMap`) and loads the next scene additively. That scene is `MainMenu` unless `ANBChangeMap.overrideMap` (a developer playtest override) is non-empty. Start Game then runs `ANBUIManager.StartGame()` -> `StartGameDelay` -> `ANBGameLogic.LoadMap` -> `The_Range_001`: the second load screen.

**Modes**
- `DirectToRange = true` (default): `overrideMap` is set to The Range before the loader reads it, so `MainMenu` never loads. Untested: the Range may expect something the menu scene sets up (`ANBStaticGameManager.inFirstMainMenu`, first-load flags, news/save checks). If anything looks wrong, the timeline shows it.
- `DirectToRange = false`: the main menu loads, and the mod presses Start Game once the game calls `ANBGameLogic.unlockMainMenu` (menu interactive), or `FallbackSeconds` after the menu scene is ready if that call never comes.

Build: `dotnet build SkipMainMenu/SkipMainMenu.csproj -c Release`. Install `SkipMainMenu.dll` in the game's `Mods` folder.

Settings in `UserData/MelonPreferences.cfg` under `[SkipMainMenu]`: `Enabled`, `DirectToRange`, `RangeScene` (`The_Range_001`), `FallbackSeconds` (6), `Timeline` (true).

**Timeline** (`UserData/SkipMainMenu/timeline.txt`, rewritten each launch): seconds since process start, then one line per event: scene init/load/unload, `ANBChangeMap` Awake/Start/PreLoadMap/PostLoadMap, `LoadMap` calls with their scene name, `MainMenuShow/Hide`, `unlockMainMenu`, `ANBUIManager.StartGame`, `PlayerStartGame`, and a `state ...` line whenever a menu-readiness flag changes. Read it to see the real order and gaps, and to pick the right trigger instead of a fixed delay.
