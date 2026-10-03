# Roadmap: open work and parked ideas

Only things **not done yet**. What a built mod does is in its README; what changed per version is in [HISTORY.md](HISTORY.md); how
the game works (addresses, mechanisms) is in [GAME_KNOWLEDGE.md](GAME_KNOWLEDGE.md). Cleaned Oct 3 2026: finished items from the old
`IDEAS.md` were removed (holster management → VR Holster Customization; Weapon Framework 0.1-0.3; holstered clubs collide with
nothing → Daredevil 1.1.1 / Grab Fix 1.1.2; Slow Motion Hands). The framework's own plan is
[WeaponFramework/ROADMAP.md](WeaponFramework/ROADMAP.md); the holster base layer's is [VRHolsterCustomization/PLAN.md](VRHolsterCustomization/PLAN.md).

## 1. Measure first

**VR frame-time baseline and mod isolation.** `FrameProbe/` logs frame timing (no CPU/GPU data in this build), so the controlled
comparison is still to run; its numbers are not per-mod attribution. A Sep 29 Range log averaged ~11.6 ms/frame at a 90 Hz target
(p95 12.1-12.3 ms); the holster mod's repeated socket scans were reduced in 0.1.1 and it felt better, but nothing was measured.
1. Same headset, PC restart state, runtime, refresh rate, render resolution, reprojection setting, graphics settings, scene,
   standing spot and view direction. Warm up 2 min, record ≥ 3 min. Record CPU and GPU frame time separately, p95, dropped /
   reprojected frames from the runtime's overlay. At 90 Hz the budget is 11.11 ms.
2. No-mods baseline: back up `Mods` and `UserData/MelonPreferences.cfg`, move all DLLs out (don't delete, don't reset prefs), measure.
   Still includes MelonLoader.
3. Full stack, same DLL set and config; compare distributions, not displayed FPS.
4. With the full stack, turn the verbose `DebugLog` flags off for one run (BillyClubs, ThrowAssist, WeaponFramework, VRHolsters;
   EnemyAwarenessLog logs by design) and check whether spikes line up with log bursts.
5. If the full stack is slower: halve the mods (keep dependent DLLs together), narrow down, then profile update loops, object
   scans, physics calls and logging. No DLL or preference changes while the game runs.

**Not yet exercised in play** (from the retired Nexus upload sheets, Oct 3 2026):
- Aim Colors: `SightBoost` above 1, rifle scopes beyond the collimator, enemy guns (`IncludeEnemies`). Players coming from
  `GunColors.dll` / `LaserColor.dll` must delete the old DLL (section is now `[AimColors]`, old values are not read).
- Death Details: `BleedOut`, `AnyHitEndsPain` (off, "experimental"). Corpse-face load is not measured, so do not headline "cheaper
  than vanilla". Flat mode untested; `CloseEyes.dll` must be deleted by anyone who had it.
- Heavy Melee: the open-palm strike at 8 m/s (0.4.0) was never played; the last log is from 0.3.0.
- Fire Selector 1.1.0: both `AllowBurst` settings were on the release checklist; confirm they were played.
- Weapon Framework: the live page is 0.1.1; later builds need an in-game test with VR Holster Customization and Mod Settings (a
  dependent weapon mod registering and retrieving its entry, save and removal behaviour) before an upload.
- Melee Unlocks `All`; Throw Assist 0.2.4 settings; the 0.3.1.1 smoke checklist items in [GAME_UPDATES.md](GAME_UPDATES.md)
  (Outpost culling, Retrieve-lost-weapons with mod items, savegame backup).

## 2. Known problems, not fixed

- **Club on an enemy makes no impact sound** (Oct 1 playtest), club on club none (the game never had two crowbars). The game's
  `TakeBluntWeaponDamage` should play a random `BluntHit` on every living-enemy hit. First checks: log the club's
  `ANBSoundPhysicsItem` `Material`, `customSounds`, layer mask and `PhysicsClipWait`; whether `BluntHit` is empty or quiet; whether
  Daredevil's damage path skips the call. A layer mask leaving out enemy bodies and other clubs would explain both. Plan, cheapest
  first: (1) fix the native route by setting those fields (club on club then works too); (2) play a clip from Daredevil's
  `ANBBluntWeapon.hit` hook (`BeforeBluntHit`, `Damage.cs`), tiered by speed (5/10/34 damage) and body part; (3) club on club through
  `OnCollisionEnter` when the other collider is a club. Game's call style and clip arrays: GAME_KNOWLEDGE §7. A friend offered to
  make the files: club-on-club clack (soft/medium/hard), club on body (soft thud, hard crack), head (sharper crack), limb (dull
  thud); short WAV, 44.1/48 kHz, reverb tail ≤ 0.4 s, a dry baton "tok", not a sword ring. Also: a **clip-name dump tool** (every
  clip in the arrays by name) to target AudioReplacer files; Death Details' `DebugLog` already prints the clip it plays.
- **Enemies kick one door 20-200 times in 10 s** (worse during Fix searches; every kick alerts everyone). Idea: rate-limit `openDoor`
  per enemy + door. Log 0.5.2 counts door loops.
- **Enemy gunfire exposes the player** (`FireBullet`'s `Expose()` branch runs for every enemy shot). Probable game bug; a fix would
  skip `Expose` when `FromEnemy != 0`.
- **"Invisible shooter"**: seen once (Physical Dodge 0.2.0), not reproduced; the per-shot `gun N` / `in view` fields in the dodge log
  are there to catch it.
- **Heard noise sends enemies to the player, not the noise origin** (GAME_KNOWLEDGE §6): check the logger's `HEARD` lines before
  changing anything; door-kick hunts could become "investigate the door" in the Fix.
- Story missions / contracts (beyond the Outpost and Restaurant challenge maps) have not been played with Enemy Awareness Fix and
  Physical Dodge; check that story enemies still find you eventually (the anti-stall rules) and that bomb waves bring enemies to the bomb.
- Hitting a grabbed enemy with the other hand stumbles them (the game's `isGrabbed` rule, kept in Heavy Melee); make optional if it
  spoils throws.
- Daredevil's log is long (`draw check` lines ~250 chars, `club ignores 240 player colliders` twice per spawn): trim next time it is touched.
- `ref Vector3 __result` in a `FindRandomNavMeshPosition` prefix is the one Il2Cpp-Harmony pattern not yet proven.

## 3. Mod ideas

**Hardcore: no free misses.** On Hard only two rules make enemy bullets harmless (warm-up second, hit-confirm grace; see
GAME_KNOWLEDGE §6), about 1 harmless shot per spawned enemy. A mod = two switches: force `weaponFiredOnce` true (or skip the 1 s wait)
and skip `startEnemyCooldown`; each rule separately configurable. Removing both roughly doubles to quintuples real shots at you
(1.9-6.1/min now), so Physical Dodge is what makes it playable and is the natural host. First step: have Enemy Awareness Log split
harmless shots by cause (warm-up / grace / warning); it counts only the total today.

**New melee weapons: staff and nunchaku** (clubs are done; this is the leftover). Staff: hold both clubs end to end → one long
two-hand grabbable (HVR supports two-hand grips). Nunchaku: link two clubs with a short `ConfigurableJoint` chain; riskiest part is
joint stability against a 20 kg hand body. The game has no nunchucks.

**Daredevil / Radar Sense leftovers** (polish):
- "Sound ping": Radar Sense on briefly when you get hit or on a collision/impact, not only during slow motion.
- Echolocation sweep: a ring expands from you and enemies light up as it passes, then fade. Sound reveals: an enemy that shoots,
  shouts or kicks a door flashes for a moment. Silhouettes fading with distance; a slow heartbeat pulse on the opacity.
- Clean outline instead of a filled silhouette: needs an inverted-hull or rim shader with depth test Greater.
  `Shader Graphs/Rim Dissolve` (the holster hologram) might do it for free if it exposes `_ZTest` at runtime; otherwise a new shader
  needs an AssetBundle built in the Unity Editor (6000.0.41f1 + URP), the only Radar Sense step that would need the Editor.
- Plan: Billy Clubs → "Devil"; throw assist for all weapons as its own mod.
- Club collision SFX: blocked on the friend's audio (see §2).
- A real **stun animation for club chest hits**: `ThrowReaction = Stun` only extends the AI stun timer, so the enemy stands there
  with the gun pointed at you (looks wrong); chest hits use Kneel for now. Needs a new or borrowed animator state on the Hit layer
  (see how Knee Shot Stun drives `hit_legs_1`).
- The grip slide through HVR line grabs (GAME_KNOWLEDGE §3), behind a default-off setting; first find which input loosens it.
  A grip switch on a free button (A/X is free on a club hand) is the fallback.

**Hand feel.** The glove trails the real controller: hand lag 16-24 ms plus grip delay on a club; a fast sideways swing can spin the
hand body 160-180° (torque saturates). Levers already in Daredevil (`HandStrengthScale`, `HandTorqueScale`, `ClubInertiaScale`,
`ClubMaxSpin`, `HandProbe.cs`) are a stopgap; a generic physics-hand mod (or a Grab Fix section after its release) is the right home,
since this is not specific to clubs. Slow Motion Hands already covers the slow-motion half.

**Weapon Framework, open for later:** the pistol (Small Guns) wall (`LoadAssetLoop` builds it with `Pistolset_` / `_Pistolspot_`);
data-only weapons (folder with `weapon.json` + `.obj` + `.png`, based on crowbar / knife / katana); put a club back on its wall slot by
letting go near it. Check the rest against `WeaponFramework/ROADMAP.md` (named slots, removal behaviour, JSON packs).

## 4. Flat mode (audit Sep 29 2026; how flat works is GAME_KNOWLEDGE §8)

Mod Settings 0.3.0 is the only mod with a flat version so far (Ctrl+M IMGUI menu).

| Mod | Flat verdict | Work |
|---|---|---|
| Mod Settings | Done (0.3.0) | test in flat |
| Knee Shot Stun | Probably already works (flat bullets reach `GetHit` through the shared gun code) | one flat test, then say "works in flat" on Nexus |
| Enemy Awareness Fix | Probably works (AI and `PlayerHitTarget` are mode-independent) | one flat test (waves, search) |
| Enemy Awareness Log | Works except the bow line (flat bow skips `ShootArrow`) | optional: hook `BowFPSShootEnd` |
| Radar Sense (in Daredevil) | Probably works as is (same slow-motion state, silhouettes are material copies on enemy renderers) | test; ship a flat-only toggle or run Daredevil with the club part off in flat |
| Physical Dodge | Half works: aim lag runs; the "real dodge" check measures head movement in tracking space, ~0 in flat, so no slow motion | medium, worth it: flat branch counting WASD strafe/crouch/slide as the dodge, with its own `AimLag` (start ~0.08 s) because flat strafing is fast and constant |
| Better Bow | Mostly VR hand fixes; flat bow probably can't breach doors (flat gate `useFPSDoorKick`) | low: door breach via `BowFPSShootEnd` |
| Heavy Melee | Physical VR collisions; flat melee takes the knife-slash path it doesn't hook | low; a different mod |
| Fire Selector | Decided not to build (no free gamepad button; `Weapon.automatic` is the only mode; keyboard B/N/J/K/L/U/I/Z are free) | none |
| Throw Assist, Grab Fix | VR only | none |
| Gloves | Flat arms are LPSP meshes with their own materials | low |
| Daredevil (clubs) | Clubs need hands; flat version = a new flat melee weapon (re-skin the flat knife via the framework) | high, after the framework work |
| Weapon Framework | Panel is shared, flat pickup of a mod slot untested | small now (hide mod entries in flat), large later |

Order: (1) free wins, test only (Knee Shot Stun, Enemy Awareness Fix, Radar Sense: one flat session with `DebugLog = true`); (2) a Daredevil
flat guard (skip club/belt code, keep Radar Sense; otherwise F8 spawns clubs nobody can hold and the belt search runs 30 s every scene);
(3) Weapon Framework hides mod entries in flat; (4) Physical Dodge flat branch; (5) flat melee packs through the flat knife (also the route
to flat clubs). **Flat test session:** start without the headset; Ctrl+M in The Range, page the arsenal to the mod entry and try to take
it; in a contract shoot legs (kneel line), press the slow-motion key (Radar Sense), strafe under fire (aim-lag misses logged, no dodge slow
motion expected), run a wave (search lines).
