using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BillyClubs
{
    public partial class BillyClubsMod
    {
        static Mesh CustomMesh;
        static Material CustomMaterial;
        static bool CustomLoadFailed;

        static bool TryBuildCustomVisual(Transform root, Vector3 center, Vector3 tipDir, Material source)
        {
            if (CustomLoadFailed) return false;
            GameObject go = null;
            try
            {
                if (!Alive(CustomMesh) || !Alive(CustomMaterial)) LoadCustomAssets(source);
                go = new GameObject(VisualName);
                go.layer = root.gameObject.layer;
                var t = go.transform;
                t.SetParent(root, false);
                t.localPosition = center;
                // Keep the club-space roll used by the Blender game-reference exporter.
                var up = Vector3.ProjectOnPlane(Vector3.up, tipDir);
                if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.forward, tipDir);
                t.localRotation = Quaternion.LookRotation(Vector3.Cross(tipDir, up).normalized, up.normalized);
                float length = float.IsFinite(Length.Value) && Length.Value > 0 ? Length.Value : 0.6f;
                t.localScale = new Vector3(length / 0.6f, 1, 1);
                go.AddComponent<MeshFilter>().sharedMesh = CustomMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = CustomMaterial;
                // No collider here. All hit shapes, grabbables and grip tuning remain on the crowbar.
                Log.Msg($"custom club visual ready: {CustomMesh.vertexCount} vertices, {CustomMesh.triangles.Length / 3} triangles, 2048px maps, shared mesh/material; shader '{CustomMaterial.shader.name}'");
                return true;
            }
            catch (Exception ex)
            {
                if (go != null) Object.DestroyImmediate(go);
                CustomLoadFailed = true;
                Log.Warning($"custom club visual failed; using the original cylinder visuals: {ex}");
                return false;
            }
        }

        static void LoadCustomAssets(Material source)
        {
            var created = new List<Object>();
            try
            {
                ClubObj data;
                using (var stream = Asset("billy_club.obj"))
                using (var reader = new StreamReader(stream)) data = ClubObj.Read(reader);
                var vertices = new Vector3[data.Vertices.Count];
                var normals = new Vector3[vertices.Length];
                var uv = new Vector2[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    var v = data.Vertices[i]; var n = data.Normals[i]; var t = data.UV[i];
                    vertices[i] = new Vector3(v.X, v.Y, v.Z);
                    normals[i] = new Vector3(n.X, n.Y, n.Z); uv[i] = new Vector2(t.X, t.Y);
                }
                var mesh = new Mesh { name = "BillyClubs_AuthoredMesh", hideFlags = HideFlags.DontUnloadUnusedAsset };
                created.Add(mesh);
                mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = data.Triangles.ToArray();
                mesh.RecalculateBounds();
                // Generate Unity tangents AFTER the handedness conversion; retain authored split normals.
                mesh.RecalculateTangents();
                var size = mesh.bounds.size;
                if (Mathf.Abs(size.x - .6f) > .001f || size.y > .0401f || size.z > .0401f)
                    throw new InvalidDataException($"Unexpected authored club bounds {size}");

                var shader = source != null && source.shader != null && source.shader.name == "Universal Render Pipeline/Lit"
                    ? source.shader : Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable");
                var material = new Material(shader) { name = "BillyClubs_AuthoredAtlas", hideFlags = HideFlags.DontUnloadUnusedAsset };
                created.Add(material);
                var color = LoadMap("basecolor", false, created);
                var normal = LoadMap("normal", true, created);
                var metallic = LoadMap("metallic", true, created);
                var roughness = LoadMap("roughness", true, created);
                var metals = metallic.GetPixels32(); var rough = roughness.GetPixels32();
                var pixels = new Color32[metals.Length];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(metals[i].r, 0, 0, (byte)(255 - rough[i].r));
                var packed = new Texture2D(2048, 2048, TextureFormat.RGBA32, true, true);
                created.Add(packed); ConfigureMap(packed, "metallic_smoothness");
                packed.SetPixels32(pixels); packed.Apply(true, true);
                // RGB PNG normals have implicit alpha=1, suitable for URP's RGB and RG/AG unpack paths.
                // Linear loading is essential; do not flip green or run these through sRGB conversion.
                color.Apply(true, true); normal.Apply(true, true);
                Object.Destroy(metallic); created.Remove(metallic);
                Object.Destroy(roughness); created.Remove(roughness);

                material.SetTexture("_BaseMap", color); material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BumpMap", normal); material.SetFloat("_BumpScale", 1f);
                material.SetTexture("_MetallicGlossMap", packed);
                material.SetFloat("_Metallic", 1f); material.SetFloat("_Smoothness", 1f);
                material.SetFloat("_WorkflowMode", 1f); material.SetFloat("_SmoothnessTextureChannel", 0f);
                material.SetFloat("_Surface", 0f); material.SetFloat("_AlphaClip", 0f);
                material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.DisableKeyword("_SPECULAR_SETUP"); material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                CustomMesh = mesh; CustomMaterial = material;
            }
            catch
            {
                foreach (var resource in created) if (resource != null) Object.Destroy(resource);
                throw;
            }
        }

        static Stream Asset(string filename) => Assembly.GetExecutingAssembly().GetManifestResourceStream("BillyClubs.Assets." + filename)
            ?? throw new FileNotFoundException("Missing bundled Billy Clubs asset", filename);

        static Texture2D LoadMap(string channel, bool linear, List<Object> created)
        {
            using var stream = Asset("billy_club_" + channel + ".png");
            using var bytes = new MemoryStream(); stream.CopyTo(bytes);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            created.Add(tex); ConfigureMap(tex, channel);
            if (!ImageConversion.LoadImage(tex, bytes.ToArray(), false) || tex.width != 2048 || tex.height != 2048)
                throw new InvalidDataException($"Invalid {channel} texture (expected 2048 x 2048 PNG)");
            return tex;
        }

        static void ConfigureMap(Texture2D tex, string channel)
        {
            tex.name = "BillyClubs_" + channel; tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Trilinear; tex.anisoLevel = 8;
        }
    }
}
