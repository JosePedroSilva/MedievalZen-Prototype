using MedievalZen.World;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Small birds crossing the sky high overhead by day: now and then a flock of 3–9 dark silhouettes (a loose cluster or a
    /// V) flies straight across, 40–80 m above the ground and 25–150 m to the side of the traveller, beating its wings with
    /// glides in between (<see cref="BirdMath"/>). Each bird is one draw of a tiny procedural mesh (8 wing poses built once,
    /// double-sided, about 40 triangles); no colliders, no shadows, nothing per frame beyond a few matrices. The fog pass
    /// hazes them with distance like everything else. Owned and ticked by <see cref="AmbientFxSpawner"/> (which forwards
    /// origin shifts). Cosmetic, so it uses <c>UnityEngine.Random</c>.
    /// </summary>
    internal sealed class SkyBirds
    {
        private const int PoseCount = 8;
        private const int MaxBirds = 9;
        private const float HalfPath = 320f;
        private const float Wingspan = 1f;

        private struct Bird
        {
            public float X, Z, Y;
            public float Phase;
            public float BeatHz;
            public float Cycle;
            public float FlapFraction;
            public float Scale;
            public float Wobble;
        }

        private readonly Mesh[] poses = new Mesh[PoseCount];
        private readonly Material material;
        private readonly bool ownsMaterial;
        private readonly Bird[] birds = new Bird[MaxBirds];
        private int count;
        private bool flying;
        private Vector3 start;
        private Vector3 heading;
        private float speed;
        private float travelled;
        private float altitude;
        private float targetAltitude;
        private float nextGroundCheck;
        private float nextFlock;

        /// <summary>Seconds between one flock leaving and the next appearing (min, max); one flock at a time.</summary>
        public Vector2 FlockInterval = new Vector2(35f, 110f);

        /// <summary>Height above the ground (min, max), metres.</summary>
        public Vector2 Altitude = new Vector2(40f, 80f);

        /// <summary>Birds drawn last frame.</summary>
        public int Drawn { get; private set; }

        /// <summary>Builds the wing poses; <paramref name="birdMaterial"/> null creates a dark URP Unlit material (editor-safe fallback).</summary>
        public SkyBirds(Material birdMaterial)
        {
            if (birdMaterial != null)
            {
                material = birdMaterial;
            }
            else
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlit != null)
                {
                    material = new Material(unlit) { name = "Bird silhouette (runtime)" };
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(0.07f, 0.07f, 0.08f));
                    ownsMaterial = true;
                }
            }

            for (int i = 0; i < PoseCount; i++) poses[i] = BuildPose(BirdMath.PoseAngle(i, PoseCount));
            nextFlock = Time.time + Random.Range(8f, 25f);
        }

        /// <summary>True when a flock is crossing now.</summary>
        public bool Flying => flying;

        /// <summary>Advances the flock (or starts one when <paramref name="allowed"/>) and draws it.</summary>
        public void Tick(float deltaTime, Vector3 traveller, bool allowed, IWorldSurface surface)
        {
            Drawn = 0;
            if (material == null) return;
            if (!flying)
            {
                if (!allowed || Time.time < nextFlock) return;
                Launch(traveller, surface);
                return;
            }

            travelled += speed * deltaTime;
            if (travelled > HalfPath * 2f)
            {
                flying = false;
                nextFlock = Time.time + Random.Range(FlockInterval.x, FlockInterval.y);
                return;
            }

            Vector3 centre = start + heading * travelled;
            if (surface != null && surface.HasGround && Time.time >= nextGroundCheck)
            {
                // Keep well above whatever the land does under the flock (a pass, a hill).
                nextGroundCheck = Time.time + 0.5f;
                targetAltitude = Mathf.Max(targetAltitude, surface.GroundHeight(centre) + Altitude.x * 0.7f);
            }

            altitude = Mathf.MoveTowards(altitude, targetAltitude, 3f * deltaTime);
            Draw(centre);
        }

        /// <summary>Floating-origin shift: the flight path moves with the land.</summary>
        public void ShiftOrigin(Vector3 delta)
        {
            start += delta;
            altitude += delta.y;
            targetAltitude += delta.y;
        }

        /// <summary>Destroys the meshes (and the material when it was made here).</summary>
        public void Dispose()
        {
            for (int i = 0; i < poses.Length; i++)
            {
                if (poses[i] != null) Object.Destroy(poses[i]);
            }

            if (ownsMaterial && material != null) Object.Destroy(material);
        }

        private void Launch(Vector3 traveller, IWorldSurface surface)
        {
            Vector2 dir = Random.insideUnitCircle;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector2.up;
            dir.Normalize();
            float side = Random.Range(25f, 150f) * (Random.value < 0.5f ? -1f : 1f);
            var (sx, sz, _, _) = BirdMath.CrossingPath(traveller.x, traveller.z, dir.x, dir.y, side, HalfPath);
            heading = new Vector3(dir.x, 0f, dir.y);
            float ground = surface != null && surface.HasGround ? surface.GroundHeight(traveller) : traveller.y;
            altitude = targetAltitude = ground + Random.Range(Altitude.x, Altitude.y);
            start = new Vector3(sx, 0f, sz);
            speed = Random.Range(9f, 13f);
            travelled = 0f;
            nextGroundCheck = 0f;

            count = Random.Range(3, MaxBirds + 1);
            bool vee = count >= 5 && Random.value < 0.35f;
            float spacing = Random.Range(2.2f, 3.4f);
            float beat = Random.Range(3f, 4.2f);
            for (int i = 0; i < count; i++)
            {
                (float x, float z) = BirdMath.FormationOffset(i, count, vee, spacing, Random.Range(-1f, 1f), Random.Range(-1f, 1f));
                birds[i] = new Bird
                {
                    X = x,
                    Z = z,
                    Y = Random.Range(-1.2f, 1.2f),
                    Phase = Random.Range(0f, 10f),
                    BeatHz = beat * Random.Range(0.92f, 1.08f),
                    Cycle = Random.Range(3.2f, 5.5f),
                    FlapFraction = Random.Range(0.35f, 0.6f),
                    Scale = Random.Range(0.8f, 1.15f),
                    Wobble = Random.Range(0f, 6.28f),
                };
            }

            flying = true;
        }

        private void Draw(Vector3 centre)
        {
            var rp = new RenderParams(material)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                motionVectorMode = MotionVectorGenerationMode.Camera,
            };

            Quaternion yaw = Quaternion.LookRotation(heading, Vector3.up);
            Vector3 right = yaw * Vector3.right;
            double time = Time.timeAsDouble;
            for (int i = 0; i < count; i++)
            {
                Bird b = birds[i];
                float t = (float)time + b.Phase;
                float bob = Mathf.Sin(t * 0.7f) * 0.6f;
                float sway = Mathf.Sin(t * 0.45f + b.Wobble) * 0.8f;
                Vector3 p = centre + right * (b.X + sway) + heading * b.Z;
                p.y = altitude + b.Y + bob;
                float bank = Mathf.Sin(t * 0.45f + b.Wobble + 1.2f) * 8f;
                float angle = BirdMath.WingAngle(time + b.Phase, b.BeatHz, b.Cycle, b.FlapFraction);
                Mesh mesh = poses[BirdMath.PoseIndex(angle, PoseCount)];
                Matrix4x4 m = Matrix4x4.TRS(p, yaw * Quaternion.Euler(0f, 0f, bank), Vector3.one * b.Scale);
                rp.worldBounds = new Bounds(p, Vector3.one * 2f);
                Graphics.RenderMesh(rp, mesh, 0, m);
                Drawn++;
            }
        }

        /// <summary>
        /// One wing pose: a slim body (nose +Z) and two wings in two panels each, the inner panel raised by
        /// <paramref name="angle"/> degrees, the outer by 1.35× that (the tips lead the beat). Every triangle is written both
        /// ways round, so the bird is solid from above and below with an ordinary culled shader.
        /// </summary>
        private static Mesh BuildPose(float angle)
        {
            var vertices = new System.Collections.Generic.List<Vector3>(48);
            var triangles = new System.Collections.Generic.List<int>(96);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int o = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);
                triangles.AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3, o, o + 2, o + 1, o, o + 3, o + 2 });
            }

            // Body: a flattened diamond, 0.46 m long.
            Quad(new Vector3(0f, 0f, 0.24f), new Vector3(0.05f, 0f, 0.02f), new Vector3(0f, 0f, -0.22f), new Vector3(-0.05f, 0f, 0.02f));
            Quad(new Vector3(0f, 0.02f, 0.2f), new Vector3(0f, 0.03f, 0f), new Vector3(0f, 0f, -0.22f), new Vector3(0f, -0.02f, 0f));
            // Tail fan.
            Quad(new Vector3(-0.02f, 0f, -0.18f), new Vector3(0.02f, 0f, -0.18f), new Vector3(0.07f, 0f, -0.32f), new Vector3(-0.07f, 0f, -0.32f));

            float half = Wingspan * 0.5f;
            const float shoulder = 0.04f, elbow = 0.24f;
            for (int side = -1; side <= 1; side += 2)
            {
                float inner = angle * Mathf.Deg2Rad;
                float outer = inner * 1.35f;
                Vector3 s = new Vector3(side * shoulder, 0f, 0f);
                Vector3 e = s + new Vector3(side * Mathf.Cos(inner), Mathf.Sin(inner), 0f) * (elbow - shoulder);
                Vector3 tip = e + new Vector3(side * Mathf.Cos(outer), Mathf.Sin(outer), 0f) * (half - elbow);
                // Inner panel: chord 0.2 m at the shoulder, 0.15 m at the elbow (swept back a little).
                Quad(s + new Vector3(0f, 0f, 0.09f), e + new Vector3(0f, 0f, 0.05f), e + new Vector3(0f, 0f, -0.1f), s + new Vector3(0f, 0f, -0.11f));
                // Outer panel tapering to a point behind the tip.
                Quad(e + new Vector3(0f, 0f, 0.05f), tip + new Vector3(0f, 0f, -0.04f), tip + new Vector3(0f, 0f, -0.1f), e + new Vector3(0f, 0f, -0.1f));
            }

            // Both windings share each vertex, so computed normals would cancel to zero; the silhouette shader ignores them,
            // and a plain "up" keeps the depth-normals prepass (SSAO) well defined.
            var normals = new System.Collections.Generic.List<Vector3>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++) normals.Add(Vector3.up);
            var mesh = new Mesh { name = $"Bird pose {angle:0}°" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
