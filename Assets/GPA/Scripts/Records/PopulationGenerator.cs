using System;
using System.Collections.Generic;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Records
{
    /// <summary>
    /// Builds the fictional population once (fixed seed): citizens living at the city's residential
    /// addresses, their licences, vehicles, warrants and criminal history. Names come from public-
    /// domain US government lists (SSA given names, Census surnames) in Resources/Names.
    /// </summary>
    public static class PopulationGenerator
    {
        private static readonly string[] Makes = { "Aldridge", "Bayline", "Corvel", "Dunmore", "Everton", "Halden Motors" };
        private static readonly string[] Models = { "Meridian", "Courier", "Sable", "Vantage", "Crestline", "Sentra", "Harbor" };
        private static readonly string[] Hair = { "Black", "Brown", "Blonde", "Red", "Grey", "Bald" };
        private static readonly string[] Eyes = { "Brown", "Blue", "Green", "Hazel", "Grey" };
        private static readonly string[] Misdemeanors = { "Petty larceny", "Disorderly conduct", "Criminal trespass", "Possession of marijuana", "Driving while intoxicated", "Criminal mischief", "Harassment", "Unlicensed operation of a vehicle" };
        private static readonly string[] Felonies = { "Burglary in the 2nd degree", "Grand larceny of a vehicle", "Assault in the 2nd degree", "Robbery in the 3rd degree", "Criminal possession of a weapon", "Criminal sale of a controlled substance" };
        private static readonly string[] Dispositions = { "Convicted", "Convicted", "Pled guilty", "Dismissed", "Acquitted" };

        // Civilian car sprites and the colour each is painted.
        private static readonly (string sprite, string colour)[] CarLooks =
        {
            ("car_sedan_maroon", "Red"), ("car_sedan_gunmetal", "Grey"), ("car_sedan_olive", "Green"), ("car_taxi", "Yellow"),
        };

        public static RecordsData Generate(int seed, int citizens, int lookCount, CityLocationDatabase locations, DateTime today)
        {
            var rng = new System.Random(seed);
            var data = new RecordsData { Seed = seed, Generated = today.ToString("yyyy-MM-dd"), GameDate = today.ToString("yyyy-MM-dd") };

            string[] male = LoadList("Names/first_male"), female = LoadList("Names/first_female"), last = LoadList("Names/surnames");
            var homes = new List<CityLocationDatabase.Building>();
            if (locations != null)
                foreach (var b in locations.Buildings)
                    if (b.Type == "apartment" || b.Type == "shop" || b.Type == "restaurant") homes.Add(b);

            var usedPlates = new HashSet<string>();
            for (int i = 0; i < citizens; i++)
            {
                bool isMale = rng.NextDouble() < 0.5;
                var home = homes.Count > 0 ? homes[rng.Next(homes.Count)] : null;
                int age = 17 + (int)(Math.Pow(rng.NextDouble(), 1.3) * 66);
                DateTime dob = today.AddYears(-age).AddDays(-rng.Next(365));

                var c = new Citizen
                {
                    Id = $"C-{i + 1:000000}",
                    FirstName = Pick(rng, isMale ? male : female, isMale ? "John" : "Jane"),
                    LastName = Pick(rng, last, "Doe"),
                    Sex = isMale ? "M" : "F",
                    DateOfBirth = dob.ToString("yyyy-MM-dd"),
                    Address = home != null ? $"{home.Address}, Apt {1 + rng.Next(Math.Max(1, home.Floors))}{(char)('A' + rng.Next(4))}" : "No fixed address",
                    BuildingId = home?.Id,
                    HeightCm = isMale ? 165 + rng.Next(30) : 152 + rng.Next(25),
                    WeightKg = isMale ? 62 + rng.Next(55) : 48 + rng.Next(45),
                    Hair = Hair[rng.Next(Hair.Length)],
                    Eyes = Eyes[rng.Next(Eyes.Length)],
                    LookIndex = lookCount > 0 ? rng.Next(lookCount) : 0,
                };

                // Driver licence.
                double l = rng.NextDouble();
                c.License.Number = $"{rng.Next(100, 999)} {rng.Next(100, 999)} {rng.Next(100, 999)}";
                c.License.Status = age < 18 && l < 0.6 ? LicenseStatus.None
                    : l < 0.84 ? LicenseStatus.Valid : l < 0.90 ? LicenseStatus.Suspended : l < 0.92 ? LicenseStatus.Revoked : l < 0.97 ? LicenseStatus.Expired : LicenseStatus.None;
                c.License.Expires = (c.License.Status == LicenseStatus.Expired ? today.AddDays(-rng.Next(30, 900)) : today.AddDays(rng.Next(30, 2900))).ToString("yyyy-MM-dd");
                if (c.License.Status == LicenseStatus.None) c.License.Number = "";

                // Criminal history and warrants.
                if (rng.NextDouble() < 0.16)
                {
                    int priors = 1 + rng.Next(3);
                    for (int p = 0; p < priors; p++)
                    {
                        bool felony = rng.NextDouble() < 0.3;
                        c.CriminalHistory.Add(new HistoryEntry
                        {
                            Date = today.AddDays(-rng.Next(60, 4000)).ToString("yyyy-MM-dd"),
                            Offense = felony ? Felonies[rng.Next(Felonies.Length)] : Misdemeanors[rng.Next(Misdemeanors.Length)],
                            Disposition = Dispositions[rng.Next(Dispositions.Length)],
                        });
                    }
                }
                if (rng.NextDouble() < (c.CriminalHistory.Count > 0 ? 0.18 : 0.02))
                {
                    bool felony = rng.NextDouble() < 0.35;
                    c.Warrants.Add(new Warrant
                    {
                        Id = $"W-{rng.Next(100000, 999999)}",
                        Felony = felony,
                        Offense = felony ? Felonies[rng.Next(Felonies.Length)] : "Failure to appear — " + Misdemeanors[rng.Next(Misdemeanors.Length)].ToLowerInvariant(),
                        Issued = today.AddDays(-rng.Next(5, 700)).ToString("yyyy-MM-dd"),
                    });
                }
                data.Citizens.Add(c);

                // Vehicles: roughly half of adults own one.
                if (age >= 18 && rng.NextDouble() < 0.55)
                {
                    var look = CarLooks[rng.Next(CarLooks.Length)];
                    var v = new VehicleRecord
                    {
                        Id = $"V-{data.Vehicles.Count + 1:000000}",
                        Make = look.sprite == "car_taxi" ? "Halden Motors" : Makes[rng.Next(Makes.Length)],
                        Model = look.sprite == "car_taxi" ? "Metro Cab" : Models[rng.Next(Models.Length)],
                        Color = look.colour,
                        Year = today.Year - rng.Next(0, 22),
                        Plate = UniquePlate(rng, usedPlates),
                        Vin = Vin(rng),
                        OwnerId = c.Id,
                        SpriteName = look.sprite,
                    };
                    double r = rng.NextDouble();
                    v.Registration = r < 0.93 ? RegistrationStatus.Valid : r < 0.98 ? RegistrationStatus.Expired : RegistrationStatus.Suspended;
                    v.RegistrationExpires = (v.Registration == RegistrationStatus.Expired ? today.AddDays(-rng.Next(10, 400)) : today.AddDays(rng.Next(20, 700))).ToString("yyyy-MM-dd");
                    v.Insured = rng.NextDouble() > 0.04;
                    v.Stolen = rng.NextDouble() < 0.015;
                    c.VehicleIds.Add(v.Id);
                    data.Vehicles.Add(v);
                }
            }

            foreach (var v in data.Vehicles)
                if (v.Stolen)
                    data.Bolos.Add(new Bolo
                    {
                        Id = $"B-{data.Bolos.Count + 1:0000}", Created = today.AddDays(-rng.Next(1, 30)).ToString("yyyy-MM-dd"),
                        Subject = $"{v.Description}, plate {v.Plate}", Reason = "Reported stolen", VehicleId = v.Id,
                    });
            return data;
        }

        private static string[] LoadList(string resource)
        {
            var asset = Resources.Load<TextAsset>(resource);
            if (asset == null) return new string[0];
            return asset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string Pick(System.Random rng, string[] list, string fallback) => list.Length > 0 ? list[rng.Next(list.Length)].Trim() : fallback;

        public static string UniquePlate(System.Random rng, HashSet<string> used)
        {
            const string letters = "ABCDEFGHJKLMNPRSTUVWXYZ";
            for (;;)
            {
                string plate = $"{letters[rng.Next(letters.Length)]}{letters[rng.Next(letters.Length)]}{letters[rng.Next(letters.Length)]}-{rng.Next(1000, 9999)}";
                if (used.Add(plate)) return plate;
            }
        }

        private static string Vin(System.Random rng)
        {
            const string chars = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";
            var c = new char[17];
            for (int i = 0; i < c.Length; i++) c[i] = chars[rng.Next(chars.Length)];
            return new string(c);
        }
    }
}
