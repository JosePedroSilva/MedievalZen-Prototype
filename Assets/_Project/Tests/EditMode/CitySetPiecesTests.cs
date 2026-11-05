using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.World;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// The city set pieces mined from the Fantasy Kingdom demo city (2026-10-06): catalog references, bounds, counts, castles; seated
    /// (2026-10-09): every part yaw-only and held by the ground, another part or a wall, and placed so in every city of three seeds.
    /// </summary>
    public class CitySetPiecesTests
    {
        private static Dictionary<string, string[]> Families()
        {
            var map = new Dictionary<string, string[]>();
            foreach (var (family, paths) in SyntyCatalog.Families)
            {
                if (!map.ContainsKey(family)) map.Add(family, paths);
            }

            return map;
        }

        private static IEnumerable<(string owner, SetPart part)> AllParts()
        {
            foreach (CityVignette v in CitySetPieces.Vignettes)
            {
                foreach (SetPart p in v.Parts) yield return (v.Name, p);
            }

            foreach (CastlePlan c in CitySetPieces.Castles)
            {
                foreach (SetPart p in c.Parts) yield return (c.Name, p);
            }
        }

        [Test]
        public void EveryPartResolvesToItsPrefabInTheCatalog()
        {
            Dictionary<string, string[]> families = Families();
            int checkedParts = 0;
            foreach (var (owner, p) in AllParts())
            {
                Assert.That(families.ContainsKey(p.Role), Is.True, $"{owner}: family {p.Role} is not in the catalog");
                string[] paths = families[p.Role];
                Assert.That(p.Variant, Is.InRange(0, paths.Length - 1), $"{owner}: {p.Prefab} variant {p.Variant} of {p.Role}");
                Assert.That(Path.GetFileNameWithoutExtension(paths[p.Variant]), Is.EqualTo(p.Prefab), $"{owner}: {p.Role}[{p.Variant}]");
                bool instancedFamily = PropRole.IsCityPiece(p.Role) || PropRole.IsOvergrowth(p.Role);
                Assert.That(p.Instanced, Is.EqualTo(instancedFamily), $"{owner}: {p.Prefab} instanced flag");
                Assert.That(p.Scale, Is.InRange(0.3f, 2.5f), $"{owner}: {p.Prefab} scale");
                checkedParts++;
            }

            Assert.That(checkedParts, Is.GreaterThan(1000));
        }

        [Test]
        public void VignettePartsStayInsideTheirWidthAndDepth()
        {
            foreach (CityVignette v in CitySetPieces.Vignettes)
            {
                Assert.That(v.Parts.Length, Is.GreaterThanOrEqualTo(1), v.Name);
                Assert.That(v.Width, Is.InRange(1f, 12f), v.Name);
                Assert.That(v.Depth, Is.InRange(1f, 12f), v.Name);
                foreach (SetPart p in v.Parts)
                {
                    Assert.That(System.Math.Abs(p.X), Is.LessThanOrEqualTo(v.Width * 0.5f + 1e-3f), $"{v.Name}: {p.Prefab} x");
                    Assert.That(p.Z, Is.InRange(-1e-3f, v.Depth + 1e-3f), $"{v.Name}: {p.Prefab} z");
                    Assert.That(p.Y, Is.InRange(-3f, 8f), $"{v.Name}: {p.Prefab} y");
                }
            }
        }

        [Test]
        public void EveryKindHasSeveralVignettesWithUniqueNames()
        {
            IReadOnlyList<CityVignette> all = CitySetPieces.Vignettes;
            Assert.That(all.Count, Is.InRange(25, 50));
            Assert.That(all.Select(v => v.Name).Distinct().Count(), Is.EqualTo(all.Count));
            var expected = new Dictionary<VignetteKind, int>
            {
                { VignetteKind.Stall, 8 }, { VignetteKind.DoorSide, 7 }, { VignetteKind.Corner, 6 }, { VignetteKind.WallFoot, 8 },
                { VignetteKind.Plaza, 6 }, { VignetteKind.Yard, 4 }, { VignetteKind.Quay, 3 },
            };
            foreach (var pair in expected)
            {
                var list = new List<CityVignette>();
                CitySetPieces.Of(pair.Key, list);
                Assert.That(list.Count, Is.EqualTo(pair.Value), pair.Key.ToString());
            }

            var stalls = new List<CityVignette>();
            CitySetPieces.Of(VignetteKind.Stall, stalls);
            Assert.That(stalls.All(v => v.Parts.Any(p => p.Role == PropRole.MarketStall || p.Role == PropRole.CityStall)), Is.True, "every stall vignette has a stall");
        }

        [Test]
        public void CastlesHaveTheirStatedFootprintAndGate()
        {
            Assert.That(CitySetPieces.Castles.Count, Is.EqualTo(2));
            var names = CitySetPieces.Castles.Select(c => c.Name).ToList();
            Assert.That(names, Is.EquivalentTo(new[] { "DemoCastle", "DemoKeep" }));
            foreach (CastlePlan c in CitySetPieces.Castles)
            {
                // 2026-10-09 (Job D): the castle is 65 x 68 m (75 x 72 m with the lower ward, castle.py LOWER_WARD).
                Assert.That(2f * c.HalfWidth, Is.InRange(38f, 80f), $"{c.Name} width");
                Assert.That(2f * c.HalfDepth, Is.InRange(45f, 80f), $"{c.Name} depth");
                Assert.That(c.Height, Is.InRange(25f, 70f), $"{c.Name} height");
                Assert.That(c.GateZ, Is.GreaterThan(0f), $"{c.Name}: the gate faces +Z");
                Assert.That(System.Math.Abs(c.GateX), Is.LessThan(c.HalfWidth), $"{c.Name} gate x");
                Assert.That(c.GateZ, Is.LessThan(c.HalfDepth), $"{c.Name} gate z");
                Assert.That(c.Parts.Any(p => p.Prefab == "SM_Bld_Castle_Wall_Gate_01"), Is.True, $"{c.Name} has its gate");
                foreach (SetPart p in c.Parts)
                {
                    Assert.That(System.Math.Abs(p.X), Is.LessThanOrEqualTo(c.HalfWidth), $"{c.Name}: {p.Prefab} x");
                    Assert.That(System.Math.Abs(p.Z), Is.LessThanOrEqualTo(c.HalfDepth), $"{c.Name}: {p.Prefab} z");
                    Assert.That(p.Y, Is.InRange(-20f, c.Height), $"{c.Name}: {p.Prefab} y");
                }

                int instanced = c.Parts.Count(p => p.Instanced);
                Assert.That(instanced, Is.GreaterThan(c.Parts.Length * 8 / 10), $"{c.Name}: the repeated modules are instanced");
            }
        }

        // ------------------------------------------------------------------ the castle stands (2026-10-09, Job D)

        // The wall columns of a castle plan, as the mining tool's colfill.py sees them (keep the two in step): pieces grouped by pivot
        // (0.5 m bins); a column is outer when nothing else stands beyond it (more than 1 m) within 2.5 m of its line in one of the four
        // directions; it stands when its lowest base is at the court (Y <= 0.2) or a part not of the column, near it, has its top within
        // 0.3 m of that base (or, not being a column piece, spans it).
        private static readonly System.Text.RegularExpressions.Regex ColumnPiece = new System.Text.RegularExpressions.Regex("Wall|Pillar|Tower|Gate|Archway");
        private static readonly System.Text.RegularExpressions.Regex NotColumnPiece =
            new System.Text.RegularExpressions.Regex("Panel|Wood_Beams|Battlements|Roof|Hoarding|Corbels|Balcony|Cross|Floor|Stairs|Bridge|Walkway|Cap|Railing|Drawbridge");

        private static bool IsColumnPiece(string prefab) => ColumnPiece.IsMatch(prefab) && !NotColumnPiece.IsMatch(prefab);

        private static CastlePlan DemoCastle() => CitySetPieces.Castles.First(c => c.Name == "DemoCastle");

        private sealed class Column
        {
            public int KX, KZ;
            public readonly List<int> Parts = new List<int>();
            public float Base = float.MaxValue;
            public bool Outer, Held;
        }

        private static List<Column> Columns(CastlePlan c)
        {
            var map = new Dictionary<(int, int), Column>();
            for (int i = 0; i < c.Parts.Length; i++)
            {
                SetPart p = c.Parts[i];
                if (!IsColumnPiece(p.Prefab)) continue;
                var key = ((int)System.Math.Floor(p.X * 2f + 0.5f), (int)System.Math.Floor(p.Z * 2f + 0.5f));
                if (!map.TryGetValue(key, out Column col)) map.Add(key, col = new Column { KX = key.Item1, KZ = key.Item2 });
                col.Parts.Add(i);
                col.Base = System.Math.Min(col.Base, p.Y + p.Low);
            }

            var cols = map.Values.ToList();
            foreach (Column col in cols)
            {
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    bool blocked = cols.Any(o => o != col && (o.KX - col.KX) * dx + (o.KZ - col.KZ) * dz > 2 && System.Math.Abs((o.KX - col.KX) * dz - (o.KZ - col.KZ) * dx) <= 5);
                    if (!blocked)
                    {
                        col.Outer = true;
                        break;
                    }
                }

                var mine = new HashSet<int>(col.Parts);
                foreach (int lo in col.Parts.Where(i => c.Parts[i].Y + c.Parts[i].Low <= col.Base + 1e-3f))
                {
                    SetPart a = c.Parts[lo];
                    for (int j = 0; j < c.Parts.Length && !col.Held; j++)
                    {
                        if (mine.Contains(j)) continue;
                        SetPart q = c.Parts[j];
                        if (Mathf.Sqrt((q.X - a.X) * (q.X - a.X) + (q.Z - a.Z) * (q.Z - a.Z)) > q.Radius + a.Radius + 1e-3f) continue;
                        float top = q.Y + q.High, bottom = q.Y + q.Low;
                        if (System.Math.Abs(top - col.Base) <= 0.3f + 1e-3f ||
                            (!IsColumnPiece(q.Prefab) && bottom <= col.Base + 0.3f + 1e-3f && top >= col.Base - 0.3f - 1e-3f)) col.Held = true;
                    }
                }
            }

            return cols;
        }

        [Test]
        public void DemoCastleWallColumnsStandOnTheGround()
        {
            CastlePlan c = DemoCastle();
            List<Column> cols = Columns(c);
            Assert.That(cols.Count, Is.GreaterThan(150), "wall columns");
            Assert.That(cols.Count(k => k.Outer), Is.GreaterThan(40), "outer columns");
            foreach (Column col in cols)
            {
                if (col.Base <= 0.2f + 1e-3f) continue;
                SetPart lo = c.Parts[col.Parts.OrderBy(i => c.Parts[i].Y + c.Parts[i].Low).First()];
                Assert.That(col.Outer, Is.False, $"outer column at ({col.KX * 0.5f}, {col.KZ * 0.5f}) hangs: lowest {lo.Prefab} at {col.Base:0.00} m");
                Assert.That(col.Held, Is.True, $"column at ({col.KX * 0.5f}, {col.KZ * 0.5f}) has nothing within 0.3 m under {lo.Prefab} at {col.Base:0.00} m");
            }

            // The upper storeys stand on their floors, walkways and pillars again (the audit found them cut by the selection).
            foreach (string carried in new[] { "SM_Bld_Castle_Floor_Wood_01", "SM_Bld_Keep_Pillar_02", "SM_Bld_Castle_Pillar_Wood_Round_02", "SM_Bld_Walkway_Wood_Railing_01" })
            {
                Assert.That(c.Parts.Count(p => p.Prefab == carried), Is.GreaterThanOrEqualTo(10), carried);
            }
        }

        [Test]
        public void CastlePartsAreYawOnlyUniformAndMeasured()
        {
            foreach (CastlePlan c in CitySetPieces.Castles)
            {
                foreach (SetPart p in c.Parts)
                {
                    Assert.That(p.Distorted, Is.False, $"{c.Name}: {p.Prefab} is drawn uniform and yaw-only");
                    Assert.That(p.ScaleY, Is.EqualTo(p.Scale), $"{c.Name}: {p.Prefab} scale");
                    Assert.That(p.High, Is.GreaterThanOrEqualTo(p.Low), $"{c.Name}: {p.Prefab} low and high");
                    Assert.That(p.Radius, Is.GreaterThan(0f), $"{c.Name}: {p.Prefab} reach");
                    Assert.That(p.Prefab, Does.Not.Contain("Stairs_Round_L_Wood"), $"{c.Name}: the keep's wood round stairs stay out");
                    Assert.That(p.Prefab, Is.Not.EqualTo("SM_Bld_Castle_Pillar_Stone_Half_03"), $"{c.Name}: the one tilted half pillar stays out");
                }
            }
        }

        [Test]
        public void CastleExtentsAndGateMatchItsParts()
        {
            foreach (CastlePlan c in CitySetPieces.Castles)
            {
                float mx = c.Parts.Max(p => System.Math.Abs(p.X)), mz = c.Parts.Max(p => System.Math.Abs(p.Z)), top = c.Parts.Max(p => p.Y + p.High);
                Assert.That(c.HalfWidth - mx, Is.InRange(-1e-3f, 2.6f), $"{c.Name}: half width {c.HalfWidth} against the parts' {mx}");
                Assert.That(c.HalfDepth - mz, Is.InRange(-1e-3f, 2.6f), $"{c.Name}: half depth {c.HalfDepth} against the parts' {mz}");
                Assert.That(c.Height, Is.EqualTo(top).Within(0.1f), $"{c.Name}: height against the parts' top");
                bool gate = c.Parts.Any(p => (p.Prefab.Contains("Gate") || p.Prefab.Contains("Door") || p.Prefab.Contains("Archway")) &&
                                             System.Math.Abs(p.X - c.GateX) < 0.5f && System.Math.Abs(p.Z - c.GateZ) < 0.5f && System.Math.Abs(p.Y) < 1f);
                Assert.That(gate, Is.True, $"{c.Name}: a gate or door stands at its gate ({c.GateX}, {c.GateZ}) at the court");
            }

            CastlePlan castle = DemoCastle();
            Assert.That(castle.Parts.Any(p => p.Prefab == "SM_Bld_Castle_Wall_Gate_01" && System.Math.Abs(p.X - castle.GateX) < 0.05f && System.Math.Abs(p.Z - castle.GateZ) < 0.05f),
                Is.True, "DemoCastle's gate is the demo's gate");
        }

        [Test]
        public void CastleFloorsAreWalkableAndBeamsOpen()
        {
            foreach (string name in new[] { "SM_Bld_Castle_Floor_Wood_01", "SM_Bld_Castle_Floor_Wood_03", "SM_Bld_Castle_Floor_Stone_01", "SM_Bld_House_Floor_Stone_01",
                         "SM_Bld_Walkway_Wood_Railing_01", "SM_Bld_Walkway_Wood_Railing_Pillar_01", "SM_Bld_Keep_Pillar_02", "SM_Bld_Castle_Pillar_Wood_Round_02", "SM_Bld_Wall_Wood_Beams_03" })
            {
                Assert.That(PieceColliders.Kind(PropRole.CastleModule, name), Is.EqualTo(PieceCollider.Box), name);
            }

            foreach (string name in new[] { "SM_Bld_Castle_Wood_Floor_Gap_02", "SM_Bld_Castle_Floor_Wood_Round_L_01" })
            {
                Assert.That(PieceColliders.Kind(PropRole.CastleModule, name), Is.EqualTo(PieceCollider.Own), name);
            }

            Assert.That(PieceColliders.Kind(PropRole.CastleModule, "SM_Bld_Wood_Beam_Curved_01"), Is.EqualTo(PieceCollider.None));
        }

        // ------------------------------------------------------------------ seating (2026-10-09)

        /// <summary>The vignettes a city seats (quay pieces stand in water; no city offers a quay).</summary>
        private static IEnumerable<CityVignette> Seated() => CitySetPieces.Vignettes.Where(v => v.Kind != VignetteKind.Quay);

        [Test]
        public void EveryVignettePartIsYawOnlyAndHeld()
        {
            int up = 0, wall = 0;
            foreach (CityVignette v in Seated())
            {
                for (int i = 0; i < v.Parts.Length; i++)
                {
                    SetPart p = v.Parts[i];
                    string at = $"{v.Name}[{i}] {p.Prefab}";
                    Assert.That(p.Pitch, Is.EqualTo(0f), at);
                    Assert.That(p.Roll, Is.EqualTo(0f), at);
                    Assert.That(p.Distorted, Is.False, at);
                    Assert.That(p.Radius, Is.GreaterThan(0f), at + " radius");
                    switch (p.Mount)
                    {
                        case SetMount.Ground:
                            Assert.That(p.Support, Is.EqualTo(-1), at);
                            Assert.That(p.Y + p.Contact, Is.EqualTo(0f).Within(1e-3f), at + ": a part on the ground meets it");
                            break;
                        case SetMount.Wall:
                            wall++;
                            Assert.That(p.Support, Is.EqualTo(-1), at);
                            Assert.That(p.MountMin, Is.GreaterThanOrEqualTo(2.2f - 1e-3f), at + ": mounted at least 2.2 m up");
                            Assert.That(p.Y, Is.GreaterThanOrEqualTo(p.MountMin - 1e-3f), at);
                            Assert.That(p.Z, Is.InRange(0f, 0.5f), at + ": flush against the wall");
                            break;
                        default:
                            Assert.That(p.Support, Is.InRange(0, i - 1), at + ": its support comes first");
                            SetPart s = v.Parts[p.Support];
                            if (p.Mount == SetMount.OnPart)
                            {
                                Assert.That(p.Y + p.Contact, Is.InRange(s.Y + s.Low - 0.05f, s.Y + s.High + 0.2f), at + $": stands on {s.Prefab}");
                            }
                            else
                            {
                                Assert.That(p.Y + p.High, Is.InRange(s.Y + s.Low - 0.2f, s.Y + s.High + 0.2f), at + $": hangs from {s.Prefab}");
                            }

                            break;
                    }

                    if (p.Y + p.Contact > 0.3f)
                    {
                        up++;
                        Assert.That(p.Mount, Is.Not.EqualTo(SetMount.Ground), at + ": more than 0.3 m up with no support or mount");
                    }
                }
            }

            Assert.That(up, Is.GreaterThan(10), "parts resting on others or on walls");
            Assert.That(wall, Is.GreaterThan(10), "wall-mounted parts");
        }

        private static List<(Dressing d, CityStats st)> AllCities(int seed)
        {
            var g = new EndlessGenerator(new EndlessParams { seed = seed }, PaletteKeys.Default);
            var list = new List<(Dressing, CityStats)>();
            for (Region r = g.Regions.At(0); r.Start < 60000.0; r = g.Regions.Get(r.Index + 1))
            {
                foreach (Place c in g.Plan.PlacesOf(r.Index).Where(p => p.City))
                {
                    Dressing d = g.Dressings.Settlement(c);
                    list.Add((d, d.City));
                }
            }

            return list;
        }

        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void PlacedSetPiecesSitOnWhatHoldsThem(int seed)
        {
            var cities = AllCities(seed);
            Assert.That(cities.Count, Is.GreaterThan(0));
            foreach (var (d, st) in cities)
            {
                List<PlacedSetPart> parts = st.SetParts;
                Assert.That(parts.Count, Is.GreaterThan(60), "parts placed");
                var rocks = new List<Vector3>();
                foreach (PropPlacement p in d.Props)
                {
                    if (!PropRole.IsRock(p.role)) continue;
                    CityComposer.RockCircle(PaletteKeys.Default, p, out Vector3 c, out float r);
                    rocks.Add(new Vector3(c.x, r, c.z));
                }

                for (int i = 0; i < parts.Count; i++)
                {
                    PlacedSetPart p = parts[i];
                    string at = $"seed {seed}: {p.Vignette} #{p.Placement} {p.Prefab} at ({p.Position.x:F1}, {p.Position.z:F1})";
                    switch (p.Mount)
                    {
                        case SetMount.Ground:
                            Assert.That(p.Contact - p.GroundLow, Is.LessThanOrEqualTo(0.151f), at + ": floats");
                            Assert.That(p.GroundHigh - p.Contact, Is.LessThanOrEqualTo(0.101f), at + ": buried");
                            foreach (Vector3 c in rocks)
                            {
                                float dist = Mathf.Sqrt((c.x - p.Position.x) * (c.x - p.Position.x) + (c.z - p.Position.z) * (c.z - p.Position.z));
                                Assert.That(dist, Is.GreaterThanOrEqualTo(c.y + 0.7f * p.Radius - 1e-3f), at + ": on a rock");
                            }

                            break;
                        case SetMount.Wall:
                            Assert.That(p.HasFace && p.OnFace, Is.True, at + ": a wall part with no wall");
                            break;
                        default:
                            Assert.That(p.Support, Is.InRange(0, i - 1), at);
                            Assert.That(parts[p.Support].Placement, Is.EqualTo(p.Placement), at + ": held by a part of its own placement");
                            break;
                    }

                    if (p.OnFace)
                    {
                        float off = Vector3.Dot(new Vector3(p.Position.x - p.FacePoint.x, 0f, p.Position.z - p.FacePoint.z), p.FaceNormal);
                        if (p.Mount == SetMount.Wall) Assert.That(off, Is.InRange(-0.05f, 0.5f), at + ": against its face");
                        Assert.That(p.Position.y - p.Street, Is.GreaterThanOrEqualTo(2.0f), at + ": at least 2 m up");
                    }
                }
            }
        }

        [TestCase(12345)]
        [TestCase(42)]
        [TestCase(777)]
        public void PlacedSetPiecesKeepTheirContext(int seed)
        {
            foreach (var (_, st) in AllCities(seed))
            {
                foreach (var group in st.SetParts.GroupBy(p => p.Placement))
                {
                    List<PlacedSetPart> list = group.ToList();
                    bool companion = list[0].Slot == VignetteKind.DoorSide || list.Any(p => p.Role == PropRole.Bench || p.Role == PropRole.CityMonument);
                    foreach (PlacedSetPart p in list)
                    {
                        string at = $"seed {seed}: {p.Vignette} in a {p.Slot} slot: {p.Prefab}";
                        if (p.Role == PropRole.Lamp)
                        {
                            Assert.That(p.Slot == VignetteKind.Corner || p.Slot == VignetteKind.DoorSide || p.Slot == VignetteKind.WallFoot && p.HasFace, Is.True,
                                at + ": a lamp away from a corner, wall or door");
                        }

                        if (p.Role == PropRole.CityPlanter) Assert.That(companion, Is.True, at + ": a planter with no door or bench");
                        if (p.Prefab.Contains("Cart") || p.Prefab.Contains("Wheelbarrow")) Assert.That(p.Slot, Is.EqualTo(VignetteKind.Yard), at + ": a cart outside a yard");
                        if (p.Role == PropRole.Bench && p.Slot != VignetteKind.Plaza)
                        {
                            Assert.That(p.HasFace, Is.True, at + ": a bench with nothing at its back");
                            float off = Vector3.Dot(new Vector3(p.Position.x - p.FacePoint.x, 0f, p.Position.z - p.FacePoint.z), p.FaceNormal);
                            Assert.That(off - p.FaceBack, Is.InRange(0f, 1.6f), at + ": a bench away from the edge at its back");
                            float yr = p.Yaw * Mathf.Deg2Rad;
                            float facing = Mathf.Sin(yr) * p.FaceNormal.x + Mathf.Cos(yr) * p.FaceNormal.z;
                            Assert.That(Mathf.Abs(facing), Is.GreaterThan(0.95f), at + ": a bench across its face");
                        }
                    }
                }
            }
        }
    }
}
