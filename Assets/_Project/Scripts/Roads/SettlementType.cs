namespace MedievalZen.Roads
{
    /// <summary>Kind of settlement a road can lead to.</summary>
    public enum SettlementType
    {
        /// <summary>Largest settlement; a handful per world.</summary>
        Town = 0,

        /// <summary>Small cluster of cabins around a well.</summary>
        Village = 1,

        /// <summary>Roadside inn where the traveller can rest until morning.</summary>
        Inn = 2,
    }
}
