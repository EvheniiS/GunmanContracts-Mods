# Updated defaults: popup (draft notes for a spec, Oct 3 2026)

Status: **idea only, not specified, nothing built.** Written down so the next chat can turn it into a real spec and a
test plan (test fixture: `Tools/DefaultsTest`, plan: `Tools/DefaultsTest/TESTING.md`).

## Problem

The "Updated defaults" page (1.2.0 / 1.2.1) works, but a player who just opens the board lands on a page of rows
that looks unlike the rest, with explanatory text at the bottom that nobody reads. Colours, labels and the reason the
mod's section changed are still unclear, even after the wording pass in 1.2.1.

## Idea (from the user)

When the board is opened and mods were updated with changed defaults, show a **small popup in a different shape**
(smaller than the board) saying, roughly: "Your mods were updated, and some default values changed." Buttons:

- **Okay**
- **Apply all new**
- **Review**
- **Skip**

## Open questions for the spec

- What exactly do Okay and Skip do? Okay = accept the current state (untouched settings already moved, yours kept) and
  stop asking? Skip = ask again next time? Is one of them redundant?
- Apply all new: Use new for the yellow (kept) rows only, as "Use all new" does today? Does it affect the blue rows?
- Review: opens the existing page (rows, Keep / Use / Undo) or a redesigned one?
- When does it show: first board open of a session only, or every open while something is undecided (today the board
  opens on the page every time)? Does Skip remember across sessions, per mod update or forever?
- What does it say: counts ("2 moved to the new default, 2 of yours kept"), which mods, versions?
- The "Settings were reset" notice (1.2.1): same popup, a second line, or separate?
- Shape and size in VR (reach, laser hover) and in flat mode (mouse); how it relates to the phone tile / Ctrl+M.
- Can a mod author mark a default change as "important" (show) versus "minor" (silent)? Probably out of scope.

## Existing behaviour to keep in mind

- Rules: value == recorded old default -> auto-updated (Undo); value == new default -> silent; otherwise asked.
- A decision updates the recorded default; undecided rows are asked again at every start.
- Record file: `UserData/ModSettings_defaults.txt`; code in `ModSettings/DefaultChanges.cs`, rows in `Panel.cs` /
  `FlatMenu.cs`, page assembly in `Pages.cs`.
