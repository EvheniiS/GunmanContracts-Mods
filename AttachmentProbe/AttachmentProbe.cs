using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppVLB;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[assembly: MelonInfo(typeof(AttachmentProbe.AttachmentProbeMod), "Attachment Probe", "0.3.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace AttachmentProbe
{
    // Diagnostic only: changes nothing. Press the key (default F9) in a scene with a weapon that has the laser and a
    // collimator sight fitted (laser switched ON is best). It writes UserData/AttachmentProbe/probe-*.txt with, for every
    // weapon attachment component: the slot names, the laser beam object, and every renderer/material under the laser
    // beam, the equipped sights and the LaserPoint dot decals (shader, colour/texture properties, enabled state).
    // Purpose: find out what a laser / red-dot recolour mod has to tint.
    public class AttachmentProbeMod : MelonMod
    {
        static MelonPreferences_Entry<string> Key, SightsKey;
        static MelonLogger.Instance Log;
        static StringBuilder sb;
        static HashSet<int> seenMats;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("AttachmentProbe", "Attachment Probe");
            Key = c.CreateEntry("Key", "F9", description: "Keyboard key that writes the attachment dump (Input System key name, e.g. F9).");
            SightsKey = c.CreateEntry("SightsKey", "F10", description: "Keyboard key that writes the sights dump: every weapon's sight slots (all options, equipped or not), scope camera and default sights.");
        }

        public override void OnUpdate()
        {
            try
            {
                var kb = Keyboard.current;
                if (kb == null) return;
                var key = kb.FindKeyOnCurrentKeyboardLayout(Key.Value) ?? kb[Key.Value]?.TryCast<UnityEngine.InputSystem.Controls.KeyControl>();
                if (key != null && key.wasPressedThisFrame) Dump();
                var key2 = kb.FindKeyOnCurrentKeyboardLayout(SightsKey.Value) ?? kb[SightsKey.Value]?.TryCast<UnityEngine.InputSystem.Controls.KeyControl>();
                if (key2 != null && key2.wasPressedThisFrame) DumpSights();
            }
            catch { }
        }

        static void Dump()
        {
            try
            {
                sb = new StringBuilder();
                seenMats = new HashSet<int>();
                var dir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "AttachmentProbe");
                Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, $"probe-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                sb.AppendLine($"# Attachment Probe 0.3.0, scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");

                int n = 0;
                foreach (var o in UnityEngine.Object.FindObjectsByType(Il2CppType.Of<ANBWeaponAttachments>(), FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var a = o.TryCast<ANBWeaponAttachments>();
                    if (a == null || !a.gameObject.activeInHierarchy) continue;   // only weapons that are in play
                    n++;
                    sb.AppendLine();
                    sb.AppendLine($"== ATTACHMENTS #{n} '{GoPath(a.gameObject)}' active={a.gameObject.activeInHierarchy} laserOn={a.laserOn} flashlightOn={a.flashlightOn} laserFlashlightCombined={a.laserFlashlightCombined} AT2={a.AT2}");
                    Obj("LaserBeam", a.LaserBeam, true);
                    Obj("LaserBeamSource", a.LaserBeamSource, false);
                    if (a.LaserBeamSource != null && a.LaserBeamSource.transform.parent != null)
                    {
                        sb.AppendLine("  -- laser module subtree (every object, its components):");
                        Tree(a.LaserBeamSource.transform.parent, 0);
                    }
                    Obj("Defaultsight", a.Defaultsight, true);
                    Obj("Flashlight", a.Flashlight, false);
                    Slot(1, a.AT1_Name, a.AT1_Objects);
                    Slot(2, a.AT2_Name, a.AT2_Objects);
                    Slot(3, a.AT3_Name, a.AT3_Objects);
                    Slot(4, a.AT4_Name, a.AT4_Objects);
                    Slot(5, a.AT5_Name, a.AT5_Objects);
                    Slot(6, a.AT6_Name, a.AT6_Objects);
                }
                if (n == 0) sb.AppendLine("no ANBWeaponAttachments found in the scene");

                int p = 0;
                foreach (var o in UnityEngine.Object.FindObjectsByType(Il2CppType.Of<LaserPoint>(), FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var lp = o.TryCast<LaserPoint>();
                    if (lp == null) continue;
                    p++;
                    sb.AppendLine();
                    sb.AppendLine($"== LASERPOINT #{p} '{GoPath(lp.gameObject)}' enabled={lp.enabled} active={lp.gameObject.activeInHierarchy} threshold={lp.Threshold}");
                    if (lp.Decal != null) Obj("Decal", lp.Decal.gameObject, true);
                    if (lp.StartPoint != null) sb.AppendLine($"  StartPoint '{GoPath(lp.StartPoint.gameObject)}'");
                }
                if (p == 0) sb.AppendLine("no LaserPoint components found");

                File.WriteAllText(path, sb.ToString());
                Log.Msg($"probe written: {path} ({n} attachment component(s), {p} laser point(s))");
            }
            catch (Exception e) { Log.Error($"dump: {e}"); }
        }

        // F10: sight-related parts of every weapon (inactive ones too), one weapon type once. Slot options are dumped even
        // when not equipped, so a collimator or scope can be inspected without fitting it.
        static void DumpSights()
        {
            try
            {
                sb = new StringBuilder();
                seenMats = new HashSet<int>();
                var dir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "AttachmentProbe");
                Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, $"sights-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                sb.AppendLine($"# Attachment Probe 0.3.0 sights dump, scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
                var seen = new HashSet<string>();
                int n = 0;
                foreach (var o in UnityEngine.Object.FindObjectsByType(Il2CppType.Of<ANBWeaponAttachments>(), FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var a = o.TryCast<ANBWeaponAttachments>();
                    if (a == null) continue;
                    var root = a.transform.parent != null ? a.transform.parent.name.Replace("(Clone)", "").Trim() : a.gameObject.name;
                    if (root.StartsWith("enemy_")) root = root.Substring(6);
                    if (!seen.Add(root)) continue;
                    n++;
                    sb.AppendLine();
                    sb.AppendLine($"== WEAPON '{root}' ('{GoPath(a.gameObject)}') useScopeCam={a.useScopeCam} AT3='{a.AT3_Name}' AT1='{a.AT1_Name}' AT4='{a.AT4_Name}'");
                    Obj("ScopeCam", a.ScopeCam, true);
                    Obj("ScopeZoomSource", a.ScopeZoomSource, false);
                    Obj("Defaultsight", a.Defaultsight, true);
                    SightSlot(3, a.AT3_Name, a.AT3_Objects);
                    SightSlot(1, a.AT1_Name, a.AT1_Objects);
                    SightSlot(4, a.AT4_Name, a.AT4_Objects);
                    SightSlot(5, a.AT5_Name, a.AT5_Objects);
                    SightSlot(6, a.AT6_Name, a.AT6_Objects);
                }
                File.WriteAllText(path, sb.ToString());
                Log.Msg($"sights probe written: {path} ({n} weapon type(s))");
            }
            catch (Exception e) { Log.Error($"sights dump: {e}"); }
        }

        static void SightSlot(int i, string name, Il2CppReferenceArray<GameObject> objs)
        {
            if (objs == null || objs.Length == 0) { sb.AppendLine($"-- slot AT{i} '{name}': none"); return; }
            sb.AppendLine($"-- slot AT{i} '{name}': {objs.Length} option(s)");
            // AT3 is the sight slot ("Reflexsight" / holosight); the others are listed by name only, to keep the file small.
            for (int k = 0; k < objs.Length; k++)
            {
                var g = objs[k];
                if (g == null) continue;
                if (i != 3) { sb.AppendLine($"  AT{i}[{k}] '{g.name}' active={g.activeInHierarchy}"); continue; }
                Obj($"AT{i}[{k}]{(g.activeInHierarchy ? " EQUIPPED" : "")}", g, true);
                sb.AppendLine("    -- components:");
                Tree(g.transform, 0);
            }
        }

        static void Tree(Transform t, int depth)
        {
            if (depth > 6) return;
            var g = t.gameObject;
            var comps = new StringBuilder();
            foreach (var c in g.GetComponents<Component>())
            {
                if (c == null) continue;
                var tn = c.GetIl2CppType().Name;
                if (tn == "Transform") continue;
                comps.Append(' ').Append(tn);
                var l = c.TryCast<Light>();
                if (l != null) comps.Append($"[type={l.type} color={C(l.color)} intensity={l.intensity:0.##} range={l.range:0.##} spot={l.spotAngle:0.#} enabled={l.enabled}]");
                var v = c.TryCast<VolumetricLightBeamSD>();
                if (v != null) comps.Append($"[colorFromLight={v.colorFromLight} colorMode={v.colorMode} usedColorMode={v.usedColorMode} color={C(v.color)} intensityFromLight={v.intensityFromLight} intensityMultiplier={v.intensityMultiplier:0.##} enabled={v.enabled}]");
                var tr = c.TryCast<TrailRenderer>();
                if (tr != null) comps.Append($"[trail start={C(tr.startColor)} end={C(tr.endColor)}]");
                var sr = c.TryCast<SpriteRenderer>();
                if (sr != null) comps.Append($"[sprite={sr.sprite?.name} color={C(sr.color)}]");
                var rend = c.TryCast<Renderer>();
                if (rend != null && sr == null) { var ms = rend.sharedMaterials; if (ms.Length > 0 && ms[0] != null) comps.Append($"[mat={ms[0].name}]"); }
            }
            sb.AppendLine($"    {new string(' ', depth * 2)}{g.name} self={g.activeSelf} inH={g.activeInHierarchy}{comps}");
            for (int i = 0; i < t.childCount; i++) Tree(t.GetChild(i), depth + 1);
        }

        static void Slot(int i, string name, Il2CppReferenceArray<GameObject> objs)
        {
            if (objs == null) { sb.AppendLine($"-- slot AT{i} '{name}': no objects"); return; }
            sb.AppendLine($"-- slot AT{i} '{name}': {objs.Length} option(s)");
            for (int k = 0; k < objs.Length; k++)
            {
                var g = objs[k];
                if (g == null) continue;
                if (g.activeInHierarchy) Obj($"AT{i}[{k}] EQUIPPED", g, true);
                else sb.AppendLine($"  AT{i}[{k}] '{g.name}' (not active)");
            }
        }

        static void Obj(string label, GameObject g, bool deep)
        {
            if (g == null) { sb.AppendLine($"  {label}: null"); return; }
            sb.AppendLine($"  {label}: '{GoPath(g)}' self={g.activeSelf} inHierarchy={g.activeInHierarchy} layer={g.layer}");
            if (!deep) return;
            int shown = 0;
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (++shown > 14) { sb.AppendLine("    ... more renderers cut"); break; }
                var extra = "";
                var lr = r.TryCast<LineRenderer>();
                if (lr != null) extra = $" LineRenderer pts={lr.positionCount} width={lr.startWidth:0.####}->{lr.endWidth:0.####} startColor={C(lr.startColor)} endColor={C(lr.endColor)} useWorldSpace={lr.useWorldSpace}";
                sb.AppendLine($"    R '{GoPath(r.gameObject)}' {r.GetIl2CppType().Name} enabled={r.enabled} active={r.gameObject.activeInHierarchy}{extra}");
                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++) Mat(m, mats[m]);
            }
            if (shown == 0) sb.AppendLine("    (no renderers)");
        }

        static void Mat(int slot, Material m)
        {
            if (m == null) { sb.AppendLine($"      mat[{slot}] null"); return; }
            var sh = m.shader;
            if (!seenMats.Add(m.GetInstanceID())) { sb.AppendLine($"      mat[{slot}] '{m.name}' shader={sh?.name} (details above)"); return; }
            sb.Append($"      mat[{slot}] '{m.name}' shader={sh?.name} queue={m.renderQueue} keywords={KW(m)}");
            if (sh != null)
            {
                int cnt = sh.GetPropertyCount();
                for (int i = 0; i < cnt; i++)
                {
                    var pn = sh.GetPropertyName(i);
                    switch (sh.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color: sb.Append($" {pn}={C(m.GetColor(pn))}"); break;
                        case ShaderPropertyType.Texture:
                            var t = m.GetTexture(pn);
                            if (t != null) sb.Append($" {pn}=tex:{t.name}");
                            break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            if (Regex.IsMatch(pn, "emi|intens|glow|alpha|cutoff|surface|blend|size|bright", RegexOptions.IgnoreCase))
                                sb.Append($" {pn}={m.GetFloat(pn):0.###}");
                            break;
                    }
                }
            }
            sb.AppendLine();
        }

        static string KW(Material m)
        {
            try { var k = m.shaderKeywords; return k == null || k.Length == 0 ? "-" : string.Join("|", k); }
            catch { return "?"; }
        }

        static string C(Color c) => $"({c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##})";

        static string GoPath(GameObject g)
        {
            var s = g.name;
            var t = g.transform.parent;
            for (int i = 0; t != null && i < 6; i++, t = t.parent) s = t.name + "/" + s;
            return s;
        }
    }
}
