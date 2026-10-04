# Roadmap: open work and parked ideas

Only things **not done yet**. What a built mod does is in its README; what changed per version is in [HISTORY.md](HISTORY.md); how
the game works (addresses, mechanisms) is in [GAME_KNOWLEDGE.md](GAME_KNOWLEDGE.md). Refined Oct 4 2026 (backlog session): items
are in priority order within each section; dropped ideas are listed at the end with the reason, so they are not re-proposed. The
framework's own plan is [WeaponFramework/ROADMAP.md](WeaponFramework/ROADMAP.md); the holster base layer's is
[VRHolsterCustomization/PLAN.md](VRHolsterCustomization/PLAN.md).

## 1. Next

**Enemy Awareness Fix: another play session, then improve it.** Play a session with `DebugLog` on, then pick the next meaningful
change from what it shows. Check in that session:
- **Player gunfire and exposure.** An unsilenced player shot runs `playerDetector.Expose()` (visibility full for 3 s) plus
  `GunFireAlert()` (GAME_KNOWLEDGE §6). In play, enemy reactions to your shots are not consistent: sometimes they come, sometimes
  not. Find out why from the logger (`HEARD` / expose lines) before changing anything.
- **Enemy gunfire also exposes the player.** The same `else` branch in `FireBullet` runs for every enemy shot, so enemies shooting
  (at you or anywhere) reset your visibility. Probable game bug; a fix would skip `Expose` when `FromEnemy != 0`. Check that it
  happens in play first, and that the fix doesn't make enemies lose you mid-fight.
- **Door kick loops** (an enemy kicks one door 20-200 times in 10 s, seen in logs, not noticed in play): a logging matter, covered
  by Enemy Awareness Log (`doorLoops`, `longestDoorStreak` in the wave summary). Read the counts after the session; no Fix change
  unless it becomes noticeable in play.
- Story missions / contracts (beyond the Outpost and Restaurant challenge maps) have not been played with the Fix and Physical
  Dodge: check that story enemies still find you eventually (the anti-stall rules) and that bomb waves bring enemies to the bomb.

**VR Holster Customization: better move-by-hand.** Moving the game's holsters is built (`[VRHolsters_Adjustment]`, settings per
slot). Two improvements, because symmetric positions are hard to set one slot at a time:
- **Mirror pairs (do first).** A setting so moving one side moves its pair (left/right hip, knife, back) mirrored across the body.
  Only if that is not enough: grab one slot per hand and move both.
- **Highlight the real holster while moving it**, not only the blue marker sphere: show the slot's own highlight / hologram
  (the game's holster visual) during the gesture so you see where the gun will actually sit.
- 0.3.12 (belt follow) is tested and held for these; consider shipping it first rather than waiting. It answers a YouTube
  request (@PsychoQuokkaStudio, Oct 2026: "the pistol holster follows my body, not where the headset faces"; the reply said it was
  in the backlog), so tell him when it is live.

**Slow Motion Hands → general hand feel** (work-in-progress mod; the name may change before a release). Requests asked for better hand feel overall, not only in slow motion. The glove trails the
real controller (hand lag 16-24 ms plus grip delay on a club; a fast sideways swing can spin the hand body 160-180° when torque
saturates). Build on the Slow Motion Hands work (hand spring/force/damper scaling, the gap log) for normal speed too, behind
settings. The YouTube request (Oct 2026) is about **guns**: pistols and rifles trail the aim when the hand moves, so log the gap
with a pistol and a two-handed rifle, not only clubs. Daredevil's levers (`HandStrengthScale`, `HandTorqueScale`, `ClubInertiaScale`, `ClubMaxSpin`, `HandProbe.cs`) are a
stopgap that should move here once it works.

## 2. Not yet exercised in play

From the retired Nexus upload sheets (Oct 3 2026), still open:
- Aim Colors: `SightBoost` above 1, rifle scopes beyond the collimator, enemy guns (`IncludeEnemies`). Players coming from
  `GunColors.dll` / `LaserColor.dll` must delete the old DLL (section is now `[AimColors]`, old values are not read).
- Death Details: `BleedOut`, `AnyHitEndsPain` (off, "experimental"). Corpse-face load is not measured, so do not headline "cheaper
  than vanilla". `CloseEyes.dll` must be deleted by anyone who had it.
- Heavy Melee: the open-palm strike at 8 m/s (0.4.0) was never played; the last log is from 0.3.0.
- Fire Selector 1.1.0: both `AllowBurst` settings were on the release checklist; confirm they were played.
- Daredevil 1.1.3 (defaults changed, F8 removed).
- The 0.3.1.1 smoke checklist items in [GAME_UPDATES.md](GAME_UPDATES.md) (Outpost culling, Retrieve-lost-weapons with mod items,
  savegame backup).

## 3. Later

**Weapon Framework: a developer fixture for adding weapons.** Not data-only weapon packs: a ready-made fixture a mod developer drops
in so their weapon shows up on the arsenal terminal and can be retrieved, with the weapon's metadata (name, picture, mount pose,
mass, …) in a JSON file next to it. Also open: the pistol (Small Guns) wall (`LoadAssetLoop` builds it with `Pistolset_` /
`_Pistolspot_`); put a club back on its wall slot by letting go near it. Details in `WeaponFramework/ROADMAP.md`.

**Requests from YouTube (@PsychoQuokkaStudio, Oct 2026).** Holster follow and input delay are in §1. The rest:
- **Pistol slide like Half-Life: Alyx.** Today, grabbing the slide of an empty pistol sends it forward at once; he wants it to stay
  back while held and go forward on release. Probably HurricaneVR's slide/gun-part logic (a line grab with a lock), not
  animation; first read how the game moves and locks the slide when empty (`il2cpp_tools`). Same pass: a better grab point for
  the slide, and the double grip. Medium; research first.
- **Props don't come back on Retry / Restart** (pencils, cups on the Dinner Out tables disappear after use; only a new start from
  the base brings them back). Lead (inferred): Retry is a same-scene restart (`resetScene`, GAME_KNOWLEDGE §5), which reloads the
  loadout but does not reload the scene, so used or destroyed props stay gone. Check what `resetScene` resets; a fix would record
  each prop's start pose at scene load and put it back (or re-instantiate it) on restart. Separate from the Weapon Framework /
  holster restore work, though the same hook.
- **NPC grab reactions.** Grabbing some enemy parts makes them go limp. Wish: grab an arm and the enemy stays standing, holds the
  arm, pain face. Large and risky (NPC jank; Death Details already fights the face animator); park until Heavy Melee and Death
  Details are released. **Headbutt** is the cheap part: a head collider through Heavy Melee's hit path.

**Hardcore: no free misses.** On Hard only two rules make enemy bullets harmless (warm-up second, hit-confirm grace; see
GAME_KNOWLEDGE §6), about 1 harmless shot per spawned enemy. A mod = two switches: force `weaponFiredOnce` true (or skip the 1 s wait)
and skip `startEnemyCooldown`; each rule separately configurable. Removing both roughly doubles to quintuples real shots at you
(1.9-6.1/min now), so Physical Dodge is what makes it playable and is the natural host. First step: have Enemy Awareness Log split
harmless shots by cause (warm-up / grace / warning); it counts only the total today.

**Daredevil polish:**
- A real **stun animation for club chest hits**: `ThrowReaction = Stun` only extends the AI stun timer, so the enemy stands there
  with the gun pointed at you; chest hits use Kneel for now. Needs a new or borrowed animator state on the Hit layer (see how Knee
  Shot Stun drives `hit_legs_1`).
- The grip slide through HVR line grabs (GAME_KNOWLEDGE §3), behind a default-off setting; first find which input loosens it.
  A grip switch on a free button (A/X is free on a club hand) is the fallback.
- Optional: custom club hit sounds. Clubs already play the crowbar's physics sound and the game's blunt-weapon hit sounds, which is
  fine. Only if the friend's files arrive (club-on-club clack, body thud/crack, head crack; short dry WAV): play them from
  Daredevil's `ANBBluntWeapon.hit` hook. Club on club has no sound (the game never had two crowbars). Game's clip arrays:
  GAME_KNOWLEDGE §7.
- Daredevil's log is long (`draw check` lines ~250 chars, `club ignores 240 player colliders` twice per spawn): trim next time it
  is touched.

**Radar Sense:** clean outline instead of a filled silhouette (inverted-hull or rim shader with depth test Greater;
`Shader Graphs/Rim Dissolve` might work if it exposes `_ZTest` at runtime, otherwise an AssetBundle from the Unity Editor
6000.0.41f1 + URP).

**Frame-time baseline (parked, still worth doing once).** `FrameProbe/` logs frame timing (no CPU/GPU split). A Sep 29 Range log
averaged ~11.6 ms/frame at a 90 Hz target (p95 12.1-12.3 ms). When there is time: same setup both runs, warm up 2 min, record ≥ 3
min with the runtime overlay (CPU/GPU frame time, p95, reprojected frames); no-mods run (move DLLs out, keep prefs) vs full stack;
one more with the verbose `DebugLog` flags off; if slower, halve the mods to narrow it down. No DLL or preference changes while the
game runs.

**Smaller items:**
- Hitting a grabbed enemy with the other hand stumbles them (the game's `isGrabbed` rule, kept in Heavy Melee); make optional if it
  spoils throws.
- "Invisible shooter": seen once (Physical Dodge 0.2.0), not reproduced; the per-shot `gun N` / `in view` fields in the dodge log
  are there to catch it.
- `ref Vector3 __result` in a `FindRandomNavMeshPosition` prefix is the one Il2Cpp-Harmony pattern not yet proven.

## 4. Flat mode

Done as far as planned: Mod Settings has a flat menu (Ctrl+M), and each mod's Nexus page says whether it is VR-only or also works on
desktop. No flat ports are planned. How flat works: GAME_KNOWLEDGE §8.

## 5. Dropped (Oct 4 2026), with the reason

- **Radar Sense "sound ping" / echolocation sweep / sound reveals:** too complex for the gain; the current Radar Sense works fine.
- **Staff and nunchaku, new melee weapons in general:** the game isn't built for melee, so new melee weapons are expensive. Prefer
  improving the existing ones; revisit if the developer adds melee support.
- **Generic mod slots and moving Daredevil's belt onto them (holster PLAN Phase 3):** "clubs too close to the pistols" is solved by
  moving the game's gun holsters with VR Holster Customization.
- **Heard noise sends enemies to the player, not the noise origin:** this is mostly the gunshot `Expose`, which is expected: shooting
  reveals you. Covered by the player-gunfire check in §1.
- **Flat ports** (Physical Dodge flat branch, Daredevil flat guard, flat melee): see §4.
