# Grab Log

Tested: none

**0.1.0 — diagnostic build; compiled against the installed game, VR testing pending.**

Records why an item does or does not become available to grab, especially when picking it up
again after throwing it. Follows the Enemy Awareness Log approach: gather evidence before
building a grab assist. No dependency on BetterBow or EnemyAwarenessLog.

## Install and output

Put `GrabLog.dll` in the game's `Mods` folder with MelonLoader 0.7.x installed, then restart
the game. A new file is created at:

```text
<game>/UserData/GrabLog/session-YYYYMMDD-HHMMSS-mmm.jsonl
```

Each line is a JSON record with a sequence number, real elapsed seconds, Unity frame number,
game time, event kind, and data. Object instance IDs distinguish identical items. Compare IDs
within a scene; scene changes reset histories. The session header lists loaded mods and versions,
including BetterBow and Throw Assist if present. A scene/end summary counts each event kind.

## First recording

1. In The Range, take an ordinary item and pick it up several times without throwing it.
2. Throw the same item, let it settle, and attempt to pick it up normally. Repeat until a pickup fails.
3. Try pressing grip just before reaching the item, then approaching with grip released and pressing
   only once the prompt appears. Include one failed attempt where you keep holding grip for a second.
4. Approach the same item from different angles and try both hands. Repeat with a second item type.
5. Optionally repeat the bow-string action for comparison. BetterBow-assisted grabs remain in the log;
   the logger does not disable any existing mod. For a baseline bow comparison, switch off
   `BetterBow.StringGrabAssist` yourself before the comparison and note that setting.
6. Quit normally to flush the last records. Keep the JSONL and `MelonLoader/Latest.log`, particularly
   if the console reports a patch or logging warning.

A short recording with both a successful and a failed pickup of the **same item** is most useful.

## What is captured

| Records / fields | Meaning |
|---|---|
| `grip_press`, `grip_release`, `trigger_press`, `trigger_release` | Input edges, analog grip/trigger values, held/activated flags, current target and nearby objects |
| `check_grab_before`, `check_grab_after` | State around the game's grab check on activated-input frames |
| `gate_result` | Actual `CanHover`, `CanGrab`, line-of-sight and grab-point-validation results; recorded when they change and on press-frame checks |
| `hover_enter`, `hover_exit`, `hand_state` | Actual hover events and changes to held/hovered targets |
| `prompt_state` | Changes to the instantiated indicator's logical enabled flags, component/hierarchy state or hover target |
| `try_grab_before`, `try_grab_result`, `grabbed` | Actual grab calls, their target, force flag, result and completed grab callback |
| `release_before`, `released` | Item state around release, velocity, angular velocity, collisions and release history |
| `sample` | Nearby-item snapshots, normally every 0.1 seconds, including while grip stays held |
| `grip_cycle` | Whether an initially empty hand acquired an object during a grip hold; this is an observation, not a claim about the player's intent |

Hand data includes palm, controller target, hand model, rotation, selected grab point, menu
detection block, toggle state, release wait, held and hover targets. Item data includes every
configured grab point's distance, allowed hand, pose angle, distance limit and observed validity.
`handWorldAngle` and `handModelToPose` use the rotation and distance sources found in the game's
`GrabPointValid`; palm and controller measurements are included for comparison.

**There is no single universal grab radius.** The log separates:

- Detection-bag `MaxDistanceAllowed`, distance source, sort mode, all/valid/ignored membership,
  closest candidate, and collider-distance setting.
- Colliders attached to the bag hierarchy, with enabled/trigger state, world bounds and shape.
  Spheres include their world-scaled radius. Boxes/capsules include local dimensions and scale;
  world AABBs are bounds, not a spherical radius. A hierarchy collider is not automatically a detector.
- Grab-point `CheckDistance`, `MaxDistance`, `AllowedAngleDifference`, pose and hand restrictions.
- Force-grab ray distance and automatic handoff distance, separately from normal hand grabbing.
- ANB bag repair/ghost-hand flags and release-fix distance, plus item release-grace state.

The overlap sphere on a hand is logged under `overlapSphere`; it is **not labelled the normal
pickup radius**, because collision-clearance checks and pickup detection are distinct mechanisms.

## Reading a failed pickup

Follow the item ID from `released` to the later `grip_press` / `check_grab_before`:

- A nearby item missing from a bag's `targetInAll` suggests examining trigger/collider membership.
- Present but not `targetValid`: inspect ignored membership, distance settings and item state.
- Valid candidate, grip held, no hover, and an observed `CanHover=false`: compare press timing
  against hover events. This is the pattern investigated for BetterBow's string-grab fix.
- Hover exists but no `try_grab_before`: check activated input, trigger versus grip control, and menu/toggle state.
- A failed `try_grab_result`: inspect the actual gate results, grab-point validity, ownership,
  required objects, socket and line-of-sight state.

These are investigation leads, not automatic root-cause verdicts. Gate values include their
original frame/time; **null means no observed call**, not false. A recorded result may be older
than the snapshot, and native inlining can bypass a Harmony hook. Other mods may also call grabs.
`releaseWait` is logged as context; existing BetterBow investigation found it gating force grabs,
not normal string grabs.

Prompt fields record engine indicator state, not pixel visibility in the headset. Occlusion,
renderer materials and indicator fade animations are not measured. `prompt_state` may also occur
when a target changes while the indicator stays enabled. Force/trigger indicators have component
and hierarchy observations; the explicit logical flags are for grip and dynamic-pose indicators.

Nearby discovery takes the three nearest active registered items within `ObservationRadius`, plus
held and hovered targets and the explicit event target. Discovery uses origin/AABB distance, so it
may include an object whose actual surface is farther away. Detailed collider distances use
`Collider.ClosestPoint` on supported primitive/convex shapes; other shapes are labelled AABB estimates.
Inactive objects are excluded from nearby discovery, but explicit event targets are still recorded.

## Settings

In `UserData/MelonPreferences.cfg`, section `[GrabLog]` (also available through Mod Settings):

| Setting | Default | Purpose |
|---|---:|---|
| `Enabled` | `true` | Enable recording |
| `SampleInterval` | `0.10` | Nearby snapshots, clamped to 0.05–2 seconds; event hooks remain independent |
| `ObservationRadius` | `0.75` | Diagnostic search distance, clamped to 0.1–3 metres; never changes gameplay radius |
| `MaxFileMB` | `128` | Stop at this session size (8–1024 MiB); console warning and `log_limit` record |

Writes are buffered and flushed once a second and on scene changes/normal exit. An abrupt crash
can lose buffered final records. Only startup, file-limit and first-occurrence errors go to the
console. Disable/remove the logger after testing; detailed diagnostic sampling has runtime cost.
No scene-wide searches run every frame: object Start hooks register items, with one fallback scan
three seconds after scene initialization. Destroyed objects and stale predicate results are pruned.

## Build and implementation notes

```powershell
dotnet build GrabLog/GrabLog.csproj -c Release -p:GameDir="D:\path\to\Gunman Contracts - Stand Alone"
```

Or use the same git-ignored `GameDir.local.props` convention as the other mods.

The current native `HVRHandGrabber.Update` inlines `UpdateGrabInputs`; logging uses a prefix on
the next actual call, `CheckGrabControlSwap`, after all input fields have been written and before
hover/grab processing. Native code inspection also confirmed that `CanHover` rejects new targets
while grip is held. That is evidence for testing the same timing issue on dropped items, not yet
proof that it explains their inconsistent pickup.

Patches never change arguments, return values or game fields. The logger never probes by calling
`CanGrab`, `CanHover`, `TryGrab`, `GetGrabPoint` or detector recalculation itself. Snapshot geometry
and pose getters only read state. No retry, wider pickup area, alignment assist or new grab behaviour
is included in this diagnostic build.
