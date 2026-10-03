# Throw Assist 0.2.4

Throw assist with real physics, and thrown pistols that hurt. Required by the Daredevil package (Billy Clubs + Radar
Sense). Moved out of Billy Clubs 0.10.x.

- The game picks the target (its own assisted-throw option must be on); the mod stops the game's drag coroutine,
  which zeroes speed and spin and drags the item with `MovePosition`, and steers the item with real velocity instead,
  keeping its spin and collisions.
- **Pistols:** the game ships every pistol's assist off (`dontUse = true`); `PistolAssist` switches it on. A thrown pistol
  that hits an enemy does `meleeDamage x PistolThrowDamage` (3 = 60 to the body, head x5 kills) and staggers them
  (`PistolStagger`), once per enemy per throw. The game's own hit is 10-20, never a stagger (needs > 5 kg; a pistol is
  1.5), and nothing during the enemy's hit stun.
- **Knives:** `SteerOtherItems` is on for new installs, so assisted knives fly blade first at no less than
  their prefab's own assist speed (17 m/s on the tested combat knife). `KnifeSpeedMultiplier = 1` matches that
  speed floor. An unassisted knife can instead tumble end over end in the throw's vertical plane with
  `KnifeVerticalSpin`; set it to `false` to keep the hand's original spin. This only changes the spin when
  there is no assist target. `SteerOtherItems` also supports other items with the game's assisted-throw component,
  but those items have not been broadly tested. There is no need to test props to validate knife throwing.
- **Billy Clubs** fly their own club flight (ricochets, spin styles, floor shots): objects named `BillyClub-*` are left
  to that mod, so the two never steer the same throw.

## 0.2.4: early releases and throws you aren't looking along (built Oct 3 2026, untested; both off by default)

The 03:43-03:57 session: 124 of 149 knife releases were assisted and nearly all stuck. The 25 misses never got an
assist: 12 were early releases at 2.8-3.2 m/s (the hand still speeding up, just under the game's 3.5 gate), 13 were
real throws (4-10 m/s) where the game found "no target in view", because its search is **gaze-based** (headset
position + forward, `assistedThrowViewAngle`), not throw-based. Two of those hit a leg anyway, unassisted.

- `KnifeAssistMinSpeed` (default 3.5 = unchanged; the user runs 2.5): a knife released at least this fast is
  assisted. The game's gate is lowered for that one `releaseKnife` call and restored after it (also on an exception).
  Logged as `early release at <speed> m/s assisted`.
- `AimByThrow` (default off; the user runs it on) + `ThrowAimMaxAngle` (25): when the game finds no target, the
  enemy nearest the throw direction (head, chest or a leg collider within the angle, within `SearchDistance`) is
  used. One `FindObjectsByType` per such throw, nothing per frame. Logged as `(by throw direction, N deg; none in view)`.
- A flight ends as `docked` if the item turns kinematic (put in a holster), so a fast put-back is never steered.

## 0.2.3: long blades (built Oct 3 2026, untested)

Assisted knives are steered by their **tip** (`ANBKnife.StabOrient`, measured along the stab line at release),
not their centre of mass, and steering stops once the steered point is within 0.3 m of the aim point or has
passed it. The item then flies straight on its own momentum, and steering can never reverse inside a body.

**Correction (same night):** this was built for katanas that went through enemies and flew off, but the 0.2.3 test
showed the real cause: every failing katana had been auto-returned to the back by VR Holster Customization, which
left it non-solid. Fixed there in 0.3.5. The tip steering stays; it tested fine (closest approach 0.19-0.28 m).

The flight summary now ends with `top <speed>` and, for steered throws,
`steering: <why it stopped> at <s>, closest <m>`; the release line says `by the tip (<m> ahead)` for knives.

## Settings (`[ThrowAssist]`)

`PistolAssist` (on), `SteerOtherItems` (on for new installs), `MinAssistSpeed` (3.5 m/s for non-pistol props),
`PistolAssistMinSpeed` (3.5 m/s, also subject to the game's assisted-throw threshold),
`PistolSteerMinSpeed` (13 m/s once a target is found),
`MaxSpeed` (18 m/s cap for release-speed steering), `KnifeSpeedMultiplier` (1: knife prefab speed floor),
`KnifeVerticalSpin` (on for unassisted knives), `SearchDistance` (15 m), `MaxFlyDistance` (20 m),
`AimHead` (**on for new installs**; false disables head selection), `HeadAimMaxAngle` (25 degrees),
`allowKneeHit` (**experimental, off by default; 0.2.2 release candidate**), `KneeAimMaxAngle` (25 degrees),
`PistolDamage` (on), `PistolThrowDamage` (3), `PistolStagger` (on), `DebugLog` (on for new installs).

Existing values in `MelonPreferences.cfg` are retained when upgrading. In particular, an older file may still
have `SteerOtherItems = false`, `AimHead = false`, or `DebugLog = false`; change them explicitly to use the new defaults.
Normal Billy Club throws in Daredevil use the same release-direction rule when built with this version. Club
ricochets use Daredevil's separate `[BillyClubs] RicochetAimHead` setting.

Selection is deterministic, with no random headshot percentage. At release, chest is the fallback. Head can win
when enabled, closer to the release direction than chest, and within `HeadAimMaxAngle`. With `allowKneeHit = true`,
the closest enabled physical `LeftLeg` or `RightLeg` collider can win if its center is closer to the release
direction than chest and any eligible head target, and within `KneeAimMaxAngle`. These are angular limits from
the thrown item's position, not fixed slices of the player's view. A tie keeps the current choice (chest first,
then head). Knee selection is skipped for an enemy already kneeling or off balance.

The selected region is locked for the throw while its position follows the enemy; a removed or disabled leg
collider falls back to chest. Set both `AimHead = false` and `allowKneeHit = false` for chest-only assist.
The 25-degree head limit was preferred in the 2026-09-30 session; 25 degrees for knees is a starting point
that still needs more VR testing. The first knee session showed uneven leg contacts; increasing this limit
can admit more release directions but does not improve the trajectory of a throw already selecting Knee.
Lower either limit for stricter aim. The game's target acquisition must still
engage before the mod steers a throw.

With `allowKneeHit = true`, actual leg contacts from tracked thrown pistols, knives, and eligible props play
the same knee-shot animation used by Billy Clubs, once per enemy per throw. This also applies to tracked
unassisted throws and does not require `PistolDamage` or `PistolStagger`. Damage remains on the existing game/mod
path; a fatal hit does not start a kneel. Held items and enemies already kneeling or off balance are excluded.
Knee Shot Stun is optional: when installed and enabled, its existing `GetHit` hook extends the kneel.
Billy Clubs continue to use their own targeting and reaction settings.

To try it in `UserData/MelonPreferences.cfg`:

```toml
[ThrowAssist]
allowKneeHit = true
KneeAimMaxAngle = 25.0
HeadAimMaxAngle = 25.0
```

For the debug session, check `[ThrowAssist]` in `UserData/MelonPreferences.cfg` and set `DebugLog = true` and
`SteerOtherItems = true` if the file already contains older values. Logs go to `MelonLoader/Latest.log`. Each release
reports raw speed, target/skip reason, game threshold, item speed, spin and chosen aim. The flight summary reports
why steering ended, last speed, and for knives the tip angle, observed body collision speed/part, and stab result.
Pistol hits report damage and health. A knife that hits a face but does not stab can then be distinguished from a
slow collision, bad blade alignment, or a missed hit zone. `DebugLog` may be turned off after testing.

Speed recap: pistols use the hand's release speed with a `PistolSteerMinSpeed` floor once assisted;
non-pistol props use `MinAssistSpeed`–`MaxSpeed`. Knives use at
least `item assist speed × KnifeSpeedMultiplier` while assisted. The game's own assist instead moves a knife at its
prefab's fixed speed while suppressing velocity and spin. The mod uses velocity steering and aligns an assisted
knife's stab line each step. `KnifeSpeedMultiplier = 1` is often visually identical to vanilla, especially for
the tested knife; it is a tuning control, not an automatic boost.
The earlier 0.2.0 test logged 14/14 assisted knife stabs, but failures that never entered the assist were initially
unlogged. This build records those releases too; face bounces still need an in-game test.

## Vanilla comparison and release timing

| Behavior | Game | Throw Assist default |
|---|---|---|
| Assisted-throw gate | Game option on and release speed at least the game's threshold (3.5 m/s in prior testing) | Keeps the game gate; pistols use `PistolAssistMinSpeed` (3.5 m/s), other props need 3.5 m/s; knives retain the game threshold |
| Pistol eligibility | Pistol prefabs ship with assist disabled | Enables it when `PistolAssist = true` |
| Target selection | Game selects by headset view and angle, usually its chest target | Keeps game selection; release direction chooses head or chest, plus optional knees with `allowKneeHit` |
| Search / flight distance | Item-specific overrides; pistol/crowbar values observed at 6 m / 4 m | 15 m / 20 m |
| Knife flight speed | Tested combat knife assist uses 17 m/s | At least prefab speed × 1; a faster release may use up to `MaxSpeed` |
| Knife orientation | Game's assisted coroutine controls rotation and removes spin | Assisted: blade first. No target: optional vertical tumble from the release spin |
| Pistol assisted flight | Game assist is disabled on pistol prefabs | At least 13 m/s after acquiring a target (0.2.2 release candidate) |
| Pistol impact | Weak game melee hit, generally no stagger | Configurable damage and stagger |

HurricaneVR computes the object's release velocity from recent hand motion when the grip opens. Throw Assist does
**not** sample the hand after release, so an early knife or pistol release does not gain speed from a later part of
the swing. If it finds a target, the knife receives at least its prefab assist speed and the pistol/prop speed is
clamped to the range above. **Daredevil's Billy Clubs have a separate 0.12-second follow-through feature** that
does measure the hand after release and can transfer more speed to the club. That feature does not apply to knives
or pistols. The 2026-09-30 log reported only three speed increases among 101 club throws (the older logger could
overstate them); the clubs' 13 m/s
minimum steering speed, 1.3x hand-throw boost, tip orientation and ricochet logic are other differences. Assisted
knives already fly at 17 m/s even when released slowly, so copying club follow-through alone would rarely change
their speed.

## Throw Assist and Daredevil boundary

Throw Assist deliberately skips objects named `BillyClub-*`. Daredevil handles their steering, speed, spin, damage,
ricochets, floor shots and follow-through with its own settings. Both let the game find a target. For normal direct
club throws, Daredevil reads only `ThrowAssist.AimHead` and `ThrowAssist.HeadAimMaxAngle` to choose head or chest;
club ricochets use `BillyClubs.RicochetAimHead`. Throw Assist's speed, search distance and flight distance settings
do not control clubs. Daredevil has `MinSteerSpeed`, `ThrowSpeed`, `ThrowSearchDistance`, `ThrowMaxFlyDistance` and
`FollowThroughTime` for those.
DirectThrowAssist and ThrowAssistMaxTurnAngle in `[BillyClubs]` control whether the club guides toward the
game-selected target at all. Ricochets can still seek another enemy after a wall hit unless set to 0.

The new knee setting applies to Throw Assist's own items. Daredevil does not read `allowKneeHit` or
`KneeAimMaxAngle`; its existing leg-hit reaction remains controlled by `KneelOnLegHits`.

## Build

```powershell
dotnet build ThrowAssist/ThrowAssist.csproj -c Release -o feature/ThrowAssist-knee-assist --no-restore
```
