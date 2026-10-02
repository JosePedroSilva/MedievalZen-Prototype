namespace MedievalZen.Endless
{
    /// <summary>The kinds of landscape the endless road passes through.</summary>
    public enum RegionKind
    {
        /// <summary>Open lowland meadows: flowers, scattered trees, long sight lines.</summary>
        Meadow = 0,

        /// <summary>Deep forest: dense trees, leaf litter, mist.</summary>
        Forest = 1,

        /// <summary>Lake country: the road skirts one or more lakes.</summary>
        LakeCountry = 2,

        /// <summary>Rolling downs: dry grass, rock outcrops, wide views.</summary>
        Downs = 3,

        /// <summary>A mountain valley climbing to a pass between rock walls, above the tree line at the top.</summary>
        HighlandPass = 4,

        /// <summary>Farmland: fields, fences, a hamlet or village on the road.</summary>
        Farmland = 5,
    }

    /// <summary>One stretch of a single kind of landscape along the road.</summary>
    public struct RegionInfo
    {
        /// <summary>Position of the region in the sequence (0 contains the journey's start; negative behind it).</summary>
        public int Index;

        /// <summary>Kind of landscape.</summary>
        public RegionKind Kind;

        /// <summary>Generated place name, e.g. "The Wexmoor Downs".</summary>
        public string Name;

        /// <summary>Road distance at which the region begins (metres).</summary>
        public double SStart;

        /// <summary>Road distance at which the region ends (metres).</summary>
        public double SEnd;
    }
}
