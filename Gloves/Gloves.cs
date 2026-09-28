using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(Gloves.GlovesMod), "Gloves", "0.1.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace Gloves
{
    // Recolours the player's gloves. Default dark red (Daredevil); any #RRGGBB from the config or the Mod Settings
    // board, applied at once. #414141 is the game's own grey.
    //
    // Every glove in the game (VR hands, their LODs, the hand-poser previews, the cutscene arms) uses one URP Lit
    // material, `fps_vr_glove`: a near-grey leather atlas (sRGB ~50-140) multiplied by _BaseColor #414141. The sleeves
    // are a separate material (`fps_vr_glove_arms`), so changing this material's colour recolours the gloves and
    // nothing else. The material is a shared asset: tinting it once also covers hands instantiated later. Scenes can
    // reload it, so it is re-tinted on every scene load, plus once a few seconds later for runtime copies
    // ("fps_vr_glove (Instance)"). (Moved out of Billy Clubs / Daredevil 0.2.0, where it was `GloveColor`.)
    public class GlovesMod : MelonMod
    {
        const string GloveMaterial = "fps_vr_glove";
        static MelonPreferences_Entry<string> Color;
        static MelonLogger.Instance Log;
        static readonly Dictionary<int, UnityEngine.Color> Tinted = new();   // material id -> colour we last set
        static float RescanAt = -1f;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("Gloves", "Gloves");
            Color = c.CreateEntry("Color", "#8A0F0F", description: "Colour of the player's gloves as #RRGGBB, applied at once. Default #8A0F0F (Daredevil dark red); #414141 is the game's own grey. It multiplies the game's grey leather texture, so the gloves look much darker than the colour itself.");
            Color.OnEntryValueChanged.Subscribe((_, _) => Tint("changed"));
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            Tint(null);
            RescanAt = Time.time + 5f;
        }

        public override void OnUpdate()
        {
            if (RescanAt > 0 && Time.time >= RescanAt) { RescanAt = -1f; Tint(null); }
        }

        static void Tint(string why)
        {
            try
            {
                var hex = Color.Value?.Trim() ?? "";
                if (!ColorUtility.TryParseHtmlString(hex, out var tint))
                {
                    Log.Warning($"Color '{hex}' is not #RRGGBB; gloves left as they are");
                    return;
                }
                tint.a = 1f;
                int found = 0, tinted = 0;
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()))
                {
                    var m = o.TryCast<Material>();
                    if (m == null) continue;
                    var name = m.name;
                    if (name != GloveMaterial && !name.StartsWith(GloveMaterial + " (")) continue;
                    found++;
                    var id = m.GetInstanceID();
                    if (Tinted.TryGetValue(id, out var was) && was == tint && m.HasProperty("_BaseColor") && m.GetColor("_BaseColor") == tint) continue;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
                    Tinted[id] = tint;
                    tinted++;
                }
                if (tinted > 0 || why != null) Log.Msg($"gloves: {tinted} material(s) tinted {hex}{(why != null ? " (" + why + ")" : "")}, {found} found");
            }
            catch (Exception e) { Log.Error($"tint: {e.Message}"); }
        }
    }
}
