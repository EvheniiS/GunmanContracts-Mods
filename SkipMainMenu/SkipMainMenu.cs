using System;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(SkipMainMenu.SkipMainMenuMod), "Skip Main Menu", "0.2.2", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace SkipMainMenu
{
    // The game boots into the tiny GameLoader scene; ANBChangeMap (there) loads MainMenu, and Start Game later loads The_Range_001.
    // Direct mode: ANBChangeMap.overrideMap = The Range, so the loader loads it first and MainMenu never loads (one load screen).
    // Fallback mode: press Start Game (ANBUIManager.StartGame) once the first main menu is ready.
    // Every step is written to UserData/SkipMainMenu/timeline.txt (one line per event, time = seconds since the process started).
    public class SkipMainMenuMod : MelonMod
    {
        internal static MelonPreferences_Entry<bool> Enabled, DirectToRange, Timeline;
        internal static MelonPreferences_Entry<string> RangeScene;
        internal static MelonPreferences_Entry<float> FallbackSeconds;

        internal static bool menuUnlocked;     // the game's own "menu is interactive" call (ANBGameLogic.unlockMainMenu) was seen
        internal static bool done;
        internal static bool overrideSet;      // we put The Range into ANBChangeMap.overrideMap; it must be cleared once that load is done
        static float readySince = -1f;
        static string lastState = "";
        static string path;
        static MelonLogger.Instance Log;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("SkipMainMenu", "Skip Main Menu");
            Enabled = c.CreateEntry("Enabled", true, description: "Skip the main menu when the game launches and go to The Range. Returning to the main menu later (phone) is left alone.");
            DirectToRange = c.CreateEntry("DirectToRange", true, description: "Make the game's boot loader load The Range instead of the main menu: one loading screen. If off (or if it fails) the mod presses Start Game once the main menu is ready instead.");
            RangeScene = c.CreateEntry("RangeScene", "The_Range_001", description: "Scene name of The Range, as the game logs it.");
            FallbackSeconds = c.CreateEntry("FallbackSeconds", 6f, description: "Start Game fallback only: if the game never reports the menu unlocked, press Start Game this many seconds after the menu scene is ready.");
            Timeline = c.CreateEntry("Timeline", true, description: "Write UserData/SkipMainMenu/timeline.txt: scene loads, loader and menu calls with times, readiness changes.");

            try
            {
                string dir = Path.Combine(MelonEnvironment.UserDataDirectory, "SkipMainMenu");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "timeline.txt");
                File.WriteAllText(path, $"# Skip Main Menu 0.2.2 timeline, {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            }
            catch { path = null; }

            Mark("init");
        }

        internal static void Mark(string what)
        {
            if (Timeline == null || !Timeline.Value || path == null) return;
            try { File.AppendAllText(path, $"{Time.realtimeSinceStartup,7:0.00}  {what}\n"); } catch { }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            Mark($"scene init '{sceneName}' ({buildIndex})");
            // Anything past the loader/menu (the Range, a contract) means the launch-time skip is over for good.
            if (sceneName != "GameLoader" && sceneName != "MainMenu") done = true;
            // The loader reads overrideMap on every map load, so leaving it set would send "Return to Main Menu" back to the Range.
            if (overrideSet && sceneName != "GameLoader")
            {
                try
                {
                    var mc = ANBStaticGameManager.MapChanger;
                    if (mc != null) { mc.overrideMap = ""; overrideSet = false; Mark("overrideMap cleared"); }
                    else Mark("overrideMap NOT cleared: no MapChanger");
                }
                catch (Exception ex) { Mark($"clear override failed: {ex.Message}"); }
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName) => Mark($"scene loaded '{sceneName}'");
        public override void OnSceneWasUnloaded(int buildIndex, string sceneName) => Mark($"scene unloaded '{sceneName}'");

        public override void OnUpdate()
        {
            try
            {
                var gm = ANBStaticGameManager.ANBmain;
                bool main = gm != null && gm.IsMainMenuScene;
                bool ui = gm != null && gm.UIManager != null;
                bool anim = gm != null && gm.MainMenuAnim != null;
                bool fx = gm != null && gm.CameraEffects != null;
                bool loading = ANBStaticGameManager.loadingMap;
                bool inbound = ui && gm.UIManager.startInbound;
                string st = $"state main={B(main)} ui={B(ui)} anim={B(anim)} fx={B(fx)} loading={B(loading)} inbound={B(inbound)} unlocked={B(menuUnlocked)}";
                if (st != lastState) { lastState = st; Mark(st); }

                // Start Game press: only for the first main menu, and in direct mode only if MainMenu got loaded anyway.
                if (done || !Enabled.Value || !main) { readySince = -1f; return; }
                if (!ui || !anim || !fx || loading) { readySince = -1f; return; }

                if (readySince < 0f) readySince = Time.unscaledTime;
                bool timedOut = Time.unscaledTime - readySince >= FallbackSeconds.Value;
                if (!menuUnlocked && !timedOut) return;

                done = true;
                Mark(menuUnlocked ? "press Start Game (menu unlocked)" : $"press Start Game (fallback after {FallbackSeconds.Value:0.#} s)");
                gm.UIManager.StartGame();
            }
            catch (Exception ex)
            {
                done = true;
                Mark($"gave up: {ex.Message}");
                Log.Warning($"gave up: {ex.Message}");
            }
        }

        static string B(bool v) => v ? "1" : "0";
    }

    // --- direct mode: the loader's coroutine reads ANBChangeMap.overrideMap (non-empty wins over the default map) ---
    [HarmonyPatch(typeof(ANBChangeMap), "Awake")]
    static class LoaderAwake
    {
        static void Prefix(ANBChangeMap __instance)
        {
            try
            {
                string scene = __instance.gameObject.scene.name;
                SkipMainMenuMod.Mark($"ANBChangeMap.Awake in '{scene}' override='{__instance.overrideMap}'");
                if (!SkipMainMenuMod.Enabled.Value || !SkipMainMenuMod.DirectToRange.Value || scene != "GameLoader") return;
                if (!string.IsNullOrEmpty(__instance.overrideMap)) return;   // a playtest override is already set: leave it
                __instance.overrideMap = SkipMainMenuMod.RangeScene.Value;
                SkipMainMenuMod.overrideSet = true;
                SkipMainMenuMod.Mark($"overrideMap -> '{__instance.overrideMap}'");
            }
            catch (Exception ex) { SkipMainMenuMod.Mark($"override failed: {ex.Message}"); }
        }
    }

    // --- timeline-only patches ---
    [HarmonyPatch(typeof(ANBChangeMap), "Start")] static class LoaderStart { static void Postfix() => SkipMainMenuMod.Mark("ANBChangeMap.Start"); }
    [HarmonyPatch(typeof(ANBChangeMap), "PreLoadMap")] static class LoaderPre { static void Postfix() => SkipMainMenuMod.Mark("ANBChangeMap.PreLoadMap (loading screen up)"); }
    [HarmonyPatch(typeof(ANBChangeMap), "PostLoadMap")] static class LoaderPost { static void Postfix() => SkipMainMenuMod.Mark("ANBChangeMap.PostLoadMap (loading screen down)"); }
    [HarmonyPatch(typeof(ANBStaticGameManager), "LoadMap")] static class StaticLoadMap { static void Prefix(string mapname) => SkipMainMenuMod.Mark($"ANBStaticGameManager.LoadMap('{mapname}')"); }
    [HarmonyPatch(typeof(ANBGameLogic), "LoadMap")] static class LogicLoadMap { static void Prefix(string mapname, float wait) => SkipMainMenuMod.Mark($"ANBGameLogic.LoadMap('{mapname}', {wait:0.##})"); }
    [HarmonyPatch(typeof(ANBGameLogic), "Start")] static class LogicStart { static void Postfix(ANBGameLogic __instance) => SkipMainMenuMod.Mark($"ANBGameLogic.Start (mainMenu={__instance.IsMainMenuScene} range={__instance.IsRangeScene})"); }
    [HarmonyPatch(typeof(ANBGameLogic), "MainMenuShow")] static class MenuShow { static void Postfix() => SkipMainMenuMod.Mark("ANBGameLogic.MainMenuShow"); }
    [HarmonyPatch(typeof(ANBGameLogic), "MainMenuHide")] static class MenuHide { static void Postfix() => SkipMainMenuMod.Mark("ANBGameLogic.MainMenuHide"); }
    [HarmonyPatch(typeof(ANBGameLogic), "PlayerStartGame")] static class PlayerStart { static void Postfix() => SkipMainMenuMod.Mark("ANBGameLogic.PlayerStartGame"); }
    [HarmonyPatch(typeof(ANBUIManager), "Start")] static class UiStart { static void Postfix() => SkipMainMenuMod.Mark("ANBUIManager.Start"); }
    [HarmonyPatch(typeof(ANBUIManager), "StartGame")] static class UiStartGame { static void Prefix() => SkipMainMenuMod.Mark("ANBUIManager.StartGame"); }

    [HarmonyPatch(typeof(ANBGameLogic), "unlockMainMenu")]
    static class MenuUnlock
    {
        static void Postfix() { SkipMainMenuMod.menuUnlocked = true; SkipMainMenuMod.Mark("ANBGameLogic.unlockMainMenu (menu interactive)"); }
    }
}
