using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core.Utils;
using UnityEngine;

namespace RadarSense
{
    // Louder enemy footsteps, so you can hear where they are.
    //
    // How the game plays them (GameAssembly.dll): ANBFootsteps.HandleNPC plays a step only while the enemy is within
    // distanceForHearing of you (ANBBasicNPC.currentDistanceToPlayer), walks a path and moves faster than
    // agentMoveThreshold. ANBplayStepSound picks a clip by surface (walkOn 1 = Metal, 2 = Wood, else Generic) and calls
    // ANBSFXPlayerManager.PlayAudioClip(clip, SoundSource.position, false, type, loud: false, pitch -1, volume -1)
    // -> HVR SFXPlayer.PlaySFX: pitch -1 = TimePitch (slow motion), volume -1 = SoundVolumeSFX. PlaySFX shares a small
    // round-robin pool of AudioSources and Stops the oldest one for each new sound, and skips a clip still in its
    // cooldown (useCooldownSFX), so in a fight gunfire and impacts cut the steps off.
    //
    // The mod raises distanceForHearing to HearDistance and plays each enemy step itself, on that enemy's own 3D
    // AudioSource, set up like the game's SFX reference source (same mixer group and spatializer, so the game's volume
    // settings still apply). LoudRadius is the source's minDistance: full volume within it, then logarithmic falloff,
    // so a bigger radius = louder at a distance.
    internal static class Steps
    {
        static readonly Dictionary<IntPtr, AudioSource> Sources = new();
        static readonly Dictionary<IntPtr, float> OrigHearing = new();
        static float nextTick;
        static bool loggedRef, loggedDefault;
        internal static int Played;            // steps played by the mod, for the perf line
        static readonly HashSet<IntPtr> Walkers = new();
        internal static int WalkersCount => Walkers.Count;
        internal static void ClearCounts() { Played = 0; Walkers.Clear(); }

        static string Mode => RadarSenseMod.LoudSteps.Value?.Trim() ?? "";
        internal static bool Active =>
            Mode.Equals("Always", StringComparison.OrdinalIgnoreCase) ||
            (Mode.Equals("Sense", StringComparison.OrdinalIgnoreCase) && Radar.On);

        internal static void Reset() { Sources.Clear(); OrigHearing.Clear(); }

        // Twice a second: set every enemy's hearing distance for the current mode.
        internal static void Tick()
        {
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.5f;
            var list = ANBStaticGameManager.ANBmain?.encounterSystem?.allEnemies;
            if (list == null) return;
            bool active = Active;
            float want = RadarSenseMod.HearDistance.Value;
            for (int i = 0; i < list.Count; i++)
            {
                var n = list[i];
                if (n == null || !n.isEnemy) continue;
                var f = n.footStepsScript;
                if (f == null) continue;
                if (!OrigHearing.TryGetValue(f.Pointer, out float orig))
                {
                    orig = f.distanceForHearing;
                    OrigHearing[f.Pointer] = orig;
                    if (!loggedDefault && RadarSenseMod.DebugLog.Value)
                    {
                        loggedDefault = true;
                        RadarSenseMod.Log.Msg($"enemy steps: game hears them within {orig:0.#} m, walk/run speed {f.agentWalkSpeed:0.#}/{f.agentRunSpeed:0.#}, move threshold {f.agentMoveThreshold:0.##}");
                    }
                }
                float d = active ? Mathf.Max(orig, want) : orig;
                if (f.distanceForHearing != d) f.distanceForHearing = d;
            }
        }

        // Harmony prefix on ANBplayStepSound. true = let the game play it.
        internal static bool Play(ANBFootsteps f)
        {
            if (f == null || f.isPlayer || !Active) return true;
            var clips = (int)f.walkOn == 1 ? f.Metal : (int)f.walkOn == 2 ? f.Wood : f.Generic;
            if (clips == null || clips.Length == 0) return true;
            var clip = clips[UnityEngine.Random.Range(0, clips.Length)];
            if (clip == null) return true;
            var src = Source(f);
            if (src == null || !src.isActiveAndEnabled) return true;   // its object is switched off: the game plays it

            var sfx = SFXPlayer.Instance;
            float baseVol = RadarSenseMod.Alive(sfx) ? sfx.SoundVolumeSFX : 1f;
            src.pitch = RadarSenseMod.Alive(sfx) && sfx.TimePitch > 0f ? sfx.TimePitch : 1f;
            src.minDistance = Mathf.Max(0.1f, RadarSenseMod.LoudRadius.Value);
            src.maxDistance = Mathf.Max(src.minDistance + 1f, RadarSenseMod.HearDistance.Value + 5f);
            src.PlayOneShot(clip, Mathf.Clamp01(baseVol * RadarSenseMod.StepVolume.Value));
            Played++;
            Walkers.Add(f.Pointer);
            return false;
        }

        static AudioSource Source(ANBFootsteps f)
        {
            if (Sources.TryGetValue(f.Pointer, out var s) && RadarSenseMod.Alive(s)) return s;
            var at = f.SoundSource != null ? f.SoundSource : f.transform;
            var go = new GameObject("RadarSenseSteps");
            go.transform.SetParent(at, false);
            s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.dopplerLevel = 0f;
            var sfx = SFXPlayer.Instance;
            var r = RadarSenseMod.Alive(sfx) ? sfx.SFXReferenceSource : null;
            if (RadarSenseMod.Alive(r))
            {
                s.outputAudioMixerGroup = r.outputAudioMixerGroup;
                s.spatialize = r.spatialize;
                s.spatializePostEffects = r.spatializePostEffects;
                s.spread = r.spread;
                s.priority = r.priority;
                s.reverbZoneMix = r.reverbZoneMix;
                s.bypassEffects = r.bypassEffects;
                s.bypassListenerEffects = r.bypassListenerEffects;
                s.bypassReverbZones = r.bypassReverbZones;
                if (!loggedRef && RadarSenseMod.DebugLog.Value)
                {
                    loggedRef = true;
                    var g = r.outputAudioMixerGroup;
                    RadarSenseMod.Log.Msg($"game SFX source: volume {r.volume:0.##}, {r.rolloffMode} {r.minDistance:0.#}-{r.maxDistance:0.#} m, blend {r.spatialBlend:0.##}, " +
                                          $"spatialize {r.spatialize}, mixer '{(g != null ? g.name : "none")}', SoundVolumeSFX {sfx.SoundVolumeSFX:0.##}, pool {sfx.SFXSourceCount}, cooldown {(sfx.useCooldownSFX ? sfx.cooldownTimeSFX.ToString("0.##") + " s" : "off")}");
                }
            }
            Sources[f.Pointer] = s;
            return s;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBFootsteps), nameof(ANBFootsteps.ANBplayStepSound))]
    internal static class StepSoundPatch
    {
        static bool Prefix(ANBFootsteps __instance)
        {
            try { return Steps.Play(__instance); }
            catch (Exception e) { RadarSenseMod.Log.Warning($"step: {e.GetType().Name}: {e.Message}"); return true; }
        }
    }
}
