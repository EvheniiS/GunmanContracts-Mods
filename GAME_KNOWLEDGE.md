# Gunman Contracts: how the game works

What we know about the game's code, for anyone (human or agent) writing or fixing a mod. Organised by topic, not by date.
Per-version build history is in [HISTORY.md](HISTORY.md), open work in [ROADMAP.md](ROADMAP.md), test status in each mod's
`Tested:` line (skill `mod-status`).

- Engine: **Unity 6000.0.41f1, IL2CPP** (metadata v31), built on the **HurricaneVR (HVR)** framework. Developer string
  `ANB_Seth`; game logic lives in `HurricaneVR.Framework.dll`, classes prefixed `ANB*` (`GD_*` are newer additions).
- `<game>` = the install folder (`...\steamapps\common\Gunman Contracts - Stand Alone`). A mod deploys as `<game>\Mods\<Mod>.dll`.
- **Evidence labels:** "checked" = read in the game code or seen in a log; "inferred" = follows from the code; "untested" = needs a run.
  Offsets (`0x..`) and method addresses are from game 0.3.1.0/0.3.1.1 and move on every build; **names are what survive**.
- Game updates: [GAME_UPDATES.md](GAME_UPDATES.md) and [Tools/UPDATE_RECOVERY.md](Tools/UPDATE_RECOVERY.md).

Contents: 1 Modding stack and tools · 2 Pitfalls that cost us time · 3 Hands, grabbing, input · 4 Weapons · 5 Holsters, saves,
the arsenal, the phone · 6 Enemies · 7 Sound · 8 Flat (non-VR) mode

---

## 1. Modding stack and tools

- **MelonLoader 0.7.x is required** (tested with 0.7.3). 0.6.2 fails on first run: its generator asks for Unity runtime libs
  `6000.0.41.zip`, which 404s, so no interop assemblies exist and no mod loads. First launch generates the interop in ~40 s;
  "Failed to restore N methods/fields" and "Class::Init signatures exhausted" are harmless.
- **A black screen or flat-mode start on Quest (Virtual Desktop) was not the mods**: a PC restart plus reconnecting the Quest fixed
  it with everything enabled. Restart the PC and VD before blaming a mod.
- Mods bind to the game **by name only** (Harmony patches on the generated interop assemblies, ~40 targets). No hard-coded
  offsets, so addresses moving costs nothing; a renamed or removed member is a compile error; a reworked method compiles and
  misbehaves; a small method that IL2CPP inlined installs but never fires (find callers with `xref.py`, patch one level up).
  Types such as `ANBWeaponAttachments` live in `Il2CppHurricaneVR.Framework.dll`, not `Assembly-CSharp.dll`.
- **IL2CPP analysis without Cpp2IL/Il2CppDumper** ([il2cpp_tools/](il2cpp_tools/)). Needs `pefile`, `capstone`, `numpy`
  (assets: `UnityPy`); set `GUNMAN_CONTRACTS_DIR` or `il2cpp_tools/game_dir.txt`.
  - `il2.py` parses v31 `global-metadata.dat` (type-def stride 88, method 36, field 12) and finds CodeRegistration /
    MetadataRegistration by pattern. `dumpt.py <Type>` = fields with offsets + methods. `xref.py <Class::Method>` = direct callers
    (scans the `il2cpp` PE section; **0 hits does not mean never called**: vtable, delegate and coroutine calls are invisible).
    `disx.py` / `disa.py` = named disassembly (`disa.py` = whole method, calls and metadata slots decoded inline).
    `scanoff.py` = which methods touch a field offset. `snapshot.py` = capture and diff structural maps between builds.
  - **`slot.py <Class::Method>` first, on any method**: it decodes the metadata-usage slots (string literals, TypeInfo, generic
    instances such as `GetComponentInParent<ANBHVRGunBase>`); the literals and component types usually explain the branches before
    you read an instruction. Encoding (v31): `ty = v >> 29`, `idx = (v & 0x1FFFFFFE) >> 1`, low bit 1; 5 = string, 1/2 = type,
    3 = method, 6 = MethodSpec.
  - **Identical-code folding** gives shared stubs random names (`FHierarchyIcons::.cctor` = a delegate ctor): trust names only on
    non-trivial bodies. The tools' fragility is the metadata **format**, not the build: check the version u32 at offset 4 of
    `global-metadata.dat` after a Unity upgrade. Maps are cached keyed on `GameAssembly.dll` + metadata size/mtime.
  - Assets (IL2CPP asset files have **no type trees**): `monoscripts.pkl` maps MonoScript pathID to class (rebuild after an update);
    `prefab_tree.py` prints a hierarchy with component classes; `gunwall_scan.py` dumps the arsenal walls; `bowpaint.py` reads paint
    tables (located by the `designColor1/max1/2/max2` floats); `anim_dump.py`, `clip_info.py`, `clip_root.py` read animators and
    clips (humanoid value index 8 = `RootT.y`). Raw MonoBehaviour layout: GO pptr 12, enabled 4, script pptr 12, name string;
    every bool is 4-byte aligned.
- **Build and diagnostics conventions:** debug logging is off by default in a release and on only for mods being worked on; check
  `<game>\UserData\MelonPreferences.cfg` before handing a mod over for testing, and edit the cfg only while the game is closed
  (MelonLoader writes it on exit). Logs are lean: one short line per real event, counts in a summary per wave/scene, context as a
  header line once, a mod's detailed log in its own file.
- **UnityExplorer was removed** (Sep 2026, moved to `<game>\_removed\`). Clubs spawn from the arsenal panel now.

## 2. Pitfalls that cost us time (each one cost a bug hunt)

**Cloning and spawning**
- **`Instantiate` runs `Awake` on everything inside the clone, and game singletons register in `Awake` with no check.** The dagger
  grip's cloned grip point woke an `ANBWristHud` that wrote itself into `ANBGameLogic.ANBwristHudRight`; when the arrow died the slot
  held a dead object, and `pauseGame` (which calls `.gameObject` on both HUDs without the null check its unpause branch has) threw
  halfway: no menu, hands left reparented onto the controllers and dragged by locomotion, Settings dead for the session. The bug
  showed up minutes later in a system the mod never touched. **Diff the game's global slots around every `Instantiate`; strip or
  disable foreign components in the clone.**
- **`GD_HVROptimiser` switches grabbables off.** First use collects every `ANBHVRGrabbable` / `HVRGrabbable` / `HVRGrabbableBag`
  with `FindObjectsByType(Include)` and disables each; its `Update` re-enables, round-robin, only those within `_grabbableRange`
  and in front of `_player`. A mod's hidden template gets disabled forever, so every copy starts disabled; items that existed
  earlier switch off whenever they are behind you. Remove new items from `GD_HVROptimiser.instance._grabbables`. Found by xref of
  `Behaviour.set_enabled` (411 callers) filtered to methods that load a grabbable type.
- **Pooled enemies get a new body on respawn.** The same `ANBBasicNPC` is reused and re-dressed: its skinned meshes are destroyed
  and new ones with the same names created. A mod that cached an enemy's renderers or bones points at a dead body. Re-fetch.
- **Game prop reset:** a wave start runs `resetPhysicObjects` → `ANBGeneratePhysics.resetMe`, teleporting anything copied from a
  physics prop. And `ANBGeneratePhysics.Init` (from `Start`) runs a frame after spawn and sets layer and `isKinematic`, knocking
  loose an item pinned that frame. Skip the reset for mod items; re-pin until `Init` has run.
- A template copied from a Range-only prop (the crowbar) does not exist in the main menu or a contract started straight from it.
  Any restore that needs it must wait, not delete the saved entry.

**HVR grab points and colliders**
- **Never `DestroyImmediate` a grab point.** HVR keeps it in more than `GrabPoints`, so the stale entry throws in
  `GrabPointValid` on every grab. **Deactivate it** (`GrabPointValid` rejects an inactive object, a disabled component, or a
  wrong hand flag). With the second grab point off, the second hand grabs anywhere through `PhysicsPoserFallback`.
- **Docked = `Rigidbody.isKinematic`.** Holsters, sockets and wall mounts park an item kinematic; a lying or flying item is not.
- **Holster click** (disassembly, Oct 3 2026; not yet heard in game for a mod item): `HVRSocket.PlaySocketedSFX(HVRSocketable)` plays the
  first set of: socket `AudioGrabbedOverride` (0x110), the item's `HVRSocketable.SocketedClip` (0x50), socket `AudioGrabbedFallback` (0x120)
  via `PlaySFX(clip)`; a null socketable throws. `PlayUnsocketedSFX(HVRGrabbable)`: `AudioReleasedOverride` (0x118), `Socketable.UnsocketedClip`
  (0x58), `AudioReleasedFallback` (0x128). Skipped while `_ignoreGrabSFX`. A mod item parked by VR Holsters never enters the socket, so it
  was silent until VR Holsters played a clip itself. Oct 3 2026 log: every gun and the bow have all of these EMPTY; a gun's holster click is
  `ANBGunSounds.HolsterIn/HolsterOut` (`PlayHolsterIn` = `PlayClip(HolsterIn, false)`, which ends in `ANBGameLogic.PlayAudioClip(..., "default", ...)`).
  Knife holsters have no clip either: a belt draw plays `S_WEP_Knife_Attack_01` (+ `handgrab`), a belt holster is silent, a wall pickup plays the 'Belts 03' jangle
  (Sound Probe, Oct 3 2026). VR Holsters plays those by clip name (`HolsterInSound` / `HolsterOutSound`).
- **Holstered items collide with nothing:** `HVRSocket.DisableCollision` (default true) makes `HandleRigidBodyGrab` call
  `HVRGrabbable.SetAllToTrigger()` (every collider in `Colliders` gets `isTrigger`); `OnReleased` calls `ResetToNonTrigger()`.
  Triggers stay grabbable (`HVRTriggerGrabbableBag` and `DistanceToGrabbable` never read `isTrigger`); real triggers live in
  `grabbable.Triggers`. A mod that measures surface distance must count trigger colliders of a docked item.
  **But line of sight ignores them:** `HVRHandGrabber.CheckLineOfSight` raycasts `Colliders` with `QueryTriggerInteraction.Ignore`
  (only `Triggers` with `Collide`), and `CanGrab` runs it when `RequireLineOfSight` (0x3c) is set. A mod that turns an item's
  colliders into triggers outside a socket must also clear `RequireLineOfSight`, or the item sits in the grab bag, never hovered.
- **A non-solid item gives a recognisable symptom:** no collision at all plus a top speed far above the steer speed (it falls out of
  the map). Check its colliders before the flight code. (`ANBKnife.nonTriggerColliders` is what a knife restores.)

**Game rules that look like bugs**
- **Reading a flag as a guard:** read the constructor default first. `ANBAssistedThrowingObject`'s ctor sets `isHoming = true`, so a
  "not already homing" guard returned on every throw. Every early return in a debug path should log.
- **`ANBBasicNPC.transform` is a static root that stays at the spawn point.** The moving body is `agentTransform` (0x868); the
  game measures player distance from `visionBase` (0x750, the eyes).
- **Spawner distances are squared in place** by `initSpawner` (`minSpawnDistanceToPlayer`, `minSpawnDistanceToOtherNPCs`,
  `maxSpawnRadius`); a logged "36 m" is 6 m.
- **Dynamic spawners never call `getSpawnPoint`**: the `spawnCheck` coroutine inlines the same logic and calls `spawnNPC(overrideSP, …)`.
  Hook `spawnNPC` and `spawnPointValidation`.
- **`makePurchase` starts with `if (!ANBGameLogic.gameStarted) return;`**, so it is a no-op while The Range loads; the free-knife
  auto-purchase inside `checkPurchaseDataWeapon` is gated the same way. Add to `purchasedContentWeapons` in memory, then call it
  once `gameStarted` is true.
- **A method that starts a coroutine by name** (`StartCoroutine("checkKeepExec")`) may have other starters (`<initSlot2>d__33`).
  Scan for every reference to the name string before hooking the wrapper; hook what every path asks (`checkPurchaseDataWeapon`).
- **Methods called by a coroutine state machine** show as `<name>d__N::MoveNext`; patch those, not the iterator method.
- **Stripped APIs in this IL2CPP build:** `ColorUtility.ToHtmlStringRGB` (use `TryParseHtmlString`; format colours yourself);
  `GUIStyle.set_wordWrap` / `set_richText` / `get_hover`. IMGUI `GUI.Button/Label/DrawTexture`, `GUIStyle(GUIStyle)`, `fontSize`,
  `alignment`, `fontStyle` survive.
- **Place UI into the game's layout by structure** (slot names, sibling order), never by measured positions: the phone home screen's
  local x is mirrored, and `allButtons` also holds the mission popup buttons above the grid.
- **MelonLoader live config:** a `FileSystemWatcher` reloads `MelonPreferences.cfg` and entries fire `OnEntryValueChanged`, so an
  external edit or `entry.BoxedValue = x` + `MelonPreferences.Save()` reaches every mod while the game runs.
- **Hooking technique:** a Harmony prefix that re-calls the method with a substitute argument avoids `ref` on object parameters
  (the recursive call passes because the substitute differs from the original). `ref bool __result` and `[ThreadStatic]` re-entry
  guards work. Harmony `ref Vector3 __result` in a `FindRandomNavMeshPosition` prefix is the one pattern not yet proven here.

**Debugging habits that paid off**
- Classify impacts from the damage/collision callback, never from proximity afterwards (a knocked-out ragdoll had already left the
  overlap sphere). A probe ray moved backwards along an object hits the object's own colliders: filter by owner, not just layer.
- A raycast never detects a collider it starts inside. A "scale only what the call just changed" guard does nothing when the call
  rewrites an identical value: track what **you** wrote. Log what a game call actually changed, not that it was called.
- Before acting on "it points the wrong way", read the user's cfg for which mode produced it.
- A game-update diff sees signatures and field offsets only; logic changes inside an unchanged signature are invisible.

---

## 3. Hands, grabbing, input

**Grab timing (why a fast second grab was lost).** `HVRHandGrabber.CanHover`: while grip is held the hand refuses any new hover
target. `CheckGrab` grabs only on the grip-press frame (`IsGripGrabActivated`). A press made just before the target becomes
hoverable is lost until the grip is released and pressed again (an unbuffered, edge-triggered input). Buffering the press for
~0.35 s and calling the game's own `TryGrab` fixes it. `HVRHandGrabber.releaseWait` is read only by `HVRForceGrabber.CanGrab`.
`HVRPhysicsBow.ShootArrow` clears the old arrow only after the next FixedUpdate, so a string grab inside that window finds
`Arrow != null` and spawns nothing.

**Grab detectors.** Distance grab = a fan of 5 trigger capsules per hand (`Main/Up/Down/Left/Right`, radius 0.17, height 4.14,
along the finger direction) in `HVRForceGrabber.GrabBags`; the local pickup is one 0.08 m trigger sphere. The palm points ~90° off
the finger axis (left +X, right -X), which is why the game feels finger-aimed. **Aiming the fan from the palm alone measured worse
than the finger aim** (38% vs 61% of presses grabbed). Widening the near sphere widens what the game's native hover reaches, so
holstered items can be grabbed by accident; the fix is to clamp the native hover check back to native range for docked items, for
both the local sphere and the distance capsules (a palm-aimed copy can swing across the belt). Enemy limbs are ordinary
`HVRGrabbable`s, so a widened sphere grabs ragdolls and faces; enemy parts need the palm on them.

**Hold type.** `HVRGrabbable.HoldType`: 0 OneHand, 1 Swap, 2 TwoHanded, 3 ManyHands. `CheckSwapRelease` releases the primary hand when
`HoldType == Swap`; `CanGrab` refuses a second grabber on `OneHand` (unless force auto-grab). The bow is `Swap`, so a grip meant
for the string that lands on the riser moves the bow to the other hand; setting it to `OneHand` stops that. Socket primaries skip
the check, so holster draws are unaffected.

**Line grabs (a grip slide).** `HVRPosableGrabPoint` has `IsLineGrab` (0x78), `LineStart`/`LineEnd`, `LineCanReposition`,
`LineCanRotate`, `LooseDamper`; `HVRHandGrabber.UpdateLineGrab` switches tight/loose, driven by `HVRSettings.LineGrabTriggerLoose`
and the hand's grab/trigger state. Not yet used.

**Throwing.** HVR takes release velocity from the hand's last few frames when the grip opens (`HVRHandGrabber.ThrowLookback`,
`ComputeThrowVelocity`, per-grabbable `ReleasedVelocityFactor`), so an early release gets the slower, earlier part of the swing.
Nothing tells the player when to let go.

**Input map while holding a gun** (`ANBHVRControllerInputs.checkVRButtonTaps`, `HVRANBAmmoReleaseAction`; all on "just pressed"):
- Gun hand A/X = magazine release; the same input as the knife grip swap (`HVRGrabPointSwapper`). Taken.
- Right B = slow motion. Left Y does nothing with a gun in the left hand. Stick clicks = sprint/crouch.
- Support hand on the foregrip: `OnHandFrontalStabilizerGrabbed` writes the gun into that hand's slot too. With the laser/light
  attachment, A/X = flashlight and B/Y = laser; **without it, support-hand A/X is a second magazine release** (`Gun.ReleaseAmmo`).
- Per-hand guns: `rightHandGun` (0x10a0), `leftHandGun` (0x10a8). Fire mode: `HVRGunBase.FireType` (0x54, 0 Single, 1
  ThreeRoundBurst, 2 Automatic) is read only by framework code (`TriggerPulled`, `UpdateShooting`, `Shoot`); game code never reads
  it, enemies fire through the separate `EnemyTriggerPulled`. Only Automatic stops on trigger release; switching mid-fire must clear
  `IsFiring` / `RoundsFired`.

**The physics hand and slow motion.**
- Chain: controller → `HVRJointHand` `ConfigurableJoint` on a **20 kg** hand body (`PDStrength`: spring 9000, damper 900, max force
  9000, torque 500/50/75 by default; `HVRHandStrengthHandler` swaps it while something is held) → `HVRHandGrabber.Joint` to the held
  item (rigid for clubs: slerp/angular 100000/1000). Strength settings are shared ScriptableObjects: change a clone, never the asset.
- At ωn ≈ 21 rad/s (critically damped) the hand trails the controller by about `a / ωn²` (100 m/s² → 22 cm). Measured: hand delay
  16-24 ms bare, a swung club adds 12-24 ms of grip delay; rotation delay is 28-48 ms with a club. A fast sideways swing can spin the
  **hand body** 160-180° (torque saturates at 75 N·m against a club's inertia about the wrist), not the grip.
- **Slow motion** only sets `Time.timeScale` (`toggleSlowMotion`). `FixedTimeTest` sets `fixedDeltaTime` = (static) original ×
  timeScale or (dynamic) the average frame time scaled, so physics runs ~30 Hz real at ×0.22 and the hand catches up 1/timeScale
  slower. Hand strength reaches the joint only through `HVRHandStrengthHandler.UpdateStrength` (`HVRJointUtilities.SetLinearDrive/
  SetSlerpDrive`); `maxAngularVelocity` is in game time. A fix has to restore a normal-rate step and scale spring/force by
  (1/s)², damper by 1/s, and track what it wrote (the game rewrites the same base value when slow motion starts).
- Hand body interpolation is None. Item spin caps seen: pistol/rifle 30, club 80, hand 150 rad/s.

**Hand unstuck (game 0.3.1.1).** `TemporarilyDisableHandCollisions(Transform, float)` became `TemporarilyDisableHand(GameObject,
HVRGrabbable)` with `handUnstuckingDuration`; `ANBHVRGrabbable` gained `autoReleaseDelay` / `autoReleaseTimer` (use not found:
watch for a held item dropping by itself).

**Pause and the phone.** `ANBSmartphone.showMainMenu` returns if `switchingToMenu` (0xec), else sets it and starts `showMenuExec`
(release phone, wait 0.05 s, `pauseGame(true, true)`, clear the flag); if the coroutine dies the flag stays set and every later
press is ignored. `pauseGame` toggles `Paused` (0x13e8): on pause the physics hands are reparented onto their `Target`, on unpause
(if `useHands`) back to `playerHealth.transform.parent`. The phone's home screen: `ANBSmartphone.mainApp`; tiles are
`ANBInterfaceButton` (fingertip `OnTriggerEnter` → `buttonPush`); `toggleInput(held)` sets `isHeld` and `SetActive(held)` on every
object in `allButtons`; `MainSceeen/Apps` is a `GridLayoutGroup` with slots `App_1..App_9` (App_4 and App_6 empty, App_5 holds
hidden mission `BreachHolder` buttons; App_9 Game Options, App_8 Set Floor Height, App_7 Score Keeper). The player rig is
`PlayerObject/TechDemoXRRigOpenXR`; the belt is `Waist/Holsters` (`HVRPlayerWaist` drives it).

---

## 4. Weapons

### Bow
- No quiver in the game: `HVRArrowLoader` listens to the **string/nock grabbable**; `OnStringGrabbed` → `CreateArrow(true)` spawns
  the arrow into the nock only if `bow.Arrow == null` and you have arrow ammo (`ANBmain.AmmoArrows`, 0x1070; effectively infinite
  in play). `ShootArrow` calls `substractAmmo` only if `createdNok`, so a quiver that draws via `CreateArrow(false)` and re-nocks
  through the string-grab path keeps ammo accounting intact. A holster's pivot is not where the hand reaches (32-42 cm off).
- **Shot speed is quadratic in the pull.** Compound bow `HVRPhysicsBow`: `StringLimit` 0.40 m, `ShootThreshold` 0.20 m, `Speed`
  50 m/s, `SpeedCurve` = t². `speed = 50 × tension²`, range ∝ speed², so 90% of the pull is 81% speed and ~66% range.
  `ArrowShootCall` writes `Tension` (0xac) and `_shootSpeed` (0x10c); a prefix on `ShootArrow` can recompute `_shootSpeed`.
  Over 50 m/s a 50 Hz step moves ~1 m (tunnelling risk).
- **Arrows are knives** (`ANBKnife.isArrow`; the bow calls `makeTempStabAll` on a shot). `ANBBreakable.hit` scales damage only for
  bullets and explosions; knife damage (100) passes raw, and a barrel has health 250, so it took three arrows. Fix = raise the
  damage for explosive, destructible breakables when an arrow hit.
- **Door breach:** the "shoot here" mark is an `ANBDoorKicker`; `ANBGameLogic.TryKickDoor(pos, dir)` (gated by `useVRDoorKick`,
  `useFPSDoorKick` in flat) raycasts `doorKickDistance` / `doorKickMask` at the moment of the shot, from the muzzle. Only guns call
  it (`ANBHVRGunBase.OnShoot`, `Character.OnTryFire`, `ANBFpsInteraction.Interact`); a bow shot can call it.
- **Paint:** the Gun Edit Table swaps pre-made mesh variants (`designColorObjects`), 2 parts × 5 colours per weapon. The compound bow
  offers grey (free), black (`col1_2`, 0.047) and brown; one spray can per colour (`substractSpraycan`), cans from spray-kit
  collectibles. **The game ships a black bow.**
- **Model:** `CompoundBow` → `Bow - basecolor` → `HuntingBow` SkinnedMeshRenderer, one mesh (38,975 verts), 28 bones, one URP Lit
  material; the three paint variants are the same mesh with different `_BaseColor`. A replacement must keep the rig.
  `il2cpp_tools/export_bow.py` exports it to `BlenderRefs/game/` (git-ignored game assets: never publish).

### Throw assist (knives, anything thrown)
- `ANBKnife.releaseKnife` (wired through the grabbable's release event) runs the assist only if `allowAssistedThrow` (0x118),
  `ANBGameLogic.assistedThrow` (0x70b), a live `ThrowScript` (an `ANBAssistedThrowingObject`) and `|velocity| ≥
  assistedThrowAtVelocity` (0x70c, ~3.5 m/s). Then `ToggleBodyPartCollisionsExec(0)` (if `ignoreBodyPartsOnThrow`),
  `makeTempStabAll()`, `ThrowScript.StartAssistedThrow()`. Arrows never got the assist because the arrow prefab has no throw script.
- `StartAssistedThrow`: target = `GetClosestAndCenteredTarget(true)` (enemies), else `(false)`; **targeting is gaze-based**
  (`MainCam` position/forward, `assistedThrowViewRadius` 0x718 / `ViewAngle` 0x71c, or the object's
  `targetSearchDistanceOverride`), not the throw direction. A throw with nobody in the gaze finds no target.
- The coroutine (`StartAssistedThrowExec`) sets `isHoming = true`, zeroes linear and angular velocity, then each fixed step
  `rb.MovePosition(pos + dir × speed × dt)` until within `stopDistance` or past `maxFlyDistance`; with `changeRotation` it sets
  `LookRotation(dir) × aimCorrectionAngle`, else it spins by `torque`. It is a knife homing: it kills thrown spin and fights physics.
  Knife tuning: 17 m/s, stop 0.7 m, skips arms (not legs) for 2 s. Ctor defaults: speed 10, stop 0.5, angle 90, torque 10.
- **Stab ray:** `ANBKnife.stabEnemy` casts `Physics.Raycast(StabOrient.position, StabLineWorld.normalized, knifeRayLength (0x48),
  StabMask (0x38))`. Hit → `StabEnemyFinal` (damage). Miss → `HVRStabber.ForceUnstab`, then a re-stab on the next contact (the
  "stuck, wiggles" thrown arrow). The enemy's physics body (`EnemyCollisions`) is bigger than its hit zones (`ANBEnemyDetect`); a
  bow shot is already deep in the body at contact, a homing throw is not. The tip is often already inside the zone, so the ray
  never sees it: start the ray behind the tip and pick the first real zone (not under the arrow's own `ANBKnife`).
- `StabEnemyFinal`: if the collider has `ANBEnemyDetect` and `isArmored` → `TakeArmorDamage` (damage 1.0 hard-coded) else
  `TakeStabDamage(knifeDamage)`. Zone flags: head, chest, stomach, groin, arms, legs, `isArmorVisor`.
- Throw aim for knives: steer by the **tip** and stop within 0.3 m of the aim point, or the blade spins inside the body.

### Melee
- Each body part's `ANBBodyMeleeCollision.OnCollisionEnter` forwards to `ANBBodyMeleeCollisionManager.collisionEnter`, which
  classifies the other collider: `ANBBluntWeapon` → `blunt.hit` (speed tiers, no stun gate); `ANBHVRGunBase` → counts only past
  `meleeMagnitude` (0x28) and only if the gun's rb mass > 1.0, stumbles only above `StumbleOnRBMass` (5 kg); `HVRHandGrabber` →
  ignored unless the hand's `LeftHandWeapon` / `RightHandWeapon` == "none"; `ANBKnife` / `ANBHVRAmmo` ignored. Any rigidbody
  over 1 m/s that is none of those counts as a thrown object. **The event fires on dead bodies too; the game's handler checks
  `isDead` inside**, so a hit on a corpse or writhing enemy is silent and does nothing.
- If `isGettingHit` (0x99c) → `TakeMeleeDamage(..., noDamage = true)`: the first punch starts the hit stun, which covers lying on
  the ground, so later punches do 0 (the "drop them three times").
- `TakeMeleeDamage(bodyPart, collision, stumble, fromEnemy, noDamage, damageOverride)`: damage = `meleeDamage` (0xf94, 10), ×5 head,
  ×2 torso; **only head/torso hits can kill, anything else floors health at 1.0**. Enemy health 100.
- `TakeBluntWeaponDamage`: tier by speed (≥ 3.5 m/s → 34, ≥ 2.1 → 10, else 5); head ×10 only with `canKill`; legs ×0.65 and set
  `gettingHitLegs`; `isUnhittable` (0x9bd) = no damage; floors at 1 unless head. `ANBBluntWeapon.hit` clears `canHit` and
  `hitPause` waits **0.6 s** (a second enemy in a chain takes no damage unless that pause is bypassed). It calls
  `getHitAnimation` (result unused) and `GetHit(isRunning = 1)` = stumble; **the blunt path never plays the knee-shot animation**.
  The crowbar: 8 kg, `canKill`, tiers 1/2.1/3.5 m/s → 5/10/34, throw speed 20.
- Down = PuppetMaster `BehaviourPuppet.state != Puppet`; `checkBalance` is the only writer of `isOffBalance` (0x9b2); get-up =
  `unpinnedTimer` passing `getUpDelay` (0x18c; prefab 0, ctor default 5, so a "stay down" that only resets the timer is a no-op).
  Shoulder throw is not collision-driven (`checkGrab` per frame from `checkAbilities`, at `throwAtDistance`).
- **Knee shot:** a leg hit sets `gettingHitLegs` (0x9bf), crossfades the animator "Hit" layer (5) to `hit_legs_1` / `_m`, and starts
  `GetHit`, which copies `stunAtHitTimeLegs` (0x564, 3.0) into `overallHitTime` and starts `endLegHit` after `endLegHitAfter`
  (0x56c, 3.0). The visual get-up follows no timer: `hit_legs_1` is one 2.8 s clip (fall 0-0.85 s, kneel 0.85-1.5 s at hip height
  0.46-0.51 m, rise 1.5-2.4 s, exit time 0.911), so a longer kneel means driving the Hit layer's time with `Animator.Play` over the
  kneel segment.
- To make a blunt hit kneel: clear `isRunning` in a `GetHit` prefix and crossfade `hit_legs_1` on the Hit layer in the postfix.
  Redirecting a chest throw to a leg collider (`bodyPart` swapped by ref, damage multiplier corrected for the leg ×0.65) also works. Stunning a chest hit by raising
  `overallHitTime` only freezes the enemy upright, gun still aimed (looks wrong).

### Fire modes, flat guns, laser
- See the input map above. Hold-A/X on the support hand works by holding back the game's press (a Harmony prefix on
  `checkVRButtonTaps`) and replaying it on a short tap.
- **Weapon laser:** each weapon has its own copy of `.../attachment_pistol_lightlaser/lasersource/laserbeam`
  (`ANBWeaponAttachments.LaserBeam`): one 1° spot `Light` (default (1, 0.21, 0.25), intensity 700000 = the dot) and a
  `VLB.VolumetricLightBeamSD` with `colorFromLight = true` (the beam). Tinting the beam material does nothing; set `Light.color`
  and the beam `color` + `UpdateAfterManualPropertyChange()`. Sight slots: `AT3` = reflex ("holosight", the collimator,
  `Vashchuk/RedDot`), `AT1` silencer, `AT4` compensator; default sights are SpriteRenderers on material `sights`.
  `LaserPoint` is not the weapon dot.
- **Gloves:** every glove uses one URP Lit material, `fps_vr_glove` (atlas `gloved_default_color` × `_BaseColor` #414141); sleeves
  are `fps_vr_glove_arms` (cutscene arms only). Tint the shared material on scene load.

### Knives and unlocks
- The katana is an `ANBKnife` (`Knife-Katana`; arrows `isArrow` and pens `isPen` are `ANBKnife` too). Blade colliders are two boxes
  plus a handle box, so "longest box" finds the handle: measure blades by the bounds of all solid boxes. Grip point is
  `GrabPointNormal`.
- **Auto-return:** `ANBKnife.Update → checkAutoReturn` counts `autoReturnAfterCurrent` (0x128, set on every release to
  `ANBGameLogic.autoReturnKnifeAfter`, 0x224) for a knife neither held nor socketed, then `returnKnife()` sends it to `savedHolster`
  (the belt knife holster, only if empty) or `myWallSpot`; with a full holster it re-arms. `ANBKnife.socketed` / `inWall` are stale
  after a draw (`grabKnife` / `releaseKnife` write only `isHeld`). `registerKnifeKill` runs from `TakeDamage` and
  `TakeKnifeSlashDamage`. Knives are moved, never instantiated, on load (`LoadContractHolsterKnife` over `allKnifeSpots`) **in The
  Range**; contracts have no knife wall and instantiate from `allOthers` (`GameObject[]`, match `knifeID`).
- **Unlocks are by kills, not credits:** each knife kill counts `killsNeededToUnlock` down, then `makePurchase(knifeID, 0)` and the
  Steam stat `Knifecollector`. The wall (`checkKeepExec`) hides a spot unless the ID is in `purchasedContentWeapons` or its price is
  0, and a locked spot's knife is **destroyed**. Free from the start: `CombatKnife_v2`, `_v3`. **`Knife-Katana-double` (`Katana2`) is
  an exact copy of the katana** (second sword for dual wield); no contract places one, so it is unobtainable in 0.3.1.1. The
  Outpost has the only katana.

---

## 5. Holsters, saves, the arsenal, the phone

**Holster loadout saving.** Holstering only changes in-memory state (`HVRShoulderSocket.OnGrabbed` → `holsterGun`, `OnReleased` →
`unholsterGun`). `SaveContractHolsters` → `ANBSaveData.SaveLoadout` has four callers: `ANBGunwallSpot.grabGunCall` / `placeGunCall`
(taking a gun from, or putting one on, the wall), `<RangeElevatorExitExec>`, and `revertAllWeaponData`. Loaded by `LoadLoadout` from
the scene loader and `resetPlayerLoadout` (`resetScene`: a same-scene checkpoint restart reloads the saved loadout and **empties
the physical holsters**, so mods that carry items must hook it). So a holster change is lost unless the gun wall or the elevator
is used (or a mod calls `SaveContractHolsters`, as VR Holster Customization does a second after a placement).

**Hip/knife holsters refuse non-guns.** `HolsterLeft/Right` and `HolsterKnifeLeft/Right` are `DemoHolster` sockets with an
`HVRTagSocketFilter`; `DemoHolster.OnGrabbed` fetches the knife/gun component and throws on null. Shoulder sockets
(`HVRShoulderSocket.CanHover`) may accept anything, but `OnGrabbed` calls `holsterGun`. A mod holster is its own object.
**Every grab, socket grabs included, ends in the virtual `HVRGrabberBase.GrabGrabbable`** (declared only by the base class;
`TryGrab(g, force)` = `CanGrab` unless forced); refuse there to keep a game socket from taking something.
**The belt's heading** (checked, game 0.3.1.x disassembly): `HVRPlayerWaist.Update` calls `FollowPlayer` only while
`ANBmain.gameStarted && !Paused`. `FollowPlayer` puts the waist at the controller's x/z, `Camera.y - CameraOffset`; then with
`p = Vector3.SignedAngle(flat camera forward, camera forward, Camera.right)` (positive = looking down): `p < CameraAngleThreshold`
→ belt yaw = camera yaw at once; otherwise it turns only while `Vector3.Angle(Camera.forward, waist.forward) > WaistAngleThreshold`
(that 3D angle includes the pitch), `RotateTowards` at `WaistSpeed` deg/s. `StartSnapTurn` adds snap turns. Prefab values (checked in a
log, Oct 4 2026): `WaistAngleThreshold` 70, `WaistSpeed` 90, `CameraAngleThreshold` 30. Snap/smooth turns
(`HandleSnapRotation` / `HandleSmoothRotation`) rotate the `HVRPlayerController`'s own transform, inside `HandleRotation` (snap/smooth/mouse via vtable; mouse returns at once unless `MouseTurning`). **That transform's yaw also follows the head** (checked in a log, Oct 4 2026: a belt eased relative to it never lagged the head), so it is no body reference; measure turns around `HandleRotation` instead. Override the yaw in a `FollowPlayer` postfix (VR Holster Customization `BeltFollowsHead`).
The belt is `Waist/Holsters` (hip holsters at ±0.266, knife holsters ±0.165/0.048/0.132). Back sockets live under
`Camera/HeadRelativeInventory/LeftShoulder|RightShoulder` (local L (-0.36, 0, -0.09), R (0.33, 0, -0.09)), a frame that follows head
pitch partly. A holster highlight (`HVRANBSocketHoverFade`) lerps `material.color` between `colorInvisible/Normal/Hover` on a
`Shader Graphs/Rim Dissolve` material; **that shader is additive: alpha is always 0 and "invisible" is black**.
Items in a back slot want a draw assist (grip press within ~20 cm of the item), because Grab Fix
allows docked items only inside the native 0.08 m sphere. A held mod item put back should give a hover haptic
(`Controller.Vibrate(0.35, 0.06, 150)`).

**Terminals and the arsenal.** The wall lists are **empty in the scene**; `ANBGameLogic.LoadAssetLoop` fills them at load from
`ANBDataCollection` (`allRifles`, `allShotguns`, `allOthers`: bow and knives), instantiating `slotPrefab` per weapon into
`ANBGunwall.Items` / `ItemsID`, ending with `LogLoadTime("Processed Completed Gunwall")`. A slot is `ANBGunwallSpot` +
`DemoHolster` (`gunstorage`) → `gunPos`. The panel (`printInfo`) reads the slot's `ANBWeaponType` (`WeaponDisplayName`,
`WeaponDescription`, `WeaponIcon` = a 1364×635 sprite, price, `ammoType`). Owned = in `purchasedContentWeapons` by `PurchaseID`
(price 0 auto-purchases). **Retrieve = `pickWeapon`**: `Items[i]` → `mainSpot`, previous → `secondarySpot`, anim `switch`;
`endAnimation` (use as "switch done") switches the previous slot off. It **saves the index** (`SaveWeapons` writes `saveSpotLarge` /
`saveSpotSmall`; `SavePurchases` does not), so a mod entry's index must be swapped for the last game index around `SaveWeapons`, or
the wall indexes past its list once the mod is removed. Game 0.3.1.1 added a **Retrieve-lost-weapons** terminal button
(`returnGunsToWall`); whether it counts a gun in a mod back holster as holstered is unknown. Savegame validation
(`SaveFileValidation`) checks key presence in 7 files, moves an invalid file to `BrokenSavefiles` and restores from `Backup/`;
it does not check values, and the backup is a copy of the current file.

**Fit to the board.** A mod item hung on a slot: lay out from the gun position's own axes (rigid with the wall), not the line to the
player; wait for `endAnimation` before raycasting the board (the slot pauses, then slides in).

---

## 6. Enemies

**Senses and the awareness leak.** All in `ANBBasicNPC`. Real senses: view cone (`viewRadius` 25 / engaged 50 m, `viewAngle`
160° / 240°), `FovSight` + `BlockedSight` set `targetInSight`, reaction times 1.2/1.0/0.8 s, hearing via `ANBAlertManagement`.
Attack navigation goes to `lastKnownPosition` (0xa50), written only by `markLastKnownTargetPosition`. **The leak
(`AL_targetCheck`)**: while the target is out of sight and `lostTargetTime` > 0 it tests `lostTargetTime > lostTargetTime −
loseTargetPositionUpdatesAfter || !canLoseTarget` (same field both sides), so every tick of the "lost you" window writes the
**live** player position; with `canLoseTarget` false (wave spawner overrides) it never ends and the timer never drains. Entry
points hand over the player Transform itself (`setSpawniesAttackPlayer` → `StartAttack(Playertarget 0xb88)`), hunting picks random
points within `huntRadius` 15 of the live player, flanking (`findFlankPosition`) and turning (`rotateToTarget`) use the live
position, `StartHunt` bounces to `StartAttack` when `!canLoseTarget`. The `lostTargetTime` timer drains by `deltaTime ×
ActionPulseRate` and not while flanking. `SetCollectivePlayerposition` shares a position only when that NPC has sight. Measured on
a vanilla wave: 26 of 26 conclusive unseen chases were live-tracked, searched-where-lost = 0; with the fix 2 : 18.
`calcWaveSetup` / `AllDeadTrigger` / `UpdateHuntTarget` have no direct callers (waves advance in coroutines); don't hook them.

**Spawners.** `SpawnPointOrder` Closest / Random / Ordered; Closest re-sorts by distance to `PlayerHitTarget` and takes the first that
passes `spawnPointValidation` (hidden `outOfSight`, min distance, radius, other-NPC distance, cooldowns), so standing still gives the
same nearest hidden point every time ("same door"). Wave setups differ per map and even per spawner on one map (Restaurant has both
a Closest/24-point and a Random/34-point one; Warehouse is Random/33; enemy-count mode is one `Once`/Random spawner that spawns
everyone idle at the start). Wave number = the last spawner's `waveSurvived` + 1. `getSpawnPoint` has one caller (`spawnNPC`);
`spawnPointValidation` writes no fields. Every spawn fires `alertWorld(attack)`.

**Out-of-view hiding.** `checkVisibilityRelatedActions`: out of view for `switchObjectsAfter` s and beyond
`outOfViewObjectsSaveDist`, with no custom animation → deactivates `outOfViewObjects` (0x818), disables `NpcMeshes` renderers, culls
the animator, sets `outOfViewObjectsOff` (0x828). Anything reading enemy renderers or bone poses sees hidden, frozen enemies unless
it keeps `outOfViewTime` (0xba4) at 0. While idle out of view the enemy is drawn by a plain `CulledMesh` MeshRenderer.
Game 0.3.1.1 adds Outpost-only object culling (`GD_CullDistanceManager`, `GD_DistanceCulling`: flips `Renderer.enabled` for small
objects and `GameObject.layer` for background roots; scenery, not NPCs).

**Faces and corpses.** Enemy faces are Synty Sidekick meshes (`CC_Combined_LOD0-4`; LOD3/4 have no shapes), 147 blend shapes = the
full ARKit set (`eyeBlinkLeft/Right`, `jawOpen`) + Sidekick keys. The game rewrites face shapes **every frame** (`faceAnimator`
+ `ANBBlendShapeSync`, which copies `mainMesh` to the other meshes in `UpdateExec`; 232/232 frames overwritten), so a mod must write
every frame, or stop a corpse's `faceAnimator` and skip its `UpdateExec` (`UpdateExec` has one caller,
`checkVisibilityRelatedActions`). `face_death` is the game's open-mouth face.
**Twitcher ("lying in pain"):** `isDead` is already true. `TakeDamage` sets `isTwitcher` on a lethal hit when `combatModeActive`, not a
headshot, `torsoHit <= 2`, `chestHit <= 1`; `startTwitcher` (after `ANBPM.twitcherWaitTime`) needs `isTwitcher && isDead`. The bleed-out
check runs in `UpdateExec`, called by `ANBUpdateCentral.UpdateCalls` only for `allNpcs` entries with `NPCSpawned` (0xb06) &&
`NPCFullySetup` (0xb03); in play twitchers writhe **forever**, and a limb hit on a twitcher is ignored by design (non-limb →
`killTwitcher`). `forcedTwitcher` 2 = force, 1 = never.

**Why most enemy shots miss (Hard).** `FireBulletNew` makes a bullet harmless when `InCooldown || nonLethalFire || !weaponFiredOnce`
(it then uses `HitLayerMaskEnemyCooldowned`). Difficulty numbers: 1 Easy, 2 Normal, 3 Hard. 47-80% of enemy shots on Hard are
harmless. **Only two rules produce harmless shots on Hard:** the **warm-up second** (`setWeaponFiredOnce` waits 1.0 s, re-armed after
`resetFirstShotTime` 5/8/15 s out of sight) and the **hit-confirm grace** (a hit, stab or grab you land runs
`startEnemyCooldown`, then every enemy's shots are harmless for `EnemyCooldownTime` 2 s, retriggerable every 4 s per enemy; the
game has `useEnemyCooldown`). The "warning shot" (`checkMissShot` → `nonLethalFire`) adds nothing on Hard (it fires only out of
your view and only while the other flags already apply); Easy/Normal add warning shots (`firstShotMiss*`, `noFirstShotMissDistance` 3 m).

**How a bullet reaches you.** `EnemyTriggerPulled` only stores `tmpEnemyBulletSource` (0x3d8) / `direction` (0x3e0) / `spread`
(0x3ec); `FireBulletNew`'s enemy branch re-reads them and applies `ApplyRandomAngle(dir, enemyBulletSpreadAddition + spread)`
(per pellet with `ShotRadius` for shotguns), then `FireBullet`. **The only bullet damage path is `BulletImpact` → `HurtPlayer`, and only
when the hit collider is `PlayerHitTarget` (0x13b0), `PlayerHitTargetLegs` (0x13b8) or `PlayerHitTargetHips` (0x13c0).** Measured:
`PlayerHitTarget` = a 0.30 m sphere exactly at the head (follows a duck; the "chest" target is the head), hips = 0.39×0.40 capsule
0.45 m below, legs = 0.39 m sphere 1.35 m below. `HurtPlayer` subtracts and `checkHealth` → `PlayerDeadCall` runs inside it.
`ANBGameLogic.FireBullet` has `if (isSilenced && FromEnemy == 0) MakeNoise("silenced shot") else { GunFireAlert(); playerDetector.Expose(); }`:
an unsilenced shot sets your visibility to full for `exposeTime` 3 s (and **the same else runs for every enemy shot**, so enemies
firing at you also expose you; probable game bug). The Outpost has the visibility system (height, speed, light, 3 s gunfire
exposure); the aim point follows a crouch.

**Slow motion on demand.** `scriptedSlowmotionMin/VeryMed/Max(time)` = step 1/2/3 into `scriptedSlowmotionExecute(time, step, nrt)`
(sets `scriptedSlowmotionActive` 0xd51, calls `toggleSlowMotion(step, scripted: true)`, waits real time, restores the saved step);
wrappers do nothing while one runs. The last-enemy slow motion is step 2.

**Doors.** `ANBPhysicsDoor.kickDoor` (via `openDoor(origin, fromenemy, kick)`) plays `SFXOpenKick` and calls `QuickAlertToPlayer` =
`AlertAllNPCs`, so **any door kick, by anyone, alerts every enemy to you** (accepted as game design). Enemies kicking the same door
20-200 times in 10 s happens in vanilla and is worse during searches; each kick alerts again.

**Sound and noise.** `FireBullet` → `MakeNoise("silenced shot")` (radius ~7 m, hear only) and `GunFireAlert` → `AlertAllNPCs`;
`HVRPhysicsBow.ShootArrow` never reaches it, **so a bow shot raises no alert**. Arrows can still cause: `StabWorld` → `SpawnDecal` →
possibly `MakeNoise("impact")` (not seen in 7 world hits), physics prop collision sounds (`PlayClipPhysicsHard/Medium/Soft` →
`MakeNoise("noise")`), `ANBBreakable.shatterMe`, NPC voice lines. Hearing is synchronous (`AlertAllNPCs` calls `StartAttack` /
`StartHunt`) and a heard noise whose `target` is the player Transform sends enemies to **you**, not to the noise origin. In a
measured run the only sound that woke enemies was a door kick (9 enemies from 15 alerts, mostly enemy-opened doors).
Pistol shots seem to "call" enemies through `Expose` (visibility), not hearing.

**Game-mode facts worth knowing.** Challenge spawn count: `ANBContractTerminal.buttonEnemiesAdd` clamps `CD_totalSpawns` (0x1498)
to `ANBContractData.maxTakedownEnemies` (0xB4; ctor default 40, serialized value lower); `ANBNpcSpawner.maxEnemiesAtOnce` is a
separate setting. Bomb ("rook") waves: `ANBGameLogic.DeathByRook` from `ANBChallengeTriggerbox`. Player/enemy `ANBEncounterSystem`
keeps the collective player position (`lastKnownPlayerPositionKeepTime` 3 s), flanking max 3.

**Blood decals.** `ANBGameLogic.SpawnBloodDecal(point, dir, ...)` (checked) raycasts along `dir` on `BloodOnMask`, picks a random prefab
from `BloodDecals` and instantiates it at the hit; budget `maxBloodPerFrame` / `bloodPerFrame`, gated by `showBlood` (0x11c4).
The prefabs carry `BFX_DecalSettings` (`startScale`, `TimeScaleMin/Max` = size animation, `BloodSettings`); hit/death blood comes from
`DecalOnHit` (`IntervalPerDecals` cooldown). **Third-party blood mods rescale every decal prefab**: Gunman Contracts Blood Overhaul
(flowz) postfixes `BFX_DecalSettings.Awake` / `DecalOnHit.Awake` / `ANBWallBlood.Awake` and multiplies `startScale` (whole Vector3,
depth too), the lifetime and the cooldown by its `[BloodOverhaul]` prefs (code defaults 0.7 / 2 / 3). Anything that calls
`SpawnBloodDecal` directly (Decapitation 1.0.0 `NeckBleeding.Tick`: decal + `SpawnVolumeBlood` every 0.16-0.32 s for
`NeckBloodSeconds`, bypassing the `DecalOnHit` cooldown) therefore inherits that scale. **Whole room turns dark red and flat after
decapitations = oversized decals (confirmed 2026-10-04 by removing the conflict): Blood Overhaul at 10 / 5 / 10 was installed.**

---

## 7. Sound (for custom sounds later)

- **Two entry points, same signature:** `ANBGameLogic.PlayAudioClip(...)` and `ANBSFXPlayerManager.PlayAudioClip(...)` take
  `(AudioClip clip, Vector3 pos, bool useDistanceCheck, string type, bool loud, float pitch, float volume)`; variants
  `PlayAudioClipLoud`, `...LoudNopitch`, `...Nopitch`, `...Quick`, `...QuickLoud`. Use the game's own call style (melee hits):
  `PlayAudioClip(clip, part.transform.position, false, "default", false, -1, -1)`: pitch -1 = `TimePitch` (slow motion lowers
  the pitch), volume -1 = the player's `SoundVolumeSFX`, 3D position.
- Under them is HVR's `SFXPlayer.PlaySFX`: a **small round-robin AudioSource pool**, each new sound stops the oldest, a clip in its
  cooldown (`useCooldownSFX`) is skipped, so gunfire and impacts cut quieter sounds in a busy fight. For a sound that must not be cut
  off or needs its own falloff, copy the game's SFX reference source (same mixer group and spatializer) onto your own AudioSource.
- **Clips:** `ANBGameLogic.BluntHit` (0x1000), `BluntHitHead` (0x1008), `FleshKnifeHit` / `FleshKnifeSlashHit` (0xff0 / 0xff8),
  `WoodHit` (0x1010) are `AudioClip[]`; `ANBSFXPlayerManager.Clips_*` (pickups, tutorial hint, slow motion start/end, damage taken,
  last health point, heartbeat, voice lines); `ANBPhysicsDoor.SFXOpenKick`; `ANBFootsteps` per surface (`walkOn` 1 Metal, 2 Wood,
  else Generic; enemy steps play only within `distanceForHearing`); `ANBBasicNPC` voice methods (`PlaySoundDeath`, `PlaySoundHurt`,
  `PlaySoundInjured`, `PlaySoundTwitcher`, `PlaySoundSilence`, `PlayVoiceSingle`, `EndVoice`/`EndAll`). Where voice clips come
  from: not read.
- **`ANBSoundPhysicsItem`** (physics props, and the crowbar so the clubs): `OnCollisionEnter` picks a clip by `PhysicsType Material`
  and impact tier, or from `customSounds` with `overrideSoundsSoft/Medium/Hard`; `useLayerMaskOverride` + `layerMaskOverride` limit
  what makes a sound; `noiseOnSoft/Medium/Hard` + `canAlert` decide whether it alerts enemies. From field names; not tested.
- **Melee hit sounds play only on living enemies** (`TakeBluntWeaponDamage` / `TakeMeleeDamage` play a random `BluntHit`).
  Death: `KillNPCExec` plays `PlaySoundDeath`, or `PlaySoundSilence` on a headshot. Writhing: `startTwitcher` → `PlaySoundTwitcher`;
  `killTwitcher` → `EndVoice` + `EndAll`; a hit on a writhing body: leg → `PlaySoundHurt`, torso/head → `PlaySoundDeath`
  (head: `PlaySoundSilence`).
- **Not checked:** whether `PlayAudioClip` itself makes noise (assumed not).
- **Finding which clip a sound is: use [SoundProbe](SoundProbe/README.md)** (diagnostic mod, off by default; logs every pool sound by clip name with time and position). Do this before guessing from field names.
- **Ways to add sounds, cheapest first:** (1) third-party AudioReplacer (installed, with AudioImportLib): patches `AudioSource.Play`
  and swaps a clip for a file in `UserData\CustomAudio` with a matching clip name, everywhere; only replaces, only `AudioSource.Play`
  paths. (2) Swap entries in the game's arrays at runtime. (3) Play our own clips at our own events with the game's call style.
  (4) Load files with AudioImportLib (BASS), or embed WAVs and use `AudioClip.Create` + `SetData`.
- Open: "club on an enemy is silent" (the call should run on every hit on a living enemy: check whether `BluntHit` is empty or quiet,
  or whether the damage path skips it); club on club has no sound. See [ROADMAP.md](ROADMAP.md).

---

## 8. Flat (non-VR) mode

- **Controller:** the Infima **Low Poly Shooter Pack** `Character` (`Il2CppInfimaGames.LowPolyShooterPack`), not HurricaneVR. No
  hands, no grabbing, no physics throws. `cursorLocked` (0x154) gates `OnLook`, `OnTryFire`, `OnTryAiming`; `UpdateCursorState`
  sets the cursor from it; `ManualLockCursor` refuses while `editingGun` (0x18d); `menuShown` 0x18c.
- **Flat guns are the same game guns:** `ANBFPSCore.addGun(pos, grabbedGun)` builds the flat inventory from the HVR guns; each
  `ANBFpsWeapons` keeps `HVRgunbase`. Firing: `Character.Fire` → `ANBFpsWeapons.fire` → `ANBHVRGunBase.FPSshoot` → `FireBulletNew`
  → `ANBGameLogic.FireBullet` → `BulletImpact` → `ANBBasicNPC.TakeDamage` → `GetHit`. **Anything hooked on the bullet → enemy damage
  path already runs in flat.** Flat `Weapon.automatic` (bool, 0x44) is the only fire mode; every gamepad button is taken.
- **Shared:** enemies, spawners, alerts, slow motion (`Character.OnSlowMotionToggle` → `buttonSlowMotion` → `toggleSlowMotion`, same
  `ANBGameLogic.Slowmotion`). **Flat melee** = LPSP "Knife Attack" (`Character.PlayMelee`), damage probably via
  `ANBKnifeSlasher.OnTriggerEnter` → `TakeKnifeSlashDamage` → `GetHit` (inferred). **Flat bow** = `ANBHVRGunBase.BowFPSShootStart/End`
  from `Character.Update`, not `ShootArrow`. **Flat pickup** = `ANBFpsInteraction` (raycast + button).
- **Detection:** `!XRSettings.isDeviceActive` (in `UnityEngine.VRModule`), or the `Character` exists and no VR hands do.
- Per-mod verdicts and the plan: [ROADMAP.md](ROADMAP.md) "Flat mode".
