using MelonLoader;

[assembly: MelonInfo(typeof(BillyClubs.BillyClubsMod), "Daredevil", "0.1.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace BillyClubs
{
    // The Daredevil package = Billy Clubs 0.11.0 (clubs, holsters, recall, ricochets, red gloves) + Radar Sense 0.3.1
    // (enemy silhouettes through walls, louder footsteps, club highlight) in one DLL. BillyClubsMod is the MelonMod;
    // Radar Sense runs from its hooks with its own log name. Settings stay in [BillyClubs] and [RadarSense].
    // Throw Assist (thrown pistols) is a separate required mod.
    public partial class BillyClubsMod
    {
        static partial void PackageInit() => RadarSense.RadarSenseMod.Init(new MelonLogger.Instance("Radar Sense"));
        static partial void PackageScene() => RadarSense.RadarSenseMod.Scene();
        static partial void PackageUpdate() => RadarSense.RadarSenseMod.Tick();
    }
}
