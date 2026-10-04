# Radar Sense (part of Daredevil)

**Oct 2 2026: silhouettes vanishing on close enemies (floating vest / nothing): FIXED, confirmed in play.** Pooled enemies
get a brand-new body (new skinned meshes) on respawn; the cached part list pointed at the destroyed one. An enemy that
should show with ≤ 2 meshes on is rescanned (at most every 2 s), dead parts are dropped, new ones outlined. Debug lines:
`rescan #id: N new renderers ...` / `nothing new ... renderers on: ...`. Tested session: 0 `NOT DRAWN` (was 563), mod
cost 0.02-0.05 ms/frame. **`ShowStill` (on) + `StillBrightness` (0.35, live):** unseen enemies standing still show
dimmed instead of not at all; moving ones and everything during focus stay at full `Brightness`. Tester: "much more accurate".

**`Brightness` (0.6, live):** multiplies the silhouette colour. Overlapping body meshes stack their opacity (3 layers of 60% = 94%), so the red reached full brightness and the game's bloom added an aura that merged neighbouring enemies. `Detail` now defaults to Body (tester: the cleanest silhouette).

**Oct 1 2026: `Detail` (live) and `IncludeLods` (untested).** `Detail` = Core (skin mesh + vest), Body (+ shirt/jacket, trousers,
sleeves, hands), Clothes (+ shoes) or Full (default: everything). Switch it on the settings board and compare the look; each
level draws fewer meshes per enemy. `IncludeLods` (default on) also outlines the lower detail levels (LOD1-4): the game
swaps an enemy to a lower level when it isn't shown to you, and with only LOD0 outlined such an enemy showed nothing or
just its vest (the 22:49 session logged 81 `NOT DRAWN` lines, all body meshes off at 2-4 m, game hiding flag off).

**Oct 1 2026: outline vanished on close enemies behind walls: FIXED (`SeenFraction`, untested in play).** Cause, from a
debug session (`view FLIP/STAY` lines, 109 samples): the game's `isInView` is true when the `VisCheck` renderer is on screen
and **ONE** of the 14 sight dots (`HiddenPosCheckDots`, feet to just above head) has a clear ray from your head
(`BlockedSight`, reverse, length distance - 0.1, `viewBlockMask`). Enemies behind a doorframe, column, counter or half wall
nearly always have a head-height dot clear, so `Style = Hidden` treated them as seen and dropped the whole silhouette.
Wall-blocked enemies had 2-6 of 14 dots clear; really visible ones 11-14. New `SeenFraction` (0.7 = 10 of 14 dots; 0 =
the game's rule): below it the enemy keeps the highlight, which with the depth test Greater shows only the parts that are
hidden. Costs up to 14 raycasts per enemy the game calls in view, 10 times a second.

**Sep 28 2026 (Daredevil 0.3.1+): clubs now come from Billy Clubs' own list** (`BillyClubsMod.CopyClubs`, same DLL,
every 0.5 s) instead of searching every loaded `HVRGrabbable` every 2 s, which had raised the mod's own cost from
0.125 to 0.23 ms/frame with 3 enemies. **Measured after the fix (13:21 session, 3 enemies, 3 clubs): 0.002-0.005
ms/frame**, about 50x lower; frames 0.1% slow in a quiet minute. Glow on a newly spawned club confirmed. The settings board pages are now "Daredevil: Clubs" and "Daredevil: Radar
Sense" (display names only; the cfg sections stay `[BillyClubs]` / `[RadarSense]`).

**Legacy as a standalone mod since Sep 28 2026:** this code ships only inside the Daredevil package (`../README.md`).
The version notes below are history (last standalone number 0.3.1).

0.3.1: `ClubMinDistance` defaults to 2 m (tester's choice). 0.3.0 tested: club highlight works; hidden clubs glow fully,
a club lying in the open only where something covers part of it.

0.3.0: **Billy Clubs show through walls too** (`ClubHighlight`, `ClubColor` `#FFC000`), so dropped or thrown clubs can
be found. Only when a club is more than `ClubMinDistance` (2 m) from your head, so a held or holstered club doesn't
glow under your glove. Clubs are found by name (`BillyClub-*`), so there's no dependency on the Billy Clubs mod. Same
on/off rules as enemies.

0.2.1: `FocusRevealsAll` (on): while your own slow motion (focus, right B) is on, every enemy in `Range` shows, even
undetected ones, as with `Reveal = All`. The rest of the time `Reveal` applies, so the extra cost is paid only
while focusing.

0.2.0: **louder enemy footsteps** (`LoudSteps`). Each enemy's steps play on its own 3D sound source, so gunfire can't
cut them off, and they're heard further away. The `perf` line counts the steps the mod played.

0.1.4: `RevealUnseen` (on/off) is replaced by `Reveal`: `Awake` (= RevealUnseen off), **`Moving`** (default: also
enemies walking or running, faster than `StepSpeed` 0.5 m/s, as if you hear their steps) or `All` (= RevealUnseen on).
A moving enemy is kept shown while it moves; once it stops, the game hides it again after its usual few seconds.
The "radar off" line counts how many unseen enemies were heard moving.

0.1.3: new default `Style = Hidden`: an enemy you can see directly (the game's `isInView`) gets no highlight, so no red
patches on visible enemies; enemies behind something show their hidden parts. `RevealUnseen` now defaults to off: the
tester prefers it, since the game then wakes enemies up when they shout, which reads like echolocation. The mod's own
work runs 10 times a second instead of every frame (0.26 ms/frame with 21 enemies tracked in 0.1.2).

**Perf, 0.1.2 test (Sep 28 2026, The Range, `Always`, 90 Hz):** in the same minutes, frames while keeping unseen
enemies shown (2–3 of them) were **slow 1.4–1.5%, p95 13.4–13.6 ms** vs **0.5–0.7%, p95 12.4–12.9 ms** without. A small
but real cost that grows with the number of enemies kept. The `RevealUnseen = off` minute had 2.4% slow, but that was a
bigger fight (10 enemies), so it isn't comparable.

0.1.2: the 0.1.1 fix is now a setting, `RevealUnseen` (on by default), and a once-a-minute performance line
(`PerfLog`) compares frame times with the sense off, on, and on while keeping unseen enemies shown.

0.1.1: enemies show through walls even if you haven't seen them yet. The game hides enemies that have been out of
view for a while (meshes off, animator culled); 0.1.0 followed that, so they only lit up after you'd seen them and
faded a few seconds after you looked away. Now the sense keeps the game from hiding enemies in range while it's on.

0.1.0 test (Sep 28 2026, `ActiveWhen = Always`): both eyes, shader picked right, "really gives you the Daredevil feeling".

Daredevil's radar sense. When enabled, you see enemies through walls as red silhouettes. It is enabled by default.
`ActiveWhen = Always` keeps it on all the time rather than limiting it to slow motion.

## Settings (`[RadarSense]`)

- `Enabled` (true): master switch for showing enemies through walls.
- `ActiveWhen`: `Always` (current default) or `SlowMotion`.
- `WithDodgeSlowMotion` (true): the dodge slow motion turns it on too.
- `Reveal`: `Awake`, `Moving` (default) or `All`: which unseen enemies the game is kept from hiding (see below). Each
  kept enemy keeps its mesh and animation, which costs performance. `StepSpeed` (0.5 m/s) for `Moving`.
- `PerfLog` (false): one line a minute, e.g. `perf 60 s (median 11.1 ms): off 2100 fr avg 11.1 p95 11.4 max 24 ms,
  slow 0.4% | keeping unseen 3300 fr avg 11.3 p95 12.0 max 30 ms, slow 1.2%, 4.2 kept | mod 0.020 ms/frame`.
  In VR the frame time is locked to the headset refresh, so the cost shows up as **slow** frames (over 1.4× the
  minute's median, i.e. missed refreshes), not a higher average. `kept` = unseen enemies held visible per frame.
  `mod` is the mod's own code only; the extra animation and drawing is in the frame times.
- `LoudSteps`: `Always` (default), `Sense` (only while the sense is on) or `Off` (the game's own steps).
- `HearDistance` (30 m): steps are heard up to this far. The game's own limit is logged (`enemy steps: game hears
  them within X m`); the mod never lowers it.
- `LoudRadius` (3 m): full volume within this distance, then logarithmic falloff. **The main loudness knob**: double it
  and a step at any distance is twice as loud (+6 dB).
- `StepVolume` (3): multiplier times the game's sound-effects volume; values above 1 make steps louder than the normal effect volume.
- `Color` (`#FF1010`), `Opacity` (0.6), `Range` (60 m).
- `Style`: `Hidden` (default: enemies you can see directly get no highlight, the rest show the parts hidden behind
  something), `Behind` (every enemy shows its hidden parts, so visible enemies can get red patches where their own
  body hides a part) or `Blocked` (the whole body, only on enemies you can't see). "Can see" = the game's
  `ANBBasicNPC.isInView`: its `VisCheck` renderer is on screen and `BlockedSight`'s rays aren't blocked.
- `SkipParts` (`eye,teeth,tooth,tongue,lash,brow`): parts left out by renderer or mesh name. Parts inside the head
  would otherwise show through the face.
- `Shader` (`Auto` = `Hidden/Internal-Colored`, then `UI/Default`). Applies on the next level load.
- `DebugLog`: the shader and its properties, each enemy body's parts once, one line per on/off.

## How it works

Each enemy body `SkinnedMeshRenderer` gets a child `SkinnedMeshRenderer` ("RadarSense") with the same mesh, bones and
root bone, so it follows the animation for free. Its material draws with depth test Greater (`Behind`) or Always
(`Blocked`), no depth write, render queue 3100, no shadows, and occlusion culling off. The copies are built the first
time the sense turns on and only switched on/off after that. Enemies come from `encounterSystem.allEnemies`.

**The game's out-of-view hiding** (`ANBBasicNPC.checkVisibilityRelatedActions`): when `!isInView`,
`outOfViewTime >= switchObjectsAfter`, `currentDistanceToPlayer > outOfViewObjectsSaveDist` and not
`isPlayingCustomAnimation`, it deactivates `outOfViewObjects`, sets every `NpcMeshes` renderer `enabled = false`, culls
the animator (`dynamicAnimatorCulled`) and sets `outOfViewObjectsOff`. Once the condition fails it restores all of it
(animator `cullingMode = AlwaysAnimate`). The sense sets `outOfViewTime = 0` every frame for enemies in `Range`, so
the game keeps them shown and animating. Cost: while the sense is on (all the time with `Always`), the game's hiding is off for enemies in range. `Reveal = Awake` turns this off.

**How to measure it:** same map and wave, a few minutes with `RevealUnseen` on, then a few with it off (switch it on the
settings board, it applies at once), and compare the `keeping unseen` and `on` parts of the `perf` lines. For GPU/CPU
headroom below the refresh, use fpsVR or CapFrameX over the same two runs.

**Enemy footsteps** (`Steps.cs`): `ANBFootsteps.HandleNPC` plays a step only while `currentDistanceToPlayer <=
distanceForHearing`, the agent has a path and moves faster than `agentMoveThreshold`. `ANBplayStepSound` picks a clip
by surface (`walkOn` 1 = Metal, 2 = Wood, else Generic) and calls `ANBSFXPlayerManager.PlayAudioClip(clip, pos, false,
type, loud: false, pitch -1, volume -1)` → HurricaneVR `SFXPlayer.PlaySFX` (pitch -1 = `TimePitch`, volume -1 =
`SoundVolumeSFX`). `PlaySFX` shares a small round-robin pool that stops the oldest sound for each new one, and skips a
clip still in its cooldown, so in a fight the steps get cut off. The mod raises `distanceForHearing` to `HearDistance`
and replaces `ANBplayStepSound` for enemies (Harmony prefix): the same clip choice, played with `PlayOneShot` on a
per-enemy `AudioSource` copied from `SFXPlayer.SFXReferenceSource` (mixer group, spatializer, spread), fully 3D,
logarithmic falloff from `LoudRadius`, pitch `TimePitch`. The game's reference source settings are logged once.

## Build

```powershell
dotnet build RadarSense/RadarSense.csproj -c Release -o feature/RadarSense-0.3.1
```

## Checks from the first test plan

1. Silhouettes in **both eyes**? A shader without single-pass stereo support draws in one eye only. If so, try
   `Shader = UI/Default`.
2. Solid colour, or tinted/black patches? Both shaders multiply by the mesh's vertex colours.
3. `Behind`: do visible enemies get red patches where their own body hides a part (an arm behind the torso)? If
   that looks bad, try `Blocked`.
4. The log's `enemy body` lines: anything that should be skipped (eyes, LODs) or is missing.
