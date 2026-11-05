using UnityEngine;
using UnityEngine.Rendering;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// One pooled animal instance: the stripped prefab's GameObject, its Animator with cached bool hashes (bools the
    /// controller lacks are skipped), its renderers, and its measured size. Shows an <see cref="AnimalAgent"/>: position on
    /// the ground tilted to the slope, the bool changes of the agent's driver, playback speed matched to ground speed,
    /// animation paused and shadows off at a distance. Plain class (no per-instance MonoBehaviour).
    /// </summary>
    internal sealed class AnimalView : IAnimatorBoolSink
    {
        public readonly string PrefabName;
        public readonly GameObject Root;
        public readonly Transform Transform;
        public readonly Animator Animator;
        private readonly Renderer[] renderers;
        private readonly int[] hashes;
        private readonly bool[] present;
        private readonly int idleHash;
        private readonly bool idleKnown;
        private bool animatorOn = true;
        private bool shadowsOn = true;
        private float pitch;

        /// <summary>Uniform scale applied to the prefab.</summary>
        public float Scale;

        /// <summary>Lowest point of the idle pose relative to the root at scale 1.</summary>
        public float FootOffset;

        /// <summary>Body length at the shown scale (for the slope tilt).</summary>
        public float Length;

        /// <summary>The species shown.</summary>
        public SpeciesInfo Info;

        public AnimalView(string prefabName, GameObject root, SpeciesInfo info, WildlifePalette.Entry entry, out string problem)
        {
            problem = null;
            PrefabName = prefabName;
            Root = root;
            Transform = root.transform;
            Info = info;
            Animator = root.GetComponentInChildren<Animator>(true);
            renderers = root.GetComponentsInChildren<Renderer>(true);
            hashes = new int[info.Bools.Length];
            present = new bool[info.Bools.Length];
            if (Animator == null)
            {
                problem = $"{prefabName} has no Animator";
                return;
            }

            Animator.applyRootMotion = false;
            Animator.keepAnimatorStateOnDisable = true;
            Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            AnimatorControllerParameter[] parameters = Animator.parameters;   // once per instance
            for (int i = 0; i < info.Bools.Length; i++)
            {
                hashes[i] = Animator.StringToHash(info.Bools[i]);
                foreach (AnimatorControllerParameter p in parameters)
                {
                    if (p.nameHash == hashes[i] && p.type == AnimatorControllerParameterType.Bool) present[i] = true;
                }

                // An Animator that is not initialised lists no parameters: trust the controller's YAML (read by the palette builder).
                if (parameters.Length == 0 && entry != null && entry.bools != null && System.Array.IndexOf(entry.bools, info.Bools[i]) >= 0) present[i] = true;

                if (!present[i]) problem = $"{prefabName}: the controller has no bool '{info.Bools[i]}' (that pose shows as idle)";
            }

            string idle = entry != null && !string.IsNullOrEmpty(entry.idleState) ? entry.idleState : info.IdleState;
            idleHash = Animator.StringToHash(idle);
            idleKnown = Animator.HasState(0, idleHash);
            if (!idleKnown) problem = $"{prefabName}: no idle state '{idle}' in layer 0 (pose changes fall back to timing)";
        }

        /// <inheritdoc />
        public void SetBool(int boolIndex, bool value)
        {
            if (Animator == null || boolIndex < 0 || boolIndex >= present.Length || !present[boolIndex]) return;
            Animator.SetBool(hashes[boolIndex], value);
        }

        /// <summary>True while the Animator runs (it is paused beyond the animation distance).</summary>
        public bool AnimatorRunning => animatorOn;

        /// <summary>True when the idle state can be observed now (the Animator runs).</summary>
        public bool IdleObservable => idleKnown && Animator != null && animatorOn && Animator.isActiveAndEnabled;

        /// <summary>True when the Animator is in (or blending into) the idle state.</summary>
        public bool InIdle
        {
            get
            {
                if (Animator.IsInTransition(0)) return Animator.GetNextAnimatorStateInfo(0).shortNameHash == idleHash;
                return Animator.GetCurrentAnimatorStateInfo(0).shortNameHash == idleHash;
            }
        }

        /// <summary>Back to the default state with every bool false (a reused instance).</summary>
        public void ResetAnimator()
        {
            if (Animator == null) return;
            Animator.enabled = true;
            animatorOn = true;
            Animator.Rebind();
            for (int i = 0; i < hashes.Length; i++)
            {
                if (present[i]) Animator.SetBool(hashes[i], false);
            }

            Animator.speed = 1f;
            Animator.Update(0f);
        }

        /// <summary>Shows the agent this frame.</summary>
        public void Sync(AnimalAgent a, float dt, float animateWithin, float shadowsWithin)
        {
            // Smooth the slope tilt; the pitch is sampled by the simulation for near animals only.
            pitch = Mathf.Lerp(pitch, a.Altitude > 0.05f ? 0f : a.Pitch, 1f - Mathf.Exp(-dt * 4f));
            float lift = a.Altitude > 0.05f ? 0f : -FootOffset * Scale;
            Transform.SetPositionAndRotation(new Vector3(a.X, a.Y + lift, a.Z), Quaternion.Euler(-pitch, a.Yaw, 0f));

            bool animate = a.TravellerDistance < animateWithin;
            if (Animator != null && animate != animatorOn)
            {
                animatorOn = animate;
                Animator.enabled = animate;
            }

            if (Animator != null && animatorOn)
            {
                float clip = a.Driver.Shown == Pose.Run ? a.Info.RunClipSpeed : a.Driver.Shown == Pose.Walk ? a.Info.WalkClipSpeed : 0f;
                float speed = clip > 0.01f && a.Speed > 0.05f ? Mathf.Clamp(a.Speed / clip, 0.55f, 2.2f) : 1f;
                Animator.speed = speed;
            }

            bool shadows = a.TravellerDistance < shadowsWithin;
            if (shadows != shadowsOn)
            {
                shadowsOn = shadows;
                foreach (Renderer r in renderers)
                {
                    if (r != null) r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                }
            }
        }

        /// <summary>Moves the instance by <paramref name="delta"/> (floating origin).</summary>
        public void Shift(Vector3 delta) => Transform.position += delta;
    }

    /// <summary>
    /// A few pooled positional AudioSources that play the animals' calls where they stand (and follow them while a loop
    /// plays): quiet, 3D, no doppler, volume scaled by the "Animal sounds" setting. No ambience bed.
    /// </summary>
    internal sealed class AnimalVoices
    {
        private const int Count = 8;
        private readonly AudioSource[] sources = new AudioSource[Count];
        private readonly AnimalAgent[] following = new AnimalAgent[Count];
        private readonly float[] stopAt = new float[Count];
        private readonly float[] baseVolume = new float[Count];
        private int next;

        public AnimalVoices(Transform parent)
        {
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("AnimalVoice" + i);
                go.transform.SetParent(parent, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 4f;
                s.maxDistance = 80f;
                s.dopplerLevel = 0f;
                s.spread = 0f;
                s.priority = 200;
                sources[i] = s;
            }
        }

        /// <summary>Plays a clip at the agent (looping for <paramref name="loopSeconds"/> when above zero).</summary>
        public void Play(AudioClip clip, AnimalAgent agent, float volume, float loopSeconds, float now)
        {
            if (clip == null || volume <= 0.001f) return;
            int index = -1;
            for (int i = 0; i < Count; i++)
            {
                if (!sources[i].isPlaying)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                index = next;
                next = (next + 1) % Count;
            }

            AudioSource s = sources[index];
            s.Stop();
            s.transform.position = new Vector3(agent.X, agent.Y + 0.6f, agent.Z);
            s.clip = clip;
            s.loop = loopSeconds > 0f;
            s.volume = volume;
            s.pitch = 0.94f + 0.12f * agent.Rng.Next();
            baseVolume[index] = volume;
            following[index] = agent;
            stopAt[index] = loopSeconds > 0f ? now + loopSeconds : float.MaxValue;
            s.Play();
        }

        /// <summary>Follows moving animals, ends loops, applies the volume setting.</summary>
        public void Update(float now, float master)
        {
            for (int i = 0; i < Count; i++)
            {
                AudioSource s = sources[i];
                if (!s.isPlaying)
                {
                    following[i] = null;
                    continue;
                }

                AnimalAgent a = following[i];
                if (a != null && a.Alive) s.transform.position = new Vector3(a.X, a.Y + 0.6f, a.Z);
                s.volume = baseVolume[i] * master;
                if (now >= stopAt[i] || (a != null && !a.Alive && s.loop) || master <= 0.001f) s.Stop();
            }
        }

        /// <summary>Stops everything.</summary>
        public void StopAll()
        {
            for (int i = 0; i < Count; i++)
            {
                sources[i].Stop();
                following[i] = null;
            }
        }

        /// <summary>Moves the sources (floating origin).</summary>
        public void Shift(Vector3 delta)
        {
            for (int i = 0; i < Count; i++) sources[i].transform.position += delta;
        }
    }
}
