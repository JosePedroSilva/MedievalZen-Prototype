using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>How <see cref="PlayerMotor"/> treats a movement request.</summary>
    public enum MoveStyle
    {
        /// <summary>
        /// Third person and auto-travel: the body turns towards the requested direction at a bounded rate and always
        /// walks where it faces. A request behind the body slows it (ease off, pivot, walk away); it never slides backwards.
        /// </summary>
        FaceMovement,

        /// <summary>
        /// First person: the body's yaw is set separately (<see cref="PlayerMotor.Face"/>, following the camera) and the
        /// velocity is free to go sideways or backwards; a change of direction rotates the velocity instead of braking through zero.
        /// </summary>
        Strafe,

        /// <summary>
        /// Riding (<see cref="PlayerMotor.MoveRiding"/>): face-movement with a horse's turn (<see cref="RidingMath"/>): the yaw rate
        /// falls as the speed rises, and a request far off the facing arcs round at a walk instead of stopping to pivot.
        /// </summary>
        Riding,
    }

    /// <summary>Tuning of <see cref="MotorMath"/> (the serialized fields of <see cref="PlayerMotor"/>).</summary>
    public struct MotorTuning
    {
        /// <summary>How quickly the speed builds up towards the requested speed, in m/s².</summary>
        public float Acceleration;

        /// <summary>How quickly the speed drops when input stops or points away, in m/s².</summary>
        public float Deceleration;

        /// <summary>Face-movement: fastest body turn in degrees per second (large direction changes turn at this rate; small ones ease in exponentially).</summary>
        public float MaxTurnRate;

        /// <summary>Face-movement: requests up to this many degrees off the facing keep full speed.</summary>
        public float FullSpeedAngle;

        /// <summary>Face-movement: requests this many degrees or more off the facing ask for zero speed (the body pivots first).</summary>
        public float ZeroSpeedAngle;

        /// <summary>Face-movement: how fast (degrees per second) the velocity swings round to the body's forward; above the max turn rate so the feet never lag the facing.</summary>
        public float VelocityTurnRate;

        /// <summary>Strafe: how fast (degrees per second) the velocity swings towards a new request.</summary>
        public float StrafeTurnRate;

        /// <summary>Strafe: a request further than this from the current velocity brakes to a stop and restarts in the new direction instead of swinging round.</summary>
        public float StrafeReverseAngle;

        /// <summary>The validated defaults: 5 / 6 m/s², 300 °/s, full speed within 35°, zero beyond 155°.</summary>
        public static MotorTuning Default => new MotorTuning
        {
            Acceleration = 5f,
            Deceleration = 6f,
            MaxTurnRate = 300f,
            FullSpeedAngle = 35f,
            ZeroSpeedAngle = 155f,
            VelocityTurnRate = 720f,
            StrafeTurnRate = 540f,
            StrafeReverseAngle = 125f,
        };
    }

    /// <summary>Per-frame motor state that <see cref="MotorMath"/> advances: body yaw and planar velocity (x, z).</summary>
    public struct MotorState
    {
        /// <summary>Body yaw in degrees, wrapped to (-180, 180].</summary>
        public float Yaw;

        /// <summary>Planar velocity in m/s: x = world X, y = world Z.</summary>
        public Vector2 Velocity;

        /// <summary>Direction of the turn in progress (+1 right, -1 left, 0 none), so a request straight behind never flip-flops.</summary>
        public float TurnSign;
    }

    /// <summary>
    /// Pure maths of the player's planar locomotion (<see cref="PlayerMotor"/>): yaw stepping with a rate limit, the
    /// speed-vs-alignment rule of face-movement, velocity steering, and body-space decomposition. Planar vectors are
    /// <see cref="Vector2"/> (x = world X, y = world Z); yaw is degrees clockwise from +Z seen from above (Unity's yaw).
    /// No state, no Unity objects; everything is unit-tested and the auto-travel simulation runs on it offline.
    /// </summary>
    public static class MotorMath
    {
        private const float Epsilon = 1e-4f;

        /// <summary>Beyond this many degrees a request counts as "behind": the turn keeps its current direction.</summary>
        public const float AmbiguousTurnAngle = 150f;

        // ------------------------------------------------------------------ angles

        /// <summary>Yaw (degrees, Unity convention) of a planar direction; 0 for a zero vector.</summary>
        public static float YawOf(Vector2 planar)
        {
            if (planar.sqrMagnitude < 1e-12f) return 0f;
            return Mathf.Atan2(planar.x, planar.y) * Mathf.Rad2Deg;
        }

        /// <summary>Unit planar direction of a yaw in degrees.</summary>
        public static Vector2 FromYaw(float yawDegrees)
        {
            float radians = yawDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        /// <summary>
        /// Signed turn (degrees, + = right) from <paramref name="yaw"/> to <paramref name="targetYaw"/>, normally the short way.
        /// When the target is almost straight behind (beyond <see cref="AmbiguousTurnAngle"/>) and a turn is already under way
        /// (<paramref name="turnSign"/> ≠ 0), it keeps turning the same way, so a stick held backwards never makes the body
        /// twitch between shoulders.
        /// </summary>
        public static float TurnDelta(float yaw, float targetYaw, float turnSign)
        {
            float delta = LocomotionMath.WrapAngle(targetYaw - yaw);
            if (turnSign != 0f && Mathf.Abs(delta) > AmbiguousTurnAngle && Mathf.Sign(delta) != Mathf.Sign(turnSign))
            {
                delta -= 360f * Mathf.Sign(delta);
            }

            return delta;
        }

        /// <summary>
        /// One frame of body turn covering part of <paramref name="delta"/> degrees: exponential easing with
        /// <paramref name="sharpness"/> (1/s; small corrections feel exactly as before), capped at
        /// <paramref name="maxRate"/> degrees per second (large ones become a smooth, bounded pivot). Returns the step in degrees.
        /// </summary>
        public static float YawStep(float delta, float sharpness, float maxRate, float deltaTime)
        {
            if (deltaTime <= 0f) return 0f;
            float step = delta * (1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * deltaTime));
            float limit = Mathf.Max(0f, maxRate) * deltaTime;
            return Mathf.Clamp(step, -limit, limit);
        }

        /// <summary>
        /// Share (0..1) of the requested speed the body may use while the request is <paramref name="angleDegrees"/> off its
        /// facing: 1 up to <paramref name="fullSpeedAngle"/>, easing (smoothstep) to 0 at <paramref name="zeroSpeedAngle"/>.
        /// </summary>
        public static float Alignment(float angleDegrees, float fullSpeedAngle, float zeroSpeedAngle)
        {
            float angle = Mathf.Abs(angleDegrees);
            if (angle <= fullSpeedAngle) return 1f;
            if (angle >= zeroSpeedAngle || zeroSpeedAngle <= fullSpeedAngle) return 0f;
            float t = (angle - fullSpeedAngle) / (zeroSpeedAngle - fullSpeedAngle);
            return 1f - t * t * (3f - 2f * t);
        }

        // ------------------------------------------------------------------ velocity

        /// <summary>
        /// Steers a planar <paramref name="velocity"/> towards <paramref name="direction"/> (any length; zero = keep the
        /// current heading) at <paramref name="targetSpeed"/>. The heading swings round at up to <paramref name="turnRate"/>
        /// degrees per second while the speed eases towards the target (<paramref name="acceleration"/> /
        /// <paramref name="deceleration"/>, m/s²). If the request is more than <paramref name="reverseAngle"/> degrees from
        /// the heading, the velocity instead brakes along its line and restarts in the new direction once stopped.
        /// </summary>
        public static Vector2 SteerVelocity(Vector2 velocity, Vector2 direction, float targetSpeed, float turnRate,
            float reverseAngle, float acceleration, float deceleration, float deltaTime)
        {
            if (deltaTime <= 0f) return velocity;
            targetSpeed = Mathf.Max(0f, targetSpeed);
            float speed = velocity.magnitude;
            bool hasDirection = direction.sqrMagnitude > 1e-10f;

            if (speed < Epsilon)
            {
                if (!hasDirection || targetSpeed < Epsilon) return Vector2.zero;
                return direction.normalized * Mathf.Min(targetSpeed, acceleration * deltaTime);
            }

            float heading = YawOf(velocity);
            if (hasDirection)
            {
                float turn = LocomotionMath.WrapAngle(YawOf(direction) - heading);
                if (Mathf.Abs(turn) > reverseAngle)
                {
                    // A reversal: brake along the current line; the next frames restart from standstill.
                    float braked = Mathf.MoveTowards(speed, 0f, deceleration * deltaTime);
                    return braked < Epsilon ? Vector2.zero : velocity * (braked / speed);
                }

                float limit = Mathf.Max(0f, turnRate) * deltaTime;
                heading += Mathf.Clamp(turn, -limit, limit);
            }
            else
            {
                targetSpeed = 0f;
            }

            float rate = targetSpeed > speed ? acceleration : deceleration;
            float newSpeed = Mathf.MoveTowards(speed, targetSpeed, rate * deltaTime);
            return newSpeed < Epsilon ? Vector2.zero : FromYaw(heading) * newSpeed;
        }

        /// <summary>
        /// Face-movement frame: turns the body towards <paramref name="request"/> (m/s; its length is the wanted speed) at a
        /// bounded rate, derives the speed from how well the body is aligned with the request, and swings the velocity round
        /// to the body's forward. A zero request keeps the facing and eases to a stop along it.
        /// </summary>
        public static void StepFaceMovement(ref MotorState state, Vector2 request, float turnSharpness, in MotorTuning tuning, float deltaTime)
        {
            if (deltaTime <= 0f) return;
            float wanted = request.magnitude;
            float targetSpeed = 0f;
            if (wanted > Epsilon)
            {
                float delta = TurnDelta(state.Yaw, YawOf(request), state.TurnSign);
                float step = YawStep(delta, turnSharpness, tuning.MaxTurnRate, deltaTime);
                state.TurnSign = Mathf.Abs(delta) > 1f ? Mathf.Sign(delta) : 0f;
                state.Yaw = LocomotionMath.WrapAngle(state.Yaw + step);
                targetSpeed = wanted * Alignment(delta - step, tuning.FullSpeedAngle, tuning.ZeroSpeedAngle);
            }
            else
            {
                state.TurnSign = 0f;
            }

            state.Velocity = SteerVelocity(state.Velocity, FromYaw(state.Yaw), targetSpeed, tuning.VelocityTurnRate, 180f,
                tuning.Acceleration, tuning.Deceleration, deltaTime);
        }

        /// <summary>
        /// Strafe frame: the velocity steers towards <paramref name="request"/> (m/s) — swinging round for turns up to
        /// <see cref="MotorTuning.StrafeReverseAngle"/>, braking and restarting for reversals. The yaw is not touched.
        /// </summary>
        public static void StepStrafe(ref MotorState state, Vector2 request, in MotorTuning tuning, float deltaTime)
        {
            state.TurnSign = 0f;
            state.Velocity = SteerVelocity(state.Velocity, request, request.magnitude, tuning.StrafeTurnRate,
                tuning.StrafeReverseAngle, tuning.Acceleration, tuning.Deceleration, deltaTime);
        }

        /// <summary>Exponential yaw follow (the pre-existing <see cref="PlayerMotor.Face"/> behaviour): new yaw, wrapped.</summary>
        public static float FollowYaw(float yaw, float targetYaw, float sharpness, float deltaTime)
        {
            float t = 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * Mathf.Max(0f, deltaTime));
            return LocomotionMath.WrapAngle(yaw + LocomotionMath.WrapAngle(targetYaw - yaw) * t);
        }

        // ------------------------------------------------------------------ body space

        /// <summary>
        /// A planar world velocity in body space for a body at <paramref name="bodyYaw"/>: x = rightwards, y = forwards (m/s).
        /// </summary>
        public static Vector2 LocalVelocity(Vector2 worldVelocity, float bodyYaw)
        {
            Vector2 forward = FromYaw(bodyYaw);
            var right = new Vector2(forward.y, -forward.x);
            return new Vector2(Vector2.Dot(worldVelocity, right), Vector2.Dot(worldVelocity, forward));
        }
    }
}
