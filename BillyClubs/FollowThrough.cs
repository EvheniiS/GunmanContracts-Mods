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

        static void InitFollowPrefs(MelonPreferences_Category c)
        {
            FollowThroughTime = c.CreateEntry("FollowThroughTime", 0.12f, description: "Seconds after you let go during which the club still picks up speed from your swing, so opening the hand early in the throw still counts the full swing. 0 = off (HVR's release velocity only).");
        }

        internal class Follow
        {
            public float Until, HandAtRelease, Peak, PeakAt, Released, ClubAtRelease;
            public bool Boosted;
        }

        // Every physics step: measure each club's hand (while held, and during the follow window).
        static void SampleHand(Club k)
        {
            if (!Alive(k.HandT)) { k.HandOk = false; return; }
            var p = k.HandT.position;
            float dt = Time.fixedDeltaTime;
            // Two-step smoothing: one physics step of tracking jitter alone can read ~1 m/s.
            var raw = k.HandOk && dt > 0f ? (p - k.HandPrev) / dt : Vector3.zero;
            k.HandV = k.HandOk ? Vector3.Lerp(k.HandV, raw, 0.5f) : raw;
            k.HandPrev = p; k.HandOk = true;
        }

        static void NoteGrabber(Club k, HVRGrabberBase grabber)
        {
            var hand = Alive(grabber) ? grabber.TryCast<HVRHandGrabber>() : null;
            if (hand == null) return;           // the force grab passes it on to a hand right after
            k.HandT = hand.transform; k.HandOk = false;
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
            var hv = k.HandV * Mathf.Max(HandThrowBoost.Value, 0.1f); // compare like with like: the release was boosted
            float hs = hv.magnitude;
            if (hs > w.Peak) { w.Peak = hs; w.PeakAt = Time.time; }
            float club = f.Ours ? f.SteerAt : k.Rb.linearVelocity.magnitude;
            if (hs <= club + 0.5f) return;
            w.Boosted = true;
            if (f.Ours) f.SteerAt = SteerSpeed(hs);
            else
            {
                k.Rb.linearVelocity = hv;
                f.Dir = hv / hs; f.Speed = hs; f.PrevVy = hv.y;
            }
        }

        static void EndFollow(Flight f)
        {
            var w = f.Follow;
            if (w == null) return;
            f.Follow = null;
            if (!Dbg) return;
            if (w.Peak > w.HandAtRelease + 0.5f)
                Log.Msg($"  follow-through: hand {w.HandAtRelease:0.0} m/s at the release (club {w.ClubAtRelease:0.0}), still speeding up to {w.Peak:0.0} m/s {(w.PeakAt - w.Released) * 1000f:0} ms later - early release{(w.Boosted ? ", club sped up" : "")}");
            else
                Log.Msg($"  follow-through: hand {w.HandAtRelease:0.0} m/s at the release (club {w.ClubAtRelease:0.0}), slowing after it - released at the peak");
        }
    }
}
