using System.Collections.Generic;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The rules of the arrival warm-up (pure, unit tested): the per-frame budget, the glyphs every text may need.
    /// </summary>
    public static class ArrivalWarmupRules
    {
        /// <summary>Main-thread milliseconds per frame the warm-up may take behind a black screen (it stops after the item that crosses it).</summary>
        public const float BudgetMs = 5f;

        /// <summary>Most frames the warm-up runs (all its black holds together) before it gives up on what never became ready.</summary>
        public const int MaxFrames = 600;

        /// <summary>Most instant items (a clip's load request, a glyph set) per frame.</summary>
        public const int InstantPerFrame = 32;

        /// <summary>
        /// The characters requested for every text size and style in use: printable ASCII and the few typographic and accented letters
        /// the HUD, the journal and the place names use. Requested once, so the shared dynamic font atlas grows (rebuilds) behind the
        /// hold instead of on the road.
        /// </summary>
        public const string Glyphs =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "·—–’‘“”…°éèêëáàâäíîïóôöúûüçñÉÀ";

        /// <summary>True when another item may start this frame: always the first, then while under <see cref="BudgetMs"/>.</summary>
        public static bool MayContinue(float elapsedMs, int itemsThisFrame) => itemsThisFrame == 0 || elapsedMs < BudgetMs;

        /// <summary>The pixel sizes a uGUI text of <paramref name="fontSize"/> may be laid out at on a canvas scaled by <paramref name="scale"/>.</summary>
        public static void Sizes(int fontSize, float scale, List<int> into)
        {
            into.Clear();
            if (fontSize <= 0) return;
            into.Add(fontSize);
            if (!(scale > 0f) || float.IsInfinity(scale)) return;
            float scaled = fontSize * scale;
            int down = (int)System.Math.Floor(scaled), up = (int)System.Math.Ceiling(scaled);
            if (down > 0 && !into.Contains(down)) into.Add(down);
            if (up > 0 && !into.Contains(up)) into.Add(up);
        }
    }
}
