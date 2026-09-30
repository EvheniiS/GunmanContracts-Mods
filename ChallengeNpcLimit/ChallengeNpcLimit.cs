using Il2Cpp;
using MelonLoader;

[assembly: MelonInfo(typeof(ChallengeNpcLimit.ChallengeNpcLimitMod), "Challenge NPC Limit", "0.1.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace ChallengeNpcLimit
{
    public class ChallengeNpcLimitMod : MelonMod
    {
        internal static MelonPreferences_Entry<bool> Enabled;
        internal static MelonPreferences_Entry<int> MaxEnemies;

        public override void OnInitializeMelon()
        {
            var category = MelonPreferences.CreateCategory("ChallengeNpcLimit", "Challenge NPC Limit");
            Enabled = category.CreateEntry("Enabled", true, description: "Raise the enemy-count selection limit in takedown challenges.");
            MaxEnemies = category.CreateEntry("MaxEnemies", 60, description: "Maximum selectable enemies in takedown challenges. Must be greater than 30; existing higher game limits are preserved.");
            LoggerInstance.Msg($"loaded - challenge enemy selection limit {MaxEnemies.Value}");
        }

        internal static void RaiseLimit(ANBContractData data)
        {
            if (Enabled == null || !Enabled.Value || data == null || !data.takedownAvailable)
                return;

            int limit = MaxEnemies.Value;
            if (limit > 30 && data.maxTakedownEnemies < limit)
                data.maxTakedownEnemies = limit;
        }
    }

    // The terminal's + button and updateContractInfo both clamp CD_totalSpawns to this field.
    [HarmonyLib.HarmonyPatch(typeof(ANBContractData), nameof(ANBContractData.Awake))]
    internal static class ContractDataAwakePatch
    {
        static void Postfix(ANBContractData __instance) => ChallengeNpcLimitMod.RaiseLimit(__instance);
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBContractTerminal), nameof(ANBContractTerminal.updateContractInfo))]
    internal static class UpdateContractInfoPatch
    {
        static void Prefix(ANBContractTerminal __instance) => ChallengeNpcLimitMod.RaiseLimit(__instance.contractData);
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBContractTerminal), nameof(ANBContractTerminal.buttonEnemiesAdd))]
    internal static class AddEnemyPatch
    {
        static void Prefix(ANBContractTerminal __instance) => ChallengeNpcLimitMod.RaiseLimit(__instance.contractData);
    }
}
