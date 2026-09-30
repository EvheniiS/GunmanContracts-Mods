using System.Collections.Generic;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core.Grabbers;

namespace BillyClubs
{
    public partial class BillyClubsMod
    {
        // HVR's primary button maps to A on the right controller and X on the left.
        // Keep per-hand edge state so a held button produces only one door-kick attempt.
        static readonly Dictionary<System.IntPtr, bool> DoorButtonWasDown = new();

        static void UpdateDoorKicks()
        {
            if (!ClubDoorKick.Value)
            {
                DoorButtonWasDown.Clear();
                return;
            }

            var game = ANBStaticGameManager.ANBmain;
            if (!Alive(game)) return;

            foreach (var club in Clubs)
            {
                if (!Alive(club.Go) || !club.Held || !Alive(club.Grab)) continue;

                try
                {
                    var grabber = club.Grab.PrimaryGrabber;
                    var hand = Alive(grabber) ? grabber.TryCast<HVRHandGrabber>() : null;
                    if (!Alive(hand) || !Alive(hand.Controller)) continue;

                    bool down = hand.Controller.PrimaryButtonState.Active;
                    bool wasDown;
                    DoorButtonWasDown.TryGetValue(hand.Pointer, out wasDown);
                    DoorButtonWasDown[hand.Pointer] = down;
                    if (!down || wasDown) continue;

                    var aim = hand.ControllerHandTarget;
                    if (!Alive(aim))
                    {
                        if (Dbg) Log.Warning($"{(hand.IsLeftHand ? "left" : "right")} club door kick skipped: no controller aim transform");
                        continue;
                    }

                    bool kicked = game.TryKickDoor(aim.position, aim.forward);
                    if (kicked || Dbg)
                    {
                        string side = hand.IsLeftHand ? "left" : "right";
                        Log.Msg(kicked
                            ? $"{side} club door kick: opened marked door"
                            : $"{side} club door kick: no marked door in range (VR door kick {(game.useVRDoorKick ? "on" : "off")})");
                    }
                }
                catch (System.Exception e)
                {
                    if (Dbg) Log.Warning($"club door kick failed: {e.GetType().Name}: {e.Message}");
                }
            }
        }
    }
}
