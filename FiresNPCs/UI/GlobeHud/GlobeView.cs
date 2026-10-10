using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.UI
{
    internal enum GlobeResource
    {
        Health,
        Eitr,
        Stamina,
        Adrenaline,
    }

    /// <summary>
    /// One liquid globe (or the adrenaline tube): reads its resource, smooths the level, turns quick gains into a pour
    /// and a glint, runs the slosh spring and feeds everything to its own copies of the liquid and glass materials.
    /// </summary>
    internal sealed class GlobeView
    {
        private const float FillSmoothSeconds = 0.18f;
        private const float LowThreshold = 0.25f;
        private const float LowPulseStrength = 0.85f;
        private const float InflowStartRate = 0.1f;
        private const float InflowGain = 4f;
        private const float InflowRisePerSecond = 4f;
        private const float InflowFallPerSecond = 1.5f;
        private const float GlintGainTrigger = 0.08f;
        private const float GainMemoryDecayPerSecond = 1.5f;
        private const float GlintSeconds = 0.7f;
        private const float TubeModeThreshold = 0.5f;

        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int VesselCenterId = Shader.PropertyToID("_VesselCenter");
        private static readonly int VesselRadiusId = Shader.PropertyToID("_VesselRadius");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int SurgeId = Shader.PropertyToID("_Surge");
        private static readonly int InflowId = Shader.PropertyToID("_Inflow");
        private static readonly int TiltId = Shader.PropertyToID("_Tilt");
        private static readonly int WaveAmpId = Shader.PropertyToID("_WaveAmp");
        private static readonly int WavePhaseId = Shader.PropertyToID("_WavePhase");
        private static readonly int AgitationId = Shader.PropertyToID("_Agitation");
        private static readonly int TimeId = Shader.PropertyToID("_GlobeTime");
        private static readonly int LowPulseId = Shader.PropertyToID("_LowPulse");
        private static readonly int GlintId = Shader.PropertyToID("_Glint");
        private static readonly int TopColorId = Shader.PropertyToID("_ColorTop");

        private static readonly Vector3[] Corners = new Vector3[4];

        private readonly Material _liquid;
        private readonly Material _glass;
        private readonly GlobeLiquidSlosh _slosh;
        private readonly Vector2 _vesselCenter;
        private readonly float _vesselRadius;

        private float _displayFill;
        private float _fillVelocity;
        private float _inflow;
        private float _gainMemory;
        private float _glintProgress = 1f;
        private float _previousTarget;
        private bool _primed;

        public GlobeView(GlobeResource resource, Image fill, Image glass, float sloshFrequency)
        {
            Resource = resource;
            Fill = fill;
            _liquid = new Material(fill.material);
            fill.material = _liquid;
            _glass = new Material(glass.material);
            glass.material = _glass;
            glass.raycastTarget = false;
            fill.raycastTarget = false;
            IsTube = _liquid.GetFloat(ModeId) > TubeModeThreshold;
            _vesselCenter = _liquid.GetVector(VesselCenterId);
            _vesselRadius = _liquid.GetFloat(VesselRadiusId);
            _slosh = new GlobeLiquidSlosh(sloshFrequency);
        }

        public GlobeResource Resource { get; }
        public Image Fill { get; }
        public RectTransform Rect => Fill.rectTransform;
        public bool IsTube { get; }
        public float Current { get; private set; }
        public float Max { get; private set; }
        public Color LiquidColor => _liquid.GetColor(TopColorId);
        public float TargetFill => Max > 0f ? Mathf.Clamp01(Current / Max) : 0f;

        public void Read(Player player)
        {
            switch (Resource)
            {
                case GlobeResource.Health:
                    Current = player.GetHealth();
                    Max = player.GetMaxHealth();
                    break;
                case GlobeResource.Stamina:
                    Current = player.GetStamina();
                    Max = player.GetMaxStamina();
                    break;
                case GlobeResource.Eitr:
                    Current = player.GetEitr();
                    Max = player.GetMaxEitr();
                    break;
                case GlobeResource.Adrenaline:
                    Current = player.GetAdrenaline();
                    Max = player.GetMaxAdrenaline();
                    break;
            }
        }

        public void Advance(float dt, in GlobeMotion motion)
        {
            float target = TargetFill;
            if (!_primed)
            {
                _displayFill = _previousTarget = target;
                _primed = true;
            }

            _gainMemory = _gainMemory * Mathf.Exp(-dt * GainMemoryDecayPerSecond) + Mathf.Max(0f, target - _previousTarget);
            _previousTarget = target;
            if (_gainMemory >= GlintGainTrigger)
            {
                _glintProgress = 0f;
                _gainMemory = 0f;
            }
            _glintProgress = Mathf.Min(1f, _glintProgress + dt / GlintSeconds);

            _displayFill = Mathf.SmoothDamp(_displayFill, target, ref _fillVelocity, FillSmoothSeconds, Mathf.Infinity, dt);
            float inflowTarget = IsTube ? 0f : Mathf.Clamp01((_fillVelocity - InflowStartRate) * InflowGain);
            float inflowRate = inflowTarget > _inflow ? InflowRisePerSecond : InflowFallPerSecond;
            _inflow = Mathf.MoveTowards(_inflow, inflowTarget, dt * inflowRate);
            _slosh.Advance(dt, motion, _fillVelocity);
        }

        public void Apply(float time)
        {
            float aspect = OnScreenAspect();
            bool low = !IsTube && Max > 0f && TargetFill < LowThreshold;
            _liquid.SetFloat(FillId, _displayFill);
            _liquid.SetFloat(SurgeId, _slosh.Surge);
            _liquid.SetFloat(InflowId, _inflow);
            _liquid.SetFloat(TiltId, _slosh.Tilt);
            _liquid.SetFloat(WaveAmpId, _slosh.Wave);
            _liquid.SetFloat(WavePhaseId, _slosh.Phase);
            _liquid.SetFloat(AgitationId, _slosh.Agitation);
            _liquid.SetFloat(TimeId, time);
            _liquid.SetFloat(LowPulseId, low ? LowPulseStrength : 0f);
            _liquid.SetFloat(AspectId, aspect);
            _glass.SetFloat(GlintId, _glintProgress < 1f ? _glintProgress : 0f);
            _glass.SetFloat(AspectId, aspect);
        }

        public bool Contains(Vector2 screenPoint, Camera eventCamera)
        {
            if (!Fill.gameObject.activeInHierarchy) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screenPoint, eventCamera, out var local)) return false;
            var rect = Rect.rect;
            var uv = new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
            if (IsTube) return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
            return ((uv - _vesselCenter) / _vesselRadius).sqrMagnitude <= 1f;
        }

        public Vector3 TopCenterWorld()
        {
            Rect.GetWorldCorners(Corners);
            return (Corners[1] + Corners[2]) * 0.5f;
        }

        public float WorldHeight()
        {
            Rect.GetWorldCorners(Corners);
            return Vector3.Distance(Corners[0], Corners[1]);
        }

        public float WorldWidth()
        {
            Rect.GetWorldCorners(Corners);
            return Vector3.Distance(Corners[0], Corners[3]);
        }

        public void Destroy()
        {
            Object.Destroy(_liquid);
            Object.Destroy(_glass);
        }

        private float OnScreenAspect()
        {
            float height = WorldHeight();
            return height > Mathf.Epsilon ? WorldWidth() / height : 1f;
        }
    }
}
