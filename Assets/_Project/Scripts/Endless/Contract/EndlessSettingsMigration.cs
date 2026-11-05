using System;
using System.Collections.Generic;

namespace MedievalZen.Endless
{
    /// <summary>
    /// One-time upgrades of the endless settings asset (pure; applied by the editor's <c>EndlessRoadMenu</c> to the asset already
    /// loaded, which keeps its old values across script reloads whatever the fields' new defaults are; see CLAUDE.md).
    /// <para>Version 1 (performance pass, 2026-10-05): the tree shadow distance moves from the old 90 m to 300 m if it still holds it
    /// (the 2026-10-02 rule, folded in); the new <c>undergrowthDrawDistance</c> gets 250 m when it holds nothing valid; the shadow-caster
    /// culling and the reuse of the renderer's pass are switched on (new fields); all of it is then written to the asset.</para>
    /// <para>Version 2 (second performance pass, 2026-10-06): <c>treeImpostorDistance</c> 420 m and <c>treeImpostorMaxPixels</c> 3 px
    /// where they hold nothing valid (0, the value of a field the asset did not have).</para>
    /// <para>Version 3 (impostor pass, 2026-10-06): lobed impostors match their meshes within 3 px from about 310-370 m, so the frame's
    /// impostor distance moves from the old default 420 m to 320 m (each prototype still swaps no nearer than its own 3 px distance,
    /// and never inside the 300 m band); a hand-tuned value survives.</para>
    /// </summary>
    public static class EndlessSettingsMigration
    {
        /// <summary>The version the asset is brought to.</summary>
        public const int Current = 3;

        /// <summary>Version 2 (second performance pass, 2026-10-06): the impostor distance and pixel rule a fresh or invalid asset gets.</summary>
        public const float ImpostorDistance = 420f, MaxImpostorDistance = 900f, ImpostorMaxPixels = 3f, MaxImpostorPixels = 8f;

        /// <summary>Version 3 (impostor pass, 2026-10-06): the frame's impostor distance moves from <see cref="ImpostorDistance"/> to this.</summary>
        public const float LobedImpostorDistance = 320f;

        /// <summary>The old tree shadow distance (before 2026-10-02) and the new one.</summary>
        public const float OldTreeShadowDistance = 90f, NewTreeShadowDistance = 300f;

        /// <summary>The undergrowth draw distance a fresh or invalid asset gets, and its valid range.</summary>
        public const float UndergrowthDrawDistance = 250f, MinUndergrowth = 60f, MaxUndergrowth = 420f;

        /// <summary>The settings the upgrades touch.</summary>
        public struct Values
        {
            public int Version;
            public float TreeShadowDistance;
            public float UndergrowthDrawDistance;
            public bool ShadowCasterCulling;
            public bool WalkReuse;
            public float ImpostorDistance;
            public float ImpostorMaxPixels;
        }

        /// <summary>
        /// Brings <paramref name="v"/> to <see cref="Current"/>, describing each change in <paramref name="changes"/>; true when the asset must
        /// be saved (the version moved). A value tuned by hand survives (only the old default or an invalid value is replaced).
        /// </summary>
        public static bool Upgrade(ref Values v, List<string> changes)
        {
            if (v.Version >= Current) return false;
            if (v.Version < 1)
            {
                if (Math.Abs(v.TreeShadowDistance - OldTreeShadowDistance) < 1e-3f)
                {
                    v.TreeShadowDistance = NewTreeShadowDistance;
                    changes?.Add($"tree shadow distance {OldTreeShadowDistance} → {NewTreeShadowDistance} m");
                }

                if (float.IsNaN(v.UndergrowthDrawDistance) || v.UndergrowthDrawDistance < MinUndergrowth || v.UndergrowthDrawDistance > MaxUndergrowth)
                {
                    changes?.Add($"undergrowth draw distance {v.UndergrowthDrawDistance} → {UndergrowthDrawDistance} m");
                    v.UndergrowthDrawDistance = UndergrowthDrawDistance;
                }

                if (!v.ShadowCasterCulling) changes?.Add("shadow-caster culling on");
                if (!v.WalkReuse) changes?.Add("reuse of the tree pass on");
                v.ShadowCasterCulling = true;
                v.WalkReuse = true;
            }

            if (v.Version < 2)
            {
                // New fields: a missing field normally loads with its initialiser, but a 0 (or an out-of-range value) left by an
                // earlier script version would mean "never"; replace only those, keep anything tuned by hand.
                if (float.IsNaN(v.ImpostorDistance) || v.ImpostorDistance <= 0f || v.ImpostorDistance > MaxImpostorDistance)
                {
                    changes?.Add($"tree impostor distance {v.ImpostorDistance} → {ImpostorDistance} m");
                    v.ImpostorDistance = ImpostorDistance;
                }

                if (float.IsNaN(v.ImpostorMaxPixels) || v.ImpostorMaxPixels <= 0f || v.ImpostorMaxPixels > MaxImpostorPixels)
                {
                    changes?.Add($"tree impostor pixel rule {v.ImpostorMaxPixels} → {ImpostorMaxPixels} px");
                    v.ImpostorMaxPixels = ImpostorMaxPixels;
                }
            }

            if (v.Version < 3)
            {
                if (Math.Abs(v.ImpostorDistance - ImpostorDistance) < 1e-3f)
                {
                    changes?.Add($"tree impostor distance {ImpostorDistance} → {LobedImpostorDistance} m (lobed impostors)");
                    v.ImpostorDistance = LobedImpostorDistance;
                }
            }

            v.Version = Current;
            return true;
        }
    }
}
