---
name: mod-status
description: Record and use the tested/untested state of a Gunman Contracts mod, and refresh the root README's mod table when dev goes into main. Use this whenever the user reports a test result ("tested X, works", "X failed in game", "mark it tested"), asks which mods are tested or ready to release, asks what the README says is current, or is merging `dev` to `main` / finishing a release and the root README index needs updating. Also use it when a mod's version was bumped and its Tested line needs a sanity check.
---

# Mod test status

Test state lives in ONE machine-readable place: a line near the top of each mod's own README.

```
# Daredevil 1.1.2

Tested: 1.1.0 (2026-10-02)
```

- Format: `Tested: <version> (<yyyy-mm-dd>)` (date optional) or `Tested: none`. First 40 lines of `<Mod>/README.md`.
- Meaning: the **newest version the user actually played**. Not built, not installed, not "should work".
- Bumping the source version automatically makes a mod "untested" (Tested no longer equals source). Nothing to edit until the user plays it.
- Free-text status in the README body ("built, untested", per-feature notes) stays as human context; the `Tested:` line is what the scripts read.

## Recording a result (on `dev`, any time)

Only when the user says they played it. Set `Tested:` to that version and today's date. If they report a failure, leave `Tested:` where it was (or `none`) and put the failure in the README body (or, if it is a game finding, `GAME_KNOWLEDGE.md`). Never raise `Tested:` on your own judgement, a passing build, or a log that looks fine; the user's word is the evidence. Mention what you changed.

If the user tested several versions in one session, use the highest one they confirm.

## Reading it

- `pwsh -NoProfile -File Tools\Check-Nexus.ps1` has a **Tested** column and appends the state to verdicts: `UNRELEASED (untested, last tested 1.2.0)`, or `IN SYNC (untested, ...)` when the live version was never confirmed. Only `UNRELEASED (tested)` means ready for `nexus-release`.
- `Pack-Release.ps1` warns `README Tested line: ...` when it is not `tested` for the version being packaged.
- To answer "which mods can we update?": list UNRELEASED rows labelled `tested`; for the rest say what was last tested and what changed since.

## Root README index: only when going into `main`

The Status column of the root README table is generated, not hand-edited, and only refreshed at release time:

```
pwsh -NoProfile -File Tools\Sync-Readme.ps1            # dry run: prints each row that would change
pwsh -NoProfile -File Tools\Sync-Readme.ps1 -Apply     # writes README.md; refuses unless the branch is main
```

Run it after `dev` is merged into `main` (or while preparing that merge on `main`), review the diff, commit it with the release. It never commits or switches branches. Do not run `-Apply` on `dev` (`-AllowDev` exists for tests; use `-ReadmePath <copy>` to try it safely).

What it writes per row: `<source version>, [on Nexus](url)` when the zip equals the source; `<version>, tested` / `<version>, untested, last tested <x>` when unreleased, followed by `; <zip version> on Nexus` if an older one is live; then the optional note from `Tools/readme-notes.json` (dependencies, "diagnostic", per-feature caveats; static text only). Rows with no `Tested:` line are flagged. The "What it does" column stays hand-written.

When adding a new mod: give it a README with a `Tested:` line, a row in the root table (link `Folder/...`), and its Nexus id in `Tools/nexus-mods.json` once the page exists.

## Boundaries

- Editing a mod README's `Tested:` line is ordinary dev work. The root README table changes only via `Sync-Readme.ps1` on `main`.
- Tested lines were seeded Oct 3 2026 from notes and may be wrong; if the user corrects one, trust them.
