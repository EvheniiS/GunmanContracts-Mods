using System;
using MelonLoader;

[assembly: MelonInfo(typeof(BillyClubs.BillyClubsMod), "Daredevil", "0.4.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace BillyClubs
{
    // The Daredevil package: clubs (BillyClubs/: holsters, recall, ricochets, arsenal entry) + radar sense (RadarSense/:
    // enemy silhouettes through walls, louder footsteps, club highlight) in one DLL. BillyClubsMod is the MelonMod;
    // Radar Sense runs from its hooks with its own log name. Settings stay in [BillyClubs] and [RadarSense].
    // Companion mods are checked at startup. Weapon Framework supplies the arsenal terminal;
    // VR Holster Customization supplies the club back slots and hologram tint.
    public partial class BillyClubsMod
    {
        // Separate mods the package expects alongside it. Daredevil still runs without them; the log says what is missing.
        static readonly (string Assembly, string What)[] Required =
        {
            ("Gloves", "Gloves (dark red gloves)"),
            ("ThrowAssist", "Throw Assist (thrown pistols)"),
            ("ModSettings", "Mod Settings (the in-VR settings board)"),
            ("WeaponFramework", "Weapon Framework (clubs on the arsenal terminal)"),
            ("VRHolsterCustomization", "VR Holster Customization (clubs on your back)"),
        };

        static partial void PackageInit()
        {
            var missing = new System.Collections.Generic.List<string>();
            foreach (var (asm, what) in Required)
            {
                bool found = false;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) if (a.GetName().Name == asm) { found = true; break; }
                if (!found) missing.Add(what);
            }
            if (missing.Count > 0) MelonLogger.Warning($"[Daredevil] required mods not installed: {string.Join(", ", missing)} - put their DLLs in Mods");
            RadarSense.RadarSenseMod.Init(new MelonLogger.Instance("Radar Sense"));
        }
        static partial void PackageScene() => RadarSense.RadarSenseMod.Scene();
        static partial void PackageUpdate() => RadarSense.RadarSenseMod.Tick();
    }
}
