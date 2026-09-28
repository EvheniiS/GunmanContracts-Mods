# Enemy Awareness baseline: sessions WITH Enemy Awareness Fix (Sep 26 2026)

All on `Contract_01_Outpost_001` wave challenges (bow mostly, some pistol). The range, menus and loader are left out.
Made with `awareness_stats.py` in this folder. To measure another session the same way:

```
python awareness_stats.py --game "E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone" session-<time>.log
```

**Compare like with like:** the no-fix run `session-20260927-171428.log` used Log 0.5.1, so the first table
(Fix 0.1.6 + Log 0.5.x) is the fair baseline. Log 0.5.x no longer writes a line for unclear chases, but still counts
them in the wave summaries, which is what the script reads. Per-minute rates are per minute of Outpost time.

Things to read with care:
- **live-tracked : searched** is the headline metric (vanilla first log, Sep 25: 27 : 0). Most chases are "unclear"
  (you moved < 3 m from where the enemy last saw you), so compare the share of *conclusive* chases.
- **Fix lines exist only with the Fix loaded**, so they have no no-fix counterpart.
- STUCK? in 0.5.0 still counted enemies > 50 m away after a death (10 in `041812`); 0.5.1 skips them.
- Deaths and restarts cut waves short; compare per minute, not per session.

## Result: no Fix vs Fix 0.1.6 (Sep 27 2026)

No Fix = `session-20260927-171428.log` (Log 0.5.1, Dodge 0.4.2, 5.7 min). Fix = the two sessions in the next table.

| Metric | No Fix | Fix 0.1.6 |
|---|---|---|
| **live-tracked : searched : unclear** | **26 : 0 : 29 (100% live)** | **2 : 18 : 34 (10% live)** |
| avg spawn distance | 9.2 m | 12.6 m |
| unseen chases / min | 9.6 | 6.7 |
| lost-target timer ran out / min | 0.70 | 0.12 |
| STUCK? / min | 0.18 | 0.48 (042542 only; 041812 has the 0.5.0 counting bug) |
| hits that hurt you / min (Dodge) | 0.70 | 3.0 |
| enemy gunshots / min, harmless share | 9.4, 80% | 11.6, 47% |
| your hits started the harmless window / min | 8.9 | ~6.9 (042542) |
| first sightings / spawn, distance, delay | 0.95, 5.8 m, 9 s | 0.93, 5.2 m, 8 s |
| door kicks / min | 10.8 | 11.2 |
| weapons | bow 85 + 25 gunshots | bow only |

- **The Fix works as designed:** vanilla live-tracks every conclusive chase (matches Sep 25: 27 : 0); with the Fix
  almost all become searches. Spawns are ~3.4 m further out, which is the randomised spawn points.
- **The harmless-share gap is not awareness.** "Harmless" = the game's cooldowned shots (warning shot, first second
  of fire, grace after you hit someone). The no-Fix run hit enemies more often (8.9 vs ~6.9 graces/min, 7.5 vs 6.4
  kills/min), so more of their fire fell in grace windows. Damage taken tracks that, so don't read "vanilla hurts
  less" from one 5.7-minute run.
- Confounds: one short session; pistol used in waves 4-5. (HEARD 8 vs 3 is NOT the pistol: only 3 of the 8
  followed a gunshot, and the bow-only run below had 7. Most HEARD lines are enemies reacting to a scream.)

**Bow-only no-Fix run, `session-20260927-174137.log` (4.0 min, 3 waves, died in wave 3):**
- live-tracked : searched : unclear **13 : 0 : 19** (no-Fix total today 39 : 0).
- Harmless 55% (waves 1-2; wave 3 has no summary), not 80%: the 80% run was the outlier, caused by fast kills.
- Real shots 22 = 5.5/min, hurt 13 = 3.25/min, dodged 6 of 22 (27%). Close to the Fix runs (6.1/min, 3.0/min).
- **The death shows the vanilla bug:** E42 and E43 spawned, never saw you, and walked 17 m / 11 m straight to
  0.1 m from you (live-tracked 13/13 and 6/6). They came round the corner at 1.0-1.6 m and landed 7 hits in 8 s;
  you moved <= 0.10 m on each, so there was no time to dodge.
- awareness_stats.py now counts dodges/hits/hurts from the per-shot lines, so a wave cut short still counts.

**Physical Dodge (0.4.2 no Fix; 0.4.2 + 0.4.1 with Fix):**

| | No Fix (5.7 min) | Fix (8.0 min) |
|---|---|---|
| shots at you | 54 | 92 |
| free misses (game-harmless) | 43 (80%) | 43 (47%) |
| real shots / min | 1.9 | 6.1 |
| real shots dodged | 7 of 11 (64%) | 18 of 49 (37%), + 3 spread misses |
| hurt / min | 0.70 | 3.0 |
| real shots from enemies out of your view | 0 | 9 (5 hurt) |

The difference is how many real shots got through the game's free misses, not the dodging: 11 real shots is too
few to rate the dodge. All 11 no-Fix real shots came from 1.2-5.7 m, from enemies in view.

## Fix 0.1.6 + Log 0.5.x (same logger as the no-fix run)

Sessions: `session-20260926-042542.log`, `session-20260926-041812.log`

| Metric | Value |
|---|---|
| combat minutes (Outpost) | 8.0 |
| waves started | 5 |
| spawns / min | 5.1  (avg spawn distance 12.6 m) |
| first sightings / spawn | 0.93  (avg 5.2 m away, avg 8 s after spawn) |
| unseen chases / min | 6.7 |
| live-tracked : searched : unclear | 2 : 18 : 34  -> live-tracked 10% of conclusive |
| lost-target timer ran out / min | 0.12 |
| STUCK? / min | 1.50 |
| door kicks / min | 11.2 |
| enemy gunshots / min | 11.6  (47% harmless) |
| your gunshots / bow shots | 0 / 82 |
| visibility avg | 0.77 |
| Fix: shared sightings / min (avg age) | 10.9  (0.6 s) |
| Fix: reached / stopped short / search over / gave up | 21 / 14 / 8 / 8 |
| Physical Dodge: hits that hurt you / min | 3.0 |

## Fix 0.1.4 + Log 0.4.0 (older logger)

Sessions: `session-20260926-034457.log`, `session-20260926-023017.log`

| Metric | Value |
|---|---|
| combat minutes (Outpost) | 13.4 |
| waves started | 9 |
| spawns / min | 5.1  (avg spawn distance 12.3 m) |
| first sightings / spawn | 0.87  (avg 6.3 m away, avg 14 s after spawn) |
| unseen chases / min | 7.7 |
| live-tracked : searched : unclear | 1 : 41 : 62  -> live-tracked 2% of conclusive |
| lost-target timer ran out / min | 1.19 |
| STUCK? / min | 0.74 |
| door kicks / min | 11.8 |
| enemy gunshots / min | 8.2  (56% harmless) |
| your gunshots / bow shots | 35 / 125 |
| visibility avg | 0.77 |
| Fix: shared sightings / min (avg age) | 11.1  (3.3 s) |
| Fix: reached / stopped short / search over / gave up | 32 / 3 / 12 / 10 |
| Physical Dodge: hits that hurt you / min | not logged |

## Per session

### session-20260926-042542.log
- versions: {'Enemy Awareness Fix': '0.1.6', 'Enemy Awareness Log': '0.5.1', 'Physical Dodge': '0.4.2'}; MelonLoader log: 26-9-26_4-25-35.log
- combat scenes: Contract_01_Outpost_001; 4.2 min, 4 waves started
- spawns 28 (avg 12.6 m from you), enemies died 27
- first sightings 26: avg 5.1 m away, avg 8 s after spawn
- unseen chases 39: live-tracked 1, searched 10, unclear 28 -> live-tracked share of conclusive 9%
- lost-target timer ran out 1, STUCK? 2, HEARD (someone reacted) 2
- door kicks 67 (15.9/min); your gunshots 0, bow 61
- enemy gunshots 53 (12.6/min), harmless 23 (43%); visibility avg 0.82
- Fix: told 62 (avg age 0.7 s), reached 15, stopped short 4, couldn't reach 0, search over 0, gave up 0, guesses 9
- Physical Dodge: dodged 11, hit 16 (hurt 14), free miss 22

### session-20260926-041812.log
- versions: {'Enemy Awareness Fix': '0.1.6', 'Enemy Awareness Log': '0.5.0', 'Physical Dodge': '0.4.1'}; MelonLoader log: 26-9-26_4-18-6.log
- combat scenes: Contract_01_Outpost_001; 3.8 min, 1 waves started
- spawns 13 (avg 12.7 m from you), enemies died 9
- first sightings 12: avg 5.4 m away, avg 8 s after spawn
- unseen chases 15: live-tracked 1, searched 8, unclear 6 -> live-tracked share of conclusive 11%
- lost-target timer ran out 0, STUCK? 10, HEARD (someone reacted) 1
- door kicks 23 (6.0/min); your gunshots 0, bow 21
- enemy gunshots 40 (10.5/min), harmless 21 (52%); visibility avg 0.56
- Fix: told 25 (avg age 0.5 s), reached 6, stopped short 10, couldn't reach 0, search over 8, gave up 8, guesses 12
- Physical Dodge: dodged 7, hit 12 (hurt 10), free miss 21

### session-20260926-034457.log
- versions: {'Enemy Awareness Fix': '0.1.4', 'Enemy Awareness Log': '0.4.0', 'Physical Dodge': '0.3.0'}; MelonLoader log: 26-9-26_3-44-41.log
- combat scenes: Contract_01_Outpost_001; 9.0 min, 7 waves started
- spawns 50 (avg 12.4 m from you), enemies died 52
- first sightings 44: avg 6.4 m away, avg 13 s after spawn
- unseen chases 79: live-tracked 1, searched 29, unclear 49 -> live-tracked share of conclusive 3%
- lost-target timer ran out 11, STUCK? 8, HEARD (someone reacted) 6
- door kicks 115 (12.8/min); your gunshots 27, bow 84
- enemy gunshots 80 (8.9/min), harmless 45 (56%); visibility avg 0.76
- Fix: told 115 (avg age 3.4 s), reached 19, stopped short 3, couldn't reach 0, search over 9, gave up 7, guesses 9

### session-20260926-023017.log
- versions: {'Enemy Awareness Fix': '0.1.4', 'Enemy Awareness Log': '0.4.0', 'Physical Dodge': '0.1.0'}; MelonLoader log: 26-9-26_2-30-11.log
- combat scenes: Contract_01_Outpost_001; 4.4 min, 2 waves started
- spawns 18 (avg 12.0 m from you), enemies died 14
- first sightings 15: avg 6.0 m away, avg 15 s after spawn
- unseen chases 25: live-tracked 0, searched 12, unclear 13 -> live-tracked share of conclusive 0%
- lost-target timer ran out 5, STUCK? 2, HEARD (someone reacted) 1
- door kicks 43 (9.7/min); your gunshots 8, bow 41
- enemy gunshots 30 (6.8/min), harmless 17 (57%); visibility avg 0.77
- Fix: told 34 (avg age 3.0 s), reached 13, stopped short 0, couldn't reach 0, search over 3, gave up 3, guesses 5
