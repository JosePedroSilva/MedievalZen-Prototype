using UnityEngine;

namespace MedievalZen.People
{
    /// <summary>
    /// One shown person: puts the pooled character where its <see cref="VillagerBrain"/> is (absolute coordinates through the floating origin,
    /// on the ground the world surface reports), feeds the Animator its ground speed (idle to walk, no root motion) and the seated pose its
    /// weight. Driven by <see cref="PeopleManager"/>; not interactive and without colliders.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Villager : MonoBehaviour
    {
        /// <summary>Animator float parameter: ground speed in m/s.</summary>
        public const string SpeedParameter = "Speed";

        private static readonly int SpeedHash = Animator.StringToHash(SpeedParameter);

        private Animator animator;
        private VillagerPose pose;
        private bool hasSpeed;
        private float shownSpeed;

        /// <summary>The brain shown, or null when pooled.</summary>
        public VillagerBrain Brain { get; private set; }

        /// <summary>The palette entry (prefab) this instance was made from.</summary>
        public PeoplePalette.Entry Entry { get; private set; }

        /// <summary>Binds a pooled instance to a brain.</summary>
        public void Bind(VillagerBrain brain, PeoplePalette.Entry entry)
        {
            Brain = brain;
            Entry = entry;
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    animator.applyRootMotion = false;
                    foreach (AnimatorControllerParameter p in animator.parameters)
                    {
                        if (p.nameHash == SpeedHash && p.type == AnimatorControllerParameterType.Float) hasSpeed = true;
                    }
                }
            }

            if (pose == null)
            {
                pose = GetComponent<VillagerPose>();
                if (pose == null) pose = gameObject.AddComponent<VillagerPose>();
            }

            pose.StandingHipHeight = entry != null && entry.hipHeight > 0.3f ? entry.hipHeight : 0.9f;
            shownSpeed = brain != null ? brain.Speed : 0f;
        }

        /// <summary>Releases the brain (back to the pool).</summary>
        public void Unbind()
        {
            Brain = null;
            Entry = null;
            if (pose != null) pose.Weight = 0f;
        }

        /// <summary>
        /// Places the instance: scene position from the absolute one minus the origin (<paramref name="originX"/>, <paramref name="originZ"/>)
        /// at ground height <paramref name="ground"/>; animates while <paramref name="animate"/> (paused far away).
        /// </summary>
        public void Apply(double originX, double originZ, float ground, bool animate, float dt)
        {
            if (Brain == null) return;
            transform.SetPositionAndRotation(new Vector3((float)(Brain.X - originX), ground, (float)(Brain.Z - originZ)), Quaternion.Euler(0f, Brain.Yaw, 0f));
            if (animator != null)
            {
                if (animator.enabled != animate) animator.enabled = animate;
                shownSpeed = Mathf.MoveTowards(shownSpeed, Brain.Speed, dt * 3f);
                if (animate && hasSpeed) animator.SetFloat(SpeedHash, shownSpeed);
            }

            if (pose != null)
            {
                pose.Weight = animate ? Brain.SitWeight : 0f;
                pose.SeatHeight = Brain.SeatHeight;
            }
        }
    }
}
