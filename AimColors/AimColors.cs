using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppVLB;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(AimColors.AimColorsMod), "Aim Colors", "0.1.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace AimColors
{
    // Recolours two things on the player's weapons, both live from the Mod Settings board:
    //
    // 1. The LASER (beam + dot) of the laser/light attachment. Each weapon has its own copy of
    //    Attachments/Originals/flashlight/attachment_pistol_lightlaser/lasersource/laserbeam, holding one very bright
    //    1-degree spot Light (default (1, 0.21, 0.25), intensity 700000: the dot) and a VolumetricLightBeamSD with
    //    colorFromLight = true (the beam). Its shader is instanced, so tinting the material does nothing: we set the
    //    Light colour, the beam's own colour, and optionally a brightness multiplier. (Attachment Probe dump, Oct 2 2026.)
    //
    // 2. The IRON SIGHTS (the default sights on pistols and rifles). They are SpriteRenderers using one shared URP Lit
    //    material, `sights`, with no texture: the colour is _BaseColor (1, 0.69, 0.23) plus an orange _EmissionColor.
    //    The material is shared by every gun, so tinting it once covers all guns (enemy guns too).
    //
    // 3. The COLLIMATOR / red-dot RETICLE (sight slot AT3, "Reflexsight", object `holosight`). Its dot is an unlit shader,
    //    Vashchuk/RedDot(Unlit_Fixed), with _RedDotColor (0.91, 0.6, 0.18), a dot texture and _RedDotSize, one material per
    //    sight type ("RedDot Sight Glass Pistols", "... Howler 2"). We set the colour (times a brightness) and scale the size.
    //
    // Weapons spawn and are enabled at runtime, so every couple of seconds new ones are found and anything whose
    // colour drifted is re-applied. Enemy lasers are left alone unless IncludeEnemies is on.
    public class AimColorsMod : MelonMod
    {
        class LaserRig { public Light[] Lights; public float[] LightIntensity; public VolumetricLightBeamSD[] Beams; public float[] BeamIntensity; }
        class SightOrig { public Color Base, Emission; public bool HasEmission; }
        class ReticleOrig { public Color Color; public float Size; }

        static MelonPreferences_Entry<string> LaserColor, SightColor, ReticleColor;
        static MelonPreferences_Entry<float> LaserBoost, SightBoost, ReticleBoost, ReticleSize;
        static MelonPreferences_Entry<bool> IncludeEnemies;
        static MelonLogger.Instance Log;
        static readonly Dictionary<int, LaserRig> Rigs = new();
        static readonly Dictionary<int, SightOrig> Sights = new();
        static readonly Dictionary<int, ReticleOrig> Reticles = new();
        static readonly Dictionary<int, List<Material>> ReticleMats = new();      // weapon id -> its red-dot materials
        static float NextScan;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("AimColors", "Aim Colors");
            LaserColor = c.CreateEntry("LaserColor", "#FF3640", description: "Colour of the weapon laser (beam and dot) as #RRGGBB, applied at once. Default #FF3640 is the game's own red.");
            LaserBoost = c.CreateEntry("LaserBoost", 5f, description: "Brightness multiplier for the laser beam and dot (0.2 to 8). 1 = the game's own brightness; the default 5 is much easier to see, even through wireless-stream compression.");
            SightColor = c.CreateEntry("SightColor", "#5A080A", description: "Colour of the default iron sights as #RRGGBB, applied at once. Default #5A080A is a dark red; #FFB03B is the game's own yellow-orange. Shared by every gun, enemy guns included.");
            SightBoost = c.CreateEntry("SightBoost", 1f, description: "Glow multiplier for the iron sights (0.2 to 8). 1 = the game's glow; higher makes them stand out, especially in a compressed stream.");
            ReticleColor = c.CreateEntry("ReticleColor", "#5A080A", description: "Colour of the collimator (red dot sight) reticle as #RRGGBB, applied at once. Default #5A080A is a dark red that the brightness setting turns into a vivid red; #E8992E is the game's own amber.");
            ReticleBoost = c.CreateEntry("ReticleBoost", 5f, description: "Brightness multiplier for the collimator reticle (0.2 to 8). 1 = the game's brightness; the default 5 makes the dark red default glow.");
            ReticleSize = c.CreateEntry("ReticleSize", 1.2f, description: "Size multiplier for the collimator reticle dot (0.3 to 4). 1 = the game's size.");
            IncludeEnemies = c.CreateEntry("IncludeEnemies", true, description: "Also recolour enemy weapons' lasers. Off leaves their lasers at the game's own look.");
            foreach (var e in new MelonPreferences_Entry[] { LaserColor, SightColor, ReticleColor, LaserBoost, SightBoost, ReticleBoost, ReticleSize, IncludeEnemies })
                e.OnEntryValueChangedUntyped.Subscribe((_, _) => NextScan = 0f);
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            Rigs.Clear();
            Sights.Clear();
            Reticles.Clear();
            ReticleMats.Clear();
            NextScan = Time.time + 2f;
        }

        public override void OnUpdate()
        {
            if (Time.time < NextScan) return;
            NextScan = Time.time + 2f;
            Scan();
        }

        static void Scan()
        {
            try
            {
                bool laserOk = Parse(LaserColor.Value, out var laser);
                bool sightOk = Parse(SightColor.Value, out var sight);
                bool reticleOk = Parse(ReticleColor.Value, out var reticle);
                float boost = Mathf.Clamp(LaserBoost.Value, 0.2f, 8f);
                float sightBoost = Mathf.Clamp(SightBoost.Value, 0.2f, 8f);
                float reticleBoost = Mathf.Clamp(ReticleBoost.Value, 0.2f, 8f);
                float reticleSize = Mathf.Clamp(ReticleSize.Value, 0.3f, 4f);
                int lasers = 0, sights = 0, dots = 0;
                foreach (var o in UnityEngine.Object.FindObjectsByType(Il2CppType.Of<ANBWeaponAttachments>(), FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var a = o.TryCast<ANBWeaponAttachments>();
                    if (a == null) continue;
                    if (laserOk && a.LaserBeam != null && (IncludeEnemies.Value || !IsEnemy(a.gameObject)))
                    {
                        int id = a.GetInstanceID();
                        if (!Rigs.TryGetValue(id, out var rig) || rig.Lights.Length == 0 || rig.Lights[0] == null)
                        {
                            var lights = a.LaserBeam.GetComponentsInChildren<Light>(true);
                            var beams = a.LaserBeam.GetComponentsInChildren<VolumetricLightBeamSD>(true);
                            rig = new LaserRig { Lights = lights, Beams = beams, LightIntensity = new float[lights.Length], BeamIntensity = new float[beams.Length] };
                            for (int i = 0; i < lights.Length; i++) rig.LightIntensity[i] = lights[i].intensity;      // the game's own values
                            for (int i = 0; i < beams.Length; i++) rig.BeamIntensity[i] = beams[i].intensityMultiplier;
                            Rigs[id] = rig;
                        }
                        if (ApplyLaser(rig, laser, boost)) lasers++;
                    }
                    if (sightOk && a.Defaultsight != null)
                        foreach (var sr in a.Defaultsight.GetComponentsInChildren<SpriteRenderer>(true))
                        {
                            if (sr == null) continue;
                            var m = sr.sharedMaterial;
                            if (m != null && ApplySight(m, sight, sightBoost)) sights++;
                        }
                    if (reticleOk) dots += ApplyReticles(a, reticle, reticleBoost, reticleSize);
                }
                if (lasers > 0) Log.Msg($"laser: {lasers} weapon(s) tinted {LaserColor.Value} x{boost:0.##}");
                if (sights > 0) Log.Msg($"sights: {sights} material(s) tinted {SightColor.Value} glow x{sightBoost:0.##}");
                if (dots > 0) Log.Msg($"reticle: {dots} material(s) set {ReticleColor.Value} x{reticleBoost:0.##} size x{reticleSize:0.##}");
            }
            catch (Exception e) { Log.Error($"scan: {e.Message}"); }
        }

        static bool Parse(string hex, out Color c)
        {
            hex = hex?.Trim() ?? "";
            if (!ColorUtility.TryParseHtmlString(hex, out c))
            {
                Log.Warning($"'{hex}' is not #RRGGBB; left as it is");
                return false;
            }
            c.a = 1f;
            return true;
        }

        static bool ApplyLaser(LaserRig rig, Color tint, float boost)
        {
            bool changed = false;
            for (int i = 0; i < rig.Lights.Length; i++)
            {
                var l = rig.Lights[i];
                if (l == null) continue;
                float want = rig.LightIntensity[i] * boost;
                if (l.color == tint && Mathf.Approximately(l.intensity, want)) continue;
                l.color = tint;
                l.intensity = want;
                changed = true;
            }
            for (int i = 0; i < rig.Beams.Length; i++)
            {
                var b = rig.Beams[i];
                if (b == null) continue;
                float want = rig.BeamIntensity[i] * boost;
                if (b.color == tint && Mathf.Approximately(b.intensityMultiplier, want)) continue;
                b.color = tint;
                b.intensityMultiplier = want;
                try { b.UpdateAfterManualPropertyChange(); } catch { }
                changed = true;
            }
            return changed;
        }

        // Sets _BaseColor to the tint and _EmissionColor to the tint scaled like the game's own (about half). A tint equal
        // to the material's original colour restores the original emission, so the default setting changes nothing.
        static bool ApplySight(Material m, Color tint, float glow)
        {
            if (!m.HasProperty("_BaseColor")) return false;
            int id = m.GetInstanceID();
            if (!Sights.TryGetValue(id, out var o))
            {
                o = new SightOrig { Base = m.GetColor("_BaseColor"), HasEmission = m.HasProperty("_EmissionColor") };
                if (o.HasEmission) o.Emission = m.GetColor("_EmissionColor");
                Sights[id] = o;
            }
            bool isOriginal = Mathf.Abs(tint.r - o.Base.r) < 0.01f && Mathf.Abs(tint.g - o.Base.g) < 0.01f && Mathf.Abs(tint.b - o.Base.b) < 0.01f;
            var wantBase = isOriginal ? o.Base : tint;
            float k = Mathf.Max(o.Base.r, o.Base.g, o.Base.b) > 0.001f ? Mathf.Max(o.Emission.r, o.Emission.g, o.Emission.b) / Mathf.Max(o.Base.r, o.Base.g, o.Base.b) : 0.5f;
            var e = isOriginal ? o.Emission : new Color(tint.r * k, tint.g * k, tint.b * k, 1f);
            var wantEmission = new Color(e.r * glow, e.g * glow, e.b * glow, 1f);
            bool changed = false;
            if (m.GetColor("_BaseColor") != wantBase)
            {
                m.SetColor("_BaseColor", wantBase);
                if (m.HasProperty("_Color")) m.SetColor("_Color", wantBase);
                changed = true;
            }
            if (o.HasEmission && m.GetColor("_EmissionColor") != wantEmission) { m.SetColor("_EmissionColor", wantEmission); changed = true; }
            return changed;
        }

        // The weapon's red-dot materials: every renderer under the weapon root whose material has _RedDotColor, found once per
        // weapon (inactive sight options included, so a sight fitted later is already tinted).
        static int ApplyReticles(ANBWeaponAttachments a, Color tint, float boost, float size)
        {
            int id = a.GetInstanceID();
            if (!ReticleMats.TryGetValue(id, out var mats) || (mats.Count > 0 && mats[0] == null))
            {
                mats = new List<Material>();
                var root = a.transform.parent != null ? a.transform.parent : a.transform;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    foreach (var m in r.sharedMaterials)
                        if (m != null && m.HasProperty("_RedDotColor") && !mats.Contains(m)) mats.Add(m);
                }
                ReticleMats[id] = mats;
            }
            int changed = 0;
            foreach (var m in mats)
            {
                if (m == null) continue;
                int mid = m.GetInstanceID();
                if (!Reticles.TryGetValue(mid, out var o))
                {
                    o = new ReticleOrig { Color = m.GetColor("_RedDotColor"), Size = m.HasProperty("_RedDotSize") ? m.GetFloat("_RedDotSize") : 1f };
                    Reticles[mid] = o;
                }
                bool isOriginal = Mathf.Abs(tint.r - o.Color.r) < 0.01f && Mathf.Abs(tint.g - o.Color.g) < 0.01f && Mathf.Abs(tint.b - o.Color.b) < 0.01f;
                var baseColor = isOriginal ? o.Color : tint;
                var want = new Color(baseColor.r * boost, baseColor.g * boost, baseColor.b * boost, o.Color.a);
                bool did = false;
                if (m.GetColor("_RedDotColor") != want) { m.SetColor("_RedDotColor", want); did = true; }
                if (m.HasProperty("_RedDotSize") && !Mathf.Approximately(m.GetFloat("_RedDotSize"), o.Size * size)) { m.SetFloat("_RedDotSize", o.Size * size); did = true; }
                if (did) changed++;
            }
            return changed;
        }

        static bool IsEnemy(GameObject g)
        {
            for (var t = g.transform; t != null; t = t.parent)
                if (t.name.StartsWith("enemy_")) return true;
            return false;
        }
    }
}
