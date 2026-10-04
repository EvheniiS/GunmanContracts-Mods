# "Mods updated" popup (spec, Mod Settings 1.3.0)

Status: **built and tested Oct 4 2026 (flat pass T0-T7, VR UI check).** Code: `UpdatePopup.cs` (content, pure, unit-tested),
`DefaultChanges.OpenPopup` / `DecideAll`, `Pages.PopupChoice`, drawn by `Panel.RefreshPopup` (VR) and `FlatMenu.DrawPopup`.
Test plan: skill `defaults-test`, fixture `Tools/DefaultsTest`.

## Why

The "Updated defaults" page (1.2.x) worked, but a player who opened the board landed on a page of blue/yellow rows that
looked like every other section, with the explanation at the bottom. The popup says what happened in two lines and takes
the answer in one press; the page stays for people who want details.

## When

- On the first board open of a game session (VR board or flat Ctrl+M menu) if anything happened: a customised setting
  waits for a decision, an untouched setting moved to its new default, or a "settings were reset" notice is pending.
- Once per session. Any answer, or closing the board while it is up, closes it for the session.
- Next game start: shown again only if a customised setting is still undecided (Later, or Review without deciding).
  Settings that moved by themselves and shown notices are not repeated.

## Look

- VR: a 46 × 28 cm card (board: 72 × 56) with a blue frame and a blue header strip, shown alone; the board appears after
  the answer. Not movable; the grip handle is off while it is up; laser and poke hit only the card.
- Flat: a 620 × 340 window (board: 1040 × 720) with the same frame, instead of the menu.
- First button (the main answer) blue, Later grey.

## Content

| State | Title | Text | Buttons |
|---|---|---|---|
| customised and untouched settings changed | MODS UPDATED | "Updated: <mod a -> b, up to 3>"; "N settings have new default values: X you never changed: moved to the new default. Y you changed: your value was kept. Use the new defaults for those Y too?" | Use new, Keep mine, Review, Later |
| only customised | MODS UPDATED | "Y settings you changed have new default values. Your values were kept. Use the new defaults instead?" | same |
| only untouched | MODS UPDATED | "X settings you never changed now use their new defaults. Review lists them; Undo there gives the old value back." | OK, Review |
| reset notice only | SETTINGS RESET | "Settings were reset: <mods>. Everything you had changed there is back at its default ..." | OK |

A reset notice is listed above the update text when both happen. Singular wording for 1.

**Mods are always named** (from the board sections of the changed settings, so also on later starts when the version
line is gone): one section → title "<SECTION> UPDATED" and "... in <Section> ..."; several → title "MODS UPDATED", a line
"Mods: BillyClubs (3), Gloves (1)" (up to 4, then "and N more") and "... in 2 mods ...". **Several mods = one popup**,
never a chain; Use new / Keep mine answer all of them, Review opens one page with all their rows.

## Buttons

| Button | Effect | Board afterwards |
|---|---|---|
| Use new | every waiting customised setting takes the new default; record updated | first mod section (not the changes page) |
| Keep mine | every waiting customised setting keeps its value; record updated, not asked again | same |
| Review | nothing decided; the Updated defaults page (customised rows first, then the moved ones) | that page |
| Later / closing the board | nothing decided; page stays first in List this session | first mod section |
| OK | nothing to decide | first mod section |

## Log (one line each)

`update popup: shown (2 auto, 2 ask, 0 notices)`, then `update popup: use new (2: Cat.Key, Cat.Key)` /
`keep mine (...)` / `review` / `later` / `ok`. `Test-Defaults.ps1 Check` verifies the answer against cfg and record.

## Out of scope

Mod-author "important vs minor" flags; a badge on the phone tile; a per-mod breakdown in the popup.
