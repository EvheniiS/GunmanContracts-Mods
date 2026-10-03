# Mod ideas (not started)

Parked ideas with what the game code does, so a later session can pick them up without
re-reading GameAssembly. Addresses and defaults are from the current build (Unity 6, IL2CPP) and
are code defaults: prefabs and scenes can override them.

## VR frame-time baseline and mod isolation (Sep 29 2026)

`FrameProbe/` now implements the timing logger and optional Jev log filter (Sep 30); the controlled in-game comparison below remains to be run. Its measurements are not per-mod attribution.

The Sep 29 Range log averaged about 11.6 ms per frame during steady play (roughly 86 FPS at a
90 Hz target); its 95th percentile was about 12.1-12.3 ms. The holster mod's repeated socket scans
and duplicate per-frame position pass were reduced in 0.1.1, and the next subjective test felt better,
but there is no measured before/after comparison yet. Keep this as a controlled test, not a conclusion
that one mod caused the shortfall.

1. **Prepare a repeatable scene.** Use the same headset, PC restart state, VR runtime, refresh rate,
   render resolution, reprojection setting, game graphics settings, scene, standing location and view
   direction. Warm up for two minutes after loading, then record at least three minutes of steady play.
   Record CPU and GPU frame time separately, 95th percentile, dropped/reprojected frames and effective FPS
   from the VR runtime's frame-timing overlay. At 90 Hz the frame budget is 11.11 ms. Save the game log
   and note the game/mod versions for every run. Avoid menus and scene transitions in the measured window.
2. **No mods baseline.** Close the game, back up the current `Mods` DLL list and
   `UserData/MelonPreferences.cfg`, then temporarily move all mod DLLs out of `Mods` to a named backup
   folder. Do not delete them or reset preferences. Restart the game and measure the same scene. This
   still includes MelonLoader overhead; a separate run without MelonLoader can distinguish that if needed.
3. **Full stack.** Close the game, restore the exact same DLL set and config, restart, and repeat the
   measurement. Compare frame-time distributions, not just the displayed FPS. Repeat a run if headset
   reconnects, thermal state, reprojection, or scene behavior changed.
4. **Debug overhead.** With the full stack installed, turn off verbose `DebugLog` flags for a separate
   run and change nothing else. Currently true in the live config: `[BillyClubs]`, `[ThrowAssist]`,
   `[WeaponFramework]`, and `[VRHolsters]`; `[EnemyAwarenessLog]` logs by design. Restore the original
   config after this comparison. Check log volume and whether spikes line up with bursts of messages.
5. **Isolate any measured regression.** If the full stack is slower than the no-mod baseline, test
   half the mods at a time, keeping dependent DLLs together, then narrow the affected group. Finally
   test the suspect mod alone or with only its dependencies. Profile its update loops, object scans,
   physics calls and logging before changing code. Re-run the full stack after a fix to verify the gain.

For each run, record: DLL set and versions; debug flags; runtime/render settings; CPU median and p95
frame time; GPU median and p95 frame time; dropped/reprojected frames; FPS; log path; and any headset
disconnect or scene event. No DLL or preference changes should be made while the game is running.

## Hardcore: no free misses

Players complain that "the first shot always misses". It's less noticeable on Hard. The game
makes an enemy bullet harmless in these cases:

- **Warning shot.** `ANBBasicNPC.checkMissShot`, per shot, until the enemy's `weaponFiredOnce`
  is set:
  - misses if `firstShotMiss` (per difficulty: `firstShotMissEasy/Normal/Hard`) is on and
    difficulty <= 1;
  - misses if you can't see the enemy (`isInView`: renderer visible and not blocked);
  - misses beyond `noFirstShotMissDistance` (3 m) except on difficulty 3.

  The result goes to `nonLethalFire`.
  - **Difficulty numbers (from `ANBBasicNPC.SetDifficulty`): 1 = Easy, 2 = Normal, 3 = Hard.** The ctor sets
    `firstShotMissNormal/Hard` = true, `resetFirstShotTime` Easy/Normal/Hard = 5 / 8 / 15 s (prefabs may override).
  - **So on Hard the warning shot only happens when the enemy is out of your view** (behind you, or blocked).
    In view it is a real shot at any distance; Normal adds the warning beyond 3 m, and Easy always has it.
  - **On Hard it adds no harmless shots of its own:** `checkMissShot` only returns true while `!weaponFiredOnce`
    or `InCooldown`, and both already make the bullet harmless. The rules that matter on Hard are the warm-up
    second and the hit-confirm grace (checked Sep 27 2026).
- **Warm-up second.** `setWeaponFiredOnce` waits 1.0 s after the first shot before setting
  `weaponFiredOnce`, so every shot in that first second is harmless.
  `checkVisibility` clears `weaponFiredOnce` again after the enemy has been out of your view, and
  you out of its sight, for `resetFirstShotTime`.
- **Hit-confirm grace.** When your bullet damages an enemy (`ANBGameLogic.BulletImpact`), or you
  stab or grab one, `ANBBasicNPC.encounterCall` runs `ANBEncounterSystem.startEnemyCooldown`:
  - `EnemyCooldownActive` stays on for `EnemyCooldownTime` (2 s, scaled by game time scale).
    While it is on, **every** enemy's shots are harmless (`InCooldown`, read in `checkAbilities`).
  - Each enemy can re-trigger it at most every `EnemyCooldownSendPause` (4 s).
- **How "harmless" works.** `ANBHVRGunBase.FireBulletNew` passes
  `enemyBulletCooldowned = InCooldown || nonLethalFire || !weaponFiredOnce` to
  `ANBGameLogic.FireBullet`. That bullet uses `HitLayerMaskEnemyCooldowned` instead of
  `HitLayerMaskEnemy`. Not yet confirmed in game, but most likely that mask leaves out the player,
  so the bullet flies through you.

Possible mod: settings to turn each rule off separately (force `weaponFiredOnce` true, skip
`startEnemyCooldown`, make `checkMissShot` return false). Enemy Awareness Log 0.4.0 counts
harmless enemy shots per wave, so the effect can be measured first.

## New melee weapons: staff / nunchaku (billy clubs done — this is the leftover)

Billy Clubs (now part of Daredevil) shipped the club shape, throw assist, ricochet, return-to-hand and an
authored Blender model. The staff/nunchaku join option was deliberately deferred and is still unbuilt:
- **Staff:** hold both clubs end to end → replace them with one long two-hand grabbable (HVR supports
  two-hand grips).
- **Nunchaku:** link the two clubs with a joint (a short `ConfigurableJoint` chain). Riskiest part: joint
  stability against a 20 kg hand rigidbody.

## ★★ HIGHLY REQUESTED: holster management (Sep 29 2026)

Players want to choose where their holsters sit. Plan (details in [WeaponFramework/ROADMAP.md](WeaponFramework/ROADMAP.md) section 3):
- **Move and resize every holster from the Mod Settings board**, the game's own too (hip, knife, back/shoulder sockets are
  transforms under the rig: `Waist/Holsters`, `holsterGunBackLeft/Right`), height and forward/side offset, live-applied.
- **Framework items in the BACK holsters.** The game's back sockets call `ANBGameLogic.holsterGun(side, ANBHVRGunBase)`,
  which assumes a gun; a melee item needs that call skipped and its own saved state.
- **Named slots** for mod items so two mods never fight over one spot. Also fixes Billy Clubs' belt holsters sitting
  too close to the pistol hip holsters.

## Weapon Framework: custom weapons on the arsenal panel (Sep 28 2026)

**Built:** [WeaponFramework/](WeaponFramework/README.md) 0.1.0 + Daredevil 0.2.0 (`Daredevil/BillyClubs/Arsenal.cs`); findings in
GUNMAN_CONTRACTS.md. **Plan and order of work: [WeaponFramework/ROADMAP.md](WeaponFramework/ROADMAP.md)** (Sep 29 2026:
removal behaviour, shared plumbing, named holster slots, JSON packs, flat mode). Open for later:
- The pistol (Small Guns) wall: `LoadAssetLoop` builds it differently (`Pistolset_` / `_Pistolspot_`), not supported yet.
- Move the belt holsters, carry-over and recall out of Billy Clubs into the framework, so other weapons get them.
- Data-only weapons (folder with `weapon.json` + `.obj` + `.png`, based on crowbar / knife / katana) for non-coders.
- Put a club back on its wall slot by letting go near it (like the game's guns).

## Long-term backlog: a real stun animation for Billy Clubs chest hits (Sep 28 2026)

A thrown club to the chest should stagger/daze the enemy with an animation, not just freeze them. Billy Clubs 0.8.2's
`ThrowReaction = Stun` only extends the AI stun timer (`overallHitTime` / `isGettingHit`), so the enemy stands still
with the gun pointed at you, which looks unnatural. It would need a new or borrowed animator state on the "Hit" layer
(see how Knee Shot Stun drives `hit_legs_1`). Until then, chest hits use Kneel (or Knockdown).

## Daredevil / Radar Sense / Throw Assist — leftover ideas (Sep 28 2026)

Daredevil (clubs + red gloves), Radar Sense (enemy wall vision, dropped-club detection) and Throw Assist
(pistol aim assist + thrown-pistol damage) are all **built and tested well**. What's left is unbuilt polish:

**To-do (Oct 3 2026):**
- **DONE in Daredevil 1.1.1 + Grab Fix 1.1.2 (built Oct 3 2026, untested).** Holstered clubs on the hips must have no collision. Before, they keep their colliders while holstered (the
  rigidbody only goes kinematic, `BillyClubs.cs:413`), so the bow hand grabs/bumps them and enemies collide with
  them. Turn the club colliders off (or put them on a non-colliding layer) while holstered and back on when drawn,
  the way the game's own holstered weapons behave. Check first what the game does to a holstered gun's colliders
  and copy that; the draw must still work (hand-hover detection may rely on a collider).

- **Daredevil "sound ping"**: turn Radar Sense on briefly when you get hit or on a collision/impact, instead of
  only during slow motion.
- **Echolocation sweep**: a ring expands from you and enemies light up as it passes them, then fade.
- **Sound reveals**: an enemy that shoots, shouts or kicks a door flashes for a moment (Daredevil "hears" them).
- Silhouettes fading with distance; a slow heartbeat pulse on the opacity.
- **Clean outline instead of a filled silhouette** (edge only, not the whole body): needs an inverted-hull or
  rim shader with depth test Greater. `Shader Graphs/Rim Dissolve` (the holster hologram) might do it for free
  if it exposes `_ZTest` at runtime; otherwise a new shader needs an AssetBundle built in the Unity Editor
  (6000.0.41f1 + URP) — the only step in Radar Sense that ever needed the Editor.
- **★ Low priority — club collision SFX.** Play a custom sound on club hits (wall/enemy ricochet, catch). Blocked
  on a friend making the actual SFX; nothing to build until the audio exists.


## Billy Club impact sounds (Oct 1 2026, not started)

All sound findings (clip arrays, play call, AudioReplacer, silent spots) are collected in [SOUND.md](SOUND.md).

Playtest: club swings feel good, but two impacts are silent or unclear. A friend has offered to make the sound files.
- **Club on club:** no sound. The Range has one crowbar, so the game never needed a crowbar-on-crowbar clip.
- **Club on an enemy:** no impact sound. Club on a wall does play one, so the crowbar's own collision sound still works there.

What the game has (from `il2cpp_tools/dumpt.py`, nothing tested yet):
- **`ANBSoundPhysicsItem`** is the collision-sound component on physics props (on the crowbar copy, so on the clubs): `OnCollisionEnter`
  picks a clip by `PhysicsType Material` and impact strength tier, or from `customSounds` with `overrideSoundsSoft/Medium/Hard`
  (AudioClip arrays). `useLayerMaskOverride` + `layerMaskOverride` limit which layers make a sound, `noiseOnSoft/Medium/Hard` and
  `canAlert` decide whether it alerts enemies. A layer mask that leaves out enemy bodies and other clubs would explain both gaps.
  First check: log the club's `Material`, `customSounds`, layer mask and `PhysicsClipWait` on a template build.
- **`ANBBluntWeapon.hit(bodyPart, speed, npc, collision)`** is the enemy hit (damage, blood, stagger). Daredevil already hooks it
  (`BeforeBluntHit`, `Damage.cs`) and knows the speed tier (slow/medium/fast = 5/10/34 damage), body part and whether it was a
  head hit. It is the natural place to play an enemy-impact clip.
- **Playing a clip the game's way:** `RadarSense/Steps.cs` already does it (`ANBSFXPlayerManager.PlayAudioClip` or its own
  AudioSource with the game's mixer group), so the volume settings still apply and slow motion pitches it.
- **The game's own enemy melee sounds (found Oct 2 2026 for Death Details):** `ANBGameLogic.BluntHit` / `BluntHitHead` /
  `FleshKnifeHit` / `FleshKnifeSlashHit` / `WoodHit` clip arrays. `TakeBluntWeaponDamage` plays a random `BluntHit` with
  `ANBGameLogic.PlayAudioClip(clip, part position, false, "default", false, -1, -1)`. So "club on an enemy: no impact
  sound" is odd: that call should run on every club hit on a living enemy. Check whether `BluntHit` is empty or the clip
  too quiet, or whether Daredevil's damage path skips it. These arrays are also the natural slot for the friend's clips
  (swap the entries at runtime). Death Details 0.7.1 already plays `BluntHitHead` on its finishing head hit.
- Third-party `AudioReplacer` is installed (`UserData/CustomAudio`); its folder format could carry the files without a new loader,
  but our own clips should ship inside the DLL or next to it so the mod stays self-contained.

Plan, cheapest first:
1. Find out why the native sound is silent (layer mask / material) and fix it by setting the club's `ANBSoundPhysicsItem` fields.
   Club on club then works too (both clubs carry the component).
2. If the native route can't make an enemy impact sound, play one from the `hit` hook, tiered by speed and body part (head, torso, limb).
3. Club on club through `OnCollisionEnter` when the other collider belongs to a club, volume by relative speed.

**Sound list for the friend** (suggestion, short mono or stereo WAV, 44.1 or 48 kHz, no reverb tail beyond 0.4 s so the game's
room sound adds it): club-on-club clack (3 strengths: soft, medium, hard), club on body (soft thud, hard crack), club on
head (sharper crack), club on limb/leg (dull thud). Metal tip and burgundy rubber grip: a dry baton "tok", not a sword ring.

## Hand feel: input lag of the glove behind the real controller (Oct 1 2026, measured, tuning open)

See the section in `GUNMAN_CONTRACTS.md` of the same date. Where the code should live is open: Grab Fix is pickup
detection only (1.1.0 release candidate); this is the generic physics hand, so a small separate mod (or a Grab Fix section
after its release) fits better than Billy Clubs. It is in Daredevil (`BillyClubs/HandProbe.cs`) for now so it can ship with the test builds.

## Not yet exercised in play (kept from the retired Nexus upload sheets, Oct 3 2026)

- **Aim Colors:** `SightBoost` above 1, rifle scopes beyond the collimator, enemy guns during a contract (`IncludeEnemies`). Players coming from `GunColors.dll` / `LaserColor.dll` must delete the old DLL; the section is now `[AimColors]` and old `[GunColors]` values are not read.
- **Death Details:** `BleedOut` and `AnyHitEndsPain` (off by default, "experimental" on the page). Corpse-face load is not measured, so do not headline "cheaper than vanilla" without a measurement. Flat mode untested: do not claim it on the page (the head-hit finish needs VR weapons). `CloseEyes.dll` must be deleted by anyone who had it; the section is now `[DeathDetails]`.
- **Heavy Melee:** the open-palm strike at 8 m/s (added in 0.4.0) was never played; the last log is from 0.3.0.
- **Fire Selector 1.1.0:** `AllowBurst = false` (auto, single, auto; a weapon saved on burst) and `AllowBurst = true` (auto, burst, single) were both on the release checklist; confirm they were played.
- **Weapon Framework:** the live page is 0.1.1; later builds need an in-game test with VR Holster Customization and Mod Settings (a dependent weapon mod registering and retrieving its entry, save and removal behaviour) before an upload.
