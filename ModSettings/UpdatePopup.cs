using System.Collections.Generic;

namespace ModSettings
{
    enum PopupButton { UseNew, KeepMine, Review, Later, Ok }

    // The small "Mods updated" card shown on the first board open of a session after a mod update changed defaults
    // (or a settings reset was noticed). ONE card for all mods; Review opens one page with every mod's rows.
    // What it says and which buttons it has; the VR board and the flat menu draw it. No MelonLoader types here, so the
    // tests can run it.
    internal sealed class UpdatePopup
    {
        public string Title;
        public readonly List<string> Lines = new();
        public readonly List<PopupButton> Buttons = new();

        // sections = the board sections (mods) with changed defaults, in page order: Auto = settings moved to the new
        // default by themselves, Ask = customised settings waiting for a decision.
        public static UpdatePopup Build(IList<string> versionChanges, IList<(string Section, int Auto, int Ask)> sections, IList<string> notices)
        {
            int auto = 0, ask = 0;
            foreach (var s in sections) { auto += s.Auto; ask += s.Ask; }
            string where = sections.Count == 1 ? sections[0].Section : $"{sections.Count} mods";
            var p = new UpdatePopup
            {
                Title = auto + ask == 0 ? "SETTINGS RESET" : sections.Count == 1 ? sections[0].Section.ToUpperInvariant() + " UPDATED" : "MODS UPDATED",
            };
            p.Lines.AddRange(notices);
            if (auto + ask > 0 && versionChanges.Count > 0) p.Lines.Add("Updated: " + Capped(versionChanges, 3));
            if (sections.Count > 1)
            {
                var names = new List<string>();
                foreach (var s in sections) names.Add($"{s.Section} ({s.Auto + s.Ask})");
                p.Lines.Add("Mods: " + Capped(names, 4));
            }
            if (ask > 0 && auto > 0)
            {
                p.Lines.Add($"{auto + ask} settings in {where} have new default values:");
                p.Lines.Add($"{auto} you never changed: moved to the new default.");
                p.Lines.Add($"{ask} you changed: your value was kept.");
                p.Lines.Add(ask == 1 ? "Use the new default for that one too?" : $"Use the new defaults for those {ask} too?");
            }
            else if (ask > 0)
            {
                p.Lines.Add(ask == 1 ? $"1 setting you changed in {where} has a new default value. Your value was kept."
                                     : $"{ask} settings you changed in {where} have new default values. Your values were kept.");
                p.Lines.Add(ask == 1 ? "Use the new default instead?" : "Use the new defaults instead?");
            }
            else if (auto > 0)
            {
                p.Lines.Add(auto == 1 ? $"1 setting you never changed in {where} now uses its new default."
                                      : $"{auto} settings you never changed in {where} now use their new defaults.");
                p.Lines.Add("Review lists them; Undo there gives the old value back.");
            }
            if (ask > 0) p.Buttons.AddRange(new[] { PopupButton.UseNew, PopupButton.KeepMine, PopupButton.Review, PopupButton.Later });
            else if (auto > 0) p.Buttons.AddRange(new[] { PopupButton.Ok, PopupButton.Review });
            else p.Buttons.Add(PopupButton.Ok);
            return p;
        }

        // "a, b, c and 2 more"
        static string Capped(IList<string> items, int max)
        {
            var v = new List<string>(items);
            return v.Count <= max ? string.Join(", ", v) : string.Join(", ", v.GetRange(0, max)) + $" and {v.Count - max} more";
        }

        public static string Label(PopupButton b) => b switch
        {
            PopupButton.UseNew => "Use new",
            PopupButton.KeepMine => "Keep mine",
            PopupButton.Review => "Review",
            PopupButton.Later => "Later",
            _ => "OK",
        };
    }
}
