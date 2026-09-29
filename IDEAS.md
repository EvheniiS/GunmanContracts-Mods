# Mod ideas (not started)

Parked ideas with what the game code does, so a later session can pick them up without
re-reading GameAssembly. Addresses and defaults are from the current build (Unity 6, IL2CPP) and
are code defaults: prefabs and scenes can override them.

## VR frame-time baseline and mod isolation (Sep 29 2026)

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

