# Nexus page text template

Skeleton for `release/<Mod>/NEXUS_DESCRIPTION.txt` (BBCode). It came from the first published pages (Better Bow, Fire Selector, Knee Shot Stun, Heavy Melee) and replaced `release/NEXUS_TEMPLATE.txt`.

**The user writes the page text and the artwork with GPT** and hands it this template. Claude does not generate pages or images unless asked; the release skill only checks the finished text against the code and makes small factual fixes (version line, Settings rows). If a page needs a rewrite, say so and let the user re-run GPT with this file.

## Rules

- Tagline bolds the 3-4 things the mod actually changes; nothing else in the opening paragraph is bold.
- One free-form section per distinct thing the mod does, `[size=4][b]...[/b][/size]`, named for what is in it ("What it does", "How to use", "Also fixed"), not a generic "Features".
- A short closing line naming what is untouched or still the game's own code, if true.
- **Install** stays word-for-word (only the `{{...}}` placeholders change), so a returning reader finds the same steps on every page.
- **"Not working?" is a spoiler.** People get overwhelmed by the amount of text, so the troubleshooting list is collapsed.
- **Settings** lists every `MelonPreferences` entry exactly as it ships: name, default, one-line description copied from the code's `CreateEntry`, never retyped from memory. `Tools/Pack-Release.ps1` checks names and defaults against the source. It closes with the Mod Settings line (optional for most mods; for mods that require Mod Settings, list it under Requirements on the Nexus page instead).
- **Compatibility is left out by default.** The mods work with each other and the user does not care about third-party compatibility. Add a short section only for a real overlap worth knowing (shared hook or input), in one or two sentences, and link a named mod only when it has a Nexus page.
- The version number appears in exactly one place outside a changelog: the "Check it works" line in Install.
- **Changelog:** posted in the Nexus Changelog field when uploading (plain text). Pages that already carry a changelog spoiler in the description (Better Bow, Mod Settings) keep adding to it, newest first, as `[b]New:[/b]` / `[b]Fixed:[/b]` items. Do not add a new spoiler to a page that does not have one.
- Link to another of the user's mods with its Nexus page: `https://www.nexusmods.com/gunmancontractsstandalone/mods/<id>`, ids in `Tools/nexus-mods.json`. A mod with no page links to the GitHub repo root `https://github.com/EvheniiS/GunmanContracts-Mods`.

## Template

```
[size=5][b]{{MOD NAME}}[/b][/size]

{{One or two sentences: what the mod changes, with the 3-4 headline features in [b]bold[/b].}}

[size=4][b]{{What it does / How to use / etc.}}[/b][/size]
{{Free-form: prose, [list], or both. Add more [size=4] sections for distinct behaviours.}}

{{Optional one-line closer: what is left untouched / still the game's own code.}}

[size=4][b]Install[/b][/size]
Takes about 5 minutes the first time. Steps 1 and 2 are needed only once, for all MelonLoader mods.

[b]Where is my game folder?[/b] In Steam, right-click [i]Gunman Contracts - Stand Alone[/i] →
[i]Manage[/i] → [i]Browse local files[/i]. It's the folder with [i]GunmanContracts.exe[/i] in it.

[b]1. Install MelonLoader[/b] (the mod loader)
Download the installer from [url=https://github.com/LavaGang/MelonLoader/releases]MelonLoader releases[/url],
run it, pick [i]GunmanContracts.exe[/i] in your game folder, and install [b]version 0.7.3 or newer[/b].
[b]Version 0.6 does not work[/b] with this game.

[b]2. Start the game once[/b], then quit.
A black console window opens next to the game: that's MelonLoader working. The first start takes
a minute or two, later ones are normal.

[b]3. Add the mod[/b]
Open the downloaded zip. Inside is a folder called [b]Mods[/b]. Drag that [b]Mods[/b] folder into your
game folder, and if Windows asks, choose to replace / merge.
You should end up with: [i]Gunman Contracts - Stand Alone\Mods\{{Mod}}.dll[/i]
{{Optional: "Updating from X or Y?" note, e.g. a mod that supersedes older ones or renames its DLL.}}

[b]4. Check it works[/b]
Start the game. The MelonLoader console lists [b]{{Mod Name}} v{{X.Y.Z}}[/b] among the loaded mods.

[size=4][b]Not working?[/b][/size]
[spoiler]
[list]
[*][b]The mod isn't in the console list:[/b] check the file isn't at [i]Mods\Mods\{{Mod}}.dll[/i] (a folder
too deep). Move it up so it sits directly in [i]Mods[/i].
[*][b]No console window at all:[/b] MelonLoader isn't installed on this game. Repeat step 1 and make
sure you picked [i]GunmanContracts.exe[/i].
[*][b]Errors right at startup:[/b] you likely have MelonLoader 0.6. Install 0.7.3 or newer.
[/list]
[/spoiler]

[size=4][b]Settings (optional)[/b][/size]
[spoiler]
After the first launch, edit [i]UserData\MelonPreferences.cfg[/i], section [b][{{Mod}}][/b]:
[code]
{{EntryName}}   = {{default}}   {{one-line description, from the code's CreateEntry}}
[/code]
Or change these live in-VR with [url=https://www.nexusmods.com/gunmancontractsstandalone/mods/28][b]Mod Settings[/b][/url] (optional) — no restart, no text editor.
[/spoiler]

{{Only on pages that already have one:}}
[size=4][b]Changelog[/b][/size]
[spoiler]
[b]{{X.Y.Z}}[/b]
[list]
[*][b]New: {{feature}}[/b] {{one line}}.
[*][b]Fixed: {{bug}}[/b] {{one line: symptom, then cause if short}}.
[/list]
[b]{{previous version}}[/b]: {{one line, or "first release"}}.
[/spoiler]

[size=4][b]Uninstall[/b][/size]
Delete [i]Mods\{{Mod}}.dll[/i]. {{Optional: what reverts, if not obvious.}}

[size=4][b]Source[/b][/size]
[url=https://github.com/EvheniiS/GunmanContracts-Mods]GitHub[/url] (MIT). {{Tested on / Made for}} game version {{game version}},
MelonLoader 0.7.3{{, optional headset/runtime note}}.
```

Notes for the lint: `Tools/Pack-Release.ps1` looks for the literal phrase `v<X.Y.Z>[/b] among the loaded mods`, `Mods\<Mod>.dll`, and Settings rows shaped `Name   = value   text` at the start of a line.
