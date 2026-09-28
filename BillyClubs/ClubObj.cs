using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace BillyClubs
{
    // Pure managed reader, also exercised outside Unity by Tests. Exported OBJ is Y-up/right-handed;
    // Unity club space is Y-up/left-handed. Reflect Z in positions/normals AND reverse winding.
    internal sealed class ClubObj
    {
        internal readonly List<Vector3> Vertices = new();
        internal readonly List<Vector3> Normals = new();
        internal readonly List<Vector2> UV = new();
        internal readonly List<int> Triangles = new();

        internal static ClubObj Read(TextReader reader)
        {
            var result = new ClubObj();
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var corners = new Dictionary<(int, int, int), int>();
            string line;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                int comment = line.IndexOf('#');
                if (comment >= 0) line = line.Substring(0, comment);
                var words = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) continue;
                try
                {
                    switch (words[0])
                    {
                        case "v": positions.Add(ReadVector(words)); break;
                        case "vn":
                            var n = ReadVector(words);
                            if (n.LengthSquared() < 1e-10f) throw new FormatException("Zero normal");
                            normals.Add(Vector3.Normalize(n));
                            break;
                        case "vt": uv.Add(new Vector2(Number(words[1]), Number(words[2]))); break;
                        case "f":
                            if (words.Length != 4) throw new FormatException("Expected triangulated v/vt/vn faces");
                            int a = Corner(words[1]), b = Corner(words[2]), c = Corner(words[3]);
                            result.Triangles.Add(a); result.Triangles.Add(c); result.Triangles.Add(b);
                            if (result.Triangles.Count > 6000 * 3) throw new FormatException("Club exceeds 6000 triangles");
                            break;
                    }
                }
                catch (Exception ex) when (ex is FormatException || ex is IndexOutOfRangeException || ex is OverflowException)
                { throw new FormatException($"Invalid club OBJ at line {lineNumber}: {ex.Message}", ex); }
            }
            if (result.Triangles.Count == 0) throw new FormatException("Club OBJ has no triangles");
            return result;

            int Corner(string word)
            {
                var indices = word.Split('/');
                if (indices.Length != 3) throw new FormatException("A face corner needs position, UV and normal");
                var key = (Index(indices[0], positions.Count), Index(indices[1], uv.Count), Index(indices[2], normals.Count));
                if (corners.TryGetValue(key, out int found)) return found;
                int index = result.Vertices.Count;
                if (index >= 65535) throw new FormatException("Club exceeds the 16-bit vertex limit");
                corners.Add(key, index);
                result.Vertices.Add(positions[key.Item1]); result.UV.Add(uv[key.Item2]); result.Normals.Add(normals[key.Item3]);
                return index;
            }
        }

        static int Index(string text, int count)
        {
            int i = int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            int index = i > 0 ? i - 1 : count + i;
            if (i == 0 || index < 0 || index >= count) throw new FormatException("Face index out of range");
            return index;
        }

        static Vector3 ReadVector(string[] words) => new(Number(words[1]), Number(words[2]), -Number(words[3]));
        static float Number(string text)
        {
            float f = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!float.IsFinite(f)) throw new FormatException("Non-finite coordinate");
            return f;
        }
    }
}
