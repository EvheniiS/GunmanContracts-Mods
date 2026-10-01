using Il2CppHurricaneVR.Framework.Core.Grabbers;
using MelonLoader;
using UnityEngine;

namespace BillyClubs
{
    // Throw follow-through. HVR takes the throw velocity from the hand's last few frames at the moment the grip
    // opens (ThrowLookback). Opening the hand mid-swing, as in a real throw, gives the club the slower, earlier part
    // of the swing. For FollowThroughTime after the release the mod keeps measuring the releasing hand: while the
    // club hasn't hit anything and the hand is still faster, the club takes the hand's velocity (or, when the mod is
    // steering it to a target, the hand's speed). One log line per throw says how early the release was.
    public partial class BillyClubsMod
    {
        internal static MelonPreferences_Entry<float> FollowThroughTime;
        internal static MelonPreferences_Entry<bool> FollowThroughVerboseLog;

        static void InitFollowPrefs(MelonPreferences_Category c)
        {
            FollowThroughTime = c.CreateEntry("FollowThroughTime", 0.12f, description: "Seconds after release to sample the hand. If the hand is faster than the club and no impact occurred, the club can gain speed. Many throws receive no extra speed. 0 = off.");
            FollowThroughVerboseLog = c.CreateEntry("FollowThroughVerboseLog", false, description: "Log every follow-through window. Off logs only throws where follow-through actually increased club speed when DebugLog is on.");
        }

        internal class Follow
        {
            public float Until, HandAtRelease, Peak, PeakAt, Released, ClubAtRelease, BoostTo;
            public bool Boosted;
            public string BoostKind;
        }

        // Every physics step: measure each club's hand (while held, and during the follow window).
        static void SampleHand(Club k)
        {
            if (!Alive(k.HandT)) { k.HandOk = false; return; }
            var p = k.HandT.position;
            float dt = Time.fixedDeltaTime;
            // Two-step smoothing: one physics step of tracking jitter alone can read ~1 m/s.
            var raw = k.HandOk && dt > 0f ? (p - k.HandPrev) / dt : Vector3.zero;
            // A tracking recenter can move a controller an impossible distance in one step. The 50.5 m/s
            // spike in the 2026-09-30 throw log must not accelerate a club or pollute the peak report.
            if (raw.magnitude > 25f) raw = Vector3.zero;
            k.HandV = k.HandOk ? Vector3.Lerp(k.HandV, raw, 0.5f) : raw;
            k.HandPrev = p; k.HandOk = true;
        }

        static void NoteGrabber(Club k, HVRGrabberBase grabber)
        {
            var hand = Alive(grabber) ? grabber.TryCast<HVRHandGrabber>() : null;
            if (hand == null) return;           // the force grab passes it on to a hand right after
            k.HandT = hand.transform; k.HandOk = false;
            SwingGrabbed(k, grabber);
        }

        // Called when a flight starts from a release.
        static void StartFollow(Club k, Flight f)
        {
            if (FollowThroughTime.Value <= 0f || !k.HandOk) return;
            float h = k.HandV.magnitude;
            f.Follow = new Follow { Until = Time.time + FollowThroughTime.Value, HandAtRelease = h, Peak = h, Released = Time.time, ClubAtRelease = f.Speed };
        }

        // During the flight, before impacts are checked. Stops at the first impact or when the window ends.
        static void FollowStep(Club k, Flight f)
        {
            var w = f.Follow;
            if (w == null) return;
            if (Time.time > w.Until || f.Bounces > 0 || !k.HandOk) { EndFollow(f); return; }
            // The club lost most of its speed: it hit something. Don't push it on into the wall (that hid the impact
            // until the window ended, and the flight then ended with no ricochet); let the impact check take it.
            if (k.Rb.linearVelocity.magnitude < f.Speed * 0.6f) { EndFollow(f); return; }
            float handSpeed = k.HandV.magnitude;
            if (handSpeed > w.Peak) { w.Peak = handSpeed; w.PeakAt = Time.time; }
            var hv = k.HandV * Mathf.Max(HandThrowBoost.Value, 0.1f); // compare like with like: the release was boosted
            float hs = hv.magnitude;
            float club = f.Ours ? f.SteerAt : k.Rb.linearVelocity.magnitude;
            if (hs <= club + 0.5f) return;
            if (f.Ours)
            {
                float next = SteerSpeed(hs);
                if (next <= f.SteerAt + 0.1f) return;
                f.SteerAt = next;
                w.BoostTo = next; w.BoostKind = "steering speed";
            }
            else
            {
                k.Rb.linearVelocity = hv;
                f.Dir = hv / hs; f.Speed = hs; f.PrevVy = hv.y;
                w.BoostTo = hs; w.BoostKind = "club velocity";
            }
            w.Boosted = true;
        }

        static void EndFollow(Flight f)
        {
            var w = f.Follow;
            if (w == null) return;
            f.Follow = null;
            if (!Dbg || (!w.Boosted && !FollowThroughVerboseLog.Value)) return;
            string result = w.Boosted ? $"{w.BoostKind} raised to {w.BoostTo:0.0} m/s" : "no speed added";
            Log.Msg($"  follow-through: hand {w.HandAtRelease:0.0} -> {w.Peak:0.0} m/s in {(w.PeakAt - w.Released) * 1000f:0} ms, club released at {w.ClubAtRelease:0.0} m/s; {result}");
        }
    }
}
