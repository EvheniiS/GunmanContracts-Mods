// Runs Choices over a real MelonPreferences.cfg: prints how each string setting would be shown, plus step checks.
using ModSettings;

int fail = 0;
void Check(bool ok, string what) { if (!ok) { fail++; Console.WriteLine("FAIL " + what); } }

var cfg = args.Length > 0 ? args[0] : null;
if (cfg != null)
{
    string cat = "", desc = "";
    foreach (var raw in File.ReadAllLines(cfg))
    {
        var l = raw.Trim();
        if (l.StartsWith("[")) { cat = l; continue; }
        if (l.StartsWith("#")) { desc = l.TrimStart('#', ' '); continue; }
        int eq = l.IndexOf(" = ");
        if (eq < 0) continue;
        var val = l[(eq + 3)..];
        if (val.StartsWith("\""))
        {
            var s = val.Trim('"');
            string kind = ModSettings.Choices.ManagedByMod(desc) ? "managed" : Choices.IsColor(s) ? "color"
                : Choices.FromDescription(desc, s) is { } o ? "choice: " + string.Join(" | ", o) : "read-only";
            Console.WriteLine($"{cat} {l[..eq]} = {s} -> {kind}");
        }
        desc = "";
    }
}

Check(Choices.Steps(18, 18, false) == (1, 10), "steps 18");
Check(Math.Abs(Choices.Steps(0.6, 0.6, false).small - 0.01) < 1e-12, "steps 0.6");
Check(Choices.Add(0.30000001192092896, 0.1) == 0.4, "add 0.3+0.1 = " + Choices.Add(0.30000001192092896, 0.1));
Check(Choices.Add(0.03999999910593033, 0.001) == 0.041, "add 0.04+0.001 = " + Choices.Add(0.03999999910593033, 0.001));
Check(Choices.Add(18, -10) == 8, "add 18-10");
Check(Choices.Cycle(new[] { "A", "B", "C" }, "C", 1) == "A", "cycle wrap");
Check(Choices.Cycle(new[] { "A", "B", "C" }, "a", -1) == "C", "cycle back, case-insensitive");
Console.WriteLine(fail == 0 ? "all checks passed" : $"{fail} check(s) failed");
return fail;
