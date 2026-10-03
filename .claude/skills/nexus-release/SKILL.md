---
name: nexus-release
description: Prepare a Gunman Contracts mod for a Nexus Mods upload. Builds the Release DLL, makes the zip with the right Mods/ layout, hashes it, checks the Nexus page text against the code (version line, every setting and default), drafts the changelog from git, and hands back a paste-ready upload sheet, or, for a mod that already has a Nexus page and once the user confirms, uploads the new version with its changelog through the Nexus API (Tools/Update-Nexus.ps1), then verifies Nexus afterwards. Use this whenever the user says "prepare the Nexus files", "prepare Nexus upload", "prepare for Nexus release", "package <mod> for Nexus", "release <mod>", "get <mod> ready to upload", "cut a patch", or "what do I need to upload", for one mod or several, even if they never say "skill". Also use it after they say they uploaded, to verify and finish the housekeeping.
---

# Prepare a Nexus release

The mechanical parts (build, zip, hash, lint, changelog material) are done by `Tools/Pack-Release.ps1`, so the model only spends effort on judgement: is this ready, what changed for players, what must be fixed in the page text, what does the user paste where.

**Division of labour.** The user writes the page text (`NEXUS_DESCRIPTION.txt`) and the artwork with GPT, using `references/description-template.md`. Do not generate pages or images unless asked. Do check the finished text against the code and make small factual fixes yourself (version line, missing or wrong settings rows). If a page needs a rewrite, say so and point at the template.

**Uploading is the user's call.** `Pack-Release.ps1` never uploads, tags, merges or pushes. For an update to a mod that already has a Nexus page, `Tools/Update-Nexus.ps1` can push the zip, version, file description and changelog through the v3 API (step 7), but only after the user has said yes to that exact mod and version, because a public upload cannot be quietly undone. The API cannot edit page text, summary, tags or images (see `../nexus-status/references/nexus-api.md`), and it cannot create pages: new pages are always the user's manual upload.

## Workflow

1. **Pick the mod(s) and version.** Use what the user named, or run `pwsh -NoProfile -File Tools\Check-Nexus.ps1` and take the UNRELEASED mods. The version being released is the one in source (`MelonInfo`). If the user means a different version, stop and sort that out first.

2. **Gate: is it actually ready?** AGENTS.md says release artifacts change only after the intended release and its test status are verified, so look before packaging:
   - Test status: the mod's README status, and the last commit subjects (a subject ending in "untested" means it has not been played). If it is untested, say so plainly and ask before going further; do not quietly package it.
   - Requirements: read the mod's README for what it requires (for example Daredevil needs Grab Fix, Gloves, Throw Assist, Mod Settings, Weapon Framework). Run `Check-Nexus.ps1 -Mod <those>` and flag any required mod whose live version is older than needed, because then they must be released together, dependencies first.
   - Dirty tree: `git status` under the mod folder. The packaged DLL includes uncommitted work, so mention it.

3. **Run the packager.**
   ```
   pwsh -NoProfile -File Tools\Pack-Release.ps1 -Mod <Mod>
   ```
   It builds (`-c Release -o feature\<Mod>-<ver>`), copies the DLL to `release\<Mod>\`, writes `<Mod>-<ver>.zip` containing only `Mods/<Mod>.dll`, verifies the zip entry hash, then lints the page text, checks for header and thumbnail art, and lists the commits touching the mod since its release DLL was last committed. It refuses to overwrite an existing zip of the same version (`-Force` only for a version that is not live). `-LintOnly` skips build and zip and just checks the page text; use it when the user only wants to know what is stale. If the build fails or the game folder is unavailable, report that; do not call an unbuilt package ready.

4. **Deal with the warnings.** Each one is either a real gap or something intentional; decide, do not just relay.
   - *Check-it-works line says vX*: fix the version in `NEXUS_DESCRIPTION.txt`.
   - *Setting in code but not in the page*: add a row, copying name, default and description straight from `CreateEntry` in the source. If it is a deliberate test or hidden entry (for example a `TestEntries` flag), leave it out and say so.
   - *Setting in the page but not in code*, or *default differs*: the page is wrong; fix it from the code.
   - *Placeholders left, art missing, uncommitted changes*: report them; art is the user's.
   Keep edits to these factual fixes. Anything beyond that is a GPT rewrite for the user.

5. **Draft the changelog** from the commit subjects the packager printed. Rewrite for players, not for developers: what they will notice, in plain sentences, one line per change, no commit jargon, no "untested", no internal class names. Plain text, because it goes into the Nexus Changelog field. If this page's description already has a changelog spoiler (Better Bow, Mod Settings), also add the entry there in `[b]New:[/b]` / `[b]Fixed:[/b]` form. Do not start a changelog spoiler on a page that has none.

6. **Hand over the upload sheet, in chat.** Do not write a `NEXUS_UPLOAD.md` (see below). Keep it short and paste-ready:

   | | |
   |---|---|
   | File to upload | `release\<Mod>\<Mod>-<ver>.zip` (size, sha256) |
   | File name | same pattern as the live file (read it from `Check-Nexus.ps1`'s MAIN file name) |
   | Version | `<ver>` |
   | Category | Main files, archive the previous main file as old |
   | File description | `Extract into the game folder. Contains Mods/<Mod>.dll. Requires MelonLoader 0.7.3 or newer.` plus one line for anything special (Mod Settings optional/required) |
   | Changelog | the text from step 5 |
   | Page text | **re-paste `NEXUS_DESCRIPTION.txt` only if it changed**: run `Check-Nexus.ps1 -Mod <Mod> -Docs`; MATCH means the live page already has it |
   | Requirements | other mods that must be installed first, set as site requirements |

   For a **new page** also give: mod name, summary (max 350 chars), category, tags, permissions, and the art files. Summary and tags exist only here, so write them once. If the DLL's file name changed from the previous release, say the old and new names and that the old DLL must be deleted from `Mods`, which AGENTS.md requires.

7. **Upload through the API (existing pages only), if the user wants it.** Offer it after the sheet; the manual sheet stays the fallback and is the only route for a new page.
   - Write the step-5 changelog to a scratch file (plain text, one change per line).
   - Dry run first (reads only, changes nothing):
     ```
     pwsh -NoProfile -File Tools\Update-Nexus.ps1 -Mod <Mod> -ChangelogFile <file>
     ```
     It prints the plan (zip, md5, new file name in the live naming pattern, what happens to the previous file, file description) and runs the checks: zip layout and DLL hash, zip not newer than source, newer than the live version, name and version valid, changelog not already on Nexus (the API only appends, so a second post would double it). Fix every FAIL before going on.
   - Show the user the plan and ask for an explicit yes to this mod and version. The step is public and not undoable, so a yes to the packaging or to an earlier mod does not carry over.
   - Then run the same command with `-Apply`. It uploads, creates the new version (previous main file archived as old, page version updated, primary download), posts the changelog, and verifies the version chain, the v1 file list and the changelog. `-KeepOld` keeps the previous file as a main file. `-FileDescription` overrides the default "Extract into the game folder..." line, for example to add "Mod Settings optional".
   - The v3 ids per mod live in the `v3` block of `Tools/nexus-mods.json`; a new page needs `Update-Nexus.ps1 -Resolve -Mod <Mod>` once, and a page with two files (EnemyAwarenessFix also carries the Log) resolves to the one named like the mod.
   - `-Apply -UploadOnly` is a rehearsal of the upload half only (nothing attached to a page).
   - Requirements between your mods, and the page text, stay manual. Say which pages `Check-Nexus.ps1 -Docs` flags for a re-paste.

8. **After the upload** (the API run's own verification, or the user saying they uploaded by hand): run `Check-Nexus.ps1 -Mod <Mod> -Docs` and confirm the live version, that the page text matches, that the Settings column says OK (live page defaults equal the shipped config), and that a changelog entry exists. Nexus can lag a minute or two, so re-run once before calling it a failure. A brand-new page: add its id to `Tools/nexus-mods.json` (find it with `-Discover`).

9. **Housekeeping, offered, not done unasked.** The repo rule is `main` = released on Nexus, `dev` = unreleased, no feature branches. Remind them of: merge `dev` to `main` for the release, tag `<mod>-v<ver>` (existing tags are lowercase, for example `betterbow-v1.1.0`), update the mod's README status with the Nexus link, and the root README index. Do the git steps only when asked, and never switch branches over a dirty tree.

## NEXUS_UPLOAD.md is retired

All the sheets were deleted on Oct 3 2026 (their still-open mod-specific test notes moved to `IDEAS.md`). Most of what they held is now derived: version, zip name, hash, size and rebuild command from the code and `Pack-Release.ps1`; summary, tags and the live changelog from Nexus itself; and a stale status line is a risk (the DeathDetails sheet still said "Not uploaded" after it was live). Do not create new ones and do not maintain existing ones unless the user asks. Open to-dos belong in the mod's README or `IDEAS.md`, not in a sheet.

## Other tools in `Tools/`

- `Check-Releases.ps1` checks the local release folders: source version vs newest zip, release DLL vs the DLL inside the zip, source changed in git since the release DLL was committed (REBUILD NEEDED), page text naming an old version. Run it before packaging when the question is "is my release folder healthy", it makes no API calls. `-Fix` rebuilds a flagged DLL and zip at the same version and never bumps a version.
- `Compare-ReleaseDll.ps1` answers "does the release DLL still match the source?" by comparing compiled code (per-method IL and strings), since a fresh build never hashes the same as the committed DLL. Use it when `Check-Releases.ps1` says REBUILD NEEDED and you want to know whether the code really changed.
- `Check-Nexus.ps1` (skill `nexus-status`) is the Nexus side, read-only.
- `Update-Nexus.ps1` is the Nexus side that writes: new version of an existing mod file (step 7). Dry run unless `-Apply`.

## Several mods at once

Release in dependency order (a required mod's page must carry the needed version before the mod that needs it goes up). Run the packager per mod, give one combined sheet, and call out which uploads must happen together. With the API path, run `Update-Nexus.ps1` per mod in the same order, one confirmed `-Apply` per mod, and verify each before starting the next.
