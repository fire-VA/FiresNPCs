using UnityEngine;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Two damped springs per globe (sideways tilt, front-back surge) pulled toward what the player's acceleration
    /// and camera turn imply, plus a decaying wave energy. FiresGlobeHudBuilder in the ValheimRip1.0 project mirrors
    /// these constants to render its film strips; change both together.
    /// </summary>
    internal sealed class GlobeLiquidSlosh
    {
        public const float HealthFrequency = 1.35f;
        public const float StaminaFrequency = 1.6f;
        public const float EitrFrequency = 1.9f;
        public const float AdrenalineFrequency = 2.2f;

        private const float DampingRatio = 0.18f;
        private const float Gravity = 9.81f;
        private const float TiltGain = 0.55f;
        private const float YawTiltGain = 0.06f;
        private const float MaxTiltTarget = 0.42f;
        private const float MaxTilt = 0.6f;
        private const float SurgeGain = 0.035f;
        private const float MaxSurgeTarget = 0.2f;
        private const float MaxSurge = 0.3f;
        private const float MaxWave = 0.08f;
        private const float WaveDecayPerSecond = 1.4f;
        private const float WaveTravel = 2.0f;
        private const float KickToWave = 0.001f;
        private const float VerticalToWave = 0.00008f;
        private const float FillRateToWave = 0.006f;
        private const float SwingToWave = 0.004f;
        private const float SurgeSwingToWave = 0.01f;
        private const float SwingToAgitation = 0.2f;
        private const float SurgeSwingToAgitation = 0.5f;
        private const float MaxStepSeconds = 1f / 60f;
        private const float PhaseWrap = 1000f * Mathf.PI;

        private readonly float _omega;
        private float _tilt;
        private float _tiltVelocity;
        private float _surge;
        private float _surgeVelocity;
        private float _wave;
        private float _previousTiltTarget;
        private float _previousSurgeTarget;

        public GlobeLiquidSlosh(float frequency)
        {
            _omega = 2f * Mathf.PI * frequency;
        }

        public float Tilt => _tilt;
        public float Surge => _surge;
        public float Wave => _wave;
        public float Phase { get; private set; }

        public float Agitation => Mathf.Clamp01(_wave / MaxWave
            + Mathf.Abs(_tiltVelocity) * SwingToAgitation
            + Mathf.Abs(_surgeVelocity) * SurgeSwingToAgitation);

        public void Advance(float dt, in GlobeMotion motion, float fillRate)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / MaxStepSeconds));
            float stepSeconds = dt / steps;
            for (int step = 0; step < steps; step++)
                Step(stepSeconds, motion, fillRate);
        }

        private void Step(float dt, in GlobeMotion motion, float fillRate)
        {
            float tiltTarget = Mathf.Clamp(-Mathf.Atan(motion.Lateral / Gravity) * TiltGain - motion.YawRate * YawTiltGain, -MaxTiltTarget, MaxTiltTarget);
            float surgeTarget = Mathf.Clamp(motion.Forward * SurgeGain, -MaxSurgeTarget, MaxSurgeTarget);
            float kick = (Mathf.Abs(tiltTarget - _previousTiltTarget) + Mathf.Abs(surgeTarget - _previousSurgeTarget)) / dt;
            _previousTiltTarget = tiltTarget;
            _previousSurgeTarget = surgeTarget;

            Spring(dt, tiltTarget, ref _tilt, ref _tiltVelocity, MaxTilt);
            Spring(dt, surgeTarget, ref _surge, ref _surgeVelocity, MaxSurge);

            _wave += kick * KickToWave + Mathf.Abs(motion.Vertical) * VerticalToWave + Mathf.Abs(fillRate) * FillRateToWave
                   + Mathf.Abs(_tiltVelocity) * SwingToWave + Mathf.Abs(_surgeVelocity) * SurgeSwingToWave;
            _wave = Mathf.Min(MaxWave, _wave) * Mathf.Exp(-dt * WaveDecayPerSecond);
            Phase = Mathf.Repeat(Phase + dt * (_omega + WaveTravel), PhaseWrap);
        }

        private void Spring(float dt, float target, ref float value, ref float velocity, float limit)
        {
            float acceleration = _omega * _omega * (target - value) - 2f * DampingRatio * _omega * velocity;
            velocity += acceleration * dt;
            value += velocity * dt;
            if (Mathf.Abs(value) <= limit) return;
            value = Mathf.Sign(value) * limit;
            velocity = 0f;
        }
    }
}
