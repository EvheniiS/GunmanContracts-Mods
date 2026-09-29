# Flat-screen mode: which mods could support it (audit, Sep 29 2026)

Mod Settings 0.3.0 is the only mod with a flat version so far (Ctrl+M IMGUI menu). This file checks every other mod
against how flat mode actually works in the game code. Weapon Framework's flat plan is in
[WeaponFramework/ROADMAP.md](WeaponFramework/ROADMAP.md) section 4.

**checked** = read in the game code; **inferred** = follows from the code, not seen in game; **untested** = needs a run.

## How flat mode works (what decides the answer)

- **Controller:** the Infima **Low Poly Shooter Pack** `Character` (`Il2CppInfimaGames.LowPolyShooterPack`), not
  HurricaneVR. No hands, no grabbing, no physics throws. Found for Fire Selector (Sep 27) and Mod Settings (Sep 28).
- **Guns are the SAME game guns underneath (checked).** `ANBFPSCore.addGun(pos, grabbedGun)` builds the flat
  inventory from the HVR guns; each flat weapon's `ANBFpsWeapons` keeps `HVRgunbase` (an `ANBHVRGunBase`). Firing:
  `Character.Fire` → `ANBFpsWeapons.fire` → **`ANBHVRGunBase.FPSshoot`** → (virtual call, inferred) `FireBulletNew`
  → `ANBGameLogic.FireBullet` → `BulletImpact` → `ANBBasicNPC.TakeDamage` → `GetHit`.
  **So anything hooked on the bullet → enemy damage path already runs in flat.**
- **Enemies, spawners, alerts, slow motion are shared.** Flat's slow-motion key: `Character.OnSlowMotionToggle` →
  `ANBGameLogic.buttonSlowMotion` → `toggleSlowMotion` (checked), the same state (`ANBGameLogic.Slowmotion`) VR uses.
- **Flat melee** = the LPSP "Knife Attack" animation (`Character.PlayMelee`, `SetActiveKnife`, checked). Its damage
  most likely comes through `ANBKnifeSlasher.OnTriggerEnter` → `TakeKnifeSlashDamage` → `GetHit` (inferred).
- **Flat bow** = `ANBHVRGunBase.BowFPSShootStart/End` from `Character.Update` (checked), not `HVRPhysicsBow.ShootArrow`.
- **Flat pickup** = `ANBFpsInteraction` (raycast + button): `grabGun`, `grabKnife`, `hangWeaponOnWall`, `grabAmmo`.
- **Detection:** `!XRSettings.isDeviceActive` (in `UnityEngine.VRModule`), or the `Character` exists and no VR hands
  do. Copy Mod Settings' 10 lines rather than share a DLL.

## Per mod

| Mod | Hooks | Flat verdict | Work |
|---|---|---|---|
| **Mod Settings** | phone tile, `buttonPush` | **Done** (0.3.0, untested in game) | test |
| **Knee Shot Stun** | `ANBBasicNPC.GetHit` | **Probably already works.** Flat bullets reach `GetHit` through the shared gun code. | **none; one flat test**, then add "works in flat" to the Nexus page |
| **Enemy Awareness Fix** | NPC AI + spawners; player = `ANBGameLogic.PlayerHitTarget` | **Probably already works** (AI and the player target are mode-independent). | **one flat test** (waves, search behaviour) |
| **Enemy Awareness Log** | AI, alerts, `FireBullet`, `ShootArrow` | Works except the bow line (flat bow skips `ShootArrow`). Diagnostic only. | optional: hook `BowFPSShootEnd` |
| **Radar Sense** (in Daredevil) | `ANBGameLogic.Slowmotion`, enemy renderers, `MainCam` | **Probably works as is:** flat slow motion sets the same state, silhouettes are material copies on enemy renderers (any camera). Loud steps are enemy-side. | test; ship as a **flat-only Radar Sense toggle** or let Daredevil run with the club part off in flat |
| **Physical Dodge** | enemy `FireBulletNew`, `HurtPlayer`, head = `MainCam` | **Half works.** Aim lag (enemies aim where you were 0.2 s ago) is enemy-side and runs in flat. The "real dodge" check measures head movement inside the tracking space, which is ~0 in flat, so slow motion never triggers. | **Medium, worth it.** Flat branch: count WASD strafe/crouch/slide speed as the dodge. **Balance risk:** flat strafing is fast and constant, so 0.2 s lag would make flat much easier; needs its own `AimLag` for flat (start ~0.08 s) |
| **Better Bow** | `ShootArrow`, arrow grab/quiver, knife stab | Mostly VR hand fixes, don't apply. Flat bow probably can't breach doors either (same missing `TryKickDoor` call, flat gate `useFPSDoorKick`). | low: door breach for the flat bow via `BowFPSShootEnd` |
| **Heavy Melee** | `TakeMeleeDamage`, `TakeBluntWeaponDamage`, `ANBBodyMeleeCollisionManager` (fists/gun bashes) | Physical VR collisions, not flat. Flat melee takes the knife-slash path, which it doesn't hook. | low: a flat "knife slash staggers" option is a different mod |
| **Fire Selector** | `checkVRButtonTaps` | Assessed Sep 27, **decided not to build** (every gamepad button taken; flat `Weapon.automatic` is the only mode). | none |
| **Throw Assist** | `StartAssistedThrow` (from `ANBKnife.releaseKnife`), thrown-pistol damage | VR throws only. Flat has grenades, no thrown knives or pistols. | none |
| **Grab Fix** | HVR hand/force grabbers | VR only by definition. | none |
| **Gloves** | VR hand materials | Flat arms are LPSP meshes with their own materials. Tinting them is possible, low value alone. | low; only as part of a flat Daredevil |
| **Daredevil (clubs)** | HVR grabbables, belt, homing throws | Clubs need hands. Flat version = a new flat melee weapon (see Weapon Framework flat step 2: re-skin the flat knife). | high; after the framework work |
| **Weapon Framework** | arsenal panel, `pickWeapon`, saves | Panel is shared, flat pickup of a mod slot untested. | **small now** (hide mod entries in flat), large later |
| Grab Log | diagnostic, retired | not needed | none |

## Recommendation, in order

1. **Free wins, test only:** Knee Shot Stun, Enemy Awareness Fix, Radar Sense. One flat session with `DebugLog = true`
   on each: shoot legs, run a wave, trigger slow motion. If they work, say so on Nexus (more players, zero code).
2. **Daredevil flat guard:** in flat, skip the club/belt code (no belt, no hands), keep Radar Sense. Otherwise F8 spawns
   clubs nobody can hold, and the belt search runs for 30 s every scene.
3. **Weapon Framework:** hide mod entries in flat (roadmap section 4 step 1).
4. **Physical Dodge flat branch** with its own lag setting: the only port that adds real gameplay to flat.
5. Later: flat melee packs through the flat knife (framework), which is also the route to flat clubs.

## Test plan for one flat session

- Start without the headset (flat mode), `DebugLog = true` for Knee Shot Stun, Enemy Awareness Fix, Radar Sense,
  Physical Dodge, Weapon Framework.
- The Range: Ctrl+M (Mod Settings), page the arsenal to the mod entry and try to take it (log what happens).
- A contract: leg shots (Knee Shot Stun kneel line), slow-motion key (Radar Sense silhouettes), strafe under fire
  (Physical Dodge: aim-lag misses logged, no dodge slow motion expected), a wave (Enemy Awareness Fix search lines).
