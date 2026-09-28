# Mod Settings 0.2.2

An in-VR settings board for every MelonLoader mod in Gunman Contracts. It reads all categories from
`MelonPreferences`, so every mod that uses MelonPreferences (ours and others) gets a page without doing anything.

## Use

- **Open / close:** take out the phone and press the **Mod Settings** tile in the middle row (an empty slot), or press
  **Ctrl+M**. The board opens 40 cm in front of your eyes, above the phone, and moves with you when you move with the
  stick. It closes on X, on the tile again, on a
  scene load, or if you walk 1.6 m away.
- **Press buttons with either index fingertip**, coming in from the front (sliding across the board doesn't press).
- `<` / `>` at the top switch mods; `Up` / `Down` scroll a mod's settings; poke a setting's **name** to read its
  description, default and restart note; `Reset` puts the selected setting back to its default.
- Numbers get `-big -small value +small +big` steps (from the default: 18 → 1/10, 0.6 → 0.01/0.1). Switches toggle.
  Choices written in the description (`TipFirst (...), Natural (...) or SpinEnd (...)`) and `#RRGGBB` colours cycle
  with `<` `>`. Other text (keys, paths, holster positions) is read-only here; edit it in the cfg.
- Yellow name = not at its default. `*` = the description says it needs a game restart.

Every change is logged (`Category.Entry: old -> new`), applied to the entry at once (it fires the entry's
`OnEntryValueChanged`) and saved to `UserData/MelonPreferences.cfg` a second later.

**Live or not is up to each mod:** it applies at once if the mod reads the value when it uses it. Audit (Sep 28 2026)
of our mods: all live except Knee Shot Stun `ExtraKneelSeconds` (read at startup) and Billy Clubs values used when the
clubs/holsters are built (`Mass`, `ThrowSearchDistance`, `Length`, `Radius`, `BodyColor`, holster positions,
`GloveColor`), which apply on the next level load.

## Settings (`[ModSettings]`)

`PhoneTile` (true), `OpenKey` (`M`, with Ctrl), `PanelDistance` (0.4 m), `PanelScale` (1), `DebugLog`.
With `DebugLog`, the phone's home-screen layout is written once to `UserData/ModSettings_phone.txt`.

0.2.0 replaced the 0.1.0 right-pocket grip gesture (too hard to find: grips landed 19-27 cm from it) with the phone
tile. The old `Pocket*` keys in the cfg are unused.

## The phone tile

A copy of the Game Options tile, relabelled "Mod Settings", in the centre slot (`App_5` of the `Apps` 3x3 grid). That
slot also holds the mission Data Breach holders; while one is switched on, the tile moves to the empty left slot
(`App_4`) and returns afterwards. Placed by the grid's structure, not by
position: the home screen's local x axis is mirrored. It is made under an
inactive holder (nothing wakes before it's cleaned), its `ANBLanguageText` is removed so the label sticks, and its
`onButtonPush` events are replaced with empty ones, so the game's own action (the pause menu) never runs. A postfix on
`ANBInterfaceButton.buttonPush` opens the board. It joins `ANBSmartphone.allButtons`, which `toggleInput` shows and
hides with the phone.

## Build

```powershell
dotnet build ModSettings/ModSettings.csproj -c Release -o feature/ModSettings-0.1.0
dotnet run --project ModSettings/Tests/Tests.csproj -- "<game>/UserData/MelonPreferences.cfg"
```

The test prints how every string setting in the cfg would be shown (choice / colour / read-only) and checks the number
stepping.

## How it works

Quads (URP Unlit) + TextMeshPro 3D text under one `DontDestroyOnLoad` root, no colliders. Each frame the index
fingertip bones (`LndexNub` / `RndexNub` in the glove rig; palm as a fallback) are transformed into panel space and
tested against the button rectangles. Hands come from a Harmony postfix on `HVRHandGrabber.Start`.
