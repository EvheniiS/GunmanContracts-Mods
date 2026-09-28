using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BillyClubs
{
    // Daredevil gloves: recolours the player's gloves.
    //
    // Every glove in the game (VR hands, their LODs, the hand-poser previews, the cutscene arms) uses one
    // URP Lit material, `fps_vr_glove`: a near-grey leather atlas (sRGB ~50-140) multiplied by
    // _BaseColor #414141. The sleeves are a separate material (`fps_vr_glove_arms`), so changing this
    // material's colour recolours the gloves and nothing else. The material is a shared asset: tinting it
    // once also covers hands instantiated later. Scenes can reload it, so it is re-tinted on every scene
    // load, plus once a few seconds later for runtime copies ("fps_vr_glove (Instance)").
    public partial class BillyClubsMod
    {
        const string GloveMaterial = "fps_vr_glove";
        static readonly HashSet<int> TintedGloves = new();
        static float GloveRescanAt = -1f;

        static void TintGloves()
        {
            var hex = GloveColor.Value?.Trim() ?? "";
            if (hex.Length == 0 || hex.Equals("off", System.StringComparison.OrdinalIgnoreCase)) return;
            if (!ColorUtility.TryParseHtmlString(hex, out var tint))
            {
                Log.Warning($"GloveColor '{hex}' is not #RRGGBB; gloves left as they are");
                return;
            }
            tint.a = 1f;
            int found = 0, tinted = 0;
            Color game = default;
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()))
            {
                var m = o.TryCast<Material>();
                if (m == null) continue;
                var name = m.name;
                if (name != GloveMaterial && !name.StartsWith(GloveMaterial + " (")) continue;
                found++;
                var id = m.GetInstanceID();
                if (TintedGloves.Contains(id) && m.HasProperty("_BaseColor") && m.GetColor("_BaseColor") == tint) continue;
                if (m.HasProperty("_BaseColor")) { if (tinted == 0) game = m.GetColor("_BaseColor"); m.SetColor("_BaseColor", tint); }
                if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
                TintedGloves.Add(id);
                tinted++;
            }
            if (tinted > 0)
                Log.Msg($"gloves: {tinted} material(s) tinted {hex} (was {Col(game)}), {found} found");
            else if (found == 0 && Dbg)
                Log.Msg("gloves: no fps_vr_glove material loaded yet");
        }
    }
}
