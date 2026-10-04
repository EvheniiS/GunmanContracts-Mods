# Billy Clubs (part of Daredevil)

**Legacy as a standalone mod since Sep 28 2026:** this code ships only inside the Daredevil package (`../README.md`).
The version notes below are history (last standalone number 0.12.0 = Daredevil 0.2.0).

0.12.0: **on the arsenal panel** in The Range, through the new **Weapon Framework** mod (`WeaponFramework/`, optional):
page to "Billy Clubs", press Retrieve, and the pair hangs on the wall slot, tip down (`Arsenal.cs`). The wall shows the
clubs you don't carry: loose ones come back to it, new ones fill up to two; F8 also pulls clubs off the wall.
`WallOffset` / `WallGap` place the pair. Untested in game.

0.11.0: the pistol throw (assist + damage, 0.10.x) moved to its own mod, **Throw Assist** (`ThrowAssist/`), which the
Daredevil package will require. Billy Clubs keeps its club flight; Throw Assist leaves `BillyClub-*` objects alone.
0.10.0 test: the pistol assist worked but was too aggressive (every lob homed at 13 m/s).

0.10.1: the pistol assist is less aggressive. A throw slower than `PistolAssistMinSpeed` (6 m/s) gets no assist, and an
assisted pistol flies at your own throw speed (0.10.0 homed every lob and sped it up to 13 m/s, the clubs' minimum).

Swing diagnostics (Oct 1 2026): `SwingLog = true` logs one line per swing (tip lag in ms/cm, hand vs grip share, overshoot after you stop) and the hand/joint physics numbers once per grip; `SwingLogMinSpeed` (4 m/s). `Mass` now applies live. Experiments (live, defaults = the game's values): `HandStrengthScale`, `HandTorqueScale`, `ClubInertiaScale`, `ClubMaxSpin`; a `FLIP` log line explains a club that turns far around the hand.

Daredevil-style clubs based on the game's crowbar, with belt holsters, F8 recall, throw styles and ricochets.

Current follow-through diagnostics: `DebugLog` reports speed changes made after release. Set
`FollowThroughVerboseLog = true` to also report windows that made no change. A one-frame controller tracking jump
above 25 m/s is ignored. This affects Billy Clubs only; Throw Assist's knives and pistols do not use this feature.

Direct club assist uses the game's view-based target choice, then Daredevil guides the club only when the target
is within `ThrowAssistMaxTurnAngle` (25 degrees by default) of the release direction. `DirectThrowAssist = false`
gives free direct throws, useful for throwing past an enemy or at a wall. A wall directly behind an enemy can
remain inside the angle, so use the switch for that case. `Ricochets` separately controls wall/floor bounces
toward enemies; set it to 0 for a plain wall impact. The club's 13–18 m/s guided speed is unchanged.
`HandThrowBoost = 1.3` scales the release velocity before that guided speed is chosen as well as free throws;
it is not limited to unassisted throws. Follow-through samples the hand for 0.12 seconds after release and only
adds speed if it beats the club's current velocity or steering speed before impact. It does not add speed to every
throw.

0.10.0: thrown pistols. `PistolAssist` (on): a thrown pistol homes in on enemies with the clubs' throw assist (the game
ships its pistol assist switched off, `dontUse = true` on every pistol); needs the game's assisted-throw option on.
`PistolDamage` (on): a thrown pistol that hits an enemy does `meleeDamage x PistolThrowDamage` (3 = 60 to the body,
the head x5 kills) and staggers them (`PistolStagger`), once per enemy per throw. The game's own hit is 10-20 damage,
never a stagger (it needs > 5 kg; a pistol is 1.5), and nothing during the enemy's hit stun. Code: `PistolThrow.cs`.

0.9.2: one grip. The crowbar's second grab point in the middle of the club is switched off, so the hand always takes the club at its grip end (`MiddleGrip = true` brings it back).

0.8.3: Daredevil gloves. The player's gloves are dark red (`GloveColor`, default `#8A0F0F`; empty or `off` leaves them grey). Sleeves are untouched.

0.8.2: a thrown club to the body now stuns the enemy for `ChestStunSeconds` (3 s) instead of making them kneel (`ThrowReaction = Stun`); leg hits still kneel.

0.8.1: the kneel now actually plays, for thrown body hits and for every club hit to the legs (`KneelOnLegHits`); throws are 30% faster (`HandThrowBoost`, steered 13-18 m/s); TipFirst turns tip-forward much faster (`TipTurnTime` 0.04).

0.8.0: a thrown club to the body makes the enemy kneel (`ThrowReaction`: Kneel, Knockdown or None; pairs with Knee Shot Stun), and the next hit knocks them out (`ThrowDamageMultiplier` 2). Enemy hits are detected reliably, so enemy-to-enemy ricochets really stay off. Steered clubs fly at 10-14 m/s.

0.7.3: throw follow-through. Opening your hand early in the swing still gives the club the rest of the swing (`FollowThroughTime`, 0.12 s; 0 = off).

0.7.2: an assisted or ricocheting club flies at your own throw speed (6-11 m/s, `MinSteerSpeed` / `ThrowSpeed`) instead of a fixed 20 m/s.

0.7.1 brings back the tip-first throw as a smooth turn from upright to tip-forward (`TipFirst`, default; `TipTurnTime`), and by default a club that hits an enemy stops there (`RicochetFromEnemy`); walls and the floor still bounce it on.

0.7.0 replaces the game's throw assist for clubs: the game still picks the target, but the mod steers the club with real physics, so it keeps its spin and bounces properly instead of being dragged in a straight line. Throw styles are now SpinEnd (end over end, 5 turns/s) and Natural.

0.6.1 reverts the 0.6.0 holster and grip changes, which made clubs fall out of the hand.

0.6.0: clubs hit harder (x1.5 in hand, x3 thrown), a body hit that takes the last health knocks the enemy out (the game only allowed the head), every enemy in a ricochet chain takes damage, and spin throws turn 8 times a second. Settings: `SwingDamageMultiplier`, `ThrowDamageMultiplier`, `BodyKnockout`, `SameEnemyCooldown`.

0.5.3 adds floor ricochets: throw at the floor (15 degrees or more below horizontal, `FloorShotAngle`) and the club skips the homing, bounces off the floor and flies at the nearest enemy in sight. Any club that happens to hit the floor can ricochet too.

0.5.2 calms the club after it hits an enemy (no more wild spinning), sweeps fast flights so they do not tunnel, turns the club smoothly instead of snapping it, widens the holster snap to 0.4 m and adds holster/draw diagnostics to the debug log.

0.5.1 revises the grip after the first successful VR test: diamonds are 2.5 times larger, burgundy is darker, normal relief is stronger, and the coating is less glossy with subtle darker grooves. Geometry, silver material and gameplay are unchanged. The revised grip still needs a headset check for visibility and shimmer.

## Install and use

Copy `BillyClubs.dll` to the game's `Mods` folder with the game closed. The authored OBJ and all four 2048px textures are embedded in this DLL; no extra asset folders or Unity Editor build are needed.

Start the game, visit **The Range** once, then press **F8** to spawn/recall the pair. The clubs use the new burgundy knurled model automatically. The game crowbar itself is unchanged.

The custom visual keeps the original club's grip points, colliders, mass, holsters, recall, grip tuning and throw logic. The mesh and material are shared between clubs. The authored model is 600 mm long, 34 mm at the hand, 34.8 mm at its widest, and 5,876 triangles. `Length` scales the visual along the shaft; the default retains the authored scale.

Settings are in `UserData/MelonPreferences.cfg`, section `[BillyClubs]`:

- `UseCustomModel = true`: bundled model; set false and restart to use the previous cylinder visual.
- `Radius` and `BodyColor` affect the primitive fallback only.
- Existing grip, holster and throw settings are preserved.

If the model fails to load, the mod logs the error and automatically uses the primitive visual. A successful first Range load logs `custom club visual ready: 6165 vertices, 5876 triangles, 2048px maps, shared mesh/material`.

## Asset integration

`ClubObj.cs` loads the triangulated OBJ with per-corner position/UV/normal tuples. It converts Y-up OBJ to Unity club space by mirroring Z and reversing winding, preserves authored normals, and generates Unity tangents afterward. `CustomVisual.cs` aligns +X to the existing club tip direction and centers the mesh on the shaft collider. No visual colliders are created.

URP Lit uses the sRGB base color, linear tangent-space normal map, and a runtime linear RGBA texture with metallic in R and (1 - roughness) in A. Mipmaps and trilinear filtering are enabled, with anisotropy 8. Three final textures are shared; decoded metallic and roughness intermediates are destroyed. CPU image copies are released after upload. Normal RGB PNG alpha is implicitly 1, so both RGB and RG/AG normal-unpacking paths retain the authored X/Y values. Green is not flipped.

Only the authored assets in `BlenderRefs/out/` are embedded. Extracted game references and Blender scenes are excluded.

## Build and checks

```powershell
dotnet build BillyClubs/BillyClubs.csproj -c Release -o feature/BillyClubs-0.5.1
dotnet restore BillyClubs/Tests/AssetTests.csproj --configfile BillyClubs/Tests/NuGet.Config
dotnet run --project BillyClubs/Tests/AssetTests.csproj -c Release --no-restore -- "C:/path/to/GunmanContracts"
```

The mod targets .NET 6 using the game's generated MelonLoader interop assemblies. Dependency-free asset tests use the .NET 10 SDK. Tests check the assets embedded in the built DLL against the actual exported files, handedness/winding, dimensions, the hand cross-section, all triangle normals and tangent UVs, locale-independent parsing, negative indices, UV seams and malformed input.

Validation: Release build succeeded with zero warnings/errors; all asset tests passed. Runtime visual appearance, shader variants, headset motion aliasing, grip and gameplay behavior still require an in-game test. No gameplay code was changed for 0.5.0.
