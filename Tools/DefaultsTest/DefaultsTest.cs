using MelonLoader;

#if NEWER
[assembly: MelonInfo(typeof(DefaultsTest.DefaultsTestMod), "Defaults Test", "1.1.0", "Evgeeso")]
#else
[assembly: MelonInfo(typeof(DefaultsTest.DefaultsTestMod), "Defaults Test", "1.0.0", "Evgeeso")]
#endif
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace DefaultsTest
{
    // Throwaway fixture for testing Mod Settings' "Updated defaults" page (Tools/DefaultsTest/Test-Defaults.ps1).
    // One source, two builds: 1.0.0 (old defaults) and 1.1.0 (-p:Variant=New, new defaults). Does nothing in the game.
    public class DefaultsTestMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            var c = MelonPreferences.CreateCategory("DefaultsTest", "Defaults Test");
#if NEWER
            c.CreateEntry("A_Untouched", 1, description: "Default 3 -> 1. Left at the old default: moves to 1 by itself, listed with Revert.");
            c.CreateEntry("B_Customised", 1, description: "Default 3 -> 1. The script sets your value to 2: kept, listed with Use new / Keep.");
            c.CreateEntry("C_Flag", true, description: "Default false -> true. Left at the old default: moves to true by itself, listed with Revert.");
            c.CreateEntry("D_Text", "new", description: "Default \"old\" -> \"new\". The script sets your value to \"mine\": kept, listed with Use new / Keep.");
            c.CreateEntry("E_Same", 5, description: "Default 5 in both versions. Must NOT appear on the page.");
            c.CreateEntry("F_AlreadyNew", 8, description: "Default 4 -> 8. The script sets your value to 8 already: nothing to decide, must NOT appear.");
            c.CreateEntry("G_NewInUpdate", 7, description: "Exists only in 1.1.0. Recorded silently, must NOT appear.");
#else
            c.CreateEntry("A_Untouched", 3, description: "Default 3 -> 1. Left at the old default: moves to 1 by itself, listed with Revert.");
            c.CreateEntry("B_Customised", 3, description: "Default 3 -> 1. The script sets your value to 2: kept, listed with Use new / Keep.");
            c.CreateEntry("C_Flag", false, description: "Default false -> true. Left at the old default: moves to true by itself, listed with Revert.");
            c.CreateEntry("D_Text", "old", description: "Default \"old\" -> \"new\". The script sets your value to \"mine\": kept, listed with Use new / Keep.");
            c.CreateEntry("E_Same", 5, description: "Default 5 in both versions. Must NOT appear on the page.");
            c.CreateEntry("F_AlreadyNew", 4, description: "Default 4 -> 8. The script sets your value to 8 already: nothing to decide, must NOT appear.");
#endif
        }
    }
}
