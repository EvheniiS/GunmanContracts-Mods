---
name: defaults-test
description: Run the Mod Settings default-change test pass (the "Mods updated" popup, the Updated defaults page, the "Settings were reset" notice) with the throwaway Defaults Test mod and Tools/DefaultsTest/Test-Defaults.ps1. Use this whenever the user wants to test Mod Settings' defaults-update handling, the update popup, Use new / Keep mine / Review / Later, the reset notice, or first-run recording, in flat mode or VR; when they say "next", "done" or report what they saw during such a pass; or when they want the test state cleaned up ("clean the cache", "start over", "remove the test mod").
---

# Defaults Test pass (Mod Settings 1.3.x)

The fixture is a throwaway mod, **Defaults Test**, with a 1.0.0 build (old defaults) and a 1.1.0 build (new defaults).
`Tools/DefaultsTest/Test-Defaults.ps1` installs a build, edits only the `[DefaultsTest]` cfg section and the fixture's
lines in `UserData/ModSettings_defaults.txt`, and `Check` grades the last game session from `MelonLoader/Latest.log`.
Real mods are never edited. Spec: `ModSettings/UPDATE_POPUP_SPEC.md`; results: `Tools/DefaultsTest/TESTING.md`.

Settings: `A_Untouched` 3->1, `B_Customised` 3->1, `C_Flag` false->true, `D_Text` old->new, `E_Same` 5->5,
`F_AlreadyNew` 4->8, `G_NewInUpdate` (1.1.0 only).

## Working rules

- **The game must be closed** for every command (the script refuses otherwise). The user starts, plays and quits; Claude
  runs every command and reads the result. One step per message: run the step's commands, tell the user exactly what to
  do and what they should see, wait for "done", then run `Check` and set up the next step.
- Run from `Tools/DefaultsTest`: `pwsh -NoProfile -File Test-Defaults.ps1 <Command>`. Each mutating command backs up
  the cfg and record file to `UserData/DefaultsTest-backup/<timestamp>/` first.
- A newer Mod Settings build must be built and installed first (AGENTS.md deploy rule); Check only reads the log.

## Commands

| Command | Does |
|---|---|
| `FirstRun` | backs up and deletes the whole record file: next start re-records every default, no popup. **Swallows any real mod's pending default change** (Oct 4: Decapitation 1.0.1). Before using it, compare the loaded mod versions with the record's `@` lines; if a real mod changed, skip it or restore that mod's record lines afterwards |
| `Setup` | wipes the fixture's state, installs 1.0.0 |
| `Update [-Keep]` | installs 1.1.0; without `-Keep` sets B=2, D="mine", F=8 first; saves the snapshot `out/update-snapshot.txt` |
| `Replay` | one-step "1.1.0 just arrived" (no prior start needed): A=3, B=2, C=false, D="mine", E=4, F=8, record rewound to 1.0.0 (A, C auto; B, D ask) |
| `Check` | PASS/FAIL: auto/ask/silent per setting (sessions where the update arrived), first-run silence, the popup answer vs cfg and record, reset notice |
| `ResetCfg` | removes the `[DefaultsTest]` cfg section (a deleted settings file in miniature) |
| `Status` / `Remove` | show state / uninstall the fixture and its state |

Reading `Check`: `Popup answer: <x>` then one line per ASKS setting, graded by that setting's LAST decision in the log
(popup Use new / Keep mine, page Use all new, or a row's Keep / Use new). Used new = cfg and record at the new default;
kept = cfg is the player's value, record at the new default (not asked again); undecided = record still the old default
(asked again next start). What the user saw (texts, layout) has no automatic check.

One popup covers every mod: one mod is named in the title ("DEFAULTS TEST UPDATED"), several get "MODS UPDATED" and a
"Mods: A (3), B (1)" line. Review opens one page with all their rows.

## Scenario order (flat mode, ~9 starts)

| # | Run | User does (Ctrl+M) | Expect |
|---|---|---|---|
| T0 | `FirstRun`, `Setup` | start, open menu, quit | no popup; log "recorded the defaults of N settings" |
| T1 | `Update -Keep` | open → popup **OK / Review** ("5 settings you never changed") → Review: 5 blue rows; Undo A; close, reopen: no popup | `Check`: 5 auto |
| T2 | `Replay` | popup (2 never changed / 2 changed) → **Use new** | `Check`: B=1, D=new |
| T3 | `Replay` | → **Keep mine** | `Check`: B=2, D=mine, record new |
| T4 | nothing | start, open | no popup |
| T5a | `Replay` | → **Later**; reopen: no popup, Updated defaults first and yellow in List; quit | `Check`: record still old |
| T5b | nothing | start, open: popup back (2 changed only) → **Review**: Keep B, Use D | `Check` (manual) |
| T6 | `ResetCfg` (T5 leaves B=2, E=4) | start, open: "SETTINGS RESET" + OK; quit; start, open: nothing | `Check`: reset line |
| T7 | `Remove`; diff real cfg outside `[DefaultsTest]` vs the T0 backup | — | real settings unchanged (explain any managed-state diffs) |

## VR UI check (layout only; the logic is covered by the unit tests and the flat pass)

One start after `Replay` (works from scratch: it writes the cfg section if missing). Open the board from the phone tile:
1. Popup card alone (no board behind), blue frame, "DEFAULTS TEST UPDATED", all text inside the card, 4 buttons fit.
2. Laser and fingertip press only the card's buttons; grip does nothing while it is up.
3. Review: rows readable, names not cut, yellow (Keep / Use new) above blue (Undo), the step buttons don't overlap.
4. List: Updated defaults first, blue background while B or D waits; decide both: grey.
5. Close and reopen: no popup, opens on a mod section.

Write results into the table in `Tools/DefaultsTest/TESTING.md` and, when the user confirms the pass, the README
`Tested:` line via skill `mod-status`.
