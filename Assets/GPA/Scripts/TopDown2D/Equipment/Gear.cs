namespace GPA.TopDown2D
{
    /// <summary>Everything the officer can hold. Long guns live in the cruiser's trunk until taken.</summary>
    public enum Gear { Unarmed, Taser, Baton, PepperSpray, Handcuffs, Pistol, Rifle, Shotgun }

    public static class GearInfo
    {
        public static string Name(Gear g) => g switch
        {
            Gear.Unarmed => "Unarmed",
            Gear.Taser => "Taser",
            Gear.Baton => "Baton",
            Gear.PepperSpray => "Pepper spray",
            Gear.Handcuffs => "Handcuffs",
            Gear.Pistol => "Pistol",
            Gear.Rifle => "Patrol rifle",
            Gear.Shotgun => "Shotgun",
            _ => g.ToString(),
        };

        public static bool IsFirearm(Gear g) => g == Gear.Pistol || g == Gear.Rifle || g == Gear.Shotgun;
        public static bool IsLongGun(Gear g) => g == Gear.Rifle || g == Gear.Shotgun;
        /// <summary>Held out in front with both hands (otherwise arms at the sides).</summary>
        public static bool TwoHanded(Gear g) => g == Gear.Taser || g == Gear.PepperSpray || IsFirearm(g);
    }

    /// <summary>Published when the player handcuffs someone.</summary>
    public readonly struct PedCuffedEvent
    {
        public readonly Ped2D Ped;
        public readonly string CitizenId;
        public PedCuffedEvent(Ped2D ped) { Ped = ped; CitizenId = ped != null ? ped.CitizenId : null; }
    }
}
