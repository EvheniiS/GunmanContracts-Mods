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

var recCnt = new Dictionary<string, string> { ["~custom.A"] = "5", ["~custom.B"] = "1", ["~custom.C"] = "4", ["~custom.D"] = "3" };
var nowCnt = new Dictionary<string, int> { ["A"] = 0, ["B"] = 0, ["C"] = 2, ["D"] = 0, ["E"] = 0 };
var resetCats = DefaultRecord.ResetCategories(recCnt, nowCnt);
Check(resetCats.Count == 2 && resetCats[0] == "A" && resetCats[1] == "D", "categories with 2+ changed settings that are now all default are reported as reset");
Check(!resetCats.Contains("B") && !resetCats.Contains("E"), "one changed setting reset by hand, and never-customised categories, are not reported");
Check(DefaultRecord.ResetCategories(recCnt, new Dictionary<string, int> { ["C"] = 4 }).Count == 0, "still-customised category is fine");

// ---- the record file (UserData/ModSettings_defaults.txt) across game sessions, on a real file ----
var recDir = Path.Combine(Path.GetTempPath(), "ModSettingsTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(recDir);
var recFile = Path.Combine(recDir, "ModSettings_defaults.txt");
try
{
    Dictionary<string, string> known = new(), vers = new();
    List<string> changes = new();
    // One game start: read the file, compare versions, decide each setting, like DefaultChanges.Scan.
    bool Start((string, string)[] mods)
    {
        known = new(); vers = new(); changes = new();
        bool existed = DefaultRecord.Load(recFile, known, vers);
        DefaultRecord.Versions(vers, mods, changes);
        return existed;
    }
    DefaultChange Setting(string key, string def, string val) => DefaultRecord.Apply(known, key, def, val, out _);

    // 1. First run: no file. Everything is recorded, nothing is listed, and the file is created.
    Check(!Start(new[] { ("Daredevil", "1.0.0"), ("Mod Settings", "1.3.0") }), "first run: no file yet");
    Check(changes.Count == 0 && vers["Daredevil"] == "1.0.0", "first run: versions recorded, no 'Updated:' line");
    Check(Setting("BillyClubs.ChestStunSeconds", "3", "3") == DefaultChange.Record && Setting("BillyClubs.Throw", "3", "2") == DefaultChange.Record,
        "first run: untouched and customised settings are only recorded");
    DefaultRecord.Save(recFile, known, vers);
    var lines = File.ReadAllLines(recFile);
    Check(lines[0].StartsWith("# ") && lines.Contains("@Daredevil\t1.0.0") && lines.Contains("@Mod Settings\t1.3.0") && lines.Contains("BillyClubs.ChestStunSeconds\t3"),
        "file created: comment header, '@Mod<TAB>version' lines, 'Category.Entry<TAB>default' lines");
    Check(Array.IndexOf(lines, "@Mod Settings\t1.3.0") < Array.IndexOf(lines, "BillyClubs.ChestStunSeconds\t3")
          && Array.IndexOf(lines, "@Daredevil\t1.0.0") < Array.IndexOf(lines, "@Mod Settings\t1.3.0"), "versions first, each block sorted");

    // 2. Next start, nothing changed: nothing listed, nothing to decide.
    Check(Start(new[] { ("Daredevil", "1.0.0"), ("Mod Settings", "1.3.0") }) && changes.Count == 0, "same versions: nothing listed");
    Check(Setting("BillyClubs.ChestStunSeconds", "3", "3") == DefaultChange.Same, "same default: nothing to do");

    // 3. Daredevil 1.1.0 changes both defaults 3 -> 1, and a new mod is installed.
    Start(new[] { ("Daredevil", "1.1.0"), ("Mod Settings", "1.3.0"), ("Gloves", "0.1.0") });
    Check(changes.Count == 1 && changes[0] == "Daredevil 1.0.0 -> 1.1.0" && vers["Gloves"] == "0.1.0", "update listed; a new mod is recorded, not listed");
    Check(DefaultRecord.Apply(known, "BillyClubs.ChestStunSeconds", "1", "3", out var wasDef) == DefaultChange.AutoUpdate && wasDef == "3"
          && known["BillyClubs.ChestStunSeconds"] == "1", "untouched setting moves; the record now holds the new default");
    Check(Setting("BillyClubs.Throw", "1", "2") == DefaultChange.Ask && known["BillyClubs.Throw"] == "3", "customised setting asks; the old default stays recorded");
    DefaultRecord.Save(recFile, known, vers);

    // 4. Next start without deciding: the update is not listed again, the open question is.
    Start(new[] { ("Daredevil", "1.1.0"), ("Mod Settings", "1.3.0"), ("Gloves", "0.1.0") });
    Check(changes.Count == 0 && Setting("BillyClubs.ChestStunSeconds", "1", "1") == DefaultChange.Same, "auto-moved setting is not shown twice");
    Check(Setting("BillyClubs.Throw", "1", "2") == DefaultChange.Ask, "undecided setting is asked again at the next start");
    known["BillyClubs.Throw"] = "1";   // the player decides (Keep / Use new): DefaultChanges records the new default
    DefaultRecord.Save(recFile, known, vers);
    Start(new[] { ("Daredevil", "1.1.0") });
    Check(Setting("BillyClubs.Throw", "1", "2") == DefaultChange.Same, "decided setting is not asked again");

    // 5. Downgrade, and a removed mod.
    Start(new[] { ("Daredevil", "1.0.0"), ("Mod Settings", "1.3.0") });
    Check(changes.Count == 1 && changes[0] == "Daredevil 1.1.0 -> 1.0.0", "downgrade listed like an update");
    Check(vers.ContainsKey("Gloves"), "an uninstalled mod stays recorded (a reinstall of the same version is not listed)");

    // Damaged or hand-edited file: comments, blank lines, lines without a tab and a lone '@' are skipped; the rest loads.
    File.WriteAllLines(recFile, new[] { "# comment", "", "garbage line", "@", "@\tx", "\tno key", "@Daredevil\t1.1.0", "A.B\t5", "A.C\t" });
    known = new(); vers = new();
    Check(DefaultRecord.Load(recFile, known, vers) && known.Count == 2 && known["A.B"] == "5" && known["A.C"] == ""
          && vers.Count == 1 && vers["Daredevil"] == "1.1.0", "damaged lines are skipped, good lines still load");
    File.WriteAllText(recFile, "");
    known = new(); vers = new();
    Check(DefaultRecord.Load(recFile, known, vers) && known.Count == 0, "empty file: not a first run, everything is recorded fresh");

    // Special lines and awkward values survive the file: reset counts, a two-line notice, tabs/backslashes, non-ASCII names.
    known = new() { ["X.Path"] = "C:\\Mods\\a\tb", ["X.Text"] = "line1\nline2" };
    vers = new() { ["Modé ü"] = "1.0.0", [DefaultRecord.CustomKey("BillyClubs")] = "3", ["~notice"] = "Settings were reset: A.\nSettings were reset: B." };
    DefaultRecord.Save(recFile, known, vers);
    Dictionary<string, string> k2 = new(), v2 = new();
    DefaultRecord.Load(recFile, k2, v2);
    Check(k2["X.Path"] == "C:\\Mods\\a\tb" && k2["X.Text"] == "line1\nline2", "values with tabs, newlines and backslashes round-trip");
    Check(v2["Modé ü"] == "1.0.0" && v2["~custom.BillyClubs"] == "3" && v2["~notice"].Split('\n').Length == 2, "non-ASCII mod name, reset counts and a two-line notice round-trip");
    Check(File.ReadAllLines(recFile).Length == 2 + 3 + 2, "one line per entry (notice newline escaped, not split)");
}
finally { Directory.Delete(recDir, true); }

var none = new List<string>();
var ver = new List<string> { "Defaults Test 1.0.0 -> 1.1.0" };
List<(string, int, int)> Sec(params (string, int, int)[] s) => s.ToList();
var both = UpdatePopup.Build(ver, Sec(("Defaults Test", 2, 2)), none);
Check(both.Title == "DEFAULTS TEST UPDATED" && both.Buttons.SequenceEqual(new[] { PopupButton.UseNew, PopupButton.KeepMine, PopupButton.Review, PopupButton.Later }),
    "one mod: named in the title; customised settings ask: Use new / Keep mine / Review / Later");
Check(both.Lines[0] == "Updated: Defaults Test 1.0.0 -> 1.1.0" && both.Lines.Contains("4 settings in Defaults Test have new default values:") && both.Lines[^1].Contains("those 2"),
    "popup counts, versions and the mod's name");
var later = UpdatePopup.Build(none, Sec(("Defaults Test", 0, 2)), none);
Check(later.Title == "DEFAULTS TEST UPDATED" && later.Lines[0].Contains("in Defaults Test"), "next start (no version line): the mod is still named");
var twoMods = UpdatePopup.Build(new List<string> { "Daredevil 1.1.0 -> 1.2.0", "Gloves 0.1.0 -> 0.2.0" }, Sec(("BillyClubs", 1, 2), ("Gloves", 1, 0)), none);
Check(twoMods.Title == "MODS UPDATED" && twoMods.Lines.Contains("Mods: BillyClubs (3), Gloves (1)") && twoMods.Lines.Contains("4 settings in 2 mods have new default values:")
      && twoMods.Lines.Contains("2 you never changed: moved to the new default.") && twoMods.Buttons.Count == 4,
    "several mods: ONE popup, each mod listed with its count, totals summed");
var autoOnly = UpdatePopup.Build(ver, Sec(("Defaults Test", 5, 0)), none);
Check(autoOnly.Buttons.SequenceEqual(new[] { PopupButton.Ok, PopupButton.Review }) && autoOnly.Lines.Exists(l => l.StartsWith("5 settings you never changed in Defaults Test")),
    "popup with only moved settings: OK / Review");
var oneAsk = UpdatePopup.Build(none, Sec(("Gloves", 0, 1)), none);
Check(oneAsk.Lines[0].StartsWith("1 setting you changed in Gloves has") && oneAsk.Buttons.Count == 4, "popup singular wording, no version line without version changes");
var notice = UpdatePopup.Build(none, Sec(), new List<string> { "Settings were reset: Defaults Test." });
Check(notice.Title == "SETTINGS RESET" && notice.Buttons.SequenceEqual(new[] { PopupButton.Ok }) && notice.Lines.Count == 1, "reset notice alone: OK only");
var noticeAndAsk = UpdatePopup.Build(ver, Sec(("X", 0, 2)), new List<string> { "Settings were reset: X." });
Check(noticeAndAsk.Lines[0].StartsWith("Settings were reset") && noticeAndAsk.Buttons.Count == 4, "notice shown above the update question");
var many = UpdatePopup.Build(new List<string> { "A 1 -> 2", "B 1 -> 2", "C 1 -> 2", "D 1 -> 2", "E 1 -> 2" },
    Sec(("A", 1, 0), ("B", 1, 0), ("C", 1, 0), ("D", 1, 0), ("E", 1, 0)), none);
Check(many.Lines[0] == "Updated: A 1 -> 2, B 1 -> 2, C 1 -> 2 and 2 more" && many.Lines[1] == "Mods: A (1), B (1), C (1), D (1) and 1 more", "version and mod lists capped");

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
