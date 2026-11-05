using MedievalZen.Wildlife;
using MedievalZen.World;
using UnityEngine;
using Pose = MedievalZen.Wildlife.Pose;

namespace MedievalZen.Player
{
    /// <summary>
    /// One horse the traveller can ride: a stripped <c>Resources/WildlifePalette</c> horse variant with its colliders removed (the
    /// player's CharacterController is the only collider), placed by whoever owns it (it has no mover of its own) and animated through
    /// <see cref="AnimatorBoolDriver"/>: idle, walk (also the trot, played fast: the controller has no trot clip), run (the canter,
    /// played slow) and eat (grazing). Plain class, created and pooled by <see cref="MedievalZen.Travel.HorseStable"/>.
    /// </summary>
    internal sealed class HorseBody
    {
        /// <summary>Who holds the horse.</summary>
        public enum Role
        {
            Free,
            Hitched,
            Own,
        }

        public readonly GameObject Go;
        public readonly AnimalView View;
        public readonly AnimatorBoolDriver Driver;
        public readonly float Height, Length, Lift;
        private readonly Renderer[] renderers;
        private float pitch;

        /// <summary>Who holds the horse now.</summary>
        public Role Holder = Role.Free;

        /// <summary>The inn it stands at (hitched) or was taken from (own); −1 none.</summary>
        public int InnId = -1;

        /// <summary>Root on the ground (scene) and yaw in degrees.</summary>
        public Vector3 Position;
        public float Yaw;

        private HorseBody(GameObject go, AnimalView view, SpeciesInfo info, float scale, WildlifePalette.Entry e)
        {
            Go = go;
            View = view;
            Driver = new AnimatorBoolDriver(info);
            Height = Mathf.Max(1.2f, e.height * scale);
            Length = Mathf.Max(1.6f, e.length * scale);
            Lift = -e.footOffset * scale;
            renderers = go.GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>A horse from the palette (null when the palette lacks it), hidden until placed.</summary>
        public static HorseBody Create(WildlifePalette palette, string prefabName, Transform parent)
        {
            WildlifePalette.Entry e = palette != null ? palette.Find(prefabName) : null;
            if (e == null || e.prefab == null) return null;
            SpeciesInfo info = SpeciesCatalog.Get(Species.Horse);
            GameObject go = Object.Instantiate(e.prefab, parent);
            go.name = "TravellerHorse_" + prefabName;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            var view = new AnimalView(prefabName, go, info, e, out string problem);
            if (problem != null) Debug.Log("Horse: " + problem);
            float scale = palette.ScaleFor(info, e);
            view.Scale = scale;
            go.transform.localScale = Vector3.one * scale;
            go.SetActive(false);
            return new HorseBody(go, view, info, scale, e);
        }

        /// <summary>Height of the saddle above the ground, metres.</summary>
        public float SaddleHeight => AstrideMath.SaddleFraction * Height;

        /// <summary>Where a rider's root stands (scene) for this horse at <see cref="Position"/> and <see cref="Yaw"/>: the saddle, a little behind the middle.</summary>
        public Vector3 RiderRoot => Position - Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward * (0.05f * Length);

        /// <summary>The horse's root for a rider whose root is at <paramref name="riderRoot"/>.</summary>
        public Vector3 RootUnder(Vector3 riderRoot, float yaw) => riderRoot + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * (0.05f * Length);

        /// <summary>True when any renderer was drawn by a camera lately.</summary>
        public bool Visible
        {
            get
            {
                if (!Go.activeInHierarchy) return false;
                foreach (Renderer r in renderers)
                {
                    if (r != null && r.isVisible) return true;
                }

                return false;
            }
        }

        public void Show(bool on)
        {
            if (Go.activeSelf == on) return;
            Go.SetActive(on);
            if (on)
            {
                View.ResetAnimator();
                Driver.Reset(View.Info);
            }
        }

        /// <summary>Puts the horse at <see cref="Position"/>/<see cref="Yaw"/>, pitched to the ground under its ends.</summary>
        public void Place(float dt)
        {
            float target = 0f;
            IWorldSurface surface = GameWorld.Current != null ? GameWorld.Current.Surface : null;
            if (surface != null && surface.HasGround)
            {
                Vector3 f = Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward * (0.4f * Length);
                float front = surface.GroundHeight(Position + f), back = surface.GroundHeight(Position - f);
                if (!float.IsNaN(front) && !float.IsNaN(back)) target = Mathf.Clamp(Mathf.Atan2(front - back, 0.8f * Length) * Mathf.Rad2Deg, -20f, 20f);
            }

            pitch = dt > 0f ? Mathf.Lerp(pitch, target, 1f - Mathf.Exp(-dt * 6f)) : target;
            Go.transform.SetPositionAndRotation(Position + Vector3.up * Lift, Quaternion.Euler(-pitch, Yaw, 0f));
        }

        /// <summary>Shows <paramref name="speed"/> m/s (or grazing / standing when still) through the controller's bools.</summary>
        public void Animate(float speed, bool graze, float dt)
        {
            Animator a = View.Animator;
            if (a == null || !Go.activeInHierarchy) return;
            HorseClip clip = HorseGaits.Clip(speed, out float playback);
            Driver.Wanted = clip == HorseClip.Run ? Pose.Run : clip == HorseClip.Walk ? Pose.Walk : graze ? Pose.Eat : Pose.Idle;
            Driver.Update(dt, View.IdleObservable, View.IdleObservable && View.InIdle, View);
            a.speed = Driver.Shown == Pose.Walk || Driver.Shown == Pose.Run ? playback : 1f;
        }

        public void Destroy()
        {
            if (Go != null) Object.Destroy(Go);
        }
    }
}
