# Mod ideas (not started)

Parked ideas with what the game code does, so a later session can pick them up without
re-reading GameAssembly. Addresses and defaults are from the current build (Unity 6, IL2CPP) and
are code defaults: prefabs and scenes can override them.

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

## Physical dodge

The idea: moving or crouching at the right moment should really make enemy shots miss, instead of
the timers above deciding.

How enemies shoot now:
- `<fireWeaponExec>` takes a snapshot of `attackTarget.position` (or `lastKnownPosition` when the
  enemy can't see you, i.e. blind fire) at trigger time.
- It aims from `gunMuzzleFixed` with spread from `getShotErrorRate`, then calls
  `ANBHVRGunBase.EnemyTriggerPulled(source, direction, spread)`.
- The bullet is simulated (`ANBBulletManager`, `bulletSpeed` 350 m/s), so it takes 15-30 ms to
  cover a room. Nobody can react to that.

Plan:
- A Harmony prefix on `EnemyTriggerPulled` re-aims the shot at where your hit target was
  `AimLag` seconds ago (around 0.2 s, from a ring buffer of `PlayerHitTarget` positions), keeping
  the game's spread.
- Standing still gets you hit. Crouching, stepping or leaning as they fire makes them miss.
- Optionally combine it with the hardcore settings above, so real movement replaces the free
  passes.

Open question: does `PlayerHitTarget` follow your head down when you crouch? Enemy Awareness Log
0.4.0 logs hit-target height against head height to answer it.
