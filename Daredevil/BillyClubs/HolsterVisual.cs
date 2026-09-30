using System.Runtime.CompilerServices;
using Il2CppHurricaneVR.Framework.Core.Sockets;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BillyClubs
{
    // A visible holster for each club slot: a plain vertical tube, highlighted like the game's own holsters.
    // The game's holsters (HVRANBSocketHoverFade on the belt) fade a ghost mesh's material colour between
    // colorInvisible, colorNormal and colorHover. The mod copies that material and those colours from the player's
    // own holster, so the tubes match: hidden until you hold a club, shown while you do, bright when the club is
    // close enough to snap in. The club itself fills the slot when holstered, so the tube hides then.
    public partial class BillyClubsMod
    {
        internal static MelonPreferences_Entry<string> HolsterGhost;

        const float TubeLength = 0.42f, TubeRadius = 0.026f, TubeDown = 0.08f; // sheath over the lower (tip) part
        static Material GhostSource;
        static Color GhostInvisible = new(1f, 0.6f, 0.1f, 0f), GhostNormal = new(1f, 0.6f, 0.1f, 0.12f), GhostHover = new(1f, 0.6f, 0.1f, 0.45f);
        static float GhostFade = 0.15f;

        static void InitHolsterPrefs(MelonPreferences_Category c)
        {
            HolsterGhost = c.CreateEntry("HolsterGhost", "Hover", description: "Club holster tubes on the belt: Hover (like the game's holsters: shown while you hold a club, bright when it's close enough to snap), Always (always faintly visible when empty), Off.");
        }

        class Ghost { public Renderer Rend; public Material Mat; public Color Cur; }

        // Called from FindBelt, after the slot anchors exist.
        static void BuildGhosts()
        {
            if (HolsterGhost.Value == "Off") return;
            ReadGameHighlight();
            foreach (var s in Slots)
            {
                if (!Alive(s.Anchor)) continue;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                var col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                go.name = "BillyClubHolsterTube";
                go.layer = s.Anchor.gameObject.layer;
                var t = go.transform;
                t.SetParent(s.Anchor, false);
                // The club hangs tip down with its centre on the anchor; the cylinder's own axis is Y (2 m x 1 m).
                t.localPosition = Vector3.down * TubeDown;
                t.localRotation = Quaternion.identity;
                t.localScale = new Vector3(TubeRadius * 2f, TubeLength * 0.5f, TubeRadius * 2f);
                var rend = go.GetComponent<Renderer>();
                var mat = Alive(GhostSource) ? new Material(GhostSource) : FallbackGhostMaterial();
                rend.sharedMaterial = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                s.Ghost = new Ghost { Rend = rend, Mat = mat, Cur = GhostInvisible };
                SetGhostColor(s.Ghost, GhostInvisible);
            }
        }

        static void ReadGameHighlight()
        {
            if (Alive(GhostSource) || !Alive(Belt)) return;
            GhostSource = null;
            foreach (var f in Belt.root.GetComponentsInChildren<HVRANBSocketHoverFade>(true))
            {
                if (f == null || f.rend == null || f.rend.sharedMaterial == null) continue;
                GhostSource = f.rend.sharedMaterial;
                GhostInvisible = f.colorInvisible; GhostNormal = f.colorNormal; GhostHover = f.colorHover;
                if (f.duration > 0f) GhostFade = f.duration;
                Log.Msg($"holster tubes use the game's holster highlight from '{Path(f.transform)}': material '{GhostSource.name}' shader '{GhostSource.shader.name}', " +
                        $"normal {Col(GhostNormal)} hover {Col(GhostHover)} invisible {Col(GhostInvisible)}, fade {GhostFade:0.##} s");
                return;
            }
            Log.Warning("no HVRANBSocketHoverFade on the player rig - holster tubes use a plain orange see-through material");
        }

        static Material FallbackGhostMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(sh);
            // URP Unlit, transparent, alpha blended.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            return m;
        }

        static void SetGhostColor(Ghost g, Color c)
        {
            g.Cur = c;
            var shown = HolsterPresent ? TintClubColor(c) : c;
            g.Mat.color = shown; // what the game's HVRANBSocketHoverFade sets
            // The game's 'Rim Dissolve' hologram is additive: every colour has alpha 0 and "invisible" is black, so
            // visibility is the brightness, not the alpha (0.9.0 hid the tubes for good by testing alpha).
            g.Rend.enabled = Mathf.Max(Mathf.Max(shown.r, shown.g), Mathf.Max(shown.b, shown.a)) > 0.005f;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static Color TintClubColor(Color c) => VRHolsterCustomization.HolsterColors.Tint(c);

        // Every frame: which state each tube should be in.
        static void UpdateGhosts()
        {
            bool any = false;
            foreach (var s in Slots) if (s.Ghost != null && Alive(s.Ghost.Rend)) { any = true; break; }
            if (!any) return;

            float nearest = float.MaxValue; Slot near = null; bool holding = false;
            foreach (var k in Clubs)
            {
                if (!Alive(k.Go) || k.In != null || !IsHandHeld(k)) continue;
                holding = true;
                var t = k.Go.transform;
                float half = Length.Value * 0.5f;
                var a = t.TransformPoint(ClubCenter - ClubAxis * half);
                var b = t.TransformPoint(ClubCenter + ClubAxis * half);
                foreach (var s in Slots)
                {
                    if (!Alive(s.Anchor) || Alive(s.Club?.Go)) continue;
                    float d = DistToSegment(s.Anchor.position, a, b);
                    if (d < nearest) { nearest = d; near = s; }
                }
            }
            if (nearest > SnapDistance.Value) near = null;

            float step = 1f - Mathf.Exp(-Time.deltaTime * 3f / Mathf.Max(GhostFade, 0.02f));
            foreach (var s in Slots)
            {
                var g = s.Ghost;
                if (g == null || !Alive(g.Rend)) continue;
                bool full = Alive(s.Club?.Go);
                Color want = full ? GhostInvisible
                    : s == near ? GhostHover
                    : holding || HolsterGhost.Value == "Always" ? GhostNormal
                    : GhostInvisible;
                SetGhostColor(g, Color.Lerp(g.Cur, want, step));
            }
        }

        static string Col(Color c) => $"({c.r:0.##}, {c.g:0.##}, {c.b:0.##}, {c.a:0.##})";
    }
}
