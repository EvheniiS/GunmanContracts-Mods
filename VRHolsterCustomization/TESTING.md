# VR Holster Customization: back-slot save and restore test

Not yet played as a set (Oct 3 2026). Turn `DebugLog` on and read the log lines quoted below. The code is `BackSlots.cs`
(`Save`, `Restore`, `RestoreCheckpoint`) and `BackBlades.cs` (`Make`).

## How it is meant to work

- Each back side holds ONE weapon: a game gun (the game's own socket, saved by the game in its loadout) or a mod item (ours,
  `Knife-<id>` / framework items, saved in `SavedBackHolsters` as `L=kind;R=kind`). A mod item never enters the game's socket.
- **Saved only in The Range** (`Save` does nothing in a contract or the main menu), whenever a back slot changes.
- **Scene load:** about 3 s after the game has put its own loadout back, each saved side is filled from `kind.Spawn`: in The Range
  the knife from its wall spot, in a contract a copy of the prefab. A side where the game put a gun is **lost** (and dropped from the save);
  a kind whose mod isn't loaded, or can't be made yet (clubs and crowbar before a Range visit), is **kept** and waits.
- **Checkpoint retry** (`resetPlayerLoadout`, contract only): the saved sides come back 1 s later, reusing the drawn item if it still exists.

## Runs

Each run: restart the game first. Log line to look for in brackets.

1. **Range to contract.** Katana on the left back, bow on the right (game gun). Leave by the elevator. Both present in the contract.
   [`back holsters restored: back-left Knife-…`]
2. **Quit and start fresh.** Same loadout, close the game, start it, go to a contract without visiting The Range. The katana appears from
   its prefab; a club or crowbar waits instead. [`… waits (its mod can't make one before a visit to The Range)`]; visit The Range, it comes back.
3. **Game gun beats the mod item.** Katana saved on the left. At the gun wall put a pistol on the left back, then leave. The katana is lost
   and `SavedBackHolsters` no longer lists it. [`'Knife-…' lost (the game put … there)`]
4. **Refused while occupied.** Katana on the left: a game gun let go at that shoulder does not holster (the gun stays in the hand).
   A game gun on the left: the katana let go there is refused. [`not holstered - the game's … is in it`]
5. **Changing the save.** In The Range draw the katana and put it in the belt knife holster. Leave. The left side is empty in the contract
   and the cfg has no `L=` entry.
6. **Contract does not overwrite.** In a contract draw the katana and drop it, die, retry. It is back on the left (`recalled` or `spawned`),
   exactly one katana, and the Range loadout is unchanged on the next visit. [`checkpoint back holsters: back-left … recalled`]
7. **Knife wall.** Katana on the back, then walk to the wall: its spot is empty (no duplicate). Draw it and hang it back on the wall: the next
   restore must not put a second one on your back.
8. **Two katanas / mixed.** Katana left, katana (other type) right, game pistol on the belt: both restore to their own sides.
9. **Mod not loaded.** Remove Weapon Framework or Daredevil, load, leave and come back with it installed: the club on the back is still saved.
   [`'…' skipped (its mod isn't loaded)`]
10. **Auto-return interplay.** Katana drawn from the left, thrown, left lying: it goes back to the left after `OnGroundSeconds`, never the belt,
    and the save still says `L=`.

## Checks around the return timer (swapping)

- Draw the katana (the left side is free), put a game gun in that side at once, drop the katana: it must not end up in an occupied side.
- Draw the katana, drop it, and try to holster another weapon in the empty slot before it returns (1 s, then 5 s): note whether the katana
  gets there first. This is the confusing case; see the question about the return rules.
- Holster sound (0.3.8): a katana or club in and out of the back is audible and the bow still clicks as before. [`holster sound '…' (…)`]
