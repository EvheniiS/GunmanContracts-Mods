using System;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core.Player;
using UnityEngine;
using static BetterBow.BetterBowMod;
using Object = UnityEngine.Object;

namespace BetterBow
{
    // ---- Pause menu / phone Settings: log it, and undo what it leaves behind ------------------------
    // Not a bow feature, but it broke with the bow (hands drift away when moving with the left stick,
    // phone Settings does nothing).
    //
    // ANBGameLogic.pauseGame(useHands, showMenu) is a toggle on Paused. In VR, pausing first reparents
    // both physics hands (ANBUIManager.LeftHand / RightHand, HVRJointHand) onto their own Target so
    // they follow the controllers while timeScale is 0, then calls .gameObject on ANBwristHudLeft and
    // ANBwristHudRight, then sets timeScale, Paused and shows the menu. Unpausing puts the hands back
    // under the player rig. Log of Sep 24 2026: the pause threw at the wrist HUD step because the
    // slot held a destroyed ANBWristHud (taken by the dagger grip's cloned grip point, see
    // Quiver.MakeDaggerPoint). Result: hands stuck on the controllers with the game running (they
    // get dragged by locomotion on top of their joint pull), no menu, and the phone's
    // switchingToMenu never cleared, so Settings stays dead.
    //  -> Repair dead wrist HUD slots before a pause; put hands found on their Target outside a
    //     pause back under their pre-pause parent; check the parent after every unpause; finish a
    //     phone menu switch that never completed (PhoneSettingsRescue). Everything is logged.
    internal static class PauseHands
    {
        static Transform _leftBefore, _rightBefore;
        static string _leftBeforeName, _rightBeforeName;
        static ANBSmartphone _phone;
        static double _phonePressAt = -1;
        // drift monitor
        static double _leftFarSince = -1, _rightFarSince = -1, _nextDriftLog;

        internal static void OnScene()
        {
            _leftBefore = _rightBefore = null;
            _phone = null; _phonePressAt = -1;
            _leftFarSince = _rightFarSince = -1;
        }

        static HVRJointHand Hand(ANBGameLogic game, bool left)
        {
            var ui = game.UIManager;
            if (!U.Alive(ui)) return null;
            var h = left ? ui.LeftHand : ui.RightHand;
            return U.Alive(h) ? h : null;
        }

        internal static string PathOf(Transform t)
        {
            if (!U.Alive(t)) return "(none - scene root)";
            string p = t.name;
            var x = t.parent;
            for (int i = 0; i < 3 && U.Alive(x); i++, x = x.parent) p = x.name + "/" + p;
            return p;
        }

        // ---- pause / unpause -----------------------------------------------------------------------
        // Found in the Sep 24 log: pauseGame threw a NullReferenceException half-way (after moving the
        // hands onto their controllers, before Paused/timeScale/menu) because ANBwristHudRight was a
        // destroyed object - ANBWristHud.Awake on a cloned arrow grip point had taken the slot (fixed
        // at the source in Quiver.MakeDaggerPoint). The pause branch calls .gameObject on both wrist
        // HUDs without the liveness check the unpause branch has, so repair dead slots first.
        static void RepairWristHuds(ANBGameLogic game)
        {
            var l = game.ANBwristHudLeft; var r = game.ANBwristHudRight;
            bool deadL = l != null && !U.Alive(l), deadR = r != null && !U.Alive(r);
            if (!deadL && !deadR) return;
            // Each HUD knows its partner.
            ANBWristHud fixL = deadL && U.Alive(r) ? r.otherWristHud : null;
            ANBWristHud fixR = deadR && U.Alive(l) ? l.otherWristHud : null;
            if ((deadL && !U.Alive(fixL)) || (deadR && !U.Alive(fixR)))
                foreach (var o in Object.FindObjectsByType(Il2CppInterop.Runtime.Il2CppType.Of<ANBWristHud>(), FindObjectsSortMode.None))
                {
                    var w = o.TryCast<ANBWristHud>();
                    if (!U.Alive(w) || U.Alive(w.GetComponentInParent<Il2CppHurricaneVR.Framework.Weapons.Bow.HVRArrow>())) continue;
                    if (deadL && !U.Alive(fixL) && w.isLeft) fixL = w;
                    if (deadR && !U.Alive(fixR) && w.isRight) fixR = w;
                }
            if (deadL) game.ANBwristHudLeft = U.Alive(fixL) ? fixL : null;
            if (deadR) game.ANBwristHudRight = U.Alive(fixR) ? fixR : null;
            Log.Warning($"the game's {(deadL ? "left " : "")}{(deadR ? "right " : "")}wrist HUD slot held a destroyed object " +
                        $"(the pause would have crashed half-way) - repaired: left {U.Name(game.ANBwristHudLeft)}, right {U.Name(game.ANBwristHudRight)}");
        }

        internal static void BeforePause(ANBGameLogic game, bool useHands, bool showMenu)
        {
            bool pausing = !game.Paused;
            if (pausing) RepairWristHuds(game);
            var l = Hand(game, true); var r = Hand(game, false);
            if (pausing)
            {
                _leftBefore = U.Alive(l) ? l.transform.parent : null;
                _rightBefore = U.Alive(r) ? r.transform.parent : null;
                _leftBeforeName = PathOf(_leftBefore); _rightBeforeName = PathOf(_rightBefore);
            }
            Log.Msg($"game {(pausing ? "PAUSE" : "UNPAUSE")} (useHands {useHands}, showMenu {showMenu}) - hand parents: " +
                    $"left '{(U.Alive(l) ? PathOf(l.transform.parent) : "?")}', right '{(U.Alive(r) ? PathOf(r.transform.parent) : "?")}'");
        }

        internal static void AfterPause(ANBGameLogic game, bool wasPaused)
        {
            bool nowPaused = game.Paused;
            if (!wasPaused || nowPaused) return;                        // only after an unpause
            Check(game, true, _leftBefore, _leftBeforeName);
            Check(game, false, _rightBefore, _rightBeforeName);
            _phonePressAt = -1;
        }

        static void Check(ANBGameLogic game, bool left, Transform before, string beforeName)
        {
            var h = Hand(game, left);
            if (h == null || beforeName == null) return;
            var now = h.transform.parent;
            string side = left ? "left" : "right";
            bool same = U.Alive(before) ? U.Alive(now) && now.Pointer == before.Pointer : !U.Alive(now);
            if (same) { Log.Msg($"  {side} hand back under its pre-pause parent '{beforeName}'"); return; }
            if (!Settings.HandsKeepParentAfterPause.Value)
            {
                Log.Warning($"  {side} hand moved by the pause: was under '{beforeName}', now '{PathOf(now)}' (not restored: HandsKeepParentAfterPause is off)");
                return;
            }
            if (!U.Alive(before) && beforeName != "(none - scene root)")
            {
                Log.Warning($"  {side} hand moved by the pause: was under '{beforeName}' (gone now), now '{PathOf(now)}' - left as is");
                return;
            }
            h.transform.SetParent(before, true);
            Log.Warning($"  {side} hand moved by the pause: was under '{beforeName}', game put it under '{PathOf(now)}' - restored");
        }

        // ---- phone Settings --------------------------------------------------------------------------
        internal static void PhoneSettingsPressed(ANBSmartphone phone)
        {
            _phone = phone;
            bool stuck = phone.switchingToMenu;
            var game = ANBStaticGameManager.ANBmain;
            Log.Msg($"phone Settings pressed (held {phone.isHeld}, in wrist {phone.inWrist}, game paused {(game != null && game.Paused)})" +
                    (stuck ? " - IGNORED by the game: its previous menu switch never finished" : ""));
            if (!stuck) _phonePressAt = U.Now;
        }

        // ---- per frame -------------------------------------------------------------------------------
        internal static void Update()
        {
            var game = ANBStaticGameManager.ANBmain;
            if (game == null) return;
            try
            {
                UpdatePhone(game);
                RecoverStuckHands(game);
                UpdateDrift(game);
            }
            catch (Exception e) { if (U.Dbg) Log.Warning($"pause/hand check failed: {e.GetType().Name}: {e.Message}"); }
        }

        static void UpdatePhone(ANBGameLogic game)
        {
            if (_phonePressAt < 0 || U.Now - _phonePressAt < 1.0) return;
            _phonePressAt = -1;
            if (!U.Alive(_phone)) return;
            if (!_phone.switchingToMenu) return;                        // the game finished it itself
            if (game.Paused)
            {
                _phone.switchingToMenu = false;
                Log.Warning("phone menu switch left its flag set although the game is paused - cleared it");
                return;
            }
            Log.Warning($"phone menu switch never finished (the game's coroutine died) - " +
                        (Settings.PhoneSettingsRescue.Value ? "opening the menu and clearing the flag" : "not rescued: PhoneSettingsRescue is off"));
            if (!Settings.PhoneSettingsRescue.Value) return;
            _phone.switchingToMenu = false;
            game.pauseGame(true, true);
        }

        // A pause that crashed half-way leaves the hands parented to their own Target (the controller
        // pose) with the game running: the "hands move away when I use the stick" state. The game never
        // does that outside a pause, so undo it.
        static void RecoverStuckHands(ANBGameLogic game)
        {
            if (game.Paused || !Settings.HandsKeepParentAfterPause.Value) return;
            Unstick(game, true, _leftBefore, _leftBeforeName);
            Unstick(game, false, _rightBefore, _rightBeforeName);
        }

        static void Unstick(ANBGameLogic game, bool left, Transform before, string beforeName)
        {
            var h = Hand(game, left);
            if (h == null || !U.Alive(h.Target)) return;
            var p = h.transform.parent;
            if (!U.Alive(p) || p.Pointer != h.Target.Pointer) return;
            var to = U.Alive(before) ? before : game.playerHealth != null && U.Alive(game.playerHealth) ? game.playerHealth.transform.parent : null;
            if (!U.Alive(to)) return;
            h.transform.SetParent(to, true);
            Log.Warning($"{(left ? "left" : "right")} hand was left on its controller by an unfinished pause - moved back under '{PathOf(to)}'");
        }

        // Log when a physics hand stays far from the controller it follows - the visible symptom.
        static void UpdateDrift(ANBGameLogic game)
        {
            if (game.Paused) { _leftFarSince = _rightFarSince = -1; return; }
            DriftOf(game, true, ref _leftFarSince);
            DriftOf(game, false, ref _rightFarSince);
        }

        static void DriftOf(ANBGameLogic game, bool left, ref double farSince)
        {
            var h = Hand(game, left);
            if (h == null || !U.Alive(h.Target)) { farSince = -1; return; }
            float d = U.Dist(h.transform.position, h.Target.position);
            if (d < 0.30f) { farSince = -1; return; }
            if (farSince < 0) { farSince = U.Now; return; }
            if (U.Now - farSince < 1.0 || U.Now < _nextDriftLog) return;
            _nextDriftLog = U.Now + 10;
            Log.Warning($"{(left ? "left" : "right")} hand {d * 100:0} cm from its controller for {U.Now - farSince:0.0} s " +
                        $"(parent '{PathOf(h.transform.parent)}', game paused {game.Paused})");
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.pauseGame))]
    internal static class PauseGamePatch
    {
        static void Prefix(ANBGameLogic __instance, bool __0, bool __1, out bool __state)
        {
            __state = false;
            try { __state = __instance.Paused; PauseHands.BeforePause(__instance, __0, __1); }
            catch (Exception e) { Log?.Warning($"pause log failed: {e.Message}"); }
        }

        static void Postfix(ANBGameLogic __instance, bool __state)
        {
            try { PauseHands.AfterPause(__instance, __state); }
            catch (Exception e) { Log?.Warning($"pause hand check failed: {e.Message}"); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBSmartphone), nameof(ANBSmartphone.showMainMenu))]
    internal static class PhoneShowMainMenuPatch
    {
        static void Prefix(ANBSmartphone __instance)
        {
            try { PauseHands.PhoneSettingsPressed(__instance); } catch { }
        }
    }
}
