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

## Physical dodge

Built: [PhysicalDodge/](PhysicalDodge/PhysicalDodge.cs) (0.2.0: enemies aim where you were
`AimLagSeconds` ago). The notes on how enemies shoot are in its source comments.

## New melee weapons (billy clubs, etc.)

Asked Sep 27 2026. Goal: Daredevil billy clubs (homing throw, ricochet off enemies **and walls**,
return to hand), with an option to join them into a **staff** or a **nunchaku**. Later: own model via Blender.

**What the game ships (asset scan of every level + sharedassets, Sep 27 2026):**
- **No nunchucks, bats or clubs exist.** `ANBBluntWeapon` is only on scene props:
  **`Prop-Bluntweapon-Crowbar`** (The Range = `level2`), `Prop-LongGrabBase_Pan` / `_Pan2` / `_Pan2_1` +
  `guitar` (Restaurant = `level4`), `Prop-LongGrabBase_Pan3` (Outpost = `level5`). No prefab in sharedassets.
- **★ The crowbar already carries BOTH `ANBBluntWeapon` and `ANBAssistedThrowingObject`** = a blunt weapon
  with the homing throw. Best base for a club. Only in The Range, which is the hub, so a mod can clone it there
  into a `DontDestroyOnLoad` template and spawn copies in contracts.
- Knives (`ANBKnife` + `ANBKnifeSlasher` + throw assist) are prefabs in `sharedassets2`: Combat 1–4, Kitchen 1–4,
  Pen 1–2, **Katana** and **Katana-double**. **Pistols also have `ANBAssistedThrowingObject`** (homing pistol throw).
- Scenes: level0 GameLoader, 1 MainMenu, 2 The Range, 3 Warehouse, 4 Restaurant, 5 Outpost.
- **`ANBKnife.checkAutoReturn` / `returnKnife`**: in play it feels like a timer that puts the knife back in its holster
  after a while (Evhenii), so it isn't a fly-back-to-hand throw. Called from `ANBKnife.Update` and `checkAutoReturn`.
  The club's return has to be our own code.
- Tool: the scan script finds MonoBehaviours by MonoScript pathID (`globalgamemanagers.assets`:
  ANBBluntWeapon 5360, ANBKnife 2762, ANBAssistedThrowingObject 6747, ANBKnifeSlasher 2560).

**Concept 1 (the first build, agreed Sep 27 2026):** club **shape + model**, **throw assist**, and a way to get
it into the game. UnityExplorer is fine for spawning at first; a proper in-game way is needed soon after.
**Optional / later:** fly-back-to-hand (totally optional for now), ricochet, staff, nunchaku.

**Plan (order):** clubs shape first; staff/nunchaku only after seeing how the clubs go.
1. **UnityExplorer** (yukieiji fork, ML 0.7 IL2CPP build) to test for free: clone the crowbar, swap its mesh for a
   cylinder, tweak `ANBBluntWeapon` / throw fields live.
2. Club mod: crowbar template → red cylinder mesh (`CreatePrimitive` mesh, no model needed), two per contract,
   spawned into holsters or by a button combo.
3. Ricochet: on a wall hit, `Vector3.Reflect` the velocity by the contact normal and re-home on the next enemy in
   view; on an enemy hit, re-home on the next one. Bounce limit, then return to hand.
4. Staff: hold both clubs end to end → replace them with one long two-hand grabbable (HVR supports two-hand grips).
   Nunchaku: link the two clubs with a joint (a short `ConfigurableJoint` chain). Riskiest part: joint stability
   against a 20 kg hand rigidbody.
5. Own model: **no Unity Editor needed** if the mod parses an `.obj` itself (~60 lines → `Mesh`) and clones a game
   URP Lit material with its own textures (`ImageConversion.LoadImage`). An AssetBundle route needs Unity
   6000.0.41f1 + URP and can hold meshes and materials only (IL2CPP can't load new MonoBehaviours).

**Mod menu:** a shared in-VR settings/spawn menu across the mods is worth building once the mod count justifies it.
Until then: MelonPreferences + button combos, and UnityExplorer on the desktop for testing.

## Pistol throw aim assist

Asked Sep 27 2026: throwing a pistol doesn't feel assisted. **Found why: the dev switched it off.**

- Every pistol prefab (11 `gunman_weapon_pistol`, 10 `enemy_weapon_pistol`, `sharedassets2`) has an
  `ANBAssistedThrowingObject`, and it's wired to the release event (`StartAssistedThrow` is called from UnityEvents;
  its only direct code caller is `ANBKnife.releaseKnife`). **But all 21 have `dontUse = true`**, so
  `StartAssistedThrow` returns straight away. Every other item with one (knives, katanas, crowbar, pans, bottles,
  glasses) has `dontUse = false`.
- Pistol tuning as shipped: speed 16, stop distance 1.5 m, `targetSearchDistanceOverride` 6 m,
  `maxFlyDistanceOverride` 4 m (same as crowbar/bottles; knives use 0 = game defaults), aimCorrectionAngle 90,
  torque 30 around Y. **The 4 m fly limit is short**, so it may also need raising.
- Serialized layout (60 bytes after the name): speed, stopDistance, isHoming, targetSearchDistanceOverride,
  maxFlyDistanceOverride, changeRotation, aimCorrectionAngle, torque, spinAxis, rb pptr, **dontUse**.
  `homingTarget` is not serialized.
- **Mod:** on the player's pistols (not `enemy_weapon_pistol`) set `dontUse = false` in `Awake`/`Start`, optionally
  raise the two distance overrides. Still needs the game's own assisted-throw option on.
- **Hit side:** a thrown pistol lands on the gun path in `ANBBodyMeleeCollisionManager.collisionEnter` (1.5 kg:
  damage yes, stumble no, since stumble needs > 5 kg). Heavy Melee only boosts **held** guns. A thrown-pistol stumble
  would fit in Heavy Melee.
- Unknown why the dev disabled it (might be deliberate balance, or it misbehaved). Test before shipping.

## Daredevil gloves (optional, dark red)

Asked Sep 27 2026, optional companion to the clubs: tint the player's gloves dark red.
- Glove assets: material/mesh `CHR_Gloves1`, `Righthand_Gloves` / `LeftHand_Gloves` (+ `_HD_LOD0-5`),
  `righthand_fpsgloveorig_HD`, `gloveWristMeshRight`, `vr_glove_arms` (sharedassets1 / resources).
- **Two glove models:** the "High Detail VR Gloves" setting (`useHDGloves`, `ANBGameLogic.toggleVRGloveDetail`)
  swaps between them, so tint both, and re-tint after that toggle.
- `resources.assets` has a `vr_glove_color_red` texture. Likely the stock Meta/Oculus hand-sample texture
  (`vr_glove_*` names), probably unused. Worth a look, but tinting the URP Lit `_BaseColor` is the simple route.
- Mod: find the glove renderers once per scene, set `_BaseColor` (optional: a darker `_BaseMap` via
  `ImageConversion.LoadImage`). Config: on/off + colour.

## Weapon Framework: custom weapons on the gun wall panel (feasibility, Sep 28 2026)

Asked Sep 28 2026: replace the F8 spawn key with an entry on the gun wall panel (the one with the crossbow):
page to "Billy Clubs", retrieve, take them off the wall. Make it a framework other weapon mods can register with.

**How the panel works (`ANBGunwall`, from the binary):**
- It's a carousel over **`Items` (0x40, a list of GameObjects) + `ItemsID` (0x48, a list of strings)**, index
  `currentSpot` (0x1e8). `nextWeapon`/`prevWeapon` step the index; **`pickWeapon`** takes `Items[currentSpot]`,
  moves it to `mainSpot` (0x20), parents it, `SetActive(true)`, moves the previous one to `secondarySpot`,
  plays the `switch`/`intro` animation and calls `ANBSaveData.SavePurchases`.
- **`printInfo`** fills the text and icon from the item's **`ANBWeaponType`** component (`WeaponDisplayName`,
  `WeaponDescription`, `WeaponIcon` sprite, `WeaponPrice`, `WeaponID`, `ammoType`, attachment prices) and asks
  **`ANBDataCollection.checkPurchaseDataWeapon(ANBWeaponType)`** whether it's owned (retrieve vs buy button).
- `ANBGunwallSpot` is a different thing: the wall hangers (`WeaponPrefab`, `demoObject`, `Hanger` socket, `IsKnife`);
  their `initSlot2` calls `ANBWeaponType.CreatePreview` and `ANBWeaponAttachments.checkForSave`.
- `SavePurchases` writes `PC_C` / `PC_W` under `SaveData_PurchasesAndUnlocks`. Unconfirmed whether the selected
  index is part of it (`setSavedSpot` has no direct callers).

**Plan:**
1. On a Range load, append one entry per registered weapon to `Items`/`ItemsID`. The entry = a rack object with an
   added `ANBWeaponType` (name, description, PNG icon via `Sprite.Create`, price 0, own `WeaponID`) holding the club
   pair. Retrieve shows it at `mainSpot`, you grab the clubs off it. Postfix `checkPurchaseDataWeapon` → true for our IDs.
2. **Uninstall safety:** never let a save hold our entry's index or ID. Removing the mod must leave a valid vanilla save.
3. Framework split: `WeaponFramework.dll` owns the panel entry, belt holster slots, carry-over into contracts, recall
   and its own save; Billy Clubs becomes its first client (`MelonAdditionalDependencies`). F8 stays as an optional
   dev key. Optional later: data-only weapons (folder with `weapon.json` + `.obj` + `.png`, based on crowbar / knife /
   katana), so non-coders can add weapons.

**First checks (one session):** log one existing `Items` entry (hierarchy + components; the bow's is the best
template, it's a non-gun); see whether `printInfo`/`enableBuyButton` throw on an entry with no mag/attachments;
find whether a gun wall also exists outside The Range (`carScript` = `ANBGunmanCar` hints at one in the car).

**Estimate:** medium. Panel entry = 1–2 sessions; framework extraction = 1–2 more (mostly moving working Billy Clubs code).

## Long-term backlog: a real stun animation for Billy Clubs chest hits (Sep 28 2026)

A thrown club to the chest should stagger/daze the enemy with an animation, not just freeze them. Billy Clubs 0.8.2's
`ThrowReaction = Stun` only extends the AI stun timer (`overallHitTime` / `isGettingHit`), so the enemy stands still
with the gun pointed at you, which looks unnatural. It would need a new or borrowed animator state on the "Hit" layer
(see how Knee Shot Stun drives `hit_legs_1`). Until then, chest hits use Kneel (or Knockdown).

## Daredevil split: "Devil" (clubs) + "Radar Sense" (enemy sense) + Throw Assist (Sep 28 2026)

Status: the clubs work, the red gloves are done, and what's left is polish. **The missing piece is seeing enemies
through walls.** Plan to split into three mods:

1. **Devil** (Billy Clubs renamed): clubs, holsters, recall, ricochet, red gloves.
2. **Radar Sense** (built Sep 28 2026, 0.1.0 untested: [RadarSense/](RadarSense/README.md)): **while slow motion is
   on (the dodge slow motion too), enemies show through walls as a red silhouette.** Red, not the game's yellow.
   Setting `ActiveWhen = Always` keeps it on.
   **Packaging decided (Sep 28 2026): Throw Assist is its own mod (`ThrowAssist/`, 0.1.0: pistols moved out of Billy
   Clubs), and the Daredevil package (Billy Clubs/"Devil" + Radar Sense) lists it as required on Nexus.**
   **Built the same day: `Daredevil/` 0.1.0 = Billy Clubs 0.11.0 + Radar Sense 0.3.1 in one DLL** (compiled from both
   folders with `DAREDEVIL` defined; zips in `release/Daredevil/` and `release/ThrowAssist/`). Club flight
   (ricochets, styles) stays in Billy Clubs; Throw Assist skips `BillyClub-*`. **Later idea:** turn it on briefly when you get hit or on a
   collision/impact (a Daredevil "sound ping").
   **More Daredevil ideas (Sep 28 2026, not built):** an echolocation sweep (a ring expands from you and enemies light
   up as it passes them, then fade); sound reveals (an enemy that shoots, shouts or kicks a door flashes for a moment,
   since Daredevil hears them); silhouettes fading with distance; a slow heartbeat pulse on the opacity.
3. **Throw assist for every weapon**, including pistols (ships `dontUse = true`, see "Pistol throw aim assist"
   above). Billy Clubs' physics-steered assist already works better than vanilla, so reuse it for pistols, knives
   and anything with `ANBAssistedThrowingObject`. **Undecided:** part of Devil, or its own mod. Lean: its own mod,
   because it helps players who never use the clubs. **Whichever mod owns it must be the only one steering throws.**
   So move `ThrowAssist.cs` out of Billy Clubs and keep only the club-specific parts there (spin styles, ricochet).

### Next (asked Sep 28 2026): BOTH BUILT the same day (Radar Sense 0.3.0 clubs, Billy Clubs 0.10.0 pistols), untested

**1. Radar Sense finds dropped Billy Clubs.** Easy. Clubs are plain `MeshRenderer`s (spawned from the `BillyClub`
template, `Instantiate(Template)`), so the copy is a child `MeshFilter` + `MeshRenderer` with the same mesh and the
radar material. **No skinning, and no problem with the game hiding them.** Find them by name (`BillyClub`, `(Clone)`
suffix) in a 1 Hz scan, so Radar Sense doesn't depend on Billy Clubs. Own colour setting (not enemy red). **Show a club
only when it's more than ~1.5 m from your head.** Otherwise a club in your hand or holster would light up where your
glove covers it (depth test Greater). Same `ActiveWhen` / focus rules as enemies. Maybe later: any dropped weapon.

**2. Pistol throw: aim assist + real damage.** Two parts:
- **Assist:** every player pistol ships with `ANBAssistedThrowingObject.dontUse = true` (see "Pistol throw aim assist"
  above), so `StartAssistedThrow` returns at once. Set `dontUse = false` on `gunman_weapon_pistol*` (not
  `enemy_weapon_pistol`), and raise `maxFlyDistanceOverride` (4 m as shipped). **Better: steer it with Billy Clubs'
  physics assist** (`ThrowAssist.cs` already hooks `StartAssistedThrow` for every item and feels better than vanilla).
  This is where the planned standalone Throw Assist mod starts.
- **Damage:** a thrown pistol is 1.5 kg on the gun path of `ANBBodyMeleeCollisionManager.collisionEnter`: some damage,
  never a stumble (needs > 5 kg). Heavy Melee only boosts HELD guns. Add a thrown-pistol damage multiplier + a stagger or
  kneel (the way Billy Clubs does `ThrowReaction`), only for a pistol in flight after a throw.
- Decide where it lives: Throw Assist (own mod, lean) or Heavy Melee (damage side).

### Enemy wall vision: feasibility (checked Sep 28 2026)

**Finding enemies is solved.** Awareness Fix/Log already walk every `ANBBasicNPC`. **Slow motion:** patch
`ANBGameLogic.toggleSlowMotion(step, scripted)`, or read `Slowmotion` (0xcfc). The player's right-B slow motion is
`scripted = false`. The dodge slow motion is `scripted = true`, and a setting decides whether that one counts too.

**Drawing through walls** is the only real work. Standard method: for each enemy body `SkinnedMeshRenderer`, add a
child renderer with the same `sharedMesh`, `bones` and `rootBone` (it skins with the enemy for free), no shadows, and
a red material whose **depth test is Greater (only the hidden parts show) or Always**. Enable those renderers only
while slow motion is on. About 10–20 extra skinned draws, only during slow motion.

Shaders registered in the build (`globalgamemanagers` shader-name list) that can do the depth trick, cheapest first:
- **`Custom/UIAlwaysOnTop`**: the game's own draw-on-top shader. Flat colour.
- **`UI/Default`**: `unity_GUIZTestMode` = Greater/Always. Flat colour. Supports single-pass stereo.
- **`Hidden/Internal-Colored`**: has `_ZTest`, `_Cull` and blend properties.
- **`Shader Graphs/Rim Dissolve`**: the holster hologram (additive Fresnel rim), so an outline-like look for free
  **if** it exposes `_ZTest` (check `HasProperty` at runtime).
- **Not usable:** the **Highlighters** package (`Il2CppHighlighters`, `HighlightersUrp`: outline + see-through
  `DepthMask.BehindOnly`) is compiled in, but no asset file mentions it, so its renderer feature and shaders
  are almost certainly not in the build.

**⚠ The VR risk is single-pass instanced stereo.** A shader without stereo-instancing support renders in one
eye only. First test = a flat red silhouette with each candidate, log which ones show in **both** eyes.

**A clean outline** (edge only, not a filled body) needs an inverted-hull or rim shader with depth test Greater.
If Rim Dissolve can't do it, build one shader in an AssetBundle (Unity 6000.0.41f1 + URP, material and shader only).
That is the only step that needs the Editor.

**Estimate:** easy. One session gets a working red silhouette. The outline look is a second, optional step.

