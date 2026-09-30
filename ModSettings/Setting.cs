using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;

namespace ModSettings
{
    enum Kind { Bool, Int, Float, Enum, Choice, Color, ReadOnly }

    // One MelonPreferences entry as the menu sees it. Any mod's entry works: the type comes from the entry itself.
    internal sealed class Setting
    {
        public readonly MelonPreferences_Entry Entry;
        public readonly Kind Kind;
        public readonly List<string> Options; // Choice / Color
        public readonly double Small, Big;    // Int / Float steps
        public readonly bool Restart;
        readonly Type type;

        public Setting(MelonPreferences_Entry e)
        {
            Entry = e;
            type = e.GetReflectedType();
            Restart = Choices.NeedsRestart(e.Description);
            var v = e.BoxedValue;
            if (type == typeof(bool)) Kind = Kind.Bool;
            else if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)) Kind = Kind.Int;
            else if (type == typeof(float) || type == typeof(double)) Kind = Kind.Float;
            else if (type.IsEnum) Kind = Enum.GetValues(type).Length <= 24 ? Kind.Enum : Kind.ReadOnly; // not KeyCode
            else if (type == typeof(string) && !Choices.ManagedByMod(e.Description))
            {
                var s = v as string ?? "";
                if (Choices.IsColorSetting(s, DefaultValue() as string, e.Description))
                {
                    Kind = Kind.Color;
                    Options = new List<string>(Choices.Palette);
                    if (string.Equals(DefaultValue() as string, "Default", StringComparison.OrdinalIgnoreCase)) Options.Insert(0, "Default");
                    if (!Options.Exists(x => x.Equals(s.Trim(), StringComparison.OrdinalIgnoreCase))) Options.Insert(0, s.Trim().ToUpperInvariant());
                }
                else if ((Options = Choices.FromDescription(e.Description, s)) != null) Kind = Kind.Choice;
                else Kind = Kind.ReadOnly;
            }
            else Kind = Kind.ReadOnly;

            if (Kind == Kind.Int || Kind == Kind.Float)
                (Small, Big) = Choices.Steps(ToDouble(DefaultValue() ?? v), ToDouble(v), Kind == Kind.Int);
        }

        public string Name => string.IsNullOrEmpty(Entry.DisplayName) ? Entry.Identifier : Entry.DisplayName;

        public string ValueText()
        {
            var v = Entry.BoxedValue;
            return Kind switch
            {
                Kind.Bool => (bool)v ? "ON" : "OFF",
                Kind.Int or Kind.Float => Choices.Num(ToDouble(v)),
                _ => v?.ToString() ?? "",
            };
        }

        public bool IsDefault => string.Equals(Entry.GetValueAsString(), Entry.GetDefaultValueAsString(), StringComparison.Ordinal);
        public bool CanReset => !Choices.ManagedByMod(Entry.Description);

        // dir: -2 / -1 / +1 / +2 (big/small steps for numbers; -1/+1 cycles choices; bools toggle on any).
        // Returns false when the value didn't change (at a limit, or the validator refused it).
        public bool Change(int dir)
        {
            var v = Entry.BoxedValue;
            object next;
            switch (Kind)
            {
                case Kind.Bool: next = !(bool)v; break;
                case Kind.Int:
                case Kind.Float:
                    double step = (Math.Abs(dir) == 2 ? Big : Small) * Math.Sign(dir);
                    next = Convert.ChangeType(Kind == Kind.Int ? ToDouble(v) + step : Choices.Add(ToDouble(v), step), type, CultureInfo.InvariantCulture);
                    break;
                case Kind.Enum:
                    var vals = Enum.GetValues(type);
                    int i = Array.IndexOf(vals, v);
                    next = vals.GetValue(((i + Math.Sign(dir)) % vals.Length + vals.Length) % vals.Length);
                    break;
                case Kind.Choice:
                case Kind.Color:
                    next = Choices.Cycle(Options, v as string, Math.Sign(dir));
                    break;
                default: return false;
            }
            if (Entry.Validator != null) next = Entry.Validator.EnsureValid(next);
            if (Equals(next, v)) return false;
            Entry.BoxedValue = next;
            return true;
        }

        object DefaultValue()
        {
            try { return Entry.GetType().GetProperty("DefaultValue")?.GetValue(Entry); } catch { return null; }
        }

        static double ToDouble(object o)
        {
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return 0; }
        }
    }
}
