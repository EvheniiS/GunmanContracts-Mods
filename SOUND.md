# Sound: what we know (for custom sounds later)

Everything found about how Gunman Contracts plays sounds, gathered in one place so custom sounds can be added later
without re-reading the game code. Sources: `GameAssembly.dll` via `il2cpp_tools/` (`dumpt.py`, `disa.py`, `xref.py`),
and what our mods already do. "Checked" = read in the game code or tested in play; everything else says so.

## 1. How a sound gets played

- **Two entry points, same signature:** `ANBGameLogic.PlayAudioClip(...)` and `ANBSFXPlayerManager.PlayAudioClip(...)`
  take `(AudioClip clip, Vector3 pos, bool useDistanceCheck, string type, bool loud, float pitch, float volume)`.
  Variants: `PlayAudioClipLoud`, `...LoudNopitch`, `...Nopitch` (same arguments minus pitch/volume) and
  `PlayAudioClipQuick` / `...QuickLoud` (clip only). Checked.
- **Under them is HurricaneVR's `SFXPlayer.PlaySFX`:** `pitch -1` = `TimePitch` (so slow motion lowers the pitch),
  `volume -1` = the player's `SoundVolumeSFX` setting. It uses a **small round-robin pool of AudioSources**: each new
  sound stops the oldest one, and a clip still in its cooldown (`useCooldownSFX`) is skipped. So in a busy fight,
  gunfire and impacts cut off quieter sounds (found while building Radar Sense's footsteps). Checked.
- **The game's own call style** (melee hits): `PlayAudioClip(clip, bodyPart.transform.position, false, "default",
  false, -1, -1)`. Use exactly this for any new sound and it behaves like the game's own: volume setting, slow-motion
  pitch, 3D position.
- **Own AudioSource instead of the pool** (Radar Sense `LoudSteps`, `Daredevil/RadarSense/Steps.cs`): copy the game's
  SFX reference source (same mixer group and spatializer, so the volume settings still apply) onto a source of your
  own. Needed when a sound must not be cut off by the pool, or needs its own `minDistance` / falloff.

## 2. Where the game's clips live

| Where | What | Notes |
|---|---|---|
| `ANBGameLogic.BluntHit` (0x1000) | AudioClip[] | Blunt melee hit on an enemy; random pick. Checked. |
| `ANBGameLogic.BluntHitHead` (0x1008) | AudioClip[] | Head hit. Death Details plays it on its finishing head hit (tested, sounds right). |
| `ANBGameLogic.FleshKnifeHit` / `FleshKnifeSlashHit` (0xff0 / 0xff8) | AudioClip[] | Knife stab / slash into flesh (call sites not read yet). |
| `ANBGameLogic.WoodHit` (0x1010) | AudioClip[] | Call sites not read yet. |
| `ANBSFXPlayerManager.Clips_*` | AudioClip[] | Player-side sounds: ammo/credit/spray-kit pickups, tutorial hint, slow motion start/end/more/none, damage taken, last health point, heartbeat, recharge, and voice lines (`Clips_Voice_Gunman*`, `Clips_Voice_Handler*`). Each has a method that plays it (`CollectAmmoPistol`, `StartSlowmotion`, `TakeDamage`, `Heartbeat`, ...). |
| `ANBSoundPhysicsItem` | on physics props (and the crowbar, so on the clubs) | `OnCollisionEnter` picks a clip by `PhysicsType Material` and impact-strength tier, or from `customSounds` with `overrideSoundsSoft/Medium/Hard`. `useLayerMaskOverride` + `layerMaskOverride` limit what makes a sound; `noiseOnSoft/Medium/Hard` + `canAlert` decide whether it alerts enemies. From field names; behaviour not tested. |
| `ANBPhysicsDoor.SFXOpenKick` | AudioClip | Door kick. Checked (from the sound-awareness work). |
| `ANBFootsteps` | per surface | `ANBplayStepSound` picks by `walkOn` (1 Metal, 2 Wood, else Generic). Enemy steps play only within `distanceForHearing`. Checked. |
| `ANBBasicNPC` voice | methods | `PlaySoundDeath(alert)`, `PlaySoundHurt`, `PlaySoundInjured(alert)`, `PlaySoundTwitcher`, `PlaySoundSilence`, `PlaySoundDeadAlly`, `PlaySoundDeadBody`, `PlayVoiceSingle(clip, boostedVolume, preventRepeat, usePause)`, `PlaySounds(clip, boostedVolume, storySub)`, `EndVoice` / `EndAll` (coroutines that stop the voice), `BoostVolume(clip, boost)`, `overridePlayerSeenScream`. Where the clips come from: not read yet. |

## 3. When the game plays them (and when it doesn't)

- **Melee hit sounds play only on LIVING enemies.** `TakeBluntWeaponDamage` (clubs, bats, crowbar) and `TakeMeleeDamage`
  (fists, Heavy Melee's gun/bow hits) play a random `BluntHit`. Every melee contact reaches
  `ANBBodyMeleeCollisionManager.collisionEnter`, also on dead bodies, but the game's handler stops for dead ones, so
  **a hit on a corpse or a writhing enemy is silent**. Death Details now plays `BluntHitHead` on the head hit that
  finishes a writhing enemy. Checked.
- **Death:** `KillNPCExec` plays `PlaySoundDeath` (death cry), or `PlaySoundSilence` on a headshot. Checked.
- **Writhing ("twitcher") enemies:** `startTwitcher` → `PlaySoundTwitcher` (pain sounds while writhing); `killTwitcher`
  → `EndVoice` + `EndAll` (stops them). A hit on a writhing body: leg → `PlaySoundHurt`; torso/head → `PlaySoundDeath`
  (or `PlaySoundSilence` for the head). Checked.
- **Known silent spots:**
  - Club on an enemy: reported silent (Oct 1 playtest), although `TakeBluntWeaponDamage` should play `BluntHit`. Lead:
    check whether `BluntHit` is empty or quiet, or whether Daredevil's damage path skips the call. Open.
  - Club on club: no sound (the game never had two crowbars). Open.
  - Any melee hit on a dead body (corpse): silent, by the rule above. Only the finishing head hit is covered.

## 4. Sound vs. what enemies hear

Playing a clip and making **noise** that enemies react to are separate. Noise is `ANBGameLogic.MakeNoise(type)` and
`ANBAlertManagement.AlertAllNPCs` / `QuickAlertToPlayer`, called by gunshots, door kicks, breaking glass, NPC voices
and the physics-prop sound player (`PlayClipPhysicsHard/Medium/Soft` → `MakeNoise("noise")`). The bow is silent to AI.
Full map: `GUNMAN_CONTRACTS.md`, "Sound awareness: what makes noise". **Not checked:** whether `PlayAudioClip` itself
makes noise (assumed not). Check before adding a loud custom sound that should not give the player away.

## 5. Ways to add custom sounds (cheapest first)

1. **AudioReplacer, no code.** Third-party (`MelonLoader-AudioTools`), already installed with AudioImportLib. From its
   strings (not verified in its source): it patches `AudioSource.Play` and swaps the clip for a file in
   `UserData\CustomAudio` whose name matches the clip's name (`gunman_pistol_fire0.wav`, ... are there now). Replaces
   a sound **everywhere** it is used. Needs the exact clip names (see the tool below). Limits: only sounds started with
   `AudioSource.Play`, and only replacing, not adding variants.
2. **Swap entries in the game's arrays at runtime** (`ANBGameLogic.BluntHit`, `BluntHitHead`, `Clips_*`, ...): add our
   clips as extra variants or replace them, and every game path that plays from the array uses them. Targeted, no
   per-hit code.
3. **Play our own clips at our own events** (Death Details' `HeadHitSound` pattern): for things the game has no sound
   for (club on club, a hit on a corpse). `PlayAudioClip` with the game's arguments.
4. **Loading the files:** AudioImportLib (BASS: wav, ogg, mp3, ...) is already a dependency of AudioReplacer, so a mod
   can use it; or embed WAVs in the DLL and build `AudioClip.Create` + `SetData` ourselves to stay self-contained.

## 6. Next steps when this starts

- **Clip-name dump tool:** list every clip in the arrays above (and `ANBSoundPhysicsItem.customSounds` on the clubs) by
  name, so AudioReplacer files and array swaps can be targeted. Death Details' `DebugLog` already prints the clip it
  plays (`sound BluntHitHead/<name>`).
- Solve "club on an enemy is silent" first (section 3): it may be a one-field fix.
- Sound list for the friend (club clacks, body/head/limb thuds): `IDEAS.md`, "Billy Club impact sounds".
