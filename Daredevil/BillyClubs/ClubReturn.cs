using MelonLoader;
using UnityEngine;

namespace BillyClubs
{
    // A club lying around goes back to its belt holster after a while, the way the game sends a thrown knife home
    // (ANBKnife.checkAutoReturn -> returnKnife) and the way VR Holster Customization times it (KnifeReturn.cs):
    // the timer starts when the club comes to rest (on the floor, against a wall, in a corner), not at the throw, so
    // a long flight never returns it in mid-air. Off by default: the clubs stay where they fall, like in the show.
    //
    // A club last drawn from a back holster (VR Holster Customization) returns there; any other goes to the belt.
    // The belt return itself is the F8 recall (SendToSlot): free the hand, end the flight, pin the club to the belt slot.
    // Nothing runs while ReturnClubs is off, so the default costs one bool check per club per frame.
    public partial class BillyClubsMod
    {
        internal static MelonPreferences_Entry<bool> ReturnClubs;
        internal static MelonPreferences_Entry<float> ReturnSeconds;

        const float RestSpeed = 0.15f;  // m/s
        const float RestTime = 0.2f;    // s below RestSpeed = at rest
        const float MaxRoll = 10f;      // a club still moving this long after release counts as at rest (fell out of the map)

        static void InitReturnPrefs(MelonPreferences_Category c)
        {
            ReturnClubs = c.CreateEntry("ReturnClubs", false, description: "A club you threw or dropped goes back to its holster on your belt by itself, like the game's knives do. Off = clubs stay where they land (F8 or the arsenal terminal bring them back).");
            ReturnSeconds = c.CreateEntry("ReturnSeconds", 10f, description: "How long a club lies where it landed before it returns to its holster, in seconds. Counted from the moment it comes to rest. Only used when ReturnClubs is on.");
        }

        // Every frame, for every club (OnUpdate).
        static void WatchReturn(Club k, bool held)
        {
            if (!ReturnClubs.Value) return;
            if (held || k.In != null || k.Fly != null || k.Floating || !Alive(k.Rb) || IsHandHeld(k) || OnBack(k.Go))
            {
                k.LooseAt = k.RestAt = -99f; k.StillFor = 0f;   // in a hand, a holster or the air: nothing is counting
                return;
            }
            if (k.LooseAt < 0f) k.LooseAt = Time.time;
            float speed = k.Rb.isKinematic ? 0f : k.Rb.linearVelocity.magnitude;
            if (speed >= RestSpeed) { k.StillFor = 0f; k.RestAt = -99f; }
            else k.StillFor += Time.deltaTime;
            if (k.RestAt < 0f && (k.StillFor >= RestTime || Time.time - k.LooseAt >= MaxRoll)) k.RestAt = Time.time;
            if (k.RestAt < 0f || Time.time - k.RestAt < Mathf.Max(0f, ReturnSeconds.Value)) return;

            float lay = Time.time - k.RestAt;
            // Drawn from a back holster: back it goes (VR Holster Customization remembers the side), like a katana.
            if (FwReturnHome(k.Go))
            {
                EndFlight(k, null, null); k.Held = false;
                Log.Msg($"club returned to its back holster ({lay:0.#} s after it came to rest)");
                return;
            }
            var free = FreeSlot();
            if (free == null) { k.RestAt = Time.time; return; }   // no belt yet (or both slots taken): ask again later
            SendToSlot(k, free);
            SaveSlots();
            Log.Msg($"club returned to the {free.Name} holster ({lay:0.#} s after it came to rest)");
        }
    }
}
