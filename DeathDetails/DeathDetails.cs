using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Weapons;
using Il2CppHurricaneVR.Framework.Weapons.Bow;
using Il2CppHurricaneVR.Framework.Weapons.Guns;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(DeathDetails.DeathDetailsMod), "Death Details", "0.7.1", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace DeathDetails
{
    // Death Details (was Close Eyes): dead enemies close their eyes, keep a slightly parted mouth, and enemies writhing
    // in pain keep their pain face until they die; a melee hit to the head finishes them.
    //
    // The faces carry ARKit blend shapes (eyeBlinkLeft / eyeBlinkRight close the eyelids). The game rewrites face
    // shapes every frame (0.2.0 log: 232/232 frames): ANBBasicNPC.faceAnimator animates the sync's mainMesh
    // (CC_Combined_LOD0), and ANBBlendShapeSync.UpdateExec, called per enemy from checkVisibilityRelatedActions,
    // reads all 147 weights off mainMesh and copies them onto every other face and beard mesh.
    //
    // So when the eyelids start to close, the mod switches that corpse's face animator off and skips its sync
    // (UpdateExec prefix). It eases the eyelids shut on every face mesh, then leaves them: nothing rewrites them any
    // more. The jaw (jawOpen) eases to JawOpen the same way, from where the game's death face had it. Per frame a
    // frozen corpse costs one weight read on its main face; if something did change it, the mod writes the eyelids
    // again. A corpse costs less than in the vanilla game, which keeps animating and syncing
    // every dead face.
    //
    // Death is caught at KillNPC (postfix) and by polling isDead. Enemies are pooled: when one is alive again, its
    // face animator, sync and original weights come back.
    //
    // Two death states. A lethal hit sets ANBBasicNPC.isTwitcher (TakeDamage) when the enemy is in combat, it is not
    // a headshot and it took at most 2 torso hits and 1 chest hit: the body shot. isDead is already true, but the
    // enemy lies writhing in pain: startTwitcher plays the twitcher animation, face "face_twitcher" and pain sounds.
    // It ends after twitcherTimeTillDeathMin..Max s (killedTimer > twitcherSurvivalTime), after
    // killTwitcherAfterOutOfSightTime out of view, or when shot again (TakeDamage, ExplosionDamage): killTwitcher
    // plays "twitcherKill", clears isTwitcher and plays face "face_death". So while isTwitcher is set the face is left
    // to the game, and the eyes close Delay s after it clears. Headshots and other kills go straight to face_death.
    //
    // The pain can last forever: the bleed-out check sits in ANBBasicNPC.UpdateExec, which ANBUpdateCentral only
    // calls for enemies in encounterSystem.allNpcs with NPCSpawned && NPCFullySetup. And a hit on a twitcher only ends
    // it when it is not a limb hit (TakeDamage: a leg hit only pushes the body and plays a hurt sound). So the mod
    // ends the pain itself, the way the game would: past the game's own random time (or MaxPainSeconds) it calls
    // forcePuppetMasterActive(1, false) + killTwitcher(noHit: true) like UpdateExec; a hit that leaves it writhing
    // calls killTwitcher(false) like a torso shot.
    public class DeathDetailsMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        static MelonPreferences_Entry<bool> Enabled, WaitForTwitch, BleedOut, AnyHitEndsPain, HeadHitEndsPain, HeadHitSfx, DebugLog;
        static MelonPreferences_Entry<float> EyesClosed, JawOpen, CloseSeconds, Delay, MaxPainSeconds, HeadHitSpeed;

        class Face { public SkinnedMeshRenderer R; public int L, Rt, J = -1; public float Full, OrigL, OrigR, JawFull, OrigJ, JawFrom; }

        class Body
        {
            public ANBBasicNPC Npc;
            public bool Dead, Closing, Frozen, Twitching;
            public List<Face> Faces;
            public float DiedAt, DeathAt;
            public string Via;
            public Animator Anim;
            public bool AnimWasOn;
            public IntPtr Sync;
            public int Rewrites;
            public bool PainEnded;
            public IntPtr Mgr;
            public bool Detailed;
        }

        static readonly Dictionary<IntPtr, Body> Bodies = new();
        static readonly HashSet<IntPtr> SkippedSyncs = new();
        static readonly Dictionary<IntPtr, Body> Writhing = new();   // body manager -> body writhing right now
        static float nextPoll;
        static int detailedLeft, painLogLeft;
        static bool warned;
        const int DetailedDeaths = 3;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("DeathDetails", "Death Details");
            Enabled = c.CreateEntry("Enabled", true, description: "Dead enemies close their eyes.");
            WaitForTwitch = c.CreateEntry("WaitForTwitch", true, description: "An enemy killed by a body shot can lie writhing in pain before it dies for good. On: its face stays animated (pain face) and the eyes close when it stops. Off: the eyes close right away like any other death.");
            HeadHitEndsPain = c.CreateEntry("HeadHitEndsPain", true, description: "A melee hit to the head of an enemy writhing in pain finishes it, like a headshot: clubs, bats and the crowbar (swung or thrown), a pistol or rifle swung with Heavy Melee, the bow, a fist. The game ignores melee hits on dead bodies.");
            HeadHitSfx = c.CreateEntry("HeadHitSfx", true, description: "Play the game's own blunt head-hit sound on that finishing hit (the game plays none on a dead body).");
            HeadHitSpeed = c.CreateEntry("HeadHitSpeed", 2f, description: "With HeadHitEndsPain: how fast (m/s) the weapon or hand must hit the head. Lower = a light tap counts.");
            BleedOut = c.CreateEntry("BleedOut", false, description: "Experimental, untested. Enemies writhing in pain on the floor die after a while (the game has a bleed-out time but often never applies it, so they writhe forever). Off = the game's behaviour.");
            MaxPainSeconds = c.CreateEntry("MaxPainSeconds", 0f, description: "With BleedOut: longest an enemy writhes, in seconds after death. 0 = the game's own random bleed-out time.");
            AnyHitEndsPain = c.CreateEntry("AnyHitEndsPain", false, description: "Experimental, untested. Any hit on an enemy writhing in pain finishes it, leg shots included (the game only reacts to torso and head shots).");
            EyesClosed = c.CreateEntry("EyesClosed", 1.0f, description: "How far the eyelids close: 1 = fully shut, 0.8 = a slit left open.");
            JawOpen = c.CreateEntry("JawOpen", 0.1f, description: "How far a dead enemy's mouth stays open, set together with the eyes: 0 = closed, 0.1 = slightly parted (default), 1 = fully open. -1 = leave it to the game's death face (often wide open).");
            CloseSeconds = c.CreateEntry("CloseSeconds", 0.6f, description: "Seconds the eyelids take to close.");
            Delay = c.CreateEntry("Delay", 0.3f, description: "Seconds after death before the eyelids start to close (the face keeps its expression until then).");
            DebugLog = c.CreateEntry("DebugLog", false, description: "One line per death and one when the eyes are shut. The first 3 deaths of each level also list every mesh with blend shapes.");
            LoggerInstance.Msg("loaded");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            Bodies.Clear();
            SkippedSyncs.Clear();
            Writhing.Clear();
            detailedLeft = DetailedDeaths;
            painLogLeft = 3;
            warned = false;
        }

        public override void OnLateUpdate()
        {
            if (Time.time >= nextPoll) { nextPoll = Time.time + 0.1f; Poll(); }
            if (!Enabled.Value) return;
            foreach (var b in Bodies.Values)
            {
                if (!b.Dead || b.Faces == null || b.Faces.Count == 0) continue;
                try
                {
                    if (b.Frozen) Check(b);
                    else Close(b);
                }
                catch (Exception ex) { Log.Warning($"close: {ex.Message}"); b.Faces = null; }
            }
        }

        // ------------------------------------------------------------------ death / revive

        internal static void OnKill(ANBBasicNPC n)
        {
            if (!Enabled.Value || n == null) return;
            try
            {
                var b = Get(n);
                if (!b.Dead) Died(b, "KillNPC");
            }
            catch (Exception ex) { Log.Warning($"kill: {ex.Message}"); }
        }

        internal static bool SyncSkipped(ANBBlendShapeSync s) => s != null && SkippedSyncs.Contains(s.Pointer);

        static Body Get(ANBBasicNPC n)
        {
            IntPtr p = n.Pointer;
            if (!Bodies.TryGetValue(p, out var b)) Bodies[p] = b = new Body { Npc = n };
            return b;
        }

        static void Poll()
        {
            if (Enabled.Value)
                try
                {
                    var list = ANBStaticGameManager.ANBmain?.encounterSystem?.allEnemies;
                    if (list != null)
                        for (int i = 0; i < list.Count; i++)
                        {
                            var n = list[i];
                            if (n == null) continue;
                            var b = Get(n);
                            if (n.isDead && !b.Dead) Died(b, "isDead");
                        }
                }
                catch (Exception ex) { Log.Warning($"enemy list: {ex.Message}"); }

            foreach (var b in Bodies.Values)
                if (b.Dead && !b.PainEnded && Enabled.Value && (BleedOut.Value || HeadHitEndsPain.Value))
                    try { Pain(b); } catch (Exception ex) { Log.Warning($"pain: {ex.Message}"); b.PainEnded = true; }

            // Pooled enemies come back alive (and may have left the list while dead). Switching the mod off
            // gives every face back too.
            foreach (var b in Bodies.Values)
            {
                if (!b.Dead || (Enabled.Value && Time.time - b.DiedAt < 2f)) continue;
                bool alive;
                try { alive = b.Npc == null || b.Npc.WasCollected || !b.Npc.isDead; }
                catch { alive = true; }
                if (alive || !Enabled.Value) Revived(b);
            }
        }

        static void Died(Body b, string via)
        {
            b.Dead = true;
            b.Closing = b.Frozen = b.Twitching = false;
            b.DiedAt = b.DeathAt = Time.time;
            b.PainEnded = false;
            b.Via = via;
            b.Rewrites = 0;
            b.Detailed = DebugLog.Value && detailedLeft > 0;
            if (b.Detailed) detailedLeft--;
            b.Faces = FindFaces(b);
            if (b.Faces.Count == 0) Log.Msg($"{b.Npc.name}: no face mesh with eyeBlinkLeft/Right, eyes stay open");
            else if (DebugLog.Value && !b.Detailed)
                Log.Msg($"{b.Npc.name}: dead ({via}), {b.Faces.Count} face mesh(es)");
        }

        static void Revived(Body b)
        {
            b.Dead = b.Closing = b.Frozen = b.Twitching = false;
            SkippedSyncs.Remove(b.Sync);
            Unregister(b);
            try { if (b.Anim != null && !b.Anim.WasCollected && b.AnimWasOn) b.Anim.enabled = true; } catch { }
            b.Anim = null;
            if (b.Faces != null)
                foreach (var f in b.Faces)
                    try
                    {
                        if (f.R == null || f.R.WasCollected) continue;
                        f.R.SetBlendShapeWeight(f.L, f.OrigL);
                        f.R.SetBlendShapeWeight(f.Rt, f.OrigR);
                        if (f.J >= 0) f.R.SetBlendShapeWeight(f.J, f.OrigJ);
                    }
                    catch { }
            b.Faces = null;   // a pooled enemy can come back with a different face
        }

        // ------------------------------------------------------------------ closing

        // Eases the eyelids shut. When it starts, the face animator goes off and the sync is skipped, so the
        // face keeps the expression it had and nothing overwrites what we write.
        static void Close(Body b)
        {
            if (!b.Closing && WaitForTwitch.Value)
            {
                bool twitch = b.Npc.isTwitcher;
                if (twitch != b.Twitching)
                {
                    if (DebugLog.Value)
                        Log.Msg(twitch ? $"{b.Npc.name}: lying in pain, eyes wait" : $"{b.Npc.name}: pain over after {Time.time - b.DiedAt:0.0} s, closing eyes");
                    b.Twitching = twitch;
                    if (!twitch) b.DiedAt = Time.time;   // Delay counts from the final death
                }
                if (twitch) return;
            }
            float t = CloseSeconds.Value <= 0f ? 1f : Mathf.Clamp01((Time.time - b.DiedAt - Delay.Value) / CloseSeconds.Value);
            if (Time.time - b.DiedAt < Delay.Value) return;
            if (!b.Closing)
            {
                b.Closing = true;
                StopFace(b);
                foreach (var f in b.Faces)   // the jaw eases from where the game's death face has it now
                    if (f.R != null && f.J >= 0) f.JawFrom = f.R.GetBlendShapeWeight(f.J);
            }
            Write(b, t * t * (3f - 2f * t));
            if (t < 1f) return;
            b.Frozen = true;
            if (DebugLog.Value) Log.Msg($"{b.Npc.name}: eyes shut, face animator {(b.AnimWasOn ? "off" : "was already off")}, sync {(b.Sync != IntPtr.Zero ? "skipped" : "none")}");
        }

        static void StopFace(Body b)
        {
            try
            {
                var a = b.Npc.faceAnimator;
                b.Anim = a;
                b.AnimWasOn = a != null && a.enabled;
                if (b.AnimWasOn) a.enabled = false;
            }
            catch { b.Anim = null; b.AnimWasOn = false; }
            if (b.Sync != IntPtr.Zero) SkippedSyncs.Add(b.Sync);
        }

        // Frozen corpse: one read. If something rewrote the eyelids, write them again (and stop the animator
        // again in case the game switched it back on).
        static void Check(Body b)
        {
            var f = b.Faces[0];
            if (f.R == null) return;
            if (Mathf.Abs(f.R.GetBlendShapeWeight(f.L) - Target(f)) <= 0.5f) return;
            StopFace(b);
            Write(b, 1f);
            if (++b.Rewrites == 10 && !warned)
            {
                warned = true;
                Log.Msg($"{b.Npc.name}: something keeps reopening the eyes after the face animator was stopped; rewriting them each time (logged once per level)");
            }
        }

        static void Write(Body b, float k)
        {
            foreach (var f in b.Faces)
            {
                if (f.R == null) continue;
                float target = Target(f);
                f.R.SetBlendShapeWeight(f.L, Mathf.Lerp(f.OrigL, target, k));
                f.R.SetBlendShapeWeight(f.Rt, Mathf.Lerp(f.OrigR, target, k));
                if (f.J >= 0 && JawOpen.Value >= 0f)
                    f.R.SetBlendShapeWeight(f.J, Mathf.Lerp(f.JawFrom, f.JawFull * Mathf.Clamp01(JawOpen.Value), k));
            }
        }

        static float Target(Face f) => f.Full * Mathf.Clamp01(EyesClosed.Value);

        // ------------------------------------------------------------------ pain (twitcher)

        // Every 0.1 s per dead body until its pain is over: register writhing bodies for the head-hit check, and
        // (BleedOut) end the pain past its time. A body that is not writhing 1 s after death is done for good.
        static void Pain(Body b)
        {
            var n = b.Npc;
            if (n == null || n.WasCollected) { Unregister(b); b.PainEnded = true; return; }
            bool writhing = n.isTwitcher && n.isTwitcherStarted && !n.isKillingTwitcher;
            if (!n.isTwitcher)
            {
                Unregister(b);
                if (Time.time - b.DeathAt > 1f) b.PainEnded = true;   // not writhing (any more)
                return;
            }
            if (HeadHitEndsPain.Value && writhing) Register(b); else Unregister(b);
            if (!BleedOut.Value || !writhing) return;
            var pm = n.ANBPM;
            if (pm == null) return;
            float since = Time.time - b.DeathAt;
            float limit = MaxPainSeconds.Value > 0f ? MaxPainSeconds.Value : pm.twitcherSurvivalTime;
            if (since < limit + 0.5f) return;
            if (painLogLeft > 0 || DebugLog.Value)
            {
                if (painLogLeft > 0) painLogLeft--;
                bool listed = false;
                try { var l = ANBStaticGameManager.ANBmain?.encounterSystem?.allNpcs; if (l != null) for (int i = 0; i < l.Count && !listed; i++) listed = l[i] != null && l[i].Pointer == n.Pointer; } catch { }
                Log.Msg($"{n.name}: still in pain {since:0.0} s after death, ending it. Game: killedTimer {n.killedTimer:0.0} vs " +
                        $"survival {pm.twitcherSurvivalTime:0.0} ({pm.twitcherTimeTillDeathMin:0.#}-{pm.twitcherTimeTillDeathMax:0.#}), outOfView " +
                        $"{n.outOfViewTime:0.0} vs {pm.killTwitcherAfterOutOfSightTime:0.#}, updated by the game: " +
                        $"{(listed ? "in allNpcs" : "NOT in allNpcs")}, spawned {n.NPCSpawned}, setup {n.NPCFullySetup}");
            }
            End(b, true);
        }

        static void Register(Body b)
        {
            if (b.Mgr != IntPtr.Zero) return;
            var m = b.Npc.bodyManager;
            if (m == null) return;
            b.Mgr = m.Pointer;
            Writhing[b.Mgr] = b;
        }

        static void Unregister(Body b)
        {
            if (b.Mgr == IntPtr.Zero) return;
            Writhing.Remove(b.Mgr);
            b.Mgr = IntPtr.Zero;
        }

        static void End(Body b, bool noHit)
        {
            b.PainEnded = true;
            Unregister(b);
            var n = b.Npc;
            var pm = n.ANBPM;
            if (noHit) n.forcePuppetMasterActive(1f, false);
            pm.StartCoroutine(pm.killTwitcher(noHit));
        }

        // collisionEnter postfix (every melee contact on an enemy body part, dead or alive; the game's own handler
        // ignores dead bodies). Only bodies writhing right now are in Writhing, so any other contact costs one lookup.
        // A head contact from the player's blunt weapon (clubs, bats, crowbar, swung or thrown), held gun or bow, or
        // hand, at HeadHitSpeed or faster, finishes it like a headshot does.
        internal static void AfterBodyCollision(ANBBodyMeleeCollisionManager mgr, ANBBodyMeleeCollision bmc, Collision collision)
        {
            if (Writhing.Count == 0 || mgr == null || !Writhing.TryGetValue(mgr.Pointer, out var b)) return;
            try
            {
                if (bmc == null || !bmc.isHead || collision == null) return;
                float speed = collision.relativeVelocity.magnitude;
                if (speed < HeadHitSpeed.Value) return;
                var col = collision.collider;
                if (col == null || col.attachedRigidbody == null) return;
                string what = PlayerHitter(col.gameObject);
                if (what == null) return;
                string sound = HeadHitSound(bmc);
                if (DebugLog.Value) Log.Msg($"{b.Npc.name}: {what} to the head at {speed:0.0} m/s while in pain, finishing it{sound}");
                End(b, false);
            }
            catch (Exception ex) { Log.Warning($"head hit: {ex.Message}"); Unregister(b); }
        }

        static string PlayerHitter(GameObject go)
        {
            var blunt = go.GetComponentInParent<ANBBluntWeapon>();
            if (blunt != null) return blunt.name;
            if (go.GetComponentInParent<HVRHandGrabber>() != null) return "hand";
            var gun = go.GetComponentInParent<ANBHVRGunBase>();
            if (gun != null) return gun.EnemyGun || !(Held(gun.Grabbable) || Held(gun.StabilizerGrabbable)) ? null : gun.name;
            var bow = go.GetComponentInParent<HVRBowBase>();
            if (bow != null && Held(go.GetComponentInParent<HVRGrabbable>())) return bow.name;
            return null;
        }

        static bool Held(HVRGrabbable g) => g != null && g.IsHandGrabbed;

        // The game's own melee hit sound never plays on a dead body: TakeBluntWeaponDamage picks a random
        // ANBGameLogic.BluntHit clip and plays it with PlayAudioClip(clip, part position, false, "default", false, -1, -1)
        // only for a living target. Same call here, with the BluntHitHead set (BluntHit if that is empty).
        static string HeadHitSound(ANBBodyMeleeCollision bmc)
        {
            if (!HeadHitSfx.Value) return "";
            try
            {
                var g = ANBStaticGameManager.ANBmain;
                if (g == null) return ", no sound (no game)";
                var clips = g.BluntHitHead;
                string set = "BluntHitHead";
                if (clips == null || clips.Length == 0) { clips = g.BluntHit; set = "BluntHit"; }
                if (clips == null || clips.Length == 0) return ", no sound (no clips)";
                var clip = clips[UnityEngine.Random.Range(0, clips.Length)];
                if (clip == null) return ", no sound (empty clip)";
                Vector3 pos = bmc.myCol != null ? bmc.myCol.transform.position : bmc.transform.position;
                g.PlayAudioClip(clip, pos, false, "default", false, -1f, -1f);
                return $", sound {set}/{clip.name}";
            }
            catch (Exception ex) { return $", no sound ({ex.Message})"; }
        }

        // TakeDamage postfix: the game ends the pain on a torso or head hit (it sets isKillingTwitcher); anything else
        // that leaves the enemy writhing (leg shots) is finished here.
        internal static void AfterDamage(ANBBasicNPC n, float damage)
        {
            if (!Enabled.Value || !AnyHitEndsPain.Value || n == null || damage <= 0f) return;
            try
            {
                if (!n.isDead || !n.isTwitcher || !n.isTwitcherStarted || n.isKillingTwitcher) return;
                var b = Get(n);
                if (!b.Dead || b.PainEnded) return;
                if (DebugLog.Value) Log.Msg($"{n.name}: hit while in pain, finishing it");
                End(b, false);
            }
            catch (Exception ex) { Log.Warning($"hit: {ex.Message}"); }
        }

        // ------------------------------------------------------------------ faces

        // The sync's main mesh first (it is the one Check reads), then the synced meshes, then any other child
        // mesh with both blink shapes (face LOD0-2 and beards; LOD3/4 faces have no blend shapes).
        static List<Face> FindFaces(Body b)
        {
            var n = b.Npc;
            var faces = new List<Face>();
            var seen = new HashSet<IntPtr>();
            var diag = b.Detailed ? new List<string>() : null;

            ANBBlendShapeSync sync = null;
            try { sync = n.blendShapeSync; } catch { }
            b.Sync = sync != null ? sync.Pointer : IntPtr.Zero;
            SkinnedMeshRenderer main = null;
            if (sync != null)
            {
                try { main = sync.mainMesh; } catch { }
                Add(main, true);
                try { var a = sync.syncMeshes; if (a != null) for (int i = 0; i < a.Length; i++) Add(a[i], false); } catch { }
                try { var l = sync.otherMeshes; if (l != null) for (int i = 0; i < l.Count; i++) Add(l[i], false); } catch { }
            }
            foreach (var r in n.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Add(r, false);

            void Add(SkinnedMeshRenderer r, bool isMain)
            {
                if (r == null || !seen.Add(r.Pointer)) return;
                var m = r.sharedMesh;
                int count = m == null ? 0 : m.blendShapeCount;
                int l = count == 0 ? -1 : m.GetBlendShapeIndex("eyeBlinkLeft");
                int rt = count == 0 ? -1 : m.GetBlendShapeIndex("eyeBlinkRight");
                if (diag != null && (count > 1 || isMain))
                    diag.Add($"{r.name}{(isMain ? "*" : "")} '{m?.name}' {count} shapes, blink {l}/{rt}{(r.gameObject.activeInHierarchy && r.enabled ? "" : " (off)")}");
                if (l < 0 || rt < 0) return;
                float full = m.GetBlendShapeFrameWeight(l, m.GetBlendShapeFrameCount(l) - 1);
                var face = new Face { R = r, L = l, Rt = rt, Full = full, OrigL = r.GetBlendShapeWeight(l), OrigR = r.GetBlendShapeWeight(rt) };
                int j = m.GetBlendShapeIndex("jawOpen");
                if (j >= 0)
                {
                    face.J = j;
                    face.JawFull = m.GetBlendShapeFrameWeight(j, m.GetBlendShapeFrameCount(j) - 1);
                    face.OrigJ = r.GetBlendShapeWeight(j);
                }
                faces.Add(face);
            }

            if (diag != null)
            {
                string fa = "none";
                try { var a = n.faceAnimator; if (a != null) fa = $"{a.name} {(a.isActiveAndEnabled ? "on" : "off")}"; } catch { }
                Log.Msg($"{n.name}: dead via {b.Via}, faceAnimator {fa}, sync main '{main?.name}', {faces.Count} face mesh(es): {string.Join("; ", diag)}");
            }
            return faces;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.KillNPC))]
    internal static class KillPatch
    {
        static void Postfix(ANBBasicNPC __instance) => DeathDetailsMod.OnKill(__instance);
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBodyMeleeCollisionManager), nameof(ANBBodyMeleeCollisionManager.collisionEnter))]
    internal static class BodyCollisionPatch
    {
        static void Postfix(ANBBodyMeleeCollisionManager __instance, ANBBodyMeleeCollision bmc, Collision collision) =>
            DeathDetailsMod.AfterBodyCollision(__instance, bmc, collision);
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.TakeDamage))]
    internal static class DamagePatch
    {
        static void Postfix(ANBBasicNPC __instance, float damage) => DeathDetailsMod.AfterDamage(__instance, damage);
    }

    // A corpse whose eyes are closing or shut: the sync would copy the (stopped) main mesh onto the other meshes
    // every frame, so it is skipped.
    [HarmonyLib.HarmonyPatch(typeof(ANBBlendShapeSync), nameof(ANBBlendShapeSync.UpdateExec))]
    internal static class SyncPatch
    {
        static bool Prefix(ANBBlendShapeSync __instance) => !DeathDetailsMod.SyncSkipped(__instance);
    }
}
