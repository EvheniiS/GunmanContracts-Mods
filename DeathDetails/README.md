# Death Details

*Good night, gentlemen.* (Was **Close Eyes** until 0.7.0.)

Dead enemies close their eyes, and the mouth stays slightly parted instead of gaping. Enemies writhing in pain on the floor keep their pain face and close their eyes when they
die; a melee hit to the head (club, crowbar, pistol whip, fist) finishes them like a headshot. Optional, experimental: they bleed out instead of writhing forever, and any hit finishes them.

**Status: 0.7.1 RELEASE CANDIDATE (Oct 2 2026), all tested in game:** eyes close (`JawOpen` 0.1, mouth slightly
parted), pain face while writhing, corpse faces frozen (cheaper than vanilla), a melee hit to a writhing enemy's head
finishes it with the game's head-hit sound. `BleedOut` and `AnyHitEndsPain` stay **off by default and untested**.
History: 0.1.0 did nothing; 0.2.0 first working; 0.4.0: writhing never ends and leg shots are ignored (vanilla);
renamed from Close Eyes at 0.7.0.

**Load:** a writhing body that never dies keeps its ragdoll (PuppetMaster), body animator, face animator and face sync
running in vanilla. `BleedOut` ends it through the game's own `killTwitcher`, which switches the body animator off, so
it should lower the load, not raise it; its own cost is one field read per corpse every 0.1 s. Not measured yet.

## How it works

The faces carry ARKit blend shapes, so `eyeBlinkLeft` / `eyeBlinkRight` close the eyelids. The game drives face
shapes itself: `ANBBasicNPC.faceAnimator` (`PlayFaceAnim`) animates them, and `ANBBlendShapeSync` copies its
`mainMesh` weights onto `syncMeshes` / `otherMeshes` in `UpdateExec`. **The game overwrites the
eyelids every frame** (0.2.0 log: 232/232 and 231/232 frames), so 0.1.0's one-time set could never show.

**0.3.0 (optimised):** `UpdateExec` (one caller: `ANBBasicNPC.checkVisibilityRelatedActions`, per enemy) reads all
147 weights off `mainMesh` and copies them onto every other face and beard mesh; `faceAnimator` animates `mainMesh`.
So when the eyelids start to close (after `Delay`), the mod switches that corpse's face animator off and skips its
sync (`UpdateExec` prefix), eases the eyelids shut on every face mesh, and then leaves them. A shut corpse costs one
`GetBlendShapeWeight` per frame (its main face); if something changed it, the eyelids are written again and the
animator stopped again (logged once per level after 10 rewrites). **A corpse now costs less than in vanilla**, where
the face animator and the 147-shape copy keep running on every dead face. The face keeps the expression it had when
closing started. On pool revive: animator back on, sync back, original weights restored. Switching `Enabled` off
restores all faces within 0.1 s.

(0.2.0: wrote the eyelids every frame on every dead face, after the game's own writes; worked, cost grew with the
number of corpses.)

**0.4.0, two death states ("twitcher"):** a lethal hit sets `isTwitcher` (in `TakeDamage`) when the enemy is in
combat (`combatModeActive`), it is not a headshot and it has at most 2 torso hits / 1 chest hit (`torsoHit`/`chestHit`
counters): the body-shot kill. `isDead` is already true, but it lies writhing: `startTwitcher` (after
`twitcherWaitTime`) plays the twitcher animation, face `face_twitcher` and pain sounds. It ends when `killedTimer >
twitcherSurvivalTime` (random `twitcherTimeTillDeathMin..Max`), after `killTwitcherAfterOutOfSightTime` out of view,
or when shot again; `killTwitcher` plays `twitcherKill`, clears `isTwitcher` and plays face `face_death`.
(`forcedTwitcher` on `ANBNpcPuppetmasterSettings` = 2 forces it, 1 forbids it.) With `WaitForTwitch` on, the mod
leaves the face to the game while `isTwitcher` is set and closes the eyes `Delay` s after it clears.

**0.5.0, bleed-out (`BleedOut` / `AnyHitEndsPain`, off by default):** in play the writhing never ended. The game's bleed-out check (`killedTimer >
twitcherSurvivalTime`, or `outOfViewTime > killTwitcherAfterOutOfSightTime`) lives in `ANBBasicNPC.UpdateExec`, which
`ANBUpdateCentral.UpdateCalls` only calls for enemies in `encounterSystem.allNpcs` with `NPCSpawned && NPCFullySetup`;
which of these drops the corpse is not known yet. And `TakeDamage` ends it only on a non-limb hit more than 1.75 s
after death: a leg hit only pushes the body and plays a hurt sound. The mod now: past the game's own random time
(`twitcherSurvivalTime`, or `MaxPainSeconds`) + 0.5 s, calls `forcePuppetMasterActive(1, false)` +
`ANBPM.killTwitcher(noHit: true)`, exactly what `UpdateExec` would; a `TakeDamage` that leaves the enemy writhing
calls `killTwitcher(false)`, what a torso shot does. The first 3 forced bleed-outs per level log the game's numbers
(killedTimer vs survival, out-of-view vs limit, in `allNpcs`?, spawned, setup), which say why the game didn't.

**0.7.0, melee head hits on a writhing enemy:** every melee contact goes through
`ANBBodyMeleeCollision.OnCollisionEnter`, which always forwards to `ANBBodyMeleeCollisionManager.collisionEnter` (also
for dead bodies; Unity sends collision events to disabled scripts too). The game's handler, Heavy Melee and Daredevil
all skip dead bodies, so a club or pistol whip to a writhing enemy's head did nothing; only a headshot (`TakeDamage`)
ended it. The mod adds a `collisionEnter` postfix: if that body is writhing (`isTwitcher && isTwitcherStarted &&
!isKillingTwitcher`), the part is a head (`ANBBodyMeleeCollision.isHead`), the contact moves at `HeadHitSpeed`+ and
comes from the player (an `ANBBluntWeapon` (clubs, bats, crowbar, swung or thrown), a held gun or bow, or a
`HVRHandGrabber`), it calls `ANBPM.killTwitcher(false)` like a headshot. 0.7.1: the game's melee hit sound also
only plays for living targets (`TakeBluntWeaponDamage` / `TakeMeleeDamage` play a random `ANBGameLogic.BluntHit`
clip via `ANBGameLogic.PlayAudioClip(clip, part position, false, "default", false, -1, -1)`), so the finishing hit
plays a random `ANBGameLogic.BluntHitHead` clip (`BluntHit` if empty) the same way. **Load:** writhing bodies are registered (by
body manager) in a small table from the 0.1 s poll; any other contact costs one lookup, and the table is empty most of
the time. Each dead body is polled (one field read every 0.1 s) only until it is known not to be writhing (1 s) or
the writhing ends.

**Runtime facts (0.2.0 log):** sync `mainMesh` = `CC_Combined_LOD0` (mesh `Male_Combined_LOD0`); blink shapes
are indices 19/82 on `CC_Combined_LOD0-2` and on beard meshes (`Beard_04_LOD*`, 147 shapes too, so beards follow
the eyelids). **`CC_Combined_LOD3/4` have no blend shapes**, so far-away bodies keep their eyes open (too far to see).
Death comes through `KillNPC` with `deathAnimation` = 'no death animation'; `faceAnimator` = 'FaceAnimator'.

**Diagnostics (`DebugLog`):** the first 3 deaths of each level are logged in detail: death route, `isDead`, death animation, face
animator, sync main mesh, and every mesh with blend shapes (blink indices, weight; meshes without `eyeBlink*` list
their eye-ish shape names). About 3 s after each of those deaths there is one line, `eyes closed; the game overwrote
them in N/M frames`, or `NOT closed: no mesh with eyeBlinkLeft/Right`.

## Settings (`[DeathDetails]`; was `[CloseEyes]` before the rename)

- `Enabled` (true)
- `WaitForTwitch` (true): leave the pain face alone while an enemy lies writhing; eyes close when it stops.
- `HeadHitEndsPain` (true): a melee hit to the head of a writhing enemy finishes it. `HeadHitSfx` (true): with the
  game's own blunt head-hit sound. `HeadHitSpeed` (2 m/s): how
  fast the weapon or hand must hit.
- `BleedOut` (false, experimental): writhing enemies die after the game's own random bleed-out time.
- `MaxPainSeconds` (0 = the game's own time): with `BleedOut`, longest an enemy writhes.
- `AnyHitEndsPain` (false, experimental): any hit finishes a writhing enemy, leg shots included.
- `EyesClosed` (1.0): 1 = fully shut, 0.8 = a slit left open.
- `JawOpen` (0.1): how far the mouth stays open (0 closed, 1 fully open); -1 = leave the game's death face jaw. The
  game's `face_death` often leaves the jaw wide open; this eases `jawOpen` (also on beards) from there to the setting
  over `CloseSeconds`, once, when the face freezes. No per-frame cost.
- `CloseSeconds` (0.6), `Delay` (0.3).
- `DebugLog`: one line per death and one when the eyes are shut (face animator off, sync skipped); the first 3 deaths
  per level list every mesh with blend shapes.

## Test

1. Headshot: eyes close right away; the mouth eases to slightly parted (`JawOpen` 0.1). Near and far.
2. Body shot in combat: the enemy writhes with its pain face; the eyes close only when it stops (`DebugLog`: `lying in
   pain, eyes wait` / `pain over after N s`).
3. Hit a writhing enemy's head with a club (swung and thrown), the crowbar, a pistol (Heavy Melee) and a fist: each
   finishes it with a head-hit sound (`DebugLog`: `<weapon> to the head at N m/s while in pain, finishing it, sound
   BluntHitHead/<clip>`). Body and leg hits don't.
4. Walk away from the bodies and back: eyes stay shut; no `something keeps reopening the eyes` line in the log.
5. Experimental, with `BleedOut` + `AnyHitEndsPain` on: a writhing enemy stops on its own (log: `still in pain N s
   after death, ending it` plus the game's numbers; send that line), and a leg shot finishes it.
6. Next wave: pooled enemies come back with open, animated faces.
