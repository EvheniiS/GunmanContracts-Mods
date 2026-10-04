# Mod history

One short changelog per mod, oldest first, written Oct 3 2026 from the research log that used to be `GUNMAN_CONTRACTS.md`
(the full text is in git: `git show d0a7858:GUNMAN_CONTRACTS.md`). It records **what each version changed and why**, not
whether it was played: the test state is the `Tested:` line in each mod's README (skill `mod-status`), and what is live on
Nexus is `Tools/Check-Nexus.ps1`. How the game works: [GAME_KNOWLEDGE.md](GAME_KNOWLEDGE.md). Open work: [ROADMAP.md](ROADMAP.md).

Repository rules (settled Sep 25-26 2026): one git repo, one folder per mod; two branches only, `main` = what is on Nexus, `dev` =
unreleased; never feature branches; one checkout, normally on `dev`, so only one session works in it at a time; releases are marked
with per-mod tags (`betterbow-v1.1.0`). GitHub: `EvheniiS/GunmanContracts-Mods`.

## Better Bow (BetterBow/)
Merged from `ArrowGrabAssist` and `ArrowQuiver` (remove their DLLs; old cfg sections are dead).
- **First-press string grab** (Sep 24): the "grip twice for the next arrow" bug is an unbuffered, edge-triggered grab; a 0.35 s press
  buffer within 0.15 m of the string. 55/55 fast grabs landed.
- **One-arrow barrels** (`ExplosiveArrowsDetonateBarrels`): arrow damage 100 vs barrel health 250 took three arrows.
- **Quiver** (v0.1 draw/nock/stab): the socket the bow was holstered in is the quiver; no periodic scene searches (a 2 s
  `FindObjectsOfType` is a hitch every 2 s) — register through a `Start` postfix.
- **Arrows breach doors** (`ArrowsBreachDoors`, calls `TryKickDoor` on a shot). **1.0.0**: all merged; quiver reach also measured
  from the pose the physics hand chases.
- **1.1.0 (released Sep 24)**: throw assist for carried quiver arrows (adds the game's `ANBAssistedThrowingObject`, tuning copied
  from a real knife; needs the game's assisted-throw option on). 1.1.1: `isHoming` ctor-default guard bug. 1.1.2: the dagger grip's
  cloned `ANBWristHud` stole the wrist-HUD slot and broke the pause menu (see GAME_KNOWLEDGE §2). 1.1.3-1.1.5: thrown arrows stuck
  without damage because the stab ray starts inside the hit zone; fixed by moving the stab origin behind the tip and choosing the
  first real hit zone. Dagger tip "Down" mapping fixed in 1.1.1.
- **1.2.0**: arrows fall short because shot speed is quadratic in the pull; `FullDrawAt` 0.9 rescales the curve,
  `ArrowSpeedMultiplier`.
- **1.3.0**: bow hand lock (`BowHandSwap` false sets the bow's `HoldType` to OneHand so the other hand can't steal it).
- Paint: the game ships a black bow (Gun Edit Table, one spray can). Model exported for remodelling (see GAME_KNOWLEDGE §4).

## Knee Shot Stun (KneeShotStun/)
1.0.0 (Sep 25, released): leg-shot enemies stay down longer. A Harmony prefix on `GetHit` extends `endLegHitAfter` and the mod holds
the animator's Hit layer inside the kneel segment (0.85-1.5 s of `hit_legs_1`) for `ExtraKneelSeconds`; while it holds, `overallHitTime`
and `isGettingHit` are kept up so a follow-up hit can't shorten the stun.

## Fire Selector (FireSelector/)
1.0.0 (Sep 25, released): hold support-hand A/X ≥ 0.35 s to cycle Auto → Burst → Single (haptics 1/3/long). The mod holds back the
game's A press and replays it on a short tap; per-weapon mode remembered. 1.1.0 (Sep 27): `AllowBurst` (a Nexus request).
The flat version was assessed and not built (no free gamepad buttons).

## Heavy Melee (HeavyMelee/)
1.0.0 (= 0.4.0 + `DebugLog` off; release undecided). Guns and bows hit like heavy metal (damage ×3, always stumble on a standing
enemy), fists ×2, a downed enemy still takes hits and is held down (`getUpDelay` raised), a limb hit that would take health below 0
knocks out. Iterations: 0.2.0 `MaxDamagePerHit` 50 and the double-hit fix (the game-path punch must start the per-enemy cooldown);
0.3.0 `MinPunchSpeed`/`MinWeaponSpeed` 3 m/s, `OpenPalmNoHit` (grip ≥ 0.5 = a fist), a hand holding something never punches,
`IgnoreKickedObjects` (debris shards pushed by the enemy's own feet were counting as hits); 0.4.0 `OpenPalmStrikeSpeed` 8 m/s.

## Enemy Awareness Fix / Log (EnemyAwarenessFix/, EnemyAwarenessLog/)
The log (diagnostic, session file `UserData\EnemyAwarenessLog\session-*.log`) proved the live-tracking leak (GAME_KNOWLEDGE §6) and
measures any session (`awareness_stats.py`, `BASELINE_WITH_FIX.md`). The fix (named Stealth AI Fix on the index):
- 0.1.0: per-enemy belief frozen 1.5 s after losing sight, rough guess (3-8 m) for never-seen wave enemies, shared sightings within
  25 m, search around the belief, random pick among the 3 nearest valid spawn points, min spawn distance 10 m.
- 0.1.1: guess/search points need a complete NavMesh path; "arrived" or stood still 3 s; search starts after 30 s at the latest.
- 0.1.2: body position (`agentTransform`/`visionBase`) instead of the static root; hooks `spawnNPC` + `spawnPointValidation` because
  dynamic spawners never call `getSpawnPoint`. 0.1.4: floor-aware (no hearing through a floor, no "arrived" on the wrong floor).
  0.1.5: `ShareMaxAge` 5 s (a stale sighting from the previous wave sent the first spawns to where you had been). 0.1.6: lean logs.
- Result: vanilla 26 live-tracked : 0 searched; with the fix 2 : 18. Playing without it was judged "BAD". Log 0.5.2 counts door
  loops. Parked: rate-limit `openDoor` per enemy+door; skip `Expose` for enemy shots.

## Physical Dodge (PhysicalDodge/)
0.1.0 dodge windows did not feel right (coverage). 0.2.0: no windows; every enemy bullet whose line passes within 1 m is re-aimed at
where the player's head was `AimLagSeconds` (0.2 s) earlier. 0.3.0: a real dodge also starts the game's own timed slow motion
(haptics barely come through grips). 0.4.0: classification inside the `FireBulletNew` prefix, a dodge is pushed out to clear all
three player colliders by 10 cm (`DodgeMargin`), `SlowMotionCooldown` 1.5 s. 0.4.2: rushing a shooter converted head bob into a
downward push; now always horizontal. In practice a dodge needs ~1 m/s of head movement at the moment they fire.

## Daredevil (Daredevil/) — Billy Clubs + Radar Sense + the club package
The one place club, glove-colour and radar-sense code is developed (since Sep 28); standalone Billy Clubs 0.12.0 and Radar Sense
0.3.1 are legacy (git history). Config sections stay `[BillyClubs]` / `[RadarSense]`. Requires Gloves, Throw Assist, Mod Settings and
Weapon Framework.
- Billy Clubs 0.1-0.3 (Sep 27): copies of `Prop-Bluntweapon-Crowbar` (the Range crowbar, 8 kg → 3 kg), own belt holsters at
  `Waist/Holsters` (the game's holsters refuse non-guns), carry-over via `SavedHolsters`, skip the game's prop reset, centre on the
  shaft collider.
- 0.4-0.5: throw styles, ricochets, F8 recall, grip tuning keys; authored Blender model (OBJ + four PNGs embedded; 0.5.1 darker,
  coarser grip). 0.5.2-0.5.3: swept flight, post-hit spin cap, floor ricochets (floor → enemy → enemy).
- 0.6: club damage (`SwingDamageMultiplier`, `ThrowDamageMultiplier`), body knockout, ricochet hit-pause bypass. 0.6.1: never destroy a
  grab point (reverted). 0.7: the mod replaces the game's throw assist (it is a knife homing that kills spin); steered speed follows the
  throw; TipFirst turn; no enemy-to-enemy ricochet by default. 0.7.3: throw follow-through.
- 0.8: chest throws kneel (a `GetHit` prefix clears `isRunning`; a leg crossfade plays the kneel), `KneelOnLegHits`, +30% speed,
  `TipTurnTime` 0.04. `ThrowReaction = Stun` was tried and looked unnatural. 0.8.3: dark red gloves (since split into Gloves).
- 0.9: visible holster tubes copying the game's holster highlight (additive shader: judge visibility by max(r,g,b,a)); middle grab point
  deactivated safely; second hand grabs anywhere. 0.12 / Daredevil 0.2: clubs on the arsenal panel through Weapon Framework.
- Daredevil 0.3.1: `GD_HVROptimiser` was the "can't grab" bug. 0.3.2-0.3.3: wall layout in the slot's frame, fit when the slide-in ends.
  0.3.4: club throws follow `ThrowAssist.AimHead`; impact grace 0.05 s. 0.3.5: clubs on the back holsters.
- 1.0.0 (Sep 30): leg-holster loadout survives a same-scene checkpoint reset (hook `resetPlayerLoadout`); club door kick (A/X).
  Swing-feel and hand-lag research (HandProbe, `HandStrengthScale`, `HandTorqueScale`, `ClubInertiaScale`, `ClubMaxSpin`) in 1.0.x/1.1.
  1.1.0 (Oct 2): release candidate (change list in `Daredevil/README.md`). 1.1.1 (Oct 3): holstered/wall clubs are triggers (no collision), needs Grab Fix 1.1.2.
- Radar Sense 0.3.x: red enemy silhouettes through walls in slow motion or always, `Reveal` Awake/Moving/All keeps the game from hiding
  unseen enemies, rescan after the pooled enemy gets a new body, `LoudSteps`, `Style = Hidden`.

## Gloves (Gloves/)
0.1.0: glove colour (default #8A0F0F) live from the Mod Settings board; split out of Daredevil Sep 28. Replaces `[BillyClubs] GloveColor`.

## Throw Assist (ThrowAssist/)
Real-physics steering of the game's own throw assist; thrown pistols (switched on, damage and stagger; moved out of Billy Clubs 0.11).
0.2.0 (Sep 28): knives fly at the game's 17 m/s, blade first (14/14 stabs). 0.2.2: 3.5 m/s default gate; private knee assist (off by
default). 0.2.3 (Oct 3): knives steered by the tip, stop within 0.3 m of the aim point; the katana failures were really non-solid
colliders after a back auto-return (fixed in VR Holster Customization 0.3.5). 0.2.4: `KnifeAssistMinSpeed` (2.5) and `AimByThrow` (on), both
the defaults since the 0.2.4 playtest.

## Grab Fix (GrabFix/)
0.1.0 palm-only aim measured worse than finger aim (38% vs 61%). 0.2.0 → **1.1.0**: finger + palm-aimed fan together, wider near sphere,
grip buffer, thrown-item catch (29/29 buffered catches). Same day: docked (kinematic) items excluded from Grab Fix's own assists and the
native hover check clamped for them, local sphere and distance capsules alike. 1.1.1: enemies only grabbable with the palm on them
(`EnemyGrabRadius` 0.10). 1.1.2: reach check counts trigger-switched colliders so holstered items stay drawable. Grab Log is retired.

## Slow Motion Hands (SlowMotionHands/)
0.1.0 restores a normal-rate physics step in slow motion and scales hand spring/force by (1/s)², damper 1/s. 0.1.1 tracks what the mod
wrote (the "scale only what changed" guard did nothing). 0.1.2: `maxAngularVelocity` ×1/s; slow motion hand lag = normal. 0.1.3:
`SlowMotionSnap` ×2 (snappier than normal in slow motion; "feel is much better").

## Mod Settings (ModSettings/)
0.1.0 (Sep 28): in-VR board listing every mod's MelonPreferences (typed controls; string options parsed from the description).
0.2.0: tile on the phone home screen (clone of Game Options, push events emptied). 0.2.1: structural placement in the real empty slot,
board rides on the rig. 0.2.2: centre slot `App_5`, moved to `App_4` while a mission uses it. 0.3.0: flat-mode IMGUI menu (Ctrl+M).
1.1.0: List button (jump to a section). 1.2.0: defaults changed by a mod update (`UserData/ModSettings_defaults.txt`; untouched values
move to the new default, customised ones wait for Use new / Keep). **Release Mod Settings 1.2.0 before Daredevil 1.1.0**, because the
first run only records. 1.2.1: "Settings were reset" notice when a mod's changed settings all vanish (deleted cfg). 1.3.0 (Oct 4): a small
"Mods updated" popup on the first board open of a session (Use new / Keep mine / Review / Later) replaces opening on the page.

## Weapon Framework (WeaponFramework/)
0.1.0 (Sep 28): mod weapons on the arsenal panel in The Range. 0.1.1: `OnSettled` from `endAnimation`. 0.1.2: save guard moved to
`SaveWeapons` (the method that writes `saveSpotLarge`); gate C PASS. 0.1.3-0.1.5: one-line action log (retrieve, stand take/put, holster
in/out, bursts folded). 0.2.0: shared item code, back holsters for mod items on the game's shoulder sockets, test crowbar (3 kg, picture,
laid across the slot); draw assist at ~20 cm; the game's full side refuses guns. 0.2.4: hover haptic, saved items wait for the template.
0.3.0: requires VR Holster Customization (all holster code moved there).

## VR Holster Customization (VRHolsterCustomization/)
Base layer for every holster: adjust the game's holsters, move empty slots, hologram colour, mod-item back slots. 0.1.1 calls the game's
`SaveContractHolsters` a second after a placement. 0.2.2: back-slot loadout persisted only in The Range; recalls the item after
`resetPlayerLoadout`. 0.3.0-0.3.6: katanas and knives fit the back slots (`BackBlades`), non-solid while holstered, a blade drawn from the
back returns there (`returnKnife` prefix), contract restore instantiates from `allOthers`, `[VRHolsters_KnifeReturn]` `InEnemySeconds` /
`OnGroundSeconds` (defaults 1 s / 5 s), restores `nonTriggerColliders` after an auto-return. 0.3.8: `HolsterSounds`, the game's holster click for mod weapons in the back holsters. 0.3.9: `BeltFollowsHead` (off by default) = the belt takes the head's yaw every frame (postfix on `HVRPlayerWaist.FollowPlayer`). 0.3.10: eased (played 0.3.9: too fast, too far): rig turns apply at once, head turns past `BeltDeadZoneDeg` ease over `BeltTurnSeconds`, the rest drifts in over `BeltCenterSeconds`. Played: no effect (eased relative to `PlayerController`, whose yaw follows the head). 0.3.11: belt heading kept in world space, stick turns measured around `HVRPlayerController.HandleRotation` and added at once. Played Oct 4 2026, works. 0.3.12 (defaults only): follow on, dead zone 10, turn 0.1 s, center 1 s (the user's tuned values).

## Death Details (DeathDetails/, was Close Eyes)
Dead enemies close their eyes (writes the blend shapes every frame; stops the corpse's `faceAnimator`, cheaper than vanilla), mouth slightly
parted (`JawOpen` 0.1), writhing enemies keep their pain face until they die, a melee head hit finishes them with `BluntHitHead`
(`HeadHitEndsPain`, 0.7.x). `BleedOut` and `AnyHitEndsPain` are opt-in and experimental.

## Aim Colors (AimColors/, was Laser Color, then Gun Colors)
Recolours the weapon laser (`LaserBoost`), iron sights (shared `sights` material) and collimator reticle (`_RedDotColor`, `_RedDotSize`).
Found with the untracked Attachment Probe (F9/F10 dump of attachments, renderers, materials; reusable).

## Melee Unlocks (MeleeUnlocks/)
Unlocks knives on the Range wall as if the last kill had happened; default `Katana2` (the second katana). 0.1.0 hooked `checkKeep` (the
wall runs `initSlot2`, not `checkKeep`); 0.2.0 hooked `checkPurchaseDataWeapon` but `makePurchase` is a no-op while `gameStarted` is false;
0.2.1 adds to `purchasedContentWeapons` in memory and saves once the gate opens.

## Challenge NPC Limit (ChallengeNpcLimit/) and Frame Probe (FrameProbe/)
Challenge NPC Limit 0.1.0 raises `maxTakedownEnemies` above the 30 limit (60 by default). Frame Probe 0.1.0 logs frame timing and
draw-call summaries (the draw-call `ProfilerRecorder` constructor is unavailable in this build, so CPU/GPU fields are blank; an
unproven mod-isolation method is in ROADMAP).

## Tooling and process
- `Tools/UPDATE_RECOVERY.md` replaces the Sep 27 "what a game update breaks" plan (it overstated certainty): launch once, snapshot,
  triage, play the smoke checklist, selective repairs. `il2cpp_tools/snapshot.py` diffs builds.
- Blender club model project in `BlenderRefs/` (club space: origin = centre of the crowbar's shaft collider, +X towards the tip,
  Unity → Blender `(x, y, z) → (x, z, y)`, OBJ Y-up).
- Nexus workflow, mod-status and release skills: `.claude/skills/`; page text in `release/<Mod>/`.
