using System.Diagnostics;
using MedievalZen.Environment;
using MedievalZen.World;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace MedievalZen.Endless
{
    /// <summary>The floating-origin shift and the debug keys.</summary>
    public sealed partial class EndlessWorld
    {
        private ParticleSystem.Particle[] particleBuffer = new ParticleSystem.Particle[256];

        /// <summary>Number of origin shifts so far (overlay).</summary>
        public int ShiftCount => shiftCount;

        private void HandleDebugKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f3Key.wasPressedThisFrame) showOverlay = !showOverlay;
            if (keyboard.f4Key.wasPressedThisFrame && !startup && !scripted && traveller != null) ForceOriginShift();
        }

        /// <summary>
        /// Debug (F4): shifts the origin now — to the traveller's tile corner, or by one tile when it is already there — so
        /// the shift can be checked by eye (nothing on screen should move).
        /// </summary>
        public void ForceOriginShift()
        {
            Vector3 delta = origin.DeltaToward(traveller.position);
            if (delta.sqrMagnitude < 1f) delta = new Vector3(-parameters.tileSize, 0f, 0f);
            ShiftOrigin(delta);
            Debug.Log($"Endless road: forced an origin shift by ({delta.x:0}, {delta.z:0}); origin now {origin} ({lastShiftMs:0.0} ms).", this);
        }

        /// <summary>
        /// Moves every scene position by <paramref name="delta"/> (whole tiles, horizontal) in this frame: tiles, the player
        /// (controller off around the move, motor/animator/camera state untouched), the sky, every
        /// <see cref="IOriginShiftable"/> (ambient effects, mist, the fog noise), world-space particles, and the road window
        /// (translated, with any auto-travel re-issued in place).
        /// </summary>
        private void ShiftOrigin(Vector3 delta)
        {
            var clock = Stopwatch.StartNew();
            Vector3 applied = origin.Shift(delta);
            if (applied.sqrMagnitude <= 0f) return;

            for (int i = 0; i < tilesRoot.childCount; i++) tilesRoot.GetChild(i).position += applied;

            if (traveller != null)
            {
                bool enabledBefore = body != null && body.enabled;
                if (body != null) body.enabled = false;
                traveller.position += applied;
                if (body != null) body.enabled = enabledBefore;
            }

            foreach (SkyFollower sky in FindObjectsByType<SkyFollower>(FindObjectsSortMode.None)) sky.transform.position += applied;
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour is IOriginShiftable shiftable && !ReferenceEquals(behaviour, this)) shiftable.ShiftOrigin(applied);
            }

            ShiftParticles(applied);
            if (trees != null)
            {
                trees.ShiftOrigin(applied);
                if (traveller != null) trees.UpdateColliders(traveller.position, true);
            }

            if (window != null)
            {
                RoadWindow previous = window;
                ApplyWindow(previous, previous.Translate(applied, origin.X, origin.Z), applied);
            }

            UpdateNature(true);
            Physics.SyncTransforms();
            shiftCount++;
            lastShiftMs = (float)clock.Elapsed.TotalMilliseconds;
        }

        /// <summary>Moves the live particles of every world-space particle system by <paramref name="delta"/> (local-space ones move with their transforms).</summary>
        private void ShiftParticles(Vector3 delta)
        {
            foreach (ParticleSystem system in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (system.main.simulationSpace != ParticleSystemSimulationSpace.World) continue;
                int count = system.particleCount;
                if (count <= 0) continue;
                if (particleBuffer.Length < count) particleBuffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];
                int n = system.GetParticles(particleBuffer, count);
                for (int i = 0; i < n; i++) particleBuffer[i].position += delta;
                system.SetParticles(particleBuffer, n);
            }
        }
    }
}
