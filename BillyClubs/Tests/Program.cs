using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using BillyClubs;

string root = args.Length > 0 ? System.IO.Path.GetFullPath(args[0]) : throw new Exception("Pass the repository root");
string dll = args.Length > 1 ? System.IO.Path.GetFullPath(args[1])
    : System.IO.Path.Combine(root, "feature", "BillyClubs-0.5.1", "BillyClubs.dll");
var resources = new Dictionary<string, byte[]>();
using (var stream = File.OpenRead(dll))
using (var pe = new PEReader(stream))
{
    var md = pe.GetMetadataReader();
    var section = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress);
    foreach (var handle in md.ManifestResources)
    {
        var resource = md.GetManifestResource(handle);
        var reader = section.GetReader((int)resource.Offset, section.Length - (int)resource.Offset);
        resources.Add(md.GetString(resource.Name), reader.ReadBytes(reader.ReadInt32()));
    }
}
Assert(resources.Count == 5, "DLL embeds exactly one OBJ and four textures");
foreach (var pair in resources)
{
    string file = pair.Key.Replace("BillyClubs.Assets.", "");
    string source = System.IO.Path.Combine(root, "BlenderRefs", "out", file.EndsWith(".png") ? "textures" : "", file);
    Assert(pair.Value.SequenceEqual(File.ReadAllBytes(source)), "Embedded asset matches export: " + file);
    if (file.EndsWith(".png"))
    {
        Assert(BinaryPrimitives.ReadInt32BigEndian(pair.Value.AsSpan(16, 4)) == 2048 &&
               BinaryPrimitives.ReadInt32BigEndian(pair.Value.AsSpan(20, 4)) == 2048, "Texture is 2048 x 2048");
        if (file.Contains("normal")) Assert(pair.Value[25] == 2, "Normal PNG is RGB, with implicit opaque alpha for URP unpacking");
    }
}

// Locale, tuple splitting at UV seams, handedness and negative OBJ indices are loader regressions.
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("uk-UA");
var model = ClubObj.Read(new StringReader(System.Text.Encoding.UTF8.GetString(resources["BillyClubs.Assets.billy_club.obj"])));
Assert(model.Triangles.Count == 5876 * 3, "Final authored triangle count");
Assert(model.Vertices.Count < 65535, "Fits 16-bit mesh indices after splitting corner tuples");
var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
foreach (var v in model.Vertices) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
Assert(Vector3.Distance(max - min, new Vector3(.6f, .0348f, .0348f)) < 1e-5f, "Converted bounds are 600 x 34.8 x 34.8 mm");
Assert((min + max).Length() < 1e-5f, "Mesh remains centered");
float handRadius = 0;
for (int i = 0; i < model.Triangles.Count; i += 3)
{
    int a = model.Triangles[i], b = model.Triangles[i+1], c = model.Triangles[i+2];
    var cross = Vector3.Cross(model.Vertices[b] - model.Vertices[a], model.Vertices[c] - model.Vertices[a]);
    Assert(cross.LengthSquared() > 1e-20f, "Nondegenerate triangle", false);
    Assert(Vector3.Dot(cross, model.Normals[a] + model.Normals[b] + model.Normals[c]) > 0, "Winding agrees with reflected authored normals", false);
    var uv1 = model.UV[b] - model.UV[a]; var uv2 = model.UV[c] - model.UV[a];
    Assert(Math.Abs(uv1.X * uv2.Y - uv1.Y * uv2.X) > 1e-12f, "UV triangle supports tangent generation", false);
    foreach (var edge in new[] { (a,b), (b,c), (c,a) })
    {
        var p = model.Vertices[edge.Item1]; var q = model.Vertices[edge.Item2];
        if ((p.X + .157f) * (q.X + .157f) > 0 || Math.Abs(p.X-q.X) < 1e-8f) continue;
        var hit = Vector3.Lerp(p, q, (-.157f-p.X)/(q.X-p.X));
        handRadius = Math.Max(handRadius, new Vector2(hit.Y, hit.Z).Length());
    }
}
Assert(Math.Abs(handRadius * 2 - .034f) < 2e-6f, "34 mm grip at the game's hand position");
Console.WriteLine("PASS All 5,876 triangles have consistent normals and valid tangent UVs");

const string fixture = "v 0 0 1\nv 1 0 1\nv 0 1 1\nvt 0 0\nvt 1 0\nvt 0 1\nvn 0 0 1\nf -3/-3/-1 -2/-2/-1 -1/-1/-1\n";
var small = ClubObj.Read(new StringReader(fixture));
Assert(small.Vertices[0].Z == -1 && small.Normals[0].Z == -1 && small.Triangles.SequenceEqual(new[] {0,2,1}), "Mirror Z, reverse winding and resolve negative indices");
var seam = ClubObj.Read(new StringReader(fixture + "f 1/2/1 2/2/1 3/3/1\n"));
Assert(seam.Vertices.Count == 4, "Preserves separate vertices at a UV seam");
foreach (string bad in new[] {
    fixture.Replace("-3/-3/-1", "0/1/1"), fixture.Replace("-3/-3/-1", "99/1/1"),
    fixture.Replace("-3/-3/-1", "1//1"), fixture.Replace("v 0 0 1", "v NaN 0 1"),
    fixture.Replace("-1/-1/-1\n", "-1/-1/-1 -1/-1/-1\n"), "# empty" })
{
    bool failed = false;
    try { ClubObj.Read(new StringReader(bad)); } catch (FormatException) { failed = true; }
    Assert(failed, "Reject malformed geometry", false);
}
Console.WriteLine($"PASS Parser error cases. Runtime vertices: {model.Vertices.Count}. ALL TESTS PASS.");

static void Assert(bool condition, string message, bool log = true)
{
    if (!condition) throw new Exception("FAIL " + message);
    if (log) Console.WriteLine("PASS " + message);
}
