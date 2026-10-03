---
name: nexus-status
description: Check which version of each Gunman Contracts mod is live on Nexus Mods and compare it with the newest release zip, the source version, and the Nexus page text (description, summary, changelog) in release/<Mod>/. Use this whenever the user asks what is deployed or live on Nexus, which mods are out of date, stale or not uploaded yet, whether a mod's Nexus page matches the local NEXUS_DESCRIPTION.txt and the code's settings, whether a changelog was posted, how many downloads or endorsements a mod has, or before and after preparing or uploading any mod release. Also use it when they mention the Nexus API, "sync with Nexus", "what's pending release", or ask what still needs uploading, even if they never say "Nexus status".
---

# Nexus status for the Gunman Contracts mods

Read-only. One script asks the Nexus API what is live and lines it up against this repository, so the answer to "what is deployed?" comes from Nexus itself and not from memory or from docs that may be stale.

## Run it

```
pwsh -NoProfile -File Tools\Check-Nexus.ps1                # versions only (2 API calls per mod)
pwsh -NoProfile -File Tools\Check-Nexus.ps1 -Docs          # + live page text and settings vs local docs and code (3 calls per mod)
pwsh -NoProfile -File Tools\Check-Nexus.ps1 -Mod GrabFix,Daredevil
pwsh -NoProfile -File Tools\Check-Nexus.ps1 -Discover      # new pages of yours missing from the mapping
```

Run from the repository root. The key is `NEXUS_API_KEY` in `.env` (git- and MEGA-ignored) or in the environment; never print it, never paste it into a reply or a commit. If the console output looks empty, capture it first: `$o = & pwsh -NoProfile -File .\Tools\Check-Nexus.ps1 -Docs 2>&1; $o | Out-String -Width 220`.

The run ends with a line showing requests used and quota left. Nexus's real limits are 2,000 per hour and 20,000 per day (read from the response headers), a full `-Docs` run of 15 mods costs about 45, so there is no cache: it would only risk showing stale versions. `-Mod A,B` takes a comma list.

Mod folder to Nexus id lives in `Tools/nexus-mods.json` (static config only; keep volatile data out of it). When the user publishes a new page, add it there (run `-Discover` to find the id).

## Reading the table

Columns: **Nexus** = version parsed from the MAIN file's name (the page's own `version` field is just a counter, ignore it), **Zip** = newest `release/<Mod>/<Mod>-x.y.z.zip`, **Source** = `MelonInfo` version in the code.

| Verdict | Meaning | What to tell the user |
|---|---|---|
| IN SYNC | Nexus = zip, source not ahead | Nothing to do. |
| UNRELEASED | Source is newer than the live zip | Normal `dev` work. The verdict ends in `(tested)` or `(untested, last tested x)`, from the `Tested:` line in the mod's README (skill `mod-status`). Only `(tested)` ones are ready for `nexus-release`; for the others say what was last tested. An `IN SYNC (untested, ...)` row means the live version was never confirmed in play. |
| NOT UPLOADED | A zip exists that is newer than Nexus | Packaged but never uploaded. This is the actionable one. |
| NEXUS AHEAD | Nexus is newer than the newest local zip | Zip missing or uploaded from elsewhere. Investigate before anything else. |
| NO ZIP / NO NEXUS FILE | One side has nothing to compare | Report it plainly. |
| NOT PUBLISHED (status) | Page hidden, removed or in moderation | Nexus will not serve it, so it cannot be checked. |

`-Docs` adds a second table. Description is compared line by line after undoing Nexus's own rewriting (`<br />`, `[/*]`, HTML entities, closing tags moved onto the previous line), so **MATCH is trustworthy**. DIFFERS means the live page and `NEXUS_DESCRIPTION.txt` really differ. The note under the table names the first differing line. Usually the local file is ahead (a section added after the last paste, or text prepared for an unreleased version), and the fix is for the user to re-paste it. Do not assume the page is wrong. Changelog "none for x.y.z" means the live changelog has no entry for that version (informational; first releases often have none).

**Settings column** compares the Settings block on the *live page* with the config the *shipped DLL* was built from (the source at the commit where `release/<Mod>/<Mod>.dll` was last committed). `DEFAULT n` means the page states a default that the shipped code does not have, which is a real error on the live page and the one to act on (example: ThrowAssist's page says `MinAssistSpeed = 6`, the code ships 3.5). `missing n` means a setting exists in the shipped code but the page never lists it; `extra n` the reverse. `OK (n defaults)` means every literal default was compared and matched (computed defaults can't be, and are skipped). The notes under the table name each setting. This is a different question from `Pack-Release.ps1`'s lint, which compares the *local* page text with the *current* source before a release.

## How to report

Lead with the answer: how many mods are in sync, then the ones that need attention, each with the two version numbers. Keep it to the exceptions; do not re-print fifteen identical IN SYNC rows. If `-Docs` was run, say which pages differ and what the first difference is.

## Boundaries

- Checking is free and safe. **Uploading, publishing, or editing a Nexus page is outward-facing and the user's call every time.** Do not upload, tag, bump versions, or "fix" release artifacts as a side effect of a status check. This repository's AGENTS.md says release artifacts change only when the intended release and its test status are verified.
- The API cannot edit a page's description, summary, tags or images (see `references/nexus-api.md`). A description fix is always a manual paste by the user; this skill only tells them which pages and where they differ, and `Tools/Copy-Description.ps1 -Mod <Mod>` (see the `nexus-release` skill) gets the text onto their clipboard and opens the edit page.
- Nexus data can lag by a minute or two after an upload. If the user has just uploaded, re-run once before concluding it failed.

For what the Nexus API can and cannot do (including the v3 upload endpoints that `Tools/Update-Nexus.ps1` uses, driven from the `nexus-release` skill), read `references/nexus-api.md`.
