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

## Weapon Framework: custom weapons on the arsenal panel (Sep 28 2026)

**Built:** [WeaponFramework/](WeaponFramework/README.md) 0.1.0 + Daredevil 0.2.0 (`Daredevil/BillyClubs/Arsenal.cs`); findings in
GUNMAN_CONTRACTS.md. Open for later:
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

