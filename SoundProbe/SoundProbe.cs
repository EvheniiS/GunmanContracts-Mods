using System;
using System.IO;
using HarmonyLib;
using Il2CppHurricaneVR.Framework.Core.Utils;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(SoundProbe.SoundProbeMod), "Sound Probe", "0.1.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace SoundProbe;

// Diagnostic: writes the name of every clip the game's sound pool (HVR's SFXPlayer) plays, with the time and the 3D position,
// to UserData/SoundProbe/session-*.log. Used to find which clip a holster, a wall pickup or a hit makes, so another mod can
// play it by name or an audio replacer can swap it. It changes nothing. Sounds an AudioSource plays directly (music, some voices)
// do not go through the pool and are not seen.
public sealed class SoundProbeMod : MelonMod
{
    static MelonPreferences_Entry<bool> enabled;
    static MelonPreferences_Entry<string> hide, only;
    static StreamWriter output;
    static string[] hideList = Array.Empty<string>();
    static string onlyText = "";
    static int logged, hidden;
    static bool failed;

    public override void OnInitializeMelon()
    {
        var c = MelonPreferences.CreateCategory("SoundProbe", "Sound Probe");
        enabled = c.CreateEntry("Enabled", false, description: "Log every sound the game plays to UserData/SoundProbe/session-*.log. Off by default: turn on, do the thing, turn off.");
        hide = c.CreateEntry("Hide", "Footstep,UI Click", description: "Sounds whose clip name contains one of these words (comma separated, any case) are counted but not listed. Blank = list everything.");
        only = c.CreateEntry("Only", "", description: "List only sounds whose clip name contains this text. Blank = all.");
        hide.OnEntryValueChanged.Subscribe((_, v) => Parse());
        only.OnEntryValueChanged.Subscribe((_, v) => Parse());
        Parse();
    }

    // Opened on the first sound heard while enabled, so a switched-off probe leaves no empty files.
    static void Open()
    {
        if (output != null || failed) return;
        try
        {
            var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "SoundProbe");
            Directory.CreateDirectory(dir);
            output = new StreamWriter(Path.Combine(dir, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log")) { AutoFlush = true };
            output.WriteLine("time | clip | kind | x,y,z   (compare the time with the MelonLoader log)");
        }
        catch { failed = true; }
    }

    static void Parse()
    {
        hideList = (hide.Value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < hideList.Length; i++) hideList[i] = hideList[i].Trim();
        onlyText = (only.Value ?? "").Trim();
    }

    internal static void Heard(AudioClip clip, Vector3 pos, string kind)
    {
        if (enabled == null || !enabled.Value || clip == null) return;
        try
        {
            Open();
            if (output == null) return;
            var name = clip.name;
            if (onlyText.Length > 0 && name.IndexOf(onlyText, StringComparison.OrdinalIgnoreCase) < 0) return;
            foreach (var h in hideList)
                if (h.Length > 0 && name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) { hidden++; return; }
            logged++;
            output.WriteLine($"{DateTime.Now:HH:mm:ss.fff} | {name} | {kind} | {pos.x:0.#},{pos.y:0.#},{pos.z:0.#}");
        }
        catch { }
    }

    public override void OnApplicationQuit()
    {
        try { output?.WriteLine($"-- {logged} listed, {hidden} hidden by Hide"); output?.Dispose(); } catch { }
    }
}

[HarmonyPatch(typeof(SFXPlayer), nameof(SFXPlayer.PlaySFX))]
static class PlaySfxPatch
{
    static void Postfix(AudioClip clip, Vector3 position, string type) => SoundProbe.SoundProbeMod.Heard(clip, position, type ?? "sfx");
}

[HarmonyPatch(typeof(SFXPlayer), nameof(SFXPlayer.PlaySFXGunfire))]
static class PlaySfxGunfirePatch
{
    static void Postfix(AudioClip clip, Vector3 position) => SoundProbe.SoundProbeMod.Heard(clip, position, "gunfire");
}

[HarmonyPatch(typeof(SFXPlayer), nameof(SFXPlayer.PlaySFXRandomPitch))]
static class PlaySfxRandomPitchPatch
{
    static void Postfix(AudioClip clip, Vector3 position) => SoundProbe.SoundProbeMod.Heard(clip, position, "random pitch");
}
