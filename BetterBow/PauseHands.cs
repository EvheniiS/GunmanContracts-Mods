using System;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core.Player;
using UnityEngine;
using static BetterBow.BetterBowMod;

namespace BetterBow
{
    // ---- Pause menu / phone Settings: log it, and undo what it leaves behind ------------------------
    // Not a bow feature, but it breaks the bow (hands drift away when moving with the left stick).
    //
    // ANBGameLogic.pauseGame(useHands, showMenu) is a toggle on Paused. In VR:
    //  * pausing reparents both physics hands (ANBUIManager.LeftHand / RightHand, HVRJointHand) onto
    //    their own Target, so they follow the controllers while timeScale is 0;
    //  * unpausing (only when useHands) reparents them to playerHealth.transform.parent - the player
    //    rig - which need not be where they were before the pause. A physics hand parented under the
    //    rig is dragged along by locomotion ON TOP of its joint pulling it to the controller, so it
    //    runs ahead of the player: the "hands move away when I move with the left stick" symptom.
    //  -> Remember each hand's parent when the game pauses; after the unpause put it back if it
    //     differs (HandsKeepParentAfterPause), and log both either way.
    //
    // The phone's Settings button is ANBSmartphone.showMainMenu: it returns at once while
    // switchingToMenu is set, sets it, and starts showMenuExec, which releases the phone, waits
    // 0.05 s, calls pauseGame(true, true) and only then clears switchingToMenu. If that coroutine
    // dies on the way (its object gets disabled, e.g. the phone going back to the wrist), the flag
    // stays set and every later press is ignored: "Settings stops responding".
    //  -> If the flag is still set a second after a press and the game hasn't paused, finish the
    //     coroutine's job: pauseGame(true, true) and clear the flag (PhoneSettingsRescue).
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
        internal static void BeforePause(ANBGameLogic game, bool useHands, bool showMenu)
        {
            bool pausing = !game.Paused;
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
