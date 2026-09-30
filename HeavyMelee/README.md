# Heavy Melee

Stronger fists and held gun/bow hits, with capped damage and support for hitting and keeping downed enemies down.
Settings are in `[HeavyMelee]` in `UserData/MelonPreferences.cfg` or the in-VR Mod Settings menu.

## Leg-hit kneel

`KneelOnLegHits = true` by default. An accepted fist, held gun/bow, or hand-holding-gun/bow hit on
`LeftLeg`, `RightLeg`, `LeftUpLeg` or `RightUpLeg` triggers the same mirrored knee-shot animation used by
Billy Clubs. Knee Shot Stun is optional and can extend the kneel through its existing `GetHit` hook.
Set this option to false to restore Heavy Melee's previous reactions.

The hit must still pass the mod's speed, grip, cooldown and NPC eligibility checks. Weapon hits require
`WeaponHits`; hits during stun still respect `DownedHits`. Damage multipliers and caps are unchanged.
An enemy already kneeling or off balance does not start another kneel. Existing ground-hit and keep-down
behavior still applies. Feet are not knee targets. Blunt weapons such as Billy Clubs and knives retain their
own damage/reaction paths; this setting does not enable Throw Assist's experimental knee targeting.

## Verification

The Sep 30 leg-hit change builds against the installed game assemblies with no warnings or errors. It has
not yet been verified in VR. The assembly remains at 1.0.0 for local testing; release artifacts were not updated.

With `DebugLog = true`, accepted kneeling hits append `KNEELS` to the existing damage log. Check:

- Left/right leg punches and held pistol/bow strikes start the corresponding kneel animation.
- Knee Shot Stun extends the kneel and keeps the enemy stunned; disabling it gives the normal animation.
- Slow touches, unsqueezed-hand touches and repeated contacts during `HitCooldown` do not trigger a kneel.
- Disabling `KneelOnLegHits` or the master `Enabled` restores the corresponding previous behavior.
- Head/chest damage, fatal hits, and hits on already kneeling or downed enemies still behave correctly.
- `WeaponHits = false` and `DownedHits = false` continue to gate their respective hit paths.

Build with `dotnet build HeavyMelee/HeavyMelee.csproj -c Release -o feature/HeavyMelee-knee-assist --no-restore`.
