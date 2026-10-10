using UnityEngine;

namespace FiresRPGmaker.UI
{
    /// <summary>The player's acceleration and camera turn rate expressed in the view the HUD sits in.</summary>
    internal readonly struct GlobeMotion
    {
        public static readonly GlobeMotion Still = new GlobeMotion(0f, 0f, 0f, 0f);

        public readonly float Lateral;
        public readonly float Forward;
        public readonly float Vertical;
        public readonly float YawRate;

        public GlobeMotion(float lateral, float forward, float vertical, float yawRate)
        {
            Lateral = lateral;
            Forward = forward;
            Vertical = vertical;
            YawRate = yawRate;
        }

        public GlobeMotion Scaled(float strength) => new GlobeMotion(Lateral * strength, Forward * strength, Vertical * strength, YawRate * strength);
    }

    internal sealed class GlobeMotionSampler
    {
        private const float VelocitySmoothingPerSecond = 18f;
        private const float MaxAcceleration = 40f;
        private const float MaxYawRate = 12f;
        private const float MinDeltaTime = 1e-4f;

        private Vector3 _smoothedVelocity;
        private Vector3 _previousSmoothedVelocity;
        private float _previousYaw;
        private bool _primed;

        public GlobeMotion Sample(Player player, Transform view, float dt)
        {
            Vector3 velocity = player.GetVelocity();
            float yaw = view.eulerAngles.y;
            if (!_primed || dt < MinDeltaTime)
            {
                _smoothedVelocity = _previousSmoothedVelocity = velocity;
                _previousYaw = yaw;
                _primed = true;
                return GlobeMotion.Still;
            }

            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, velocity, 1f - Mathf.Exp(-dt * VelocitySmoothingPerSecond));
            Vector3 acceleration = Vector3.ClampMagnitude((_smoothedVelocity - _previousSmoothedVelocity) / dt, MaxAcceleration);
            _previousSmoothedVelocity = _smoothedVelocity;

            float yawRate = Mathf.Clamp(Mathf.DeltaAngle(_previousYaw, yaw) * Mathf.Deg2Rad / dt, -MaxYawRate, MaxYawRate);
            _previousYaw = yaw;

            Vector3 forwardOnGround = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            return new GlobeMotion(Vector3.Dot(acceleration, view.right), Vector3.Dot(acceleration, forwardOnGround), acceleration.y, yawRate);
        }

        public void Reset() => _primed = false;
    }
}
