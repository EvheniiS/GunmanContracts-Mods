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
Check(Choices.ManagedByMod("Managed by the mod. Saved loadout."), "managed values must not expose reset");
Check(!Choices.ManagedByMod("Position in cm"), "normal settings can reset");
Check(Choices.IsColorSetting("Default", "Default", "Default or #RRGGBB"), "native color remains editable");
Check(Choices.IsColorSetting("#FF2620", "Default", "Default or #RRGGBB"), "saved red stays editable");
Check(!Choices.IsColorSetting("Default", "Default", "ordinary text"), "ordinary Default text is not a color");

bool Ray(float x, float y, float z, float dx, float dy, float dz, float scale = 1) =>
    PointerGeometry.Hit(x, y, z, dx, dy, dz, scale, .72f, .56f, out _, out _);
Check(Ray(0, 0, -1, 0, 0, 1), "front ray hits");
Check(!Ray(0, 0, 1, 0, 0, -1), "back ray cannot press");
Check(!Ray(0, 0, -1, 1, 0, 0), "parallel ray cannot press");
Check(!Ray(0, 0, -1, 0, 0, -1), "ray aimed away cannot press");
Check(!Ray(.4f, 0, -1, 0, 0, 1), "off-board ray rejected");
Check(!Ray(0, 0, -4.1f, 0, 0, 1), "distance limited");
Check(Ray(0, 0, -3, 0, 0, 1, .5f), "small panel uses world distance");
Check(!Ray(0, 0, -3, 0, 0, 1, 2), "large panel uses world distance");
Check(!Ray(0, 0, float.NaN, 0, 0, 1), "nonfinite tracking rejected");
Check(PointerGeometry.Hit(-.2f, 0, -1, .2f, 0, 1, 1, .72f, .56f, out var hx, out var hy)
      && Math.Abs(hx) < .00001f && hy == 0, "angled ray intersects at expected row");

Check(DefaultRecord.Decide(null, "1", "3") == DefaultChange.Record, "unknown setting is only recorded");
Check(DefaultRecord.Decide("1", "1", "3") == DefaultChange.Same, "same default: nothing to do");
Check(DefaultRecord.Decide("3", "1", "3") == DefaultChange.AutoUpdate, "untouched old default moves to the new one");
Check(DefaultRecord.Decide("3", "1", "1") == DefaultChange.AlreadyNew, "value already the new default");
Check(DefaultRecord.Decide("3", "1", "2") == DefaultChange.Ask, "customised value asks");
var recD = new Dictionary<string, string> { ["BillyClubs.ChestStunSeconds"] = "3", ["X.Text"] = "a\tb\\c\nd" };
var recV = new Dictionary<string, string> { ["Daredevil"] = "1.1.0" };
var backD = new Dictionary<string, string>(); var backV = new Dictionary<string, string>();
DefaultRecord.Parse(DefaultRecord.Format(recD, recV), backD, backV);
Check(backD.Count == 2 && backD["X.Text"] == "a\tb\\c\nd" && backD["BillyClubs.ChestStunSeconds"] == "3", "record round-trips defaults");
Check(backV.Count == 1 && backV["Daredevil"] == "1.1.0", "record round-trips versions");

var hold = new VRHolsterCustomization.AdjustmentHold();
Check(!hold.TryStart(true, true, 0), "buttons already held when enabled cannot arm");
Check(!hold.TryStart(false, true, 0), "release arms without starting");
Check(hold.TryStart(true, true, 1), "fresh chord near empty holster starts hold");
Check(!hold.Mature(2.999f) && hold.Mature(3), "full two-second hold required");
hold.Cancel();
Check(!hold.Mature(100) && !hold.TryStart(true, true, 100), "cancel clears timer and requires release");
hold.TryStart(false, false, 101);
Check(!hold.TryStart(true, false, 102), "press away from holster cannot arm");
Check(!hold.TryStart(true, true, 103), "approaching with buttons held cannot arm");
hold.TryStart(false, true, 104);
Check(hold.TryStart(true, true, 105), "release permits another adjustment");
Console.WriteLine(fail == 0 ? "all checks passed" : $"{fail} check(s) failed");
return fail;
