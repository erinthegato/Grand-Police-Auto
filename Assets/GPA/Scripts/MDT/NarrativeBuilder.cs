using System;
using System.Collections.Generic;
using System.Text;
using GPA.Core;
using GPA.Records;

namespace GPA.Mdt
{
    /// <summary>A category of ready-made sentences the officer can tap into a narrative.</summary>
    internal sealed class PhraseCategory
    {
        public readonly string Name;
        public readonly string[] Phrases;
        public PhraseCategory(string name, params string[] phrases) { Name = name; Phrases = phrases; }
    }

    /// <summary>
    /// Writes incident report titles and narratives from what the records already know: the report
    /// type, place and time, the people (identity, license, warrants) and vehicles (registration,
    /// insurance, stolen) attached to it, citations and arrests written for them today, and how a
    /// matching traffic stop ended. Pure text generation — no UI and no scene access.
    /// </summary>
    internal static class NarrativeBuilder
    {
        public static readonly PhraseCategory[] Phrases =
        {
            new PhraseCategory("Observed",
                "I observed {vehicle} travelling above the posted speed limit.",
                "I observed {vehicle} fail to stop for a red signal.",
                "I observed {vehicle} fail to stop at a stop sign.",
                "I observed {vehicle} weaving within its lane.",
                "I observed that {vehicle} had a defective tail light.",
                "I observed {vehicle} make an unsafe lane change.",
                "{driver} appeared nervous and avoided eye contact.",
                "I detected an odor of alcohol coming from {driver}."),
            new PhraseCategory("Contact",
                "I approached the vehicle and made contact with {driver}.",
                "I explained the reason for the stop.",
                "{driver} was cooperative throughout the contact.",
                "{driver} was argumentative but complied with instructions.",
                "{driver} refused to provide identification.",
                "{driver} provided a valid driver's license and proof of insurance.",
                "{driver} stated they were unaware of the violation."),
            new PhraseCategory("Actions",
                "I requested a records check through dispatch.",
                "I conducted a pat-down for weapons with negative results.",
                "I conducted a search of the vehicle incident to arrest.",
                "I requested an additional unit for backup.",
                "{driver} was detained pending the investigation.",
                "{driver} was handcuffed, checked for fit and double locked.",
                "I advised {driver} of their rights."),
            new PhraseCategory("Disposition",
                "{driver} was released with a verbal warning.",
                "{driver} was issued a citation and released at the scene.",
                "{driver} was arrested and transported for booking.",
                "The vehicle was released to the registered owner.",
                "The vehicle was towed and impounded.",
                "No further action was taken."),
        };

        public static string BuildTitle(IncidentReport r, RecordsDatabase db)
        {
            string type = string.IsNullOrEmpty(r.Type) || r.Type == "General" ? "Incident" : r.Type;
            var car = r.VehicleIds.Count > 0 ? db.GetVehicle(r.VehicleIds[0]) : null;
            var person = r.CitizenIds.Count > 0 ? db.GetCitizen(r.CitizenIds[0]) : null;
            string subject = car != null ? car.Plate : person != null ? person.FullName : null;
            string place = Street(r.Location);
            var parts = new List<string> { type };
            if (subject != null) parts.Add(subject);
            if (!string.IsNullOrEmpty(place)) parts.Add(place);
            return string.Join(" - ", parts);
        }

        public static string Build(IncidentReport r, RecordsDatabase db, StopRecord stop)
        {
            var people = new List<Citizen>();
            foreach (string id in r.CitizenIds) { var c = db.GetCitizen(id); if (c != null) people.Add(c); }
            var cars = new List<VehicleRecord>();
            foreach (string id in r.VehicleIds) { var v = db.GetVehicle(id); if (v != null) cars.Add(v); }

            DateTime when = stop?.Time ?? (DateTime.TryParse(r.Created, out var created) ? created : DateTime.Now);
            string officer = string.IsNullOrEmpty(r.Officer) ? "the reporting officer" : r.Officer;
            string location = string.IsNullOrWhiteSpace(r.Location) ? stop?.Location : r.Location;
            string at = string.IsNullOrWhiteSpace(location) ? "" : $" at {location}";
            string type = string.IsNullOrEmpty(r.Type) ? "General" : r.Type;
            var car0 = cars.Count > 0 ? cars[0] : null;
            var sb = new StringBuilder();

            // Opening.
            sb.Append($"On {when:MMMM d, yyyy} at approximately {when:HHmm} hours, I ({officer}) ");
            switch (type)
            {
                case "Traffic Stop":
                    sb.Append(car0 != null ? $"conducted a traffic stop on {VehicleArticle(car0)}, plate {car0.Plate},{at}." : $"conducted a traffic stop{at}.");
                    var reasons = CitationOffenses(db, people, when);
                    if (reasons.Count > 0) sb.Append($" The vehicle was stopped for {JoinList(reasons).ToLowerInvariant()}.");
                    break;
                case "Traffic Collision":
                    sb.Append($"responded to a reported traffic collision{at}.");
                    if (cars.Count > 0) sb.Append($" {Capitalise(Count(cars.Count, "vehicle"))} {(cars.Count == 1 ? "was" : "were")} involved.");
                    break;
                case "Arrest":
                    sb.Append(people.Count > 0 ? $"arrested {people[0].FullName}{at}." : $"made an arrest{at}.");
                    break;
                case "Stolen Vehicle":
                    sb.Append($"responded{at} regarding a stolen vehicle.");
                    break;
                case "Welfare Check":
                    sb.Append($"conducted a welfare check{at}.");
                    break;
                case "General":
                case "Other":
                    sb.Append($"responded to a call for service{at}.");
                    break;
                default:
                    sb.Append($"responded{at} regarding a report of {type.ToLowerInvariant()}.");
                    break;
            }

            // People.
            for (int i = 0; i < people.Count; i++)
            {
                var c = people[i];
                sb.Append("\n\n");
                sb.Append($"{Role(type, c, i, stop, cars)} was identified as {c.FullName} (DOB {c.DateOfBirth}, age {Age(c.DateOfBirth)}) of {c.Address}. ");
                sb.Append(LicenseSentence(c)).Append(' ');
                if (c.Warrants.Count == 0) sb.Append("No active warrants were found.");
                else
                {
                    var list = new List<string>();
                    foreach (var w in c.Warrants) list.Add($"{w.Offense.ToLowerInvariant()} ({(w.Felony ? "felony" : "misdemeanor")})");
                    sb.Append($"{c.FirstName} has {Count(c.Warrants.Count, "active warrant")}: {JoinList(list)}.");
                }
                if (c.CriminalHistory.Count > 0) sb.Append($" {c.FirstName} has {Count(c.CriminalHistory.Count, "prior entry", "prior entries")} in their criminal history.");
                foreach (var b in db.ActiveBolosFor(c.Id, null)) sb.Append($" {c.FirstName} is the subject of an active BOLO ({b.Reason.ToLowerInvariant()}).");
            }

            // Vehicles.
            foreach (var v in cars)
            {
                sb.Append("\n\n");
                var owner = db.GetCitizen(v.OwnerId);
                sb.Append($"Plate {v.Plate} returned to {VehicleArticle(v)} registered to {(owner != null ? owner.FullName : "an unknown owner")}. ");
                switch (v.Registration)
                {
                    case RegistrationStatus.Valid: sb.Append($"The registration is valid through {v.RegistrationExpires}."); break;
                    case RegistrationStatus.Expired: sb.Append($"The registration expired on {v.RegistrationExpires}."); break;
                    default: sb.Append("The registration is suspended."); break;
                }
                sb.Append(v.Insured ? " Insurance is on file." : " No proof of insurance is on file.");
                if (v.Stolen) sb.Append(" The vehicle is listed as STOLEN.");
                foreach (var b in db.ActiveBolosFor(null, v.Id)) sb.Append($" The vehicle is the subject of an active BOLO ({b.Reason.ToLowerInvariant()}).");
                if (owner != null && type == "Traffic Stop" && !people.Exists(p => p.Id == owner.Id)) sb.Append(" The registered owner was not present.");
            }

            // What the officer did, from the records written for these people today.
            var actions = new List<string>();
            string day = when.ToString("yyyy-MM-dd");
            foreach (var c in people)
            {
                foreach (var ct in db.CitationsOf(c.Id))
                    if (ct.Date != null && ct.Date.StartsWith(day)) actions.Add($"I issued citation {ct.Id} to {c.FullName} for {ct.Offense.ToLowerInvariant()} (fine ${ct.Fine}).");
                foreach (var a in db.ArrestsOf(c.Id))
                    if (a.Date != null && a.Date.StartsWith(day)) actions.Add($"{c.FullName} was placed under arrest for {JoinList(a.Charges).ToLowerInvariant()} and transported for booking.");
            }
            string outcome = stop != null ? OutcomeSentence(stop.Outcome, actions.Count > 0) : null;
            if (outcome != null) actions.Add(outcome);
            if (actions.Count == 0) actions.Add("No further action was taken.");
            sb.Append("\n\n").Append(string.Join(" ", actions));

            return sb.ToString();
        }

        /// <summary>Fills a phrase's {driver} / {vehicle} / {plate} from the report's first person and vehicle.</summary>
        public static string FillPhrase(string template, IncidentReport r, RecordsDatabase db)
        {
            var person = r.CitizenIds.Count > 0 ? db.GetCitizen(r.CitizenIds[0]) : null;
            var car = r.VehicleIds.Count > 0 ? db.GetVehicle(r.VehicleIds[0]) : null;
            string text = template
                .Replace("{driver}", person != null ? person.FullName : "the subject")
                .Replace("{vehicle}", car != null ? $"the {car.Color.ToLowerInvariant()} {car.Make} {car.Model}" : "the vehicle")
                .Replace("{plate}", car != null ? car.Plate : "the plate");
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        /// <summary>Appends a sentence to a narrative with sensible spacing.</summary>
        public static string Append(string narrative, string sentence)
        {
            if (string.IsNullOrEmpty(narrative)) return sentence;
            if (narrative.EndsWith("\n") || narrative.EndsWith(" ")) return narrative + sentence;
            return narrative + " " + sentence;
        }

        // ------------------------------------------------------------------ helpers

        private static string Role(string type, Citizen c, int index, StopRecord stop, List<VehicleRecord> cars)
        {
            if (type == "Traffic Stop" && ((stop != null && stop.CitizenId == c.Id) || (stop == null && index == 0 && cars.Count > 0))) return "The driver";
            if (type == "Arrest" && index == 0) return "The arrestee";
            return index == 0 ? "The involved party" : "An additional party";
        }

        private static string LicenseSentence(Citizen c)
        {
            switch (c.License?.Status ?? LicenseStatus.None)
            {
                case LicenseStatus.Valid: return $"A records check showed a valid class {c.License.Class} driver's license, number {c.License.Number}.";
                case LicenseStatus.None: return "A records check found no driver's license on file.";
                default: return $"A records check showed that {c.FirstName}'s driver's license is {c.License.Status.ToString().ToLowerInvariant()}.";
            }
        }

        private static string OutcomeSentence(StopOutcome outcome, bool recordsCovered)
        {
            switch (outcome)
            {
                case StopOutcome.Released: return "The driver was released without further action.";
                case StopOutcome.Warning: return "The driver was released with a verbal warning.";
                case StopOutcome.Citation: return recordsCovered ? "The driver was released at the scene." : "The driver was issued a citation and released at the scene.";
                case StopOutcome.Arrest: return recordsCovered ? null : "The driver was taken into custody.";
                case StopOutcome.Fled: return "The driver fled the stop. I notified dispatch and a pursuit was initiated.";
                case StopOutcome.Lost: return "Contact with the vehicle was lost.";
                default: return "The stop was cancelled before contact was completed.";
            }
        }

        private static List<string> CitationOffenses(RecordsDatabase db, List<Citizen> people, DateTime when)
        {
            var list = new List<string>();
            string day = when.ToString("yyyy-MM-dd");
            foreach (var c in people)
                foreach (var ct in db.CitationsOf(c.Id))
                    if (ct.Date != null && ct.Date.StartsWith(day) && !list.Contains(ct.Offense)) list.Add(ct.Offense);
            return list;
        }

        private static string VehicleArticle(VehicleRecord v)
        {
            string d = $"{v.Color.ToLowerInvariant()} {v.Year} {v.Make} {v.Model}";
            return ("aeiou".IndexOf(d[0]) >= 0 ? "an " : "a ") + d;
        }

        private static string Street(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) return null;
            int comma = location.IndexOf(',');
            return comma > 0 ? location.Substring(0, comma) : location;
        }

        private static string JoinList(IList<string> items)
        {
            if (items.Count == 0) return "";
            if (items.Count == 1) return items[0];
            if (items.Count == 2) return items[0] + " and " + items[1];
            var copy = new List<string>(items);
            string last = copy[copy.Count - 1];
            copy.RemoveAt(copy.Count - 1);
            return string.Join(", ", copy) + ", and " + last;
        }

        private static string Count(int n, string singular, string plural = null)
        {
            string[] words = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };
            string number = n < words.Length ? words[n] : n.ToString();
            return $"{number} {(n == 1 ? singular : plural ?? singular + "s")}";
        }

        private static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static int Age(string dob)
        {
            if (!DateTime.TryParse(dob, out var d)) return 0;
            var today = DateTime.Today;
            int age = today.Year - d.Year;
            if (today < d.AddYears(age)) age--;
            return age;
        }
    }
}
