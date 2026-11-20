using System;
using System.Collections.Generic;

namespace GPA.Records
{
    /// <summary>
    /// Classifies charges as felonies or misdemeanours and sets bail, so arrest records can show the
    /// counts the way a booking sheet would. Keyword based, so new charge wording still lands somewhere
    /// sensible; anything not listed is a misdemeanour with standard bail.
    /// </summary>
    public static class ChargeBook
    {
        private const int MisdemeanourBail = 1000, FelonyBail = 25000;

        // (keyword, felony, bail) — first match wins, so the more specific wording comes first.
        private static readonly (string key, bool felony, int bail)[] Table =
        {
            ("murder", true, 1000000), ("homicide", true, 1000000), ("manslaughter", true, 250000),
            ("kidnap", true, 250000), ("carjack", true, 100000), ("armed robbery", true, 100000), ("robbery", true, 75000),
            ("assault on a police officer", true, 50000), ("aggravated", true, 50000), ("assault with", true, 50000),
            ("firearm", true, 35000), ("weapon", true, 35000), ("burglary", true, 40000),
            ("grand theft", true, 25000), ("auto theft", true, 25000), ("stolen vehicle", true, 25000), ("vehicle theft", true, 25000),
            ("possession with intent", true, 50000), ("trafficking", true, 100000), ("arson", true, 75000),
            ("evading", true, 15000), ("fleeing", true, 15000), ("felony", true, FelonyBail),
            ("dui", false, 2500), ("dwi", false, 2500), ("driving under", false, 2500), ("reckless", false, 2000),
            ("resisting", false, 2500), ("obstruct", false, 1500), ("assault", false, 5000), ("battery", false, 5000),
            ("petty theft", false, 1000), ("shoplift", false, 500), ("theft", false, 1500), ("vandal", false, 1500), ("criminal mischief", false, 1500),
            ("trespass", false, 500), ("disorderly", false, 500), ("public intox", false, 500), ("loiter", false, 250),
            ("possession", false, 2500), ("suspended license", false, 1000), ("driving without", false, 500),
        };

        /// <summary>Class and bail for one charge. "Warrant: X" uses the warrant's own felony flag when the citizen is known.</summary>
        public static ArrestCount Classify(string charge, Citizen citizen = null)
        {
            string c = charge ?? "";
            const string warrantPrefix = "Warrant: ";
            if (c.StartsWith(warrantPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string offense = c.Substring(warrantPrefix.Length);
                bool felony = Lookup(offense).felony;
                if (citizen != null)
                    foreach (var w in citizen.Warrants)
                        if (string.Equals(w.Offense, offense, StringComparison.OrdinalIgnoreCase)) { felony = w.Felony; break; }
                // Warrant arrests are held on the warrant's bail, set higher than a fresh charge.
                return new ArrestCount { Charge = charge, Felony = felony, Bail = felony ? 50000 : 2500 };
            }
            var hit = Lookup(c);
            return new ArrestCount { Charge = charge, Felony = hit.felony, Bail = hit.bail };
        }

        public static List<ArrestCount> ClassifyAll(IEnumerable<string> charges, Citizen citizen, out int totalBail)
        {
            var list = new List<ArrestCount>();
            totalBail = 0;
            foreach (string ch in charges) { var n = Classify(ch, citizen); list.Add(n); totalBail += n.Bail; }
            return list;
        }

        private static (bool felony, int bail) Lookup(string charge)
        {
            foreach (var (key, felony, bail) in Table)
                if (charge.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return (felony, bail);
            return (false, MisdemeanourBail);
        }
    }
}
