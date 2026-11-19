namespace GPA.Core
{
    /// <summary>A civilian car's identity, handed to the traffic system when it spawns one.</summary>
    public struct TrafficVehicleInfo
    {
        public string VehicleId;
        public string Plate;
        public string Description;     // "Red Bayline Meridian"
        public string SpriteName;      // which car sprite to draw
        public string DriverCitizenId;
    }

    /// <summary>
    /// The persistent population as seen by world systems (pedestrians, traffic, callouts) without
    /// referencing the records assembly. The MDT and traffic stops use the full records service.
    /// </summary>
    public interface IRecordsDirectory
    {
        bool Ready { get; }
        /// <summary>A citizen to "be" a pedestrian wearing outfit <paramref name="lookIndex"/>.</summary>
        string PickCitizenForPedestrian(int lookIndex);
        /// <summary>A registered vehicle (and a driver for it) for a traffic car.</summary>
        bool TryPickTrafficVehicle(out TrafficVehicleInfo info);
        /// <summary>Flags a random vehicle drawn with <paramref name="spriteName"/> as stolen (for a callout) and returns it.</summary>
        bool TryReportStolen(string spriteName, out TrafficVehicleInfo info);
        string DescribeCitizen(string citizenId);
        /// <summary>Citizens who live in building <paramref name="buildingId"/> (ids and outfit indices). Returns the count added.</summary>
        int ResidentsOf(string buildingId, System.Collections.Generic.List<string> citizenIds, System.Collections.Generic.List<int> looks);
    }
}
