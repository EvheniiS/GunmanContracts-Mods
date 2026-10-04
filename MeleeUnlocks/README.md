# Melee Unlocks 0.2.1

Tested: 0.2.1 (2026-10-04): Katana2 default and `All` (all 10 knives on the wall) both unlock and save

Unlocks knives on The Range knife wall as if you had made the kills with them. **Default: the double katana**
(`Katana2`), the second katana for dual wielding. It has a wall spot, but no contract map places one, so it can't be
unlocked by playing (game 0.3.1.1).

Setting `[MeleeUnlocks] Unlock`, a selector on the Mod Settings board: `Katana2` (default), `All` (every knife on the
wall) or `Off`. A comma-separated list of knife IDs also works in the cfg: `Katana`, `Katana2`, `CombatKnife_v1`-`v4`,
`KitchenKnife_v1`-`v4`, `KitchenPen_v1`, `KitchenPen_v2` (combat v2 and v3 are free anyway). Changing it in The Range
refills the wall at once.

How: a knife normally unlocks when its kill counter (`killsNeededToUnlock`) reaches 0, and the game then calls
`ANBDataCollection.makePurchase(knifeID, 0)`, which adds the ID to your purchases and saves. The mod makes the same
call in a prefix on `ANBDataCollection.checkPurchaseDataWeapon`, which the wall check (and the loadout and contract
holsters) asks before removing a locked knife. After a setting change, spots whose knife was already removed are
refilled with the spot's own `initSlot()`.

Log, one line each: `unlocked 'Katana2' (Knife-Katana-double), saved to the game's purchases`; 15 s after The Range
loads `knife wall (Unlock = Katana2): N unlocked, locked: ...`; after a change `wall refilled: ...`.

**Unlocks are written to the game save and stay after the mod is removed.** The Steam `Knifecollector` stat is not
raised.

**Saving:** `makePurchase` does nothing while the game's `gameStarted` flag is false, which it is while The Range
loads. Then the mod adds the ID to the purchase list in memory (the knife shows) and saves it through `makePurchase`
once the game has started (`saved 'Katana2' to the game's purchases`).

History: 0.2.0 hit that gate: it logged `unlocked` but nothing changed. 0.1.0 patched `ANBGunwallSpot.checkKeep`, but the knife spots start the same check by name from `initSlot2`,
so it never ran (nothing unlocked, nothing logged). Its `Unlock` setting was not a selector on the board.

```powershell
dotnet build MeleeUnlocks/MeleeUnlocks.csproj -c Release
```
