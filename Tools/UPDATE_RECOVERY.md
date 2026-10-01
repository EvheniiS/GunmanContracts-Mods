# Recovering mods after a game update

Reviewed October 1, 2026 against the local game 0.3.1.0, Unity 6000.0.41f1,
metadata v31 and MelonLoader 0.7.3. This replaces the recovery advice in the September 27 research entry.
No future game update has been tested. Tools gather evidence and narrow investigations; they do not automatically repair mods.

## Gaps in the old plan

| Earlier assumption | What the current source/evidence shows | Consequence |
|---|---|---|
| Seven mods and about 40 named hooks | 17 project folders, dynamic Harmony targets, shared hooks, hard and soft companion dependencies | Inventory installed DLLs, source, reference assemblies and preferences for every run. Resolve a shared dependency before its consumers. |
| A rename/signature change always gives a compile error | `nameof` proves a member exists, not that a Harmony overload remains unambiguous. Throw Assist and Grab Log resolve targets dynamically; reflection and string bindings can fail at runtime. | Separate compilation, initialization, patch installation, hook execution and observed behavior. |
| Everything binds only by method name | Scene paths, object names, shaders, materials, animation states, collider layouts, load-order assumptions and saves also matter | Same C# API does not establish unchanged game behavior. Inspect assets/hierarchies only for the affected feature. |
| No errors means the mod works | Hooks can become unused/inlined. Features can be disabled or never exercised. Catch blocks can hide failures. | Explicit smoke actions with observed outcomes; absence of a log line means unknown. |
| Launching once guarantees fresh usable interop | Generation can fail or mix stale output; a timestamp is not proof. The existing log reports generation skipped for this unchanged game. | Save generator messages and reference hashes before rebuilding. Fix generation first. |
| Address changes cost nothing | Usually harmless to named interop binding; registration discovery and research tools still rely on native binary structures | Validate map discovery for each changed binary. Address/field-offset deltas alone are not a mod failure. |
| Same Unity means the metadata parser is safe | The parser assumes version, strides, x64 architecture and registration layouts; version alone is insufficient | Check magic/version/table bounds/strides and validated pointers. Fail with evidence instead of interpreting nonsense. |
| Size/mtime sufficiently invalidate the map | A same-size replacement with retained timestamp can reuse old addresses | Cache now uses SHA-256 of both native assembly and metadata plus mapper schema. |
| `xref.py` proves whether a method runs | It scans candidate E8/E9 bytes, misses indirect calls and may match bytes within instructions | Confirm important sites by disassembly. Zero hits does not prove inlining or non-use. |
| One mission per mod is enough | Arsenal, holsters, pause, checkpoint restore, saves, challenges and restart occur in different paths | One short combined smoke session, then focused reproduction with dependency groups. |
| Problems after an update belong to the update | The current 0.3.1.0 log already contains spawn-validation exceptions and Frame Probe's missing draw-counter constructor | Preserve a pre-update baseline, including known failures. |
| Repairs take minutes unless obfuscated | Loader failures, semantic changes, asset changes and interactions have different costs | Estimate after classifying the failure. No reliable repair-time promise before seeing the update. |

MelonLoader 0.7.3's [generator source](https://github.com/LavaGang/MelonLoader/blob/v0.7.3/Dependencies/Il2CppAssemblyGenerator/Core.cs)
has multiple generation stages. A [reported reproduction in another game](https://github.com/LavaGang/MelonLoader/issues/1192)
describes stale `Cpp2IL/cpp2il_out` DLLs causing interop generation failure in 0.7.3. This is a troubleshooting lead,
not evidence that Gunman Contracts currently has that fault, or that a particular later release fixes it.
If that failure appears, archive logs and generated output, inspect the affected stage, then regenerate from a clean
generated-output directory. Do not delete the whole MelonLoader install, preferences, or saves as a routine first step.

## Local workflow

Run from the repository root in PowerShell. Python 3 is required; the wrapper finds it on PATH or uses Codex's
bundled runtime. Pass `-Python <python.exe>` if needed. The workflow itself requires no Python packages or API keys.
It does not send logs anywhere. Outputs live in git-ignored `feature/update-triage/`, outside the game's `Mods` folder.
These outputs include local preferences and paths; inspect them before sharing.

All operations require the game closed. This freezes the capture and avoids MelonLoader overwriting cfg changes on exit.
Game directory resolution: explicit `-GameDir`, `GUNMAN_CONTRACTS_DIR`, consistent top-level `GameDir.local.props`, then
`il2cpp_tools/game_dir.txt`. Conflicting project paths require an explicit choice.

```powershell
# Preserve current DLL/core/reference/source hashes and existing log as historical evidence.
./Tools/Update-Triage.ps1 Snapshot
# Optional: also retain the old native binary + metadata for code comparisons.
./Tools/Update-Triage.ps1 Snapshot -ArchiveNative

# Preview the exact preference edits without writing anything.
./Tools/Update-Triage.ps1 Prepare -DryRun

# Back up preferences and enable diagnostics for installed repository mods.
./Tools/Update-Triage.ps1 Prepare
```

Copy the printed Prepare folder into `$triageSession`. There is intentionally no auto-selected "latest" session:
restoration must use the session that owns the changed settings.

```powershell
$triageSession = 'C:\path\to\repo\feature\update-triage\prepare-...'
# Start the game yourself, exercise the checklist, then quit normally.
# Note actions and results in that session's playtest.md before collection.
./Tools/Update-Triage.ps1 Collect -Session $triageSession

# After diagnosis, restore only the diagnostic keys changed by this Prepare.
./Tools/Update-Triage.ps1 Restore -Session $triageSession -DryRun
./Tools/Update-Triage.ps1 Restore -Session $triageSession
```

`Prepare` enables 12 mods' declared `DebugLog` switches (Daredevil has two categories) when their DLLs are installed.
It enables Enemy Awareness Log and Grab Log through their diagnostic-only `Enabled` flags if installed. It does not
install absent diagnostic mods. Gloves and Challenge NPC Limit have no debug switch; inspect their effect directly.
`update-triage.json` is the reviewed mapping; add new project/category mappings there. The focused test checks project
coverage and whether projects declaring DebugLog have a mapping. Actual category names still require review when changed.
Third-party DLLs are inventoried but their undocumented settings are not guessed.

Gameplay switches are preserved, including disabled Radar Sense. Enable a feature explicitly through Mod Settings if
you want to test it, and record that change. Debug mode can itself change behavior (Better Bow uses B/Y for a diagnostic
dagger-grip flip) and adds logging cost. Use a separate run with restored diagnostics for performance/normal-input comparisons.

Optional `Prepare -WithFrameProbe` enables its performance CSV. Optional `-VerboseThrows` enables Billy Clubs' per-window
follow-through log. Neither is needed for ordinary compatibility triage. Existing values are retained unless the option
requests a change. Frame Probe's CPU/GPU counters may be unavailable in this game; missing samples are not a bottleneck diagnosis.

`Collect` requires a new log since Prepare, retains full exception blocks, groups exact repeated blocks, and lists
candidate owners from tags, stack symbols and shared targets. Unknown/loader errors stay visible. It copies any Grab Log,
Enemy Awareness Log and Frame Probe session files created/updated during the preparation window. Multiple launches in one
window may produce multiple sidecars; correlate their timestamps, not just filenames. Collect promptly after each run:
`Latest.log` belongs only to the most recent launch. Repeated collections are allowed and do not overwrite earlier captures.
The report compares log DLL hashes to the current installed folder. Copies/hashes alone cannot prove the game loaded those
current files, that initialization succeeded, or that a feature was exercised.

`Restore` backs up the current cfg, checks all changed keys first and restores their original values, while keeping
other settings/loadouts changed during testing. If an owned diagnostic key was edited during the test, restore stops
without a partial write; review the session's plan and backups. Entries originally absent are removed. A newly inserted
empty category can remain. Backups remain available. Do not start the game during Prepare/Restore; the process check
cannot lock a future launch out of Windows.

## Smoke session and isolation

Start in The Range, then run a short contract, restart from a checkpoint, return to The Range and restart the game.
Record each action's log time, setting, expected result and actual result in `playtest.md`. Skip disabled/unwanted
features with an explicit "not tested" result. Carry forward earlier issues separately.

| Group | Actions and observable result |
|---|---|
| Mod Settings + Gloves | Open phone tile and Ctrl+M, point/poke, toggle an ordinary setting, close; gloves tint on both hands, after scene load and changing color. |
| VR Holsters → Weapon Framework → Daredevil | Retrieve a mod weapon, grab/return it, draw/holster on belt and back, test both sides. Save loadout in Range, change scene, die/retry in same scene, restart; verify prepared items return once. Check ordinary arsenal entries and loadout persistence too. |
| Grab Fix | Near/distance pickup in both hands, early grip buffer, throw/regrab, ordinary docked/holstered pickup; disable and verify normal geometry returns. Grab Log can explain individual failures. |
| Better Bow | Shoulder quiver, fast string grab/nock/shoot, dagger grip/stab, throw, explosive barrel and marked door; pause/phone and resume. |
| Throw Assist + Daredevil throws | Knife and pistol release/impact, club direct/free throws and ricochet. Record assist and speed gates; do not infer missing hooks from rejected throws. |
| Knee Shot Stun + Heavy Melee | Leg shot and recovery time; gun/bow/fist melee, stagger and grounded target hits. They share GetHit with other mods. |
| Physical Dodge | Stand still, lean/duck/move under fire, wave boundary, haptic/slow-motion response. Compare with it off under similar conditions. |
| Enemy Awareness Fix | Lose sight, change position, observe search; multiple wave spawns and floors. Observe with/without the fix and use Awareness Log; record the existing spawn-validation error separately. |
| Challenge NPC Limit | Takedown terminal, increment above 30, selected value after UI refresh, start and confirm enemy count. Startup text alone is insufficient. |
| Radar Sense (if enabled) | Activate/deactivate, visible/occluded silhouettes, footsteps, club glow; repeat after scene load/checkpoint. |

If startup fails before loading mods, investigate loader/runtime/interop first. If failures start after mod loading,
use their full exception blocks and required dependencies. If no useful exception exists, reproduce the smallest
action and compare against the pre-update outcome.

For isolation, move disabled DLLs to a named backup directory **outside Mods**, while closed; record exactly what moved
and restore it after the test. Preserve hard dependencies: ModSettings → VRHolsterCustomization → WeaponFramework.
Daredevil expects the companion set for full functionality, even when a companion is a soft dependency. An `Enabled=false`
toggle does not remove Harmony patches, static constructors or initialization work, so it is weaker than isolating DLLs.
Temporarily disabling all Mods still leaves Plugins/UserLibs/loader. Use a loader-only and, if needed, an unmodified-game
comparison before calling a game/VR problem a mod regression. Third-party mods must participate in isolation.

For interacting failures, use dependency-valid groups (shared targets are listed in the report), then remove half a
group at a time. This narrows candidates; interactions may require a pair of mods, so record both removed and retained
sets. Retest the combination after a selective fix.

## Reverse engineering before and after the update

Capture a baseline **now**, before Steam replaces the files. Preserve existing research evidence locally; these
snapshots contain game symbols and are not release artifacts. A snapshot preserves a map, not the old binary or assets.
If native behavior comparison is needed, keep a legitimate local copy of the relevant old game files before the update.

```powershell
# Use your Python environment with pefile installed.
$env:GUNMAN_CONTRACTS_DIR = 'E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone'
python il2cpp_tools/snapshot.py save --output feature/game-before.json
# After the update, use the same type scope:
python il2cpp_tools/snapshot.py save --output feature/game-after.json
python il2cpp_tools/snapshot.py diff feature/game-before.json feature/game-after.json --output feature/game-diff.json
```

Default scope: ANB*, HVR*, GD_* types and their nested types (including coroutine state machines). For a narrower map, repeat `--type ANBBasicNPC --type ANBNpcSpawner`; use
the same scope before/after. Snapshots keep assembly/type identity, field names/types/offsets and every method signature,
token and exact overload RVA. Diff reports structural changes and candidate mods; pure RVA moves are ignored.
Types outside that scope, indirect uses, generic/byref type detail and asset/semantic changes require manual follow-up.
Core fingerprints do not hash every scene/asset bundle, so asset-only updates can escape them.

`il2.py` and `meta.py` now check metadata magic/version, table bounds and v31 strides. The mapper checks x64 PE data,
unique codegen modules against image names, method token bounds, registration pointers and backed file reads.
Content-keyed cache writes are atomic; a corrupt cache rebuilds. These checks reduce false maps, not prove every
IL2CPP type interpretation correct. Inheritance/generic resolution remains limited; use regenerated interop and
asset tools to cross-check. Cache files are local trusted files, never download/load someone else's pickle cache.

Use `dumpt.py` for exact signatures/addresses, then `disa.py`, `slot.py` or `disx.py` for the specific method.
`dumpt.py` now uses each method's exact pointer; `disa.py` and `slot.py` show all known overload addresses rather than
silently choosing the first. All tools can miss generic instantiations/aliased addresses. Next-known-method boundaries
are an estimate, and `scanoff.py` length caps can miss code. Field offsets must come from the current map and verified
object type; the same numeric offset can belong to unrelated classes. Use `xref.py` as a candidate finder and verify
call sites. Read assets/animations when a named object, hierarchy, material or animator assumption fails.

The baseline map and these guards were exercised on the installed v31 game. The native type/field/method dump,
annotated disassembly, metadata-slot decode and candidate caller scan were checked on `ANBNpcSpawner.spawnPointValidation`.
UnityPy asset extraction and a future metadata version were not validated during this audit.

## Selective repair and validation

1. Confirm bootstrap game/Unity/loader versions, generator completion, core/reference hashes, and which DLL hashes
   the log loaded. Do not build against a knowingly failed/stale generation.
2. If a named API/dynamic target changed, inspect that type/signature first. If a hook installs but the action fails,
   inspect native control flow/asset assumptions only for the affected path. Check whether the game already fixed the
   underlying bug before adapting a fix mod that may now override correct game behavior.
3. Build the affected project and any dependencies rebuilt by its ProjectReferences against the current game.
   Keep the old installed DLLs outside Mods, deploy every newly built mod DLL, verify hashes, restart, and repeat the
   failed smoke action plus an ordinary action and the shared-mod combination. Build success is not a gameplay pass.
4. Record game build, source revision, installed hashes, test actions/results and limitations in the affected testing
   notes. Keep `release/`, versions, tags and public compatibility claims unchanged until release intent and tests are clear.

A full compile audit can expose wider API changes, but is a separate decision: source may be newer than installed DLLs,
and project builds can rebuild dependencies. Do not automatically replace a working whole mod set merely to diagnose
one failure. This tool intentionally performs no builds or DLL moves/deployments.

## Verification

```powershell
python -m unittest discover -s Tools/tests -v
```

Focused tests cover preference comments/encoding/duplicates/missing keys, restore conflicts and preservation of tuning,
stale/new log handling and sidecar collection, game-check failure, current project coverage, unsupported metadata,
content cache keys and overload diff semantics. They use temporary fake game folders and do not launch the game.

Future improvement if silent regressions become common: add opt-in, rate-limited per-feature hook invocation counters
and explicit setup failures to affected mods. Start with the failing feature; installing a new universal Harmony observer
could itself alter patch order and make diagnosis harder. Existing debug logs are useful evidence but incomplete coverage.
