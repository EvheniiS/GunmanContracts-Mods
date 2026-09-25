using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(KneeShotStun.KneeShotStunMod), "Knee Shot Stun", "1.0.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace KneeShotStun
{
    // Enemies shot in the leg drop to one knee for longer before they get back up.
    //
    // How the game does it (GameAssembly.dll + the ANB_BasicNPC_Animator controller):
    // - A leg hit sets ANBBasicNPC.gettingHitLegs, crossfades the animator's "Hit" layer to
    //   hit_legs_1 / hit_legs_1_m, and starts GetHit. GetHit copies stunAtHitTimeLegs (3 s) into
    //   overallHitTime, and checkAbilities counts that down; isGettingHit (the AI stun) ends at 0.
    // - hit_legs_1 is ONE 2.8 s clip: fall 0-0.85 s, kneel 0.85-1.5 s (hips steady at ~0.46-0.51 m),
    //   rise 1.5-2.4 s. Its transition back to "No hit" is exit time 0.911 with no conditions, so the
    //   visual get-up is fixed by the clip, not by any timer. Raising stunAtHitTimeLegs alone would
    //   only leave the enemy standing there stunned.
    //
    // So the mod time-stretches the kneel part of the clip: from 0.85 s to 1.5 s it drives the Hit
    // layer's time itself, at a slower rate, so that part lasts ExtraKneelSeconds longer. The fall
    // and the rise play at normal speed. While it holds, it keeps the AI stun running so the enemy
    // can't shoot from the knee, and endLegHitAfter is extended by the same amount so the game's
    // "hit in the legs" flags (kneeling death animation, leg stun on follow-up hits) last as long
    // as the kneel does.
    public class KneeShotStunMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> Enabled, DebugLog;
        internal static MelonPreferences_Entry<float> ExtraKneelSeconds;

        // hit_legs_1 timing, read from the clip's hip-height curve.
        const float ClipLength = 2.8f;
        const float HoldStartN = 0.85f / ClipLength;   // on the knee
        const float HoldEndN = 1.50f / ClipLength;     // starts to rise
        const float HoldWindow = 1.50f - 0.85f;        // seconds the game spends kneeling
        const float RiseAndMargin = 1.5f;              // rest of the clip (1.3 s) + the game's 0.2 s stun margin

        static readonly int KneelA = Animator.StringToHash("hit_legs_1");
        static readonly int KneelB = Animator.StringToHash("hit_legs_1_m");

        class Kneel
        {
            public ANBBasicNPC Npc;
            public IntPtr Ptr;
            public Animator Anim;
            public int Layer;
            public float AddedAt;
            public float HoldAt = -1;
            public float Duration;
            public bool Seen;
        }

        static readonly List<Kneel> Active = new();
        // endLegHitAfter per NPC: the game's value, and what the mod last wrote. NPCs are pooled, so a
        // value that differs from what we wrote is a fresh game value.
        static readonly Dictionary<IntPtr, (float orig, float applied)> EndLegHit = new();

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("KneeShotStun", "Knee Shot Stun");
            Enabled = c.CreateEntry("Enabled", true, description: "Enemies shot in the leg stay down on one knee for longer.");
            ExtraKneelSeconds = c.CreateEntry("ExtraKneelSeconds", 3.0f, description: "Seconds added to the kneel. The game's own kneel is about 0.65 s, and the whole knee-shot animation is 2.8 s.");
            DebugLog = c.CreateEntry("DebugLog", false, description: "Log each knee shot: when the hold starts and ends, and the game's stun values.");
            LoggerInstance.Msg($"loaded - leg-shot kneel lasts {Extra:0.#} s longer.");
        }

        static float Extra => Mathf.Max(0f, ExtraKneelSeconds.Value);

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            Active.Clear();
            EndLegHit.Clear();
        }

        // Called from the GetHit prefix, before the game reads the stun values.
        internal static void OnHit(ANBBasicNPC npc)
        {
            try
            {
                IntPtr p = npc.Pointer;
                float cur = npc.endLegHitAfter;
                if (!EndLegHit.TryGetValue(p, out var e) || !Mathf.Approximately(cur, e.applied)) e = (cur, cur);
                float want = Enabled.Value ? e.orig + Extra : e.orig;
                npc.endLegHitAfter = want;
                EndLegHit[p] = (e.orig, want);

                if (!Enabled.Value || Extra <= 0f || !npc.gettingHitLegs || npc.isDead) return;
                for (int i = 0; i < Active.Count; i++)
                    if (Active[i].Ptr == p) return;                     // already kneeling; Update handles restarts
                var anim = npc.animator;
                if (anim == null) return;
                int layer = anim.GetLayerIndex("Hit");
                if (layer < 0) return;
                Active.Add(new Kneel { Npc = npc, Ptr = p, Anim = anim, Layer = layer, AddedAt = Time.time });
            }
            catch (Exception ex) { Log.Warning($"hit: {ex.Message}"); }
        }

        public override void OnUpdate() => Drive();
        public override void OnFixedUpdate() => Drive();   // in case the animator runs in AnimatePhysics

        static void Drive()
        {
            if (Active.Count == 0) return;
            float now = Time.time;
            for (int i = 0; i < Active.Count; i++)
            {
                var k = Active[i];
                try
                {
                    if (k.Npc == null || k.Anim == null || k.Npc.isDead) { Active.RemoveAt(i--); continue; }
                    var st = k.Anim.GetCurrentAnimatorStateInfo(k.Layer);
                    bool kneeling = st.shortNameHash == KneelA || st.shortNameHash == KneelB;
                    if (!kneeling)
                    {
                        // Not in the kneel (yet, while the crossfade starts, or any more).
                        if (k.Seen || now - k.AddedAt > 1f) Active.RemoveAt(i--);
                        continue;
                    }
                    k.Seen = true;
                    float n = st.normalizedTime;

                    if (k.HoldAt >= 0 && n < HoldStartN - 0.05f) k.HoldAt = -1;  // shot again: the clip restarted
                    if (k.HoldAt < 0)
                    {
                        if (n < HoldStartN) continue;                   // still falling
                        k.HoldAt = now;
                        k.Duration = HoldWindow + Extra;
                        if (DebugLog.Value)
                            Log.Msg($"{k.Npc.name}: knee shot, holding the kneel {k.Duration:0.00} s (game: {HoldWindow:0.00} s). " +
                                    $"stunAtHitTimeLegs {k.Npc.stunAtHitTimeLegs:0.##}, endLegHitAfter {k.Npc.endLegHitAfter:0.##}, " +
                                    $"animator {k.Anim.updateMode}");
                    }

                    float elapsed = now - k.HoldAt;
                    if (elapsed >= k.Duration)
                    {
                        if (DebugLog.Value) Log.Msg($"{k.Npc.name}: getting up after {elapsed:0.00} s on the knee");
                        Active.RemoveAt(i--);                           // let the clip play the rise
                        continue;
                    }

                    k.Anim.Play(st.fullPathHash, k.Layer, HoldStartN + (HoldEndN - HoldStartN) * (elapsed / k.Duration));

                    // Keep the AI stunned until the enemy is back on its feet. A follow-up hit resets
                    // overallHitTime to the game's shorter value, so this is re-applied every frame.
                    float stun = k.Duration - elapsed + RiseAndMargin;
                    if (k.Npc.overallHitTime < stun) k.Npc.overallHitTime = stun;
                    k.Npc.isGettingHit = true;
                }
                catch (Exception ex)
                {
                    Log.Warning($"kneel: {ex.Message}");
                    Active.RemoveAt(i--);
                }
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.GetHit))]
    internal static class GetHitPatch
    {
        static void Prefix(ANBBasicNPC __instance) => KneeShotStunMod.OnHit(__instance);
    }
}
