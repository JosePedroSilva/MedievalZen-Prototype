using System;
using UnityEngine;

namespace MedievalZen.Cinematic
{
    /// <summary>
    /// Pure maths of the cinematic camera: critically damped springs, rotations written out by hand, rule-of-thirds framing,
    /// ground/water clearance and the occlusion primitives (terrain along a sight line, a vertical cylinder for a tree trunk
    /// or crown). Vector3 / Mathf only (no native Unity calls), so everything is unit-tested and runs in the offline harness.
    /// </summary>
    public static class CinematicMath
    {
        // ------------------------------------------------------------------ springs

        /// <summary>
        /// Critically damped spring step (the closed form Unity's SmoothDamp uses): moves <paramref name="value"/> towards
        /// <paramref name="target"/>, reaching it in roughly <paramref name="smoothTime"/> seconds without overshoot.
        /// </summary>
        public static float Damp(float value, float target, ref float velocity, float smoothTime, float deltaTime)
        {
            smoothTime = Math.Max(1e-4f, smoothTime);
            if (deltaTime <= 0f) return value;
            float omega = 2f / smoothTime;
            float x = omega * deltaTime;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = value - target;
            float temp = (velocity + omega * change) * deltaTime;
            velocity = (velocity - omega * temp) * exp;
            float result = target + (change + temp) * exp;
            // No overshoot: once past the target, stop there.
            if ((target - value > 0f) == (result > target))
            {
                result = target;
                velocity = 0f;
            }

            return result;
        }

        /// <summary><see cref="Damp(float, float, ref float, float, float)"/> per component.</summary>
        public static Vector3 Damp(Vector3 value, Vector3 target, ref Vector3 velocity, float smoothTime, float deltaTime)
        {
            float vx = velocity.x, vy = velocity.y, vz = velocity.z;
            var result = new Vector3(
                Damp(value.x, target.x, ref vx, smoothTime, deltaTime),
                Damp(value.y, target.y, ref vy, smoothTime, deltaTime),
                Damp(value.z, target.z, ref vz, smoothTime, deltaTime));
            velocity = new Vector3(vx, vy, vz);
            return result;
        }

        /// <summary>
        /// <see cref="Damp(Vector3, Vector3, ref Vector3, float, float)"/> that never moves faster than <paramref name="maxSpeed"/>
        /// (m/s): a long blend glides at that pace instead of rushing, and still arrives without overshoot.
        /// </summary>
        public static Vector3 DampLimited(Vector3 value, Vector3 target, ref Vector3 velocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            Vector3 next = Damp(value, target, ref velocity, smoothTime, deltaTime);
            if (deltaTime <= 0f || maxSpeed <= 0f) return next;
            Vector3 step = next - value;
            float limit = maxSpeed * deltaTime;
            float length = step.magnitude;
            if (length <= limit) return next;
            Vector3 dir = step / length;
            velocity = dir * maxSpeed;
            return value + dir * limit;
        }

        // ------------------------------------------------------------------ directions and rotations

        /// <summary>Unit horizontal direction of <paramref name="v"/>, or <paramref name="fallback"/> when it has none.</summary>
        public static Vector3 Planar(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            float m = v.magnitude;
            return m > 1e-5f ? v / m : fallback;
        }

        /// <summary>Horizontal right of a horizontal forward (Unity is left-handed: right = up × forward).</summary>
        public static Vector3 RightOf(Vector3 forward) => new Vector3(forward.z, 0f, -forward.x);

        /// <summary>Unit horizontal direction for a yaw in degrees (0 = +Z, 90 = +X).</summary>
        public static Vector3 FromYaw(float yawDegrees)
        {
            float a = yawDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        /// <summary>Yaw in degrees of a direction (0 = +Z).</summary>
        public static float YawOf(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        /// <summary>Pitch in degrees of a direction, + = looking down (Unity's camera convention).</summary>
        public static float PitchOf(Vector3 direction)
        {
            float planar = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
            return -Mathf.Atan2(direction.y, planar) * Mathf.Rad2Deg;
        }

        /// <summary>Rotates <paramref name="v"/> about the world up axis by <paramref name="degrees"/> (+ turns +Z towards +X).</summary>
        public static Vector3 RotateYaw(Vector3 v, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c);
        }

        /// <summary>Rotates <paramref name="v"/> about the unit <paramref name="axis"/> by <paramref name="degrees"/> (Rodrigues).</summary>
        public static Vector3 RotateAbout(Vector3 v, Vector3 axis, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            // Same result as Quaternion.AngleAxis: a positive angle about +X turns +Z towards -Y (looks down), about +Y turns +Z towards +X.
            return v * c + Vector3.Cross(axis, v) * s + axis * (Vector3.Dot(axis, v) * (1f - c));
        }

        /// <summary>Angle in degrees between two directions.</summary>
        public static float AngleBetween(Vector3 a, Vector3 b)
        {
            float d = Mathf.Sqrt(a.sqrMagnitude * b.sqrMagnitude);
            if (d < 1e-8f) return 0f;
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(a, b) / d, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>Horizontal field of view in degrees for a vertical one and an aspect ratio (width / height).</summary>
        public static float HorizontalFov(float verticalFov, float aspect) =>
            2f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;

        // ------------------------------------------------------------------ framing

        /// <summary>
        /// A look target that puts <paramref name="subject"/> at screen position (<paramref name="screenX"/>,
        /// <paramref name="screenY"/>) (−1..1 each, e.g. ±1/3 for the thirds) for a camera at <paramref name="camera"/> with
        /// vertical field of view <paramref name="verticalFov"/> and <paramref name="aspect"/>. With a non-zero
        /// <paramref name="leadDirection"/> the sign of <paramref name="screenX"/> is chosen so the open side of the frame lies
        /// where the subject is heading (lead room).
        /// </summary>
        public static Vector3 FrameAt(Vector3 camera, Vector3 subject, Vector3 leadDirection, float verticalFov, float aspect, float screenX, float screenY)
        {
            Vector3 toSubject = subject - camera;
            float distance = toSubject.magnitude;
            if (distance < 1e-4f) return subject + Vector3.forward;
            Vector3 dir = toSubject / distance;
            Vector3 flat = Planar(dir, Vector3.forward);
            Vector3 right = RightOf(flat);
            float x = screenX;
            Vector3 lead = Planar(leadDirection, Vector3.zero);
            if (lead.sqrMagnitude > 0f && Math.Abs(x) > 1e-4f)
            {
                // Heading to the camera's right: keep the subject left of centre, and vice versa.
                float side = Vector3.Dot(lead, right);
                if (Math.Abs(side) > 0.05f) x = -Math.Sign(side) * Math.Abs(x);
            }

            float tanV = Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * Math.Max(0.1f, aspect);
            float yaw = Mathf.Atan(x * tanH) * Mathf.Rad2Deg;      // subject this far right of the view axis
            float pitch = Mathf.Atan(screenY * tanV) * Mathf.Rad2Deg; // subject this far above it
            // Turn the view axis away from the subject by those angles.
            Vector3 forward = RotateYaw(dir, -yaw);
            Vector3 axis = RightOf(Planar(forward, flat));
            forward = RotateAbout(forward, axis, pitch);
            return camera + forward * distance;
        }

        /// <summary>
        /// Where <paramref name="point"/> appears on screen (−1..1 each axis, (0, 0) = centre) for a camera at
        /// <paramref name="camera"/> looking at <paramref name="lookAt"/>; z = distance along the view axis (≤ 0: behind).
        /// </summary>
        public static Vector3 ScreenPosition(Vector3 camera, Vector3 lookAt, Vector3 point, float verticalFov, float aspect)
        {
            Vector3 forward = (lookAt - camera).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-8f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right);
            Vector3 d = point - camera;
            float z = Vector3.Dot(d, forward);
            if (z <= 1e-4f) return new Vector3(0f, 0f, z);
            float tanV = Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * aspect;
            return new Vector3(Vector3.Dot(d, right) / z / tanH, Vector3.Dot(d, up) / z / tanV, z);
        }

        /// <summary>True when a screen position (from <see cref="ScreenPosition"/>) is in front of the camera and inside the frame minus <paramref name="margin"/>.</summary>
        public static bool InFrame(Vector3 screen, float margin = 0.05f) =>
            screen.z > 0f && Math.Abs(screen.x) <= 1f - margin && Math.Abs(screen.y) <= 1f - margin;

        // ------------------------------------------------------------------ clearance and occlusion

        /// <summary>
        /// Lifts <paramref name="position"/> so it is at least <paramref name="groundMargin"/> above <paramref name="ground"/> and,
        /// where there is water (<paramref name="water"/> ≥ 0), <paramref name="waterMargin"/> above it.
        /// </summary>
        public static Vector3 Clear(Vector3 position, float ground, float water, float groundMargin, float waterMargin)
        {
            float floor = ground + groundMargin;
            if (water >= 0f) floor = Math.Max(floor, water + waterMargin);
            if (position.y < floor) position.y = floor;
            return position;
        }

        /// <summary>
        /// True when the ground rises above the straight line from <paramref name="from"/> to <paramref name="to"/>: samples every
        /// <paramref name="step"/> metres (skipping <paramref name="skipEnd"/> metres at the far end, where the subject stands on the
        /// ground) and reports a hit when the ground there is higher than the line minus <paramref name="tolerance"/>.
        /// </summary>
        public static bool TerrainBlocks(Vector3 from, Vector3 to, Func<float, float, float> groundHeight, float step = 1.5f, float skipEnd = 1.5f, float tolerance = 0.05f)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 1e-3f || groundHeight == null) return false;
            float usable = length - skipEnd;
            for (float s = step * 0.5f; s < usable; s += step)
            {
                Vector3 p = from + d * (s / length);
                if (groundHeight(p.x, p.z) > p.y - tolerance) return true;
            }

            return false;
        }

        /// <summary>
        /// True when the segment <paramref name="a"/>–<paramref name="b"/> passes through the vertical cylinder standing on
        /// <paramref name="baseCentre"/> with <paramref name="radius"/> and <paramref name="height"/> (a tree trunk, or a crown).
        /// </summary>
        public static bool SegmentHitsCylinder(Vector3 a, Vector3 b, Vector3 baseCentre, float radius, float height)
        {
            // Horizontal: the parameter range where the segment's xz is inside the circle.
            float dx = b.x - a.x, dz = b.z - a.z;
            float fx = a.x - baseCentre.x, fz = a.z - baseCentre.z;
            float qa = dx * dx + dz * dz;
            float qb = 2f * (fx * dx + fz * dz);
            float qc = fx * fx + fz * fz - radius * radius;
            float t0, t1;
            if (qa < 1e-10f)
            {
                if (qc > 0f) return false;
                t0 = 0f;
                t1 = 1f;
            }
            else
            {
                float disc = qb * qb - 4f * qa * qc;
                if (disc < 0f) return false;
                float root = Mathf.Sqrt(disc);
                t0 = (-qb - root) / (2f * qa);
                t1 = (-qb + root) / (2f * qa);
            }

            t0 = Math.Max(0f, t0);
            t1 = Math.Min(1f, t1);
            if (t0 > t1) return false;

            // Vertical: inside [base, base + height] somewhere on that range (y is linear in t).
            float y0 = a.y + (b.y - a.y) * t0 - baseCentre.y;
            float y1 = a.y + (b.y - a.y) * t1 - baseCentre.y;
            return Math.Max(y0, y1) >= 0f && Math.Min(y0, y1) <= height;
        }

        /// <summary>Share of a tree's height where the crown starts.</summary>
        public const float CrownStart = 0.3f;

        /// <summary>Share of a tree's crown radius treated as solid (the dense middle; light shows through the edge).</summary>
        public const float CrownCore = 0.5f;

        /// <summary>
        /// True when the sight line <paramref name="a"/>–<paramref name="b"/> passes through a tree standing at
        /// <paramref name="treeBase"/>: its trunk (radius <paramref name="trunkRadius"/>, up to the crown) or the dense core of its
        /// crown (<see cref="CrownCore"/> × <paramref name="crownRadius"/>, from <see cref="CrownStart"/> × height to the top).
        /// Sizes are already scaled.
        /// </summary>
        public static bool TreeBlocks(Vector3 a, Vector3 b, Vector3 treeBase, float height, float crownRadius, float trunkRadius)
        {
            if (SegmentHitsCylinder(a, b, treeBase, trunkRadius, height * CrownStart + 0.5f)) return true;
            return SegmentHitsCylinder(a, b, treeBase + Vector3.up * (height * CrownStart), crownRadius * CrownCore, height * (1f - CrownStart));
        }

        /// <summary>True when <paramref name="p"/> is within <paramref name="pad"/> of a tree's trunk or inside its crown core.</summary>
        public static bool InsideTree(Vector3 p, float pad, Vector3 treeBase, float height, float crownRadius, float trunkRadius)
        {
            float dx = p.x - treeBase.x, dz = p.z - treeBase.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            float y = p.y - treeBase.y;
            if (y < -0.5f || y > height + pad) return false;
            if (d < trunkRadius + pad) return true;
            return y > height * CrownStart - pad && d < crownRadius * CrownCore + pad;
        }

        /// <summary>
        /// Probability-style check for trees nobody can query one by one: the chance that a sight line of
        /// <paramref name="length"/> metres through woodland of <paramref name="treesPer100m2"/> misses every trunk/crown of
        /// width <paramref name="obstacleWidth"/> (Poisson). Returns that clear chance (1 = open ground).
        /// </summary>
        public static float ClearChance(float treesPer100m2, float length, float obstacleWidth)
        {
            float perM2 = Math.Max(0f, treesPer100m2) / 100f;
            return Mathf.Exp(-perM2 * Math.Max(0f, length) * Math.Max(0f, obstacleWidth));
        }

        /// <summary>Smoothstep of a 0..1 value.</summary>
        public static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
