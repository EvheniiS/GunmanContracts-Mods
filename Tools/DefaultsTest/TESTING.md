# Defaults Test: test plan for Mod Settings' "Updated defaults"

`Defaults Test` is a throwaway mod with two builds, 1.0.0 (old defaults) and 1.1.0 (new defaults), so Mod Settings can be
tested against a mod update without touching real mods. `Test-Defaults.ps1` installs the builds, edits only the test mod's
own cfg section and record lines, and `Check` compares the game log with what the rules predict. Header of the script has
the command order. Feature under test: Mod Settings 1.2.x (`DefaultChanges.cs`, the "Updated defaults" page, reset notice).

Status: **not ready for release.** The mechanics pass; the page wording is still unclear and a popup is planned (see
`../../ModSettings/UPDATE_POPUP_SPEC.md`). A full spec-driven test plan is the next step; the checklist below is the starting list.

## The rules being tested

| Player's value when the update arrives | Result |
|---|---|
| equals the old default (never changed) | **auto**: moved to the new default at start, row has Undo |
| equals the new default | silent |
| anything else | **asks**: value kept, row has Keep / Use; undecided rows stay on the page |
| default did not change, or the setting is new | silent (new settings are only recorded) |

A decision (Keep / Use / Undo / editing the setting) updates the recorded default, so it is not asked again. Undecided
rows come back at every board open and every start. Reset notice (1.2.1): a mod that had 2+ changed settings and now has
none is reported once ("Settings were reset"), kept until the board has shown it.

Fixture settings: `A_Untouched` 3->1, `B_Customised` 3->1, `C_Flag` false->true, `D_Text` old->new, `E_Same` 5->5,
`F_AlreadyNew` 4->8, `G_NewInUpdate` (1.1.0 only).

## Commands

```powershell
cd Tools\DefaultsTest
.\Test-Defaults.ps1 Setup            # installs 1.0.0, wipes the test mod's state. Start the game once, quit.
.\Test-Defaults.ps1 Update [-Keep]   # installs 1.1.0 (-Keep leaves your own cfg values). Start, open the board, quit.
.\Test-Defaults.ps1 Replay           # one-step "1.1.0 just arrived" with a fixed mix of values (A, C auto; B, D asks)
.\Test-Defaults.ps1 Check            # PASS/FAIL per row from the log, plus record file and cfg
.\Test-Defaults.ps1 ResetCfg         # remove only the [DefaultsTest] cfg section: reset-notice test
.\Test-Defaults.ps1 Status | Remove
```

## Results so far (Oct 3 2026, VR board, Mod Settings 1.2.0 then local 1.2.1 builds)

| Test | Result |
|---|---|
| Update with the player's own values (A=2, B=4, C=true, D=old, E=4, F=5): auto / asks / silent per the rules | **PASS** 7/7 by `Check` |
| Revert (D), Use new (A), Keep (B) on the board; cfg and record file agree afterwards | **PASS** |
| Undecided row (F) stays undecided and keeps its old recorded default | **PASS** (record still 4) |
| Replay (A, C auto; B, D asks): **Use all new** on the page, cfg and record agree | **PASS** 7/7 |
| Unit tests (`ModSettings/Tests`): Decide rules, record round-trip, reset detection | **PASS** |
| Page wording / clarity | **Not good enough**: needs the popup and a spec |

Not yet tested: flat mode (Ctrl+M); the reset notice (`ResetCfg`); first-run silence with 1.2.1 (formally); anything
beyond the single test mod.

## Checklist for the full suite

Per item: board (VR) and flat; expected result from the rules above.

1. First run with no record file: records everything, no page, log says so.
2. Each class on its own: auto, asks, already-new, same, new setting; all four value types (int, float, bool, text), plus
   a colour and a choice/enum setting.
3. Decisions: Keep, Use, Undo, Use all new, editing the setting in its own section counts as a decision, Reset on the
   row; decided rows read "Done" and survive a restart as decided.
4. Persistence: quit with undecided rows, restart: still listed with the same old default; decide, restart: gone.
5. Board behaviour: opens on the page while something is undecided; list entry; scrolling with more rows than fit;
   a long mod name or value; the page when only a notice exists (no rows).
6. Reset notice: `ResetCfg`; threshold (one changed setting reset by hand must not trigger); notice kept until shown
   once; not repeated next start; several mods at once ("and N more").
7. Version changes: update, downgrade (new->old default), skipping a version, mod removed then re-added.
8. Record file: deleted (re-records, no page), hand-edited default, corrupt line, read-only file.
9. Real mod regression: a real mod update (the old Daredevil 1.0.0 -> 1.1.0 DLL pair in `feature/defaults-test`) and
   the whole real cfg untouched afterwards (diff the cfg outside the test section).
10. Performance: scan time with the full real mod set at start (log timing), no per-frame cost with the board closed.
11. The popup (when specified): shown once per update, each button's result, Skip memory, flat mode.
