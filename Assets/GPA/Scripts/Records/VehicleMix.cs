using GPA.TopDown2D;

namespace GPA.Records
{
    /// <summary>
    /// Gives the registered vehicles a realistic mix of body types (compacts, SUVs, pickups, delivery
    /// vans, box trucks) from the vehicle catalog. Runs once on an existing records file: plates,
    /// owners and histories are kept, only the make-up of the car (type, paint, model) changes, so
    /// the MDT description always matches what's on the road.
    /// </summary>
    internal static class VehicleMix
    {
        private static readonly (string id, float upTo)[] Shares =
        {
            ("compact", 0.16f), ("suv", 0.36f), ("pickup", 0.50f), ("delivery_van", 0.56f), ("box_truck", 0.60f),
        };

        public static bool Apply(RecordsData data)
        {
            var catalog = VehicleCatalog.Instance;
            if (data?.Vehicles == null || catalog == null) return false;
            foreach (var v in data.Vehicles)
            {
                var spec = catalog.BySprite(v.SpriteName, out _);
                if (spec != null && spec.Id != "sedan" && spec.Id != "taxi") return false;   // already mixed
            }

            bool changed = false;
            foreach (var v in data.Vehicles)
            {
                if (v.SpriteName == "car_taxi") continue;
                var rng = new System.Random(Hash(v.Id));
                double roll = rng.NextDouble();
                string id = null;
                foreach (var s in Shares) if (roll < s.upTo) { id = s.id; break; }
                if (id == null) continue;
                var spec = catalog.Get(id);
                if (spec == null || spec.Sprites.Length == 0) continue;
                int paint = rng.Next(spec.Sprites.Length);
                v.SpriteName = spec.Sprites[paint].name;
                if (paint < spec.ColourNames.Length) v.Color = spec.ColourNames[paint];
                if (spec.Models.Length > 0) v.Model = spec.Models[rng.Next(spec.Models.Length)];
                changed = true;
            }
            return changed;
        }

        private static int Hash(string s)
        {
            unchecked { int h = 23; foreach (char c in s ?? "") h = h * 31 + c; return h; }
        }
    }
}
