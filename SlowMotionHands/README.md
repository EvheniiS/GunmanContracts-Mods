# Slow Motion Hands

Your hands keep up with your controllers during slow motion. Status: **0.1.3 tested Oct 2 2026: "feel is much better" in slow motion.** Log covered empty hands and pistol only (2 slow motions); rifle and club with 0.1.3 not yet logged. See "What we tried" at the bottom.

## Why the hands lag in slow motion

The game's slow motion only sets `Time.timeScale` (`ANBGameLogic.toggleSlowMotion`). Two things then go wrong:

1. **Physics gets slow in real time.** The game's adaptive physics step (`ANBGameLogic.FixedTimeTest`) only shrinks a little: the logs show 7.3 ms of game time at x0.22, about 33 ms real, so physics (and the hand body, which has no interpolation) updates near 30 Hz real instead of 90.
2. **The hand's pull toward the controller works in game time.** Spring 9000 / damper 900 / torque 500/50 on a 20 kg body: your hand moves in real time, the world does not, so at time scale `s` the hand catches up `1/s` times slower. Daredevil's hand probe measured 88-96 ms at x0.22 against 16-24 ms normally.

## What the mod does

- **`FullRatePhysics`**: after the game picks its step, sets `fixedDeltaTime = real frame time x timeScale`, so physics runs once per rendered frame like in normal play.
- **`HandResponse`**: the hand joint's drives are scaled, right after every `HVRHandStrengthHandler.UpdateStrength` (both overloads) and on every physics step: spring and force by `(1/s)^(2 x HandResponse)`, damper by `(1/s)^HandResponse`, capped by `MaxStrengthBoost`. At x0.22 that is spring x20.7, damper x4.5. Slow motion starting or ending makes every hand rewrite its drives.
- **`GripResponse`**: the joint that holds the weapon to the hand (`HVRHandGrabber.Joint`) is scaled the same way.
- Both joints use one tracker per drive: the game's latest value (base) and what the mod wrote (base x boost). A drive that is not what the mod wrote last is a new game value. It composes with Daredevil's `HandStrengthScale` / `HandTorqueScale` (multiplied).

## Settings (`[SlowMotionHands]`, also on the Mod Settings board, all live)

| Setting | Default | |
|---|---|---|
| `Enabled` | true | off = the game's own slow motion feel |
| `HandResponse` | 1 | 0 to 1. 1 = hands follow as fast as at normal speed, 0 = game value. Lower it if hands jitter or shove things in slow motion |
| `GripResponse` | 1 | same as HandResponse for the hand-to-weapon joint |
| `FullRatePhysics` | true | normal-rate physics during slow motion (CPU cost of normal play) |
| `RaiseSpinLimit` | true | raises the hand's and held item's spin speed limit (`maxAngularVelocity`, game-time) by 1/timeScale |
| `SlowMotionSnap` | 2 | 1 to 4: hand joint stiffer than normal during slow motion, on top of the time-scale boost. 2 = ~30% less lag than normal play, 4 = ~half |
| `MaxStrengthBoost` | 30 | cap on the spring/force multiplier |
| `DebugLog` | true | one line at the start and a summary at the end of each slow motion; turn off once it feels right |

## Testing

1. Play with it on. Slow motion (right B) and a Physical Dodge slow motion both count. Swing and move your hands fast during it.
2. Read the MelonLoader log, one line per slow motion:
   `slow motion over after 3.1 s (x0.22); physics step game 7.3 ms (33 real) -> ours 2.4 ms (11 real); hand gap while moving >1 m/s: RightHand slow mean 4 / max 14 cm (120) vs normal mean 3 / max 12 cm (400); ...`
   The goal is "slow" close to "normal". To see the vanilla numbers, set `Enabled = false` for one session and compare.
3. Too aggressive (hands jitter, shove enemies or props): lower `HandResponse` (0.7), or `FullRatePhysics = false` to split the effect.
4. `hands: ... (follows nothing)` in the log means the measuring target was not found; the fix still applies, only the gap numbers are missing.

Unverified: whether 20x drives at the lower game-time step stay stable against walls and props; whether the joint's maximum force (not only spring/damper) was what limited fast swings.

## First test result (Oct 2 2026, log 26-10-2_15-15-8)

- Physics step 7.3 -> 2.4 ms game time in every slow motion: works.
- **Empty hand fixed:** Daredevil's probe at x0.22, controller 16 m/s: delay 0 ms, lag 2 cm (was ~90 ms).
- **Holding a weapon not fixed:** pistol hand delay 32 ms / 14 cm at only 3.9 m/s; bow hand gap 16 cm in slow motion. The weapon-to-hand joint was untouched, and the logs had no gun-side numbers. 0.1.0 now also scales that joint, logs the hold's joint values at the start of each slow motion (`holds 'item' ...`) and a `held item slow ... vs normal ...` lag meter in the summary.

## What we tried (Oct 2 2026)

| Build | Change | Result |
|---|---|---|
| 0.1.0 | Physics step = real frame x timeScale; hand joint scaled by a pre/postfix on `UpdateStrength` that scaled only drives whose value changed during the call | Step works (7.3 -> 2.4 ms). Empty hands sometimes OK. **Holding a weapon: 32-48 ms delay, 12-15 cm lag** (15:15 log) |
| 0.1.0 + grip | Also scaled the hand-to-weapon joint, added hold log + held-item meter | **No change** (16:29 log): hand gap slow 12-15 cm vs normal 1-4 cm; held item = hand gap |
| 0.1.1 | Hand joint moved to the base/written tracker, polled every physics step | Boost lands (x20.7 in the log). **Pistol fixed** (slow 3.2 cm vs normal 6.7), **club fixed** (1.4-2.1 vs 0.5-4.6). **Rifle still lags:** 8.3 / max 14 cm vs 1.6 (16:37 log) |
| 0.1.2 | Spin speed limit (`maxAngularVelocity`) of the hand and the held item raised by 1/timeScale | **Slow = normal** (16:54 log): pistol 1.7-1.9 cm vs 0.9-3.4, club 5.7 / max 8 vs 6.3 / max 11, probe delay 12-24 ms like normal swings. User: "still a bit there" = the normal-speed physics-hand lag, more visible when the world is slow |
| **0.1.3** | `SlowMotionSnap` (default 2): hand spring/force x2 more in slow motion only (damper x1.41, stays critically damped) | **Feels much better** (17:06 log): empty hand slow 1.2 / max 2 cm vs normal 3.3; pistol 1.0 / max 5 vs 0.6 / max 4; probe 16 ms at 3.1 m/s. No errors, no jitter reported |

**Why 0.1.0 never boosted the hand while holding:** when slow motion started, the joint already held the game's value, and `UpdateStrength` wrote that same value again, so the "changed during the call" check skipped it. The 16:29 hold log proves it: at x0.22 (boost x20.7) the hand joint read `9000/900/9000` (pistol) and `2000/200/1000` (rifle), the plain game values. Boost only landed when a grab or release changed the strength mid-slow-motion, which is why empty hands sometimes looked fixed.

**The grip joint was never the problem:** its position drive is `0/0/0` (the joint is locked) and slerp 100000/1000. The held item's lag equals the hand's lag, so the item just rides the hand.

**What 0.1.1 should show in the log:** the `holds '...'` line at slow-motion start reads the hand joint x20.7 (pistol `186300/4050/186300`, rifle max force `20700`), and the summary's `slow` gap close to `normal`. If the joint is boosted and the hand still lags, the next suspect is the rifle's max force or the physics step, not the drive values.

**0.1.2, why the spin limit:** `maxAngularVelocity` is a game-time cap. `HVRJointHand.Awake` sets the hand's to 150 rad/s, which is 33 rad/s of real turning at x0.22; the logged flicks peak at 20-38 rad/s. Items can carry a lower one (`HVRRigidBodyOverrides`, which can rewrite it every physics step, so it uses the same base/written tracker). The `holds` line now prints `spin limit base -> now` for hand and item.

**Rifle note:** the rifle's own hand strength is weak by design (`2000/200/1000` against the default `9000/900/9000`), and it lags one-handed at normal speed too (Daredevil probe: 16-60 ms, 14-21 cm). The slow-motion target is to match that, not the pistol. If the rifle still feels worse than at normal speed with 0.1.2, compare it two-handed in both.
