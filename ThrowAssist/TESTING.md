# Throw Assist debug session — 2026-09-30

Source: `MelonLoader/Logs/26-9-30_1-30-18.log` in the game folder, through about 01:39.
The game loaded Throw Assist 0.2.1 and the matching Daredevil build. These counts describe that
session snapshot, not a controlled accuracy test.

- `HeadAimMaxAngle` moved from 5 to 10, 15, 20, 25, then 30 degrees via Mod Settings. The saved
  value is 30. The player liked the stronger head assist and suggested 35 as a new-install default.
- 29 knife releases were logged. 23 assisted knives recorded a stab; all six `no stab` summaries
  lacked an observed enemy body collision and came from unassisted throws. The assisted knives
  consistently flew at their prefab's 17 m/s floor. This log does not show an assisted face bounce.
- 101 Billy Club throws generated 101 follow-through summaries, but only three claimed the club
  sped up after release. One reported a 50.5 m/s hand peak, a likely controller tracking jump.
  The old summary mixed raw and boost-adjusted hand speed, so its "early release" label was not
  reliable. The follow-through diagnostic now logs actual changes by default, compares raw hand
  speeds consistently, and ignores a one-step reading above 25 m/s.

The club's other throw behavior is materially different: direct assisted clubs use a 13 m/s
minimum steering speed and up to 18 m/s, unassisted hand throws use `HandThrowBoost = 1.3`,
and clubs have their own tip orientation, collision handling and ricochets. Assisted knives
already use a 17 m/s speed floor, so copying the club's post-release hand sampling alone is
unlikely to change most knife flights. Keep the systems separate until a log shows a knife
failure that post-release hand motion can address.

Optional leg targeting was implemented after these sessions; its first VR observations are recorded below.

## Final tuning session — 2026-09-30, 02:00–02:14

Source: `MelonLoader/Logs/26-9-30_2-0-47.log`. The in-VR Mod Settings log records
`BillyClubs.ThrowAssistMaxTurnAngle` moving from 35 through 30 to 25 degrees (a brief 24,
then back to 25), and `ThrowAssist.HeadAimMaxAngle` moving from 30 to 25 degrees. Both
saved preferences are 25. The player reported that this balance feels right and asked for
both defaults to be packaged.

The session logged 28 knife stabs and four `no stab` summaries. The club assist reported
turn-limit skips at 35, 30 and 25 degrees, confirming the free-throw path was exercised.
The new follow-through logger reported seven speed changes. No relevant throw exception
was found in the session log. These are observations from play, not controlled success rates.

## 0.2.2 optional knee assist — first VR session, 2026-09-30 02:30–02:35

Source: `MelonLoader/Logs/26-9-30_2-30-31.log`, game 0.3.1.0, Throw Assist 0.2.2 and Knee Shot Stun 1.0.0
(startup reports four extra kneel seconds). The player enabled `allowKneeHit` at 02:31:51. Both aim limits
remained 25 degrees. The player liked the reaction but reported difficult selection and occasional inaccurate
leg trajectories, and requested that this remain a private experiment with a false default.

- 02:31:57: Knee selected at 5.2 degrees, 3.1 m; `LeftLeg: KNEELS`, body contact on LeftLeg, no stab.
- 02:32:20: Knee selected at 16.7 degrees, 2.4 m; body contact on RightUpLeg, `stabbed LeftLeg`, no `KNEELS`.
- 02:34:32: Knee selected at 12.8 degrees, 2.1 m; `RightLeg: KNEELS`, body contact on RightUpLeg,
  `stabbed RightFoot`. These contact labels can refer to different callbacks within the same throw.

All three selected knee throws were inside the existing limit. Raising the limit cannot correct these already
selected trajectories. Current steering follows the center of a LeftLeg/RightLeg collider's world bounds;
it does not track an anatomical knee joint. Collider placement, enemy pose/movement and the knife's contact
geometry are plausible factors, not confirmed causes. A `stabbed` entry records the StabEnemyFinal prefix,
not independently verified damage. No Throw Assist exception was found in this session.

The sample confirms two logged kneel triggers, not a controlled hit rate or complete Knee Shot Stun validation.
Keep `allowKneeHit = false` as the code default and the user's saved opt-in unchanged (`true` after this session).
Keep the current 0.2.1 release package; the Nexus draft describes knee assist as private testing.

## Remaining VR checks

The project builds against the installed game assemblies. This does not verify collision timing or animation
behavior across the cases below.

- Default `allowKneeHit = false`: confirm the existing head/chest selection and pistol reactions.
- Enable `allowKneeHit`, with both aim limits at 25: throw toward head, chest, left leg and right leg. Check the
  logged angles and chosen region. Repeat near the 25-degree boundary, at close and long range, and while the
  enemy moves. A chest-directed throw must stay chest-directed even when both outer targets are within 25 degrees.
- Set `AimHead = false`: head selection stops while knee selection still works. Disable both for chest only.
- Land pistol, knife and eligible prop throws on each leg: confirm the mirrored knee-shot animation, normal
  damage, and one `KNEELS` log per enemy per throw, even when a knife reports both body contact and a stab.
- Repeat with `PistolDamage = false` and `PistolStagger = false`; the optional kneel should still work.
- Compare with Knee Shot Stun enabled and disabled: the extension should hold the same animation and keep the
  enemy stunned. A fatal hit should stay fatal; already kneeling, off-balance, paused and unhittable enemies
  should not start a new kneel. Held-item contacts should not trigger this feature.
- Turn off the game's assist and land an actual leg hit with a tracked free throw. Confirm that the reaction
  works without steering. Verify Billy Clubs still use their own settings.

## Katana throws failing — 2026-10-03 03:23–03:24 (fixed in 0.2.3, untested)

Source: `MelonLoader/Latest.log`, Throw Assist 0.2.2 at The Range, `DebugLog` switched on at 03:23:24.
Six assisted katana throws (`Knife-Katana`, `Knife-Katana-double`): the first four stabbed (spine, two
legs with kneel, head). The last three (03:24:12, :20, :26; 2.1–3.1 m) logged `no stab; no body collision
observed; 4 s timeout` with last speeds 30.0, 35.3 and 12.0 m/s and the tip 56/40/116 degrees off the
flight path. The player saw the blade stuck and thrashing inside the enemy, then flying off.

Cause from the code: steering aimed the centre of mass, so the 1 m blade's tip was ~0.5 m into the body
before the middle arrived; past the aim point steering reversed (not detected as an impact, since the
check compares against the steered direction) and `BladeFirst` turned the blade round inside the body.
Steering caps at 17 m/s, so 30+ m/s came from physics depenetration. 0.2.3 steers by the tip and stops
steering at 0.3 m or once past the aim point. Test: katana and double katana throws at chest, head and
knee; expect stabs, `steering: reached the aim point`, and `top` near 17 m/s.
