# Gloves 0.1.0

Recolours the player's gloves. **Default dark red** (`#8A0F0F`, the Daredevil look); change it in
`UserData/MelonPreferences.cfg` → `[Gloves] Color`, or on the **Mod Settings** board (colour palette, applied at
once). `#414141` is the game's own grey.

Required by the Daredevil package. Moved out of Billy Clubs / Daredevil 0.2.0 on Sep 28 2026 (it was
`[BillyClubs] GloveColor` there; that old entry is no longer read).

How: every glove uses one URP Lit material, `fps_vr_glove` (grey leather atlas × `_BaseColor` #414141); the sleeves
are a separate material, so only the gloves change. Re-tinted on every scene load, 5 s later, and on every change of
the setting. Log: `gloves: N material(s) tinted #8A0F0F, M found`.

```powershell
dotnet build Gloves/Gloves.csproj -c Release -o feature/Gloves-0.1.0
```
