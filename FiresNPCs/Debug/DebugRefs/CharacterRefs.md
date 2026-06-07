// Decompiled with JetBrains decompiler
// Type: Character
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using System;
using System.Collections.Generic;
using UnityEngine;

#nullable disable
public class Character : MonoBehaviour, IDestructible, Hoverable, IWaterInteractable, IMonoUpdater
{
  private float m_underWorldCheckTimer;
  private static float takeInputDelay = 0.0f;
  private float currentRotSpeedFactor;
  private float m_standUp;
  private const float c_StandUpTime = 2f;
  private const float c_StandUpTriggerState = -100f;
  private Collider m_lowestContactCollider;
  private bool m_groundContact;
  private Vector3 m_groundContactPoint = Vector3.zero;
  private Vector3 m_groundContactNormal = Vector3.zero;
  private Transform oldParent;
  private int m_cachedCurrentAnimHash;
  private int m_cachedNextAnimHash;
  private int m_cachedNextOrCurrentAnimHash;
  private int m_cachedAnimHashFrame;
  public ZNetView m_nViewOverride;
  public Action<float, Character> m_onDamaged;
  public Action m_onDeath;
  public Action<int> m_onLevelSet;
  public Action<Vector3> m_onLand;
  [Header("Character")]
  public string m_name = "";
  public string m_group = "";
  public Character.Faction m_faction = Character.Faction.AnimalsVeg;
  public bool m_boss;
  public bool m_dontHideBossHud;
  public string m_bossEvent = "";
  [Tooltip("Also sets player unique key")]
  public string m_defeatSetGlobalKey = "";
  public bool m_aiSkipTarget;
  [Header("Movement & Physics")]
  public float m_crouchSpeed = 2f;
  public float m_walkSpeed = 5f;
  public float m_speed = 10f;
  public float m_turnSpeed = 300f;
  public float m_runSpeed = 20f;
  public float m_runTurnSpeed = 300f;
  public float m_flySlowSpeed = 5f;
  public float m_flyFastSpeed = 12f;
  public float m_flyTurnSpeed = 12f;
  public float m_acceleration = 1f;
  public float m_jumpForce = 10f;
  public float m_jumpForceForward;
  public float m_jumpForceTiredFactor = 0.7f;
  public float m_airControl = 0.1f;
  public bool m_canSwim = true;
  public float m_swimDepth = 2f;
  public float m_swimSpeed = 2f;
  public float m_swimTurnSpeed = 100f;
  public float m_swimAcceleration = 0.05f;
  public Character.GroundTiltType m_groundTilt;
  public float m_groundTiltSpeed = 50f;
  public bool m_flying;
  public float m_jumpStaminaUsage = 10f;
  public bool m_disableWhileSleeping;
  [Header("Bodyparts")]
  public Transform m_eye;
  protected Transform m_head;
  [Header("Effects")]
  public EffectList m_hitEffects = new EffectList();
  public EffectList m_critHitEffects = new EffectList();
  public EffectList m_backstabHitEffects = new EffectList();
  public EffectList m_deathEffects = new EffectList();
  public EffectList m_waterEffects = new EffectList();
  public EffectList m_tarEffects = new EffectList();
  public EffectList m_slideEffects = new EffectList();
  public EffectList m_jumpEffects = new EffectList();
  public EffectList m_flyingContinuousEffect = new EffectList();
  public EffectList m_pheromoneLoveEffect = new EffectList();
  public bool m_useAltStatusEffectScaling;
  [Header("Health & Damage")]
  public bool m_tolerateWater = true;
  public bool m_tolerateFire;
  public bool m_tolerateSmoke = true;
  public bool m_tolerateTar;
  public float m_health = 10f;
  public float m_regenAllHPTime = 3600f;
  public HitData.DamageModifiers m_damageModifiers;
  public WeakSpot[] m_weakSpots;
  public bool m_staggerWhenBlocked = true;
  public float m_staggerDamageFactor;
  public float m_enemyAdrenalineMultiplier = 1f;
  private float m_staggerDamage;
  private float m_backstabTime = -99999f;
  private GameObject[] m_waterEffects_instances;
  private GameObject[] m_slideEffects_instances;
  private GameObject[] m_flyingEffects_instances;
  protected Vector3 m_moveDir = Vector3.zero;
  protected Vector3 m_lookDir = Vector3.forward;
  protected Quaternion m_lookYaw = Quaternion.identity;
  protected bool m_run;
  protected bool m_walk;
  private Vector3 m_lookTransitionStart;
  private Vector3 m_lookTransitionTarget;
  protected float m_lookTransitionTime;
  protected float m_lookTransitionTimeTotal;
  protected bool m_attack;
  protected bool m_attackHold;
  protected bool m_secondaryAttack;
  protected bool m_secondaryAttackHold;
  protected bool m_blocking;
  protected GameObject m_visual;
  protected LODGroup m_lodGroup;
  protected Rigidbody m_body;
  protected CapsuleCollider m_collider;
  protected ZNetView m_nview;
  protected ZSyncAnimation m_zanim;
  protected Animator m_animator;
  protected CharacterAnimEvent m_animEvent;
  protected BaseAI m_baseAI;
  private const float c_MaxFallHeight = 20f;
  private const float c_MinFallHeight = 4f;
  private const float c_MaxFallDamage = 100f;
  private const float c_StaggerDamageBonus = 2f;
  private const float c_AutoJumpInterval = 0.5f;
  private const float c_MinSlideDegreesPlayer = 38f;
  private const float c_MinSlideDegreesMount = 45f;
  private const float c_MinSlideDegreesMonster = 90f;
  private const float c_RootMotionMultiplier = 55f;
  private const float c_PushForceScale = 2.5f;
  private const float c_ContinuousPushForce = 20f;
  private const float c_PushForceDissipation = 100f;
  private const float c_MaxMoveForce = 20f;
  private const float c_StaggerResetTime = 5f;
  private const float c_BackstabResetTime = 300f;
  private const float m_slopeStaminaDrain = 10f;
  public const float m_minSlideDegreesPlayer = 38f;
  public const float m_minSlideDegreesMount = 45f;
  public const float m_minSlideDegreesMonster = 90f;
  private const float m_rootMotionMultiplier = 55f;
  private const float m_pushForceScale = 2.5f;
  private const float m_continousPushForce = 20f;
  private const float m_pushForcedissipation = 100f;
  private const float m_maxMoveForce = 20f;
  private const float m_staggerResetTime = 5f;
  private const float m_backstabResetTime = 300f;
  private float m_jumpTimer;
  private float m_lastAutoJumpTime;
  private float m_lastGroundTouch;
  private Vector3 m_lastGroundNormal = Vector3.up;
  private Vector3 m_lastGroundPoint = Vector3.up;
  private Collider m_lastGroundCollider;
  private Rigidbody m_lastGroundBody;
  private float m_groundForceTimer;
  private float m_originalMass;
  private Vector3 m_lastAttachPos = Vector3.zero;
  private Rigidbody m_lastAttachBody;
  protected float m_maxAirAltitude = -10000f;
  private float m_waterLevel = -10000f;
  private float m_tarLevel = -10000f;
  private float m_liquidLevel = -10000f;
  private float m_swimTimer = 999f;
  private float m_lavaTimer = 999f;
  private float m_aboveOrInLavaTimer = 999f;
  private float m_fallTimer;
  protected SEMan m_seman;
  private float m_noiseRange;
  private float m_syncNoiseTimer;
  private bool m_tamed;
  private float m_lastTamedCheck;
  private Tameable m_tameable;
  private MonsterAI m_tameableMonsterAI;
  private int m_level = 1;
  private RaycastHit[] m_lavaRoofCheck = new RaycastHit[1];
  private bool m_localPlayerHasHit;
  protected HitData m_lastHit;
  private Vector3 m_currentVel = Vector3.zero;
  private float m_currentTurnVel;
  private float m_currentTurnVelChange;
  private Vector3 m_groundTiltNormal = Vector3.up;
  protected Vector3 m_pushForce = Vector3.zero;
  private Vector3 m_rootMotion = Vector3.zero;
  private static readonly int s_forwardSpeed = ZSyncAnimation.GetHash("forward_speed");
  private static readonly int s_sidewaySpeed = ZSyncAnimation.GetHash("sideway_speed");
  private static readonly int s_turnSpeed = ZSyncAnimation.GetHash("turn_speed");
  private static readonly int s_inWater = ZSyncAnimation.GetHash("inWater");
  private static readonly int s_onGround = ZSyncAnimation.GetHash("onGround");
  private static readonly int s_encumbered = ZSyncAnimation.GetHash("encumbered");
  private static readonly int s_flying = ZSyncAnimation.GetHash("flying");
  private float m_slippage;
  protected bool m_wallRunning;
  private bool m_sliding;
  private bool m_running;
  private bool m_walking;
  private Vector3 m_originalLocalRef;
  private bool m_lodVisible = true;
  private static int s_smokeRayMask = 0;
  private float m_smokeCheckTimer;
  [Header("Heat & Lava")]
  private float m_minLavaMaskThreshold = 0.05f;
  public float m_heatBuildupBase = 1.5f;
  public float m_heatCooldownBase = 1f;
  public float m_heatBuildupWater = 2f;
  public float m_heatWaterTouchMultiplier = 0.2f;
  public float m_lavaDamageTickInterval = 0.2f;
  public float m_heatLevelFirstDamageThreshold = 0.7f;
  public float m_lavaFirstDamage = 10f;
  public float m_lavaFullDamage = 100f;
  public float m_lavaAirDamageHeight = 3f;
  public float m_dayHeatGainRunning = 0.2f;
  public float m_dayHeatGainStill = -0.05f;
  public float m_dayHeatEquipmentStop = 0.5f;
  public float m_lavaSlowMax = 0.5f;
  public float m_lavaSlowHeight = 0.8f;
  public EffectList m_lavaHeatEffects = new EffectList();
  private Dictionary<ParticleSystem, float> m_lavaHeatParticles = new Dictionary<ParticleSystem, float>();
  private List<ZSFX> m_lavaHeatAudio = new List<ZSFX>();
  private float m_lavaHeatLevel;
  private float m_lavaProximity;
  private float m_lavaHeightFactor;
  private float m_lavaDamageTimer;
  private static bool s_dpsDebugEnabled = false;
  private static readonly List<KeyValuePair<float, float>> s_enemyDamage = new List<KeyValuePair<float, float>>();
  private static readonly List<KeyValuePair<float, float>> s_playerDamage = new List<KeyValuePair<float, float>>();
  private static readonly List<Character> s_characters = new List<Character>();
  private static int s_characterLayer = 0;
  private static int s_characterNetLayer = 0;
  private static int s_characterGhostLayer = 0;
  protected static int s_groundRayMask = 0;
  protected static int s_characterLayerMask = 0;
  private static int s_blockedRayMask;
  private float m_pheromoneTimer = 5f;
  private float m_cashedInLiquidDepth;
  private Quaternion m_tiltRotCached = Quaternion.identity;
  private Vector3 m_bodyVelocityCached = Vector3.negativeInfinity;
  protected static readonly int s_animatorTagFreeze = ZSyncAnimation.GetHash("freeze");
  protected static readonly int s_animatorTagStagger = ZSyncAnimation.GetHash("stagger");
  protected static readonly int s_animatorTagSitting = ZSyncAnimation.GetHash("sitting");
  private static readonly int s_animatorFalling = ZSyncAnimation.GetHash("falling");
  private static readonly int s_tilt = ZSyncAnimation.GetHash("tilt");
  public static int m_debugFlySpeed = 20;
  private readonly int[] m_liquids = new int[2];

  protected virtual void Awake()
  {
    Character.s_characters.Add(this);
    this.m_collider = this.GetComponent<CapsuleCollider>();
    this.m_body = this.GetComponent<Rigidbody>();
    this.m_zanim = this.GetComponent<ZSyncAnimation>();
    this.m_nview = (UnityEngine.Object) this.m_nViewOverride != (UnityEngine.Object) null ? this.m_nViewOverride : this.GetComponent<ZNetView>();
    this.m_animator = this.GetComponentInChildren<Animator>();
    this.m_animEvent = this.m_animator.GetComponent<CharacterAnimEvent>();
    this.m_baseAI = this.GetComponent<BaseAI>();
    this.m_animator.logWarnings = false;
    this.m_visual = this.transform.Find("Visual").gameObject;
    this.m_lodGroup = this.m_visual.GetComponent<LODGroup>();
    this.m_head = Utils.GetBoneTransform(this.m_animator, HumanBodyBones.Head);
    this.m_body.maxDepenetrationVelocity = 2f;
    this.m_originalMass = this.m_body.mass;
    if (Character.s_smokeRayMask == 0)
    {
      Character.s_smokeRayMask = LayerMask.GetMask("smoke");
      Character.s_characterLayer = LayerMask.NameToLayer("character");
      Character.s_characterNetLayer = LayerMask.NameToLayer("character_net");
      Character.s_characterGhostLayer = LayerMask.NameToLayer("character_ghost");
      Character.s_groundRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "blocker", "vehicle");
      Character.s_characterLayerMask = LayerMask.GetMask("character", "character_noenv");
      Character.s_blockedRayMask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain");
    }
    if ((bool) (UnityEngine.Object) this.m_lodGroup)
      this.m_originalLocalRef = this.m_lodGroup.localReferencePoint;
    this.m_seman = new SEMan(this, this.m_nview);
    if (this.m_nview.GetZDO() != null)
    {
      if (!this.IsPlayer())
      {
        this.m_tamed = this.m_nview.GetZDO().GetBool(ZDOVars.s_tamed, this.m_tamed);
        this.m_level = this.m_nview.GetZDO().GetInt(ZDOVars.s_level, 1);
        if (this.m_nview.IsOwner() && (double) this.GetHealth() == (double) this.GetMaxHealth())
          this.SetupMaxHealth();
      }
      this.m_nview.Register<HitData>("RPC_Damage", new Action<long, HitData>(this.RPC_Damage));
      this.m_nview.Register<float>("RPC_AddAdrenaline", new Action<long, float>(this.RPC_AddAdrenaline));
      this.m_nview.Register<float, bool>("RPC_Heal", new Action<long, float, bool>(this.RPC_Heal));
      this.m_nview.Register<float>("RPC_AddNoise", new Action<long, float>(this.RPC_AddNoise));
      this.m_nview.Register<Vector3>("RPC_Stagger", new Action<long, Vector3>(this.RPC_Stagger));
      this.m_nview.Register("RPC_ResetCloth", new Action<long>(this.RPC_ResetCloth));
      this.m_nview.Register<bool>("RPC_SetTamed", new Action<long, bool>(this.RPC_SetTamed));
      this.m_nview.Register<float>("RPC_FreezeFrame", new Action<long, float>(this.RPC_FreezeFrame));
      this.m_nview.Register<Vector3, Quaternion, bool>("RPC_TeleportTo", new Action<long, Vector3, Quaternion, bool>(this.RPC_TeleportTo));
    }
    if (this.IsPlayer())
      return;
    if ((double) Game.m_enemySpeedSize != 1.0 && !this.InInterior())
      this.transform.localScale *= Game.m_enemySpeedSize;
    if (Game.m_worldLevel <= 0)
      return;
    this.transform.localScale *= (float) (1.0 + (double) Game.m_worldLevel * (double) Game.instance.m_worldLevelEnemyMoveSpeedMultiplier);
  }

  protected virtual void OnEnable() => Character.Instances.Add((IMonoUpdater) this);

  protected virtual void OnDisable() => Character.Instances.Remove((IMonoUpdater) this);

  private void SetupMaxHealth()
  {
    int level = this.GetLevel();
    this.SetMaxHealth(this.GetMaxHealthBase() * (float) level);
  }

  protected virtual void Start()
  {
  }

  protected virtual void OnDestroy()
  {
    this.m_seman.OnDestroy();
    Character.s_characters.Remove(this);
    if (!((UnityEngine.Object) EnemyHud.instance != (UnityEngine.Object) null))
      return;
    EnemyHud.instance.RemoveCharacterHud(this);
  }

  public void SetLevel(int level)
  {
    if (level < 1)
      return;
    this.m_level = level;
    this.m_nview.GetZDO().Set(ZDOVars.s_level, level);
    this.SetupMaxHealth();
    if (this.m_onLevelSet == null)
      return;
    this.m_onLevelSet(this.m_level);
  }

  public int GetLevel() => this.m_level;

  public virtual bool IsPlayer() => false;

  public Character.Faction GetFaction() => this.m_faction;

  public string GetGroup() => this.m_group;

  public virtual void CustomFixedUpdate(float dt)
  {
    if (!this.m_nview.IsValid())
      return;
    ZDO zdo = this.m_nview.GetZDO();
    int num = zdo.IsOwner() ? 1 : 0;
    bool visible = zdo.HasOwner();
    this.CalculateLiquidDepth();
    this.UpdateLayer();
    this.UpdateContinousEffects();
    this.UpdateWater(dt);
    this.UpdateGroundTilt(dt);
    this.SetVisible(visible);
    this.UpdateLookTransition(dt);
    if (num != 0)
    {
      this.UpdateGroundContact(dt);
      this.UpdateNoise(dt);
      this.m_seman.Update(zdo, dt);
      this.UpdateStagger(dt);
      this.UpdatePushback(dt);
      this.UpdateMotion(dt);
      this.UpdateSmoke(dt);
      this.UpdateLava(dt);
      this.UpdateAshlandsWater(dt);
      this.UpdateHeatDamage(dt);
      this.UnderWorldCheck(dt);
      this.UpdatePheromones(dt);
      this.SyncVelocity();
      if ((double) this.m_groundForceTimer > 0.0)
        this.m_groundForceTimer -= dt;
      this.CheckDeath();
    }
    this.UpdateHeatEffects(dt);
    if (!this.IsPlayer() || !Terminal.m_showTests)
      return;
    Terminal.m_testList["Player.Zone"] = ZoneSystem.GetZone(this.transform.position).ToString();
  }

  private void UpdateLayer()
  {
    int layer = this.m_collider.gameObject.layer;
    if (layer == Character.s_characterLayer || layer == Character.s_characterNetLayer)
    {
      int num = Character.s_characterNetLayer;
      if (this.m_nview.IsOwner() && !this.IsAttached())
        num = Character.s_characterLayer;
      if (layer != num)
        this.m_collider.gameObject.layer = num;
    }
    if (!this.m_disableWhileSleeping)
      return;
    if ((bool) (UnityEngine.Object) this.m_baseAI && this.m_baseAI.IsSleeping())
      this.m_body.isKinematic = true;
    else
      this.m_body.isKinematic = false;
  }

  private void UnderWorldCheck(float dt)
  {
    if (this.IsDead())
      return;
    this.m_underWorldCheckTimer += dt;
    if ((double) this.m_underWorldCheckTimer <= 5.0 && !this.IsPlayer())
      return;
    this.m_underWorldCheckTimer = 0.0f;
    float groundHeight = ZoneSystem.instance.GetGroundHeight(this.transform.position);
    if ((double) this.transform.position.y >= (double) groundHeight - 1.0)
      return;
    Vector3 position = this.transform.position with
    {
      y = groundHeight + 0.5f
    };
    this.transform.position = position;
    this.m_body.position = position;
    this.m_body.linearVelocity = Vector3.zero;
  }

  private void UpdatePheromones(float dt)
  {
    if ((double) this.m_pheromoneTimer < 0.0)
      return;
    this.m_pheromoneTimer -= dt;
    if ((double) this.m_pheromoneTimer > 0.0 || !this.m_pheromoneLoveEffect.HasEffects())
      return;
    this.m_pheromoneTimer = 5f;
    foreach (Character allPlayer in Player.GetAllPlayers())
    {
      foreach (StatusEffect statusEffect in allPlayer.GetSEMan().GetStatusEffects())
      {
        if (statusEffect is SE_Stats seStats && (UnityEngine.Object) seStats.m_pheromoneTarget != (UnityEngine.Object) null && seStats.m_pheromoneTarget.GetComponent<Character>().m_name == this.m_name)
        {
          this.m_pheromoneLoveEffect.Create(this.transform.position, this.transform.rotation);
          if (this.GetBaseAI() is MonsterAI baseAi)
            baseAi.Alert();
        }
      }
    }
  }

  private void UpdateSmoke(float dt)
  {
    if (this.m_tolerateSmoke)
      return;
    this.m_smokeCheckTimer += dt;
    if ((double) this.m_smokeCheckTimer <= 2.0)
      return;
    this.m_smokeCheckTimer = 0.0f;
    if (Physics.CheckSphere(this.GetTopPoint() + Vector3.up * 0.1f, 0.5f, Character.s_smokeRayMask))
      this.m_seman.AddStatusEffect(SEMan.s_statusEffectSmoked, true);
    else
      this.m_seman.RemoveStatusEffect(SEMan.s_statusEffectSmoked, true);
  }

  private void UpdateLava(float dt)
  {
    this.m_lavaTimer += dt;
    this.m_aboveOrInLavaTimer += dt;
    if (!WorldGenerator.IsAshlands(this.transform.position.x, this.transform.position.z))
      return;
    Vector3 position = this.transform.position;
    this.m_lavaProximity = 0.0f;
    Heightmap.Biome biome;
    Heightmap hmap;
    ZoneSystem.instance.GetGroundData(ref position, out Vector3 _, out biome, out Heightmap.BiomeArea _, out hmap);
    if ((UnityEngine.Object) hmap != (UnityEngine.Object) null)
      this.m_lavaProximity = Mathf.Min(1f, Utils.SmoothStep(0.1f, 1f, hmap.GetLava(position)));
    if ((double) this.m_lavaProximity > (double) this.m_minLavaMaskThreshold)
      this.m_aboveOrInLavaTimer = 0.0f;
    this.m_lavaHeightFactor = this.transform.position.y - position.y;
    this.m_lavaHeightFactor = (this.m_lavaAirDamageHeight - this.m_lavaHeightFactor) / this.m_lavaAirDamageHeight;
    bool flag = false;
    RaycastHit hitInfo;
    if ((double) this.m_lavaProximity > (double) this.m_minLavaMaskThreshold && Physics.Raycast(this.transform.position + Vector3.up, Vector3.down, out hitInfo, 50f, Character.s_blockedRayMask) && (UnityEngine.Object) hitInfo.collider.GetComponent<Heightmap>() == (UnityEngine.Object) null)
      flag = true;
    if (!flag && this.IsRiding())
      flag = true;
    float num = 1f - this.GetEquipmentHeatResistanceModifier();
    if (Terminal.m_showTests && this.IsPlayer())
      Terminal.m_testList["Lava/Height/Resist"] = $"{this.m_lavaProximity.ToString("0.00")} / {this.m_lavaHeightFactor.ToString("0.00")} / {num.ToString("0.00")}";
    if ((double) this.m_lavaProximity > (double) this.m_minLavaMaskThreshold && (double) this.m_lavaHeightFactor > 0.0 && !flag)
    {
      this.m_lavaHeatLevel += this.m_lavaProximity * dt * this.m_heatBuildupBase * this.m_lavaHeightFactor * num;
      this.m_lavaTimer = 0.0f;
    }
    else if (biome == Heightmap.Biome.AshLands && (double) this.m_dayHeatGainRunning != 0.0 && this.IsPlayer() && EnvMan.IsDay() && !this.IsUnderRoof() && (double) this.GetEquipmentHeatResistanceModifier() < (double) this.m_dayHeatEquipmentStop)
    {
      if ((double) this.m_currentVel.magnitude > 0.10000000149011612 && this.IsOnGround())
        this.m_lavaHeatLevel += dt * this.m_dayHeatGainRunning * num;
      else if (!this.InWater())
        this.m_lavaHeatLevel += dt * this.m_dayHeatGainStill;
      if ((double) this.m_lavaHeatLevel > (double) this.m_heatLevelFirstDamageThreshold)
        this.m_lavaHeatLevel = this.m_heatLevelFirstDamageThreshold;
    }
    else if (!this.InWater())
      this.m_lavaHeatLevel -= dt * this.m_heatCooldownBase;
    if (this.m_tolerateFire)
      this.m_lavaHeatLevel = 0.0f;
    else
      this.m_lavaHeatLevel = Mathf.Clamp(this.m_lavaHeatLevel, 0.0f, 1f);
  }

  private void UpdateHeatEffects(float dt)
  {
    bool flag = false;
    if (!((UnityEngine.Object) Player.m_localPlayer == (UnityEngine.Object) this))
      return;
    GameCamera.instance.m_heatDistortImageEffect.enabled = (double) this.m_lavaHeatLevel > 0.0;
    GameCamera.instance.m_heatDistortImageEffect.m_intensity = flag ? 0.0f : this.m_lavaHeatLevel;
    if (!this.m_lavaHeatEffects.HasEffects())
      return;
    if ((double) this.m_lavaHeatLevel > 0.0 && this.m_lavaHeatParticles.Count == 0 && !this.IsDead())
    {
      GameObject[] gameObjectArray = this.m_lavaHeatEffects.Create(this.transform.position, Quaternion.identity, this.transform);
      foreach (KeyValuePair<ParticleSystem, float> lavaHeatParticle in this.m_lavaHeatParticles)
        UnityEngine.Object.Destroy((UnityEngine.Object) lavaHeatParticle.Key.gameObject);
      foreach (Component component in this.m_lavaHeatAudio)
        UnityEngine.Object.Destroy((UnityEngine.Object) component.gameObject);
      this.m_lavaHeatParticles.Clear();
      this.m_lavaHeatAudio.Clear();
      foreach (GameObject gameObject in gameObjectArray)
      {
        foreach (ParticleSystem componentsInChild in gameObject.GetComponentsInChildren<ParticleSystem>())
          this.m_lavaHeatParticles.Add(componentsInChild, componentsInChild.emissionRate);
        foreach (ZSFX componentsInChild in gameObject.GetComponentsInChildren<ZSFX>())
          this.m_lavaHeatAudio.Add(componentsInChild);
      }
    }
    foreach (KeyValuePair<ParticleSystem, float> lavaHeatParticle in this.m_lavaHeatParticles)
    {
      if ((UnityEngine.Object) lavaHeatParticle.Key != (UnityEngine.Object) null)
        lavaHeatParticle.Key.emissionRate = this.m_lavaHeatLevel * lavaHeatParticle.Value;
    }
    if ((UnityEngine.Object) Player.m_localPlayer == (UnityEngine.Object) this)
    {
      foreach (ZSFX zsfx in this.m_lavaHeatAudio)
      {
        if ((UnityEngine.Object) zsfx != (UnityEngine.Object) null)
          zsfx.SetVolumeModifier(this.IsDead() ? 0.0f : this.m_lavaHeatLevel);
      }
    }
    if (!this.IsDead())
      return;
    foreach (KeyValuePair<ParticleSystem, float> lavaHeatParticle in this.m_lavaHeatParticles)
    {
      ZNetView component = lavaHeatParticle.Key.gameObject.GetComponent<ZNetView>();
      if (component != null && component.IsValid() && component.IsOwner())
        component.Destroy();
    }
    foreach (Component component1 in this.m_lavaHeatAudio)
    {
      ZNetView component2 = component1.gameObject.GetComponent<ZNetView>();
      if (component2 != null && component2.IsValid() && component2.IsOwner())
        component2.Destroy();
    }
    this.m_lavaHeatParticles.Clear();
    this.m_lavaHeatAudio.Clear();
  }

  private void UpdateHeatDamage(float dt)
  {
    bool flag = false;
    this.m_lavaDamageTimer += dt;
    if ((double) this.m_lavaDamageTimer <= (double) this.m_lavaDamageTickInterval || flag)
      return;
    this.m_lavaDamageTimer = 0.0f;
    float num1 = this.InWater() ? 1f : Mathf.Max(this.m_lavaProximity, 0.05f);
    float num2 = 1f - this.GetEquipmentHeatResistanceModifier();
    if ((double) this.m_lavaHeatLevel >= 1.0)
    {
      if (!this.InWater() && (double) this.m_lavaProximity > (double) this.m_minLavaMaskThreshold)
        this.m_seman.AddStatusEffect(SEMan.s_statusEffectBurning, true);
      this.Damage(new HitData()
      {
        m_damage = {
          m_damage = this.m_lavaFullDamage * num2 * num1
        },
        m_point = this.m_lastGroundPoint,
        m_dir = this.m_lastGroundNormal,
        m_hitType = HitData.HitType.Burning
      });
    }
    else
    {
      if ((double) this.m_lavaHeatLevel < (double) this.m_heatLevelFirstDamageThreshold)
        return;
      if (!this.InWater() && (double) this.m_lavaProximity > (double) this.m_minLavaMaskThreshold)
        this.m_seman.AddStatusEffect(SEMan.s_statusEffectBurning, true);
      this.Damage(new HitData()
      {
        m_damage = {
          m_damage = this.m_lavaFirstDamage * num2 * num1
        },
        m_point = this.m_lastGroundPoint,
        m_dir = this.m_lastGroundNormal,
        m_hitType = HitData.HitType.Burning
      });
    }
  }

  private bool IsUnderRoof()
  {
    return Physics.RaycastNonAlloc(this.transform.position + Vector3.up * 0.2f, Vector3.up, this.m_lavaRoofCheck, 20f, LayerMask.GetMask("Default", "static_solid", "piece")) > 0;
  }

  private void UpdateAshlandsWater(float dt)
  {
    if (this.m_tolerateFire || !this.InWater())
      return;
    float ashlandsOceanGradient = WorldGenerator.GetAshlandsOceanGradient(this.transform.position);
    if (!this.IsSwimming())
      ashlandsOceanGradient *= this.m_heatWaterTouchMultiplier;
    if ((double) ashlandsOceanGradient < 0.0)
      return;
    float num1 = Mathf.Clamp01(ashlandsOceanGradient);
    float num2 = 1f - this.GetEquipmentHeatResistanceModifier();
    this.m_lavaHeatLevel += num1 * dt * this.m_heatBuildupWater * num2;
    if ((double) this.m_lavaHeatLevel <= (double) this.m_heatLevelFirstDamageThreshold)
      return;
    this.m_lavaHeatLevel = this.m_heatLevelFirstDamageThreshold;
  }

  private void UpdateContinousEffects()
  {
    Character.SetupContinuousEffect(this.transform, this.transform.position, this.m_sliding, this.m_slideEffects, ref this.m_slideEffects_instances);
    Vector3 position = this.transform.position with
    {
      y = this.GetLiquidLevel() + 0.05f
    };
    EffectList effects = !this.InTar() || !this.m_tarEffects.HasEffects() ? this.m_waterEffects : this.m_tarEffects;
    Character.SetupContinuousEffect(this.transform, position, this.InLiquid(), effects, ref this.m_waterEffects_instances);
    Character.SetupContinuousEffect(this.transform, this.transform.position, this.IsFlying(), this.m_flyingContinuousEffect, ref this.m_flyingEffects_instances);
  }

  public static void SetupContinuousEffect(
    Transform transform,
    Vector3 point,
    bool enabledEffect,
    EffectList effects,
    ref GameObject[] instances)
  {
    if (!effects.HasEffects())
      return;
    if (enabledEffect)
    {
      if (instances == null)
      {
        instances = effects.Create(point, Quaternion.identity, transform);
      }
      else
      {
        foreach (GameObject gameObject in instances)
        {
          if ((bool) (UnityEngine.Object) gameObject)
            gameObject.transform.position = point;
        }
      }
    }
    else
    {
      if (instances == null)
        return;
      foreach (GameObject gameObject in instances)
      {
        if ((bool) (UnityEngine.Object) gameObject)
        {
          foreach (ParticleSystem componentsInChild in gameObject.GetComponentsInChildren<ParticleSystem>())
          {
            componentsInChild.emission.enabled = false;
            componentsInChild.Stop();
          }
          CamShaker componentInChildren1 = gameObject.GetComponentInChildren<CamShaker>();
          if ((bool) (UnityEngine.Object) componentInChildren1)
            UnityEngine.Object.Destroy((UnityEngine.Object) componentInChildren1);
          ZSFX componentInChildren2 = gameObject.GetComponentInChildren<ZSFX>();
          if ((bool) (UnityEngine.Object) componentInChildren2)
            componentInChildren2.FadeOut();
          TimedDestruction component = gameObject.GetComponent<TimedDestruction>();
          if ((bool) (UnityEngine.Object) component)
            component.Trigger();
          else
            UnityEngine.Object.Destroy((UnityEngine.Object) gameObject);
        }
      }
      instances = (GameObject[]) null;
    }
  }

  protected virtual void OnSwimming(Vector3 targetVel, float dt)
  {
  }

  protected virtual void OnSneaking(float dt)
  {
  }

  protected virtual void OnJump()
  {
  }

  protected virtual bool TakeInput() => true;

  private float GetSlideAngle()
  {
    if (this.IsPlayer())
      return 38f;
    return this.HaveRider() ? 45f : 90f;
  }

  public bool HaveRider() => (bool) (UnityEngine.Object) this.m_baseAI && this.m_baseAI.HaveRider();

  private void ApplySlide(float dt, ref Vector3 currentVel, Vector3 bodyVel, bool running)
  {
    bool flag1 = this.CanWallRun();
    double num = (double) Mathf.Clamp(Mathf.Acos(Mathf.Clamp01((this.m_groundTilt != Character.GroundTiltType.None ? this.m_groundTiltNormal : this.m_lastGroundNormal).y)) * 57.29578f, 0.0f, 90f);
    (this.m_lastGroundNormal with { y = 0.0f }).Normalize();
    Vector3 linearVelocity = this.m_body.linearVelocity;
    Vector3 vector3 = Vector3.Cross(this.m_lastGroundNormal, Vector3.Cross(this.m_lastGroundNormal, Vector3.up));
    bool flag2 = (double) currentVel.magnitude > 0.10000000149011612;
    double slideAngle = (double) this.GetSlideAngle();
    if (num > slideAngle)
    {
      if (running & flag1 & flag2)
      {
        this.m_slippage = 0.0f;
        this.m_wallRunning = true;
      }
      else
        this.m_slippage = Mathf.MoveTowards(this.m_slippage, 1f, 1f * dt);
      Vector3 b = vector3 * 5f;
      currentVel = Vector3.Lerp(currentVel, b, this.m_slippage);
      this.m_sliding = (double) this.m_slippage > 0.5;
    }
    else
      this.m_slippage = 0.0f;
  }

  private void UpdateMotion(float dt)
  {
    this.UpdateBodyFriction();
    this.m_sliding = false;
    this.m_wallRunning = false;
    this.m_running = false;
    this.m_walking = false;
    if (this.IsDead())
      return;
    if (this.IsDebugFlying())
    {
      this.UpdateDebugFly(dt);
    }
    else
    {
      if (this.InIntro())
      {
        this.m_maxAirAltitude = this.transform.position.y;
        this.m_body.linearVelocity = Vector3.zero;
        this.m_body.angularVelocity = Vector3.zero;
      }
      if (!this.InLiquidSwimDepth() && !this.IsOnGround() && !this.IsAttached())
      {
        this.m_maxAirAltitude = Mathf.Max(this.m_maxAirAltitude, this.transform.position.y);
        this.m_fallTimer += dt;
        if (this.IsPlayer() && (double) this.m_fallTimer > 0.10000000149011612)
          this.m_zanim.SetBool(Character.s_animatorFalling, true);
      }
      else
      {
        this.m_fallTimer = 0.0f;
        if (this.IsPlayer())
          this.m_zanim.SetBool(Character.s_animatorFalling, false);
      }
      if (this.IsSwimming())
        this.UpdateSwimming(dt);
      else if (this.m_flying)
      {
        this.UpdateFlying(dt);
      }
      else
      {
        if ((bool) (UnityEngine.Object) this.m_baseAI && !this.m_baseAI.IsSleeping())
          this.UpdateWalking(dt);
        if (!(bool) (UnityEngine.Object) this.m_baseAI)
          this.UpdateWalking(dt);
      }
      this.UpdateSinkingPlatform(dt);
      this.m_lastGroundTouch += Time.fixedDeltaTime;
      this.m_jumpTimer += Time.fixedDeltaTime;
      if ((double) this.m_standUp <= 0.0)
        return;
      this.m_standUp -= Time.fixedDeltaTime;
    }
  }

  private void UpdateSinkingPlatform(float dt)
  {
    if ((UnityEngine.Object) this != (UnityEngine.Object) Player.m_localPlayer || this.InLiquidSwimDepth() || this.IsOnGround() || this.IsAttached() || !((UnityEngine.Object) this.m_lastGroundBody != (UnityEngine.Object) null) || !((UnityEngine.Object) this.m_lastGroundBody.GetComponent<Leviathan>() != (UnityEngine.Object) null))
      return;
    StaticPhysics component = this.m_lastGroundBody.GetComponent<StaticPhysics>();
    if (component == null || !component.IsFalling)
      return;
    this.transform.position = this.transform.position + this.m_lastGroundBody.linearVelocity * dt;
    if ((double) this.m_fallTimer <= 0.10000000149011612)
      return;
    this.m_zanim.SetBool(Character.s_animatorFalling, false);
  }

  public static void SetTakeInputDelay(float delayInSeconds)
  {
    Character.takeInputDelay = delayInSeconds;
  }

  private void UpdateDebugFly(float dt)
  {
    Character.takeInputDelay = Mathf.Max(0.0f, Character.takeInputDelay - dt);
    float num = this.m_run ? (float) Character.m_debugFlySpeed * 2.5f : (float) Character.m_debugFlySpeed;
    Vector3 b = this.m_moveDir * num;
    if (this.TakeInput())
    {
      if ((ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump")) && (double) Character.takeInputDelay <= 0.0 && !Hud.IsPieceSelectionVisible())
        b.y = num;
      else if (ZInput.GetKey(KeyCode.LeftControl) || (double) ZInput.GetButtonPressedTimer("JoyCrouch") > 0.33000001311302185)
        b.y = -num;
    }
    this.m_currentVel = Vector3.Lerp(this.m_currentVel, b, 0.5f);
    this.m_body.linearVelocity = this.m_currentVel;
    this.m_body.useGravity = false;
    this.m_lastGroundTouch = 0.0f;
    this.m_maxAirAltitude = this.transform.position.y;
    this.m_body.rotation = Quaternion.RotateTowards(this.transform.rotation, this.m_lookYaw, this.m_turnSpeed * dt);
    this.m_body.angularVelocity = Vector3.zero;
    this.UpdateEyeRotation();
  }

  private void UpdateSwimming(float dt)
  {
    bool flag = this.IsOnGround();
    if ((double) Mathf.Max(0.0f, this.m_maxAirAltitude - this.transform.position.y) > 0.5 && this.m_onLand != null)
      this.m_onLand(new Vector3(this.transform.position.x, this.GetLiquidLevel(), this.transform.position.z));
    this.m_maxAirAltitude = this.transform.position.y;
    float speed = this.m_swimSpeed * this.GetAttackSpeedFactorMovement();
    if (this.InMinorActionSlowdown())
      speed = 0.0f;
    this.m_seman.ApplyStatusEffectSpeedMods(ref speed, this.m_moveDir);
    Vector3 vector3 = this.m_moveDir * speed;
    if (this.IsPlayer())
    {
      this.m_currentVel = Vector3.Lerp(this.m_currentVel, vector3, this.m_swimAcceleration);
    }
    else
    {
      float magnitude1 = vector3.magnitude;
      float magnitude2 = this.m_currentVel.magnitude;
      if ((double) magnitude1 > (double) magnitude2)
      {
        float num = Mathf.MoveTowards(magnitude2, magnitude1, this.m_swimAcceleration);
        vector3 = vector3.normalized * num;
      }
      this.m_currentVel = Vector3.Lerp(this.m_currentVel, vector3, 0.5f);
    }
    if ((double) this.m_currentVel.magnitude > 0.10000000149011612)
      this.AddNoise(15f);
    this.AddPushbackForce(ref this.m_currentVel);
    Vector3 force = (this.m_currentVel - this.m_body.linearVelocity) with
    {
      y = 0.0f
    };
    if ((double) force.magnitude > 20.0)
      force = force.normalized * 20f;
    this.m_body.AddForce(force, ForceMode.VelocityChange);
    float num1 = this.GetLiquidLevel() - this.m_swimDepth;
    if ((double) this.transform.position.y < (double) num1)
    {
      float target = Mathf.Lerp(0.0f, 10f, Mathf.Clamp01((float) (((double) num1 - (double) this.transform.position.y) / 2.0)));
      Vector3 linearVelocity = this.m_body.linearVelocity;
      linearVelocity.y = Mathf.MoveTowards(linearVelocity.y, target, 50f * dt);
      this.m_body.linearVelocity = linearVelocity;
    }
    else
    {
      float num2 = Mathf.Lerp(0.0f, 10f, Mathf.Clamp01((float) (-((double) num1 - (double) this.transform.position.y) / 1.0)));
      Vector3 linearVelocity = this.m_body.linearVelocity;
      linearVelocity.y = Mathf.MoveTowards(linearVelocity.y, -num2, 30f * dt);
      this.m_body.linearVelocity = linearVelocity;
    }
    float target1 = 0.0f;
    if ((double) this.m_moveDir.magnitude > 0.10000000149011612 || this.AlwaysRotateCamera())
    {
      float swimTurnSpeed = this.m_swimTurnSpeed;
      this.m_seman.ApplyStatusEffectSpeedMods(ref swimTurnSpeed, this.m_currentVel);
      target1 = this.UpdateRotation(swimTurnSpeed, dt, false);
    }
    this.m_body.angularVelocity = Vector3.zero;
    this.UpdateEyeRotation();
    this.m_body.useGravity = true;
    float num3 = this.IsPlayer() || this.HaveRider() ? Vector3.Dot(this.m_currentVel, this.transform.forward) : Vector3.Dot(this.m_body.linearVelocity, this.transform.forward);
    float num4 = Vector3.Dot(this.m_currentVel, this.transform.right);
    this.m_currentTurnVel = Mathf.SmoothDamp(this.m_currentTurnVel, target1, ref this.m_currentTurnVelChange, 0.5f, 99f);
    this.m_zanim.SetFloat(Character.s_forwardSpeed, num3);
    this.m_zanim.SetFloat(Character.s_sidewaySpeed, num4);
    this.m_zanim.SetFloat(Character.s_turnSpeed, this.m_currentTurnVel);
    this.m_zanim.SetBool(Character.s_inWater, !flag);
    this.m_zanim.SetBool(Character.s_onGround, false);
    this.m_zanim.SetBool(Character.s_encumbered, false);
    this.m_zanim.SetBool(Character.s_flying, false);
    if (flag)
      return;
    this.OnSwimming(vector3, dt);
  }

  private void UpdateFlying(float dt)
  {
    float num1 = (this.m_run ? this.m_flyFastSpeed : this.m_flySlowSpeed) * this.GetAttackSpeedFactorMovement();
    this.m_currentVel = Vector3.Lerp(this.m_currentVel, this.CanMove() ? this.m_moveDir * num1 : Vector3.zero, this.m_acceleration);
    this.m_maxAirAltitude = this.transform.position.y;
    this.ApplyRootMotion(ref this.m_currentVel);
    this.AddPushbackForce(ref this.m_currentVel);
    Vector3 force = this.m_currentVel - this.m_body.linearVelocity;
    if ((double) force.magnitude > 20.0)
      force = force.normalized * 20f;
    this.m_body.AddForce(force, ForceMode.VelocityChange);
    float target = 0.0f;
    if (((double) this.m_moveDir.magnitude > 0.10000000149011612 || this.AlwaysRotateCamera()) && !this.InDodge() && this.CanMove())
    {
      float flyTurnSpeed = this.m_flyTurnSpeed;
      this.m_seman.ApplyStatusEffectSpeedMods(ref flyTurnSpeed, this.m_currentVel);
      target = this.UpdateRotation(flyTurnSpeed, dt, true);
    }
    this.m_body.angularVelocity = Vector3.zero;
    this.UpdateEyeRotation();
    this.m_body.useGravity = false;
    float num2 = Vector3.Dot(this.m_currentVel, this.transform.forward);
    float num3 = Vector3.Dot(this.m_currentVel, this.transform.right);
    float num4 = Vector3.Dot(this.m_body.linearVelocity, this.transform.forward);
    this.m_currentTurnVel = Mathf.SmoothDamp(this.m_currentTurnVel, target, ref this.m_currentTurnVelChange, 0.5f, 99f);
    this.m_zanim.SetFloat(Character.s_forwardSpeed, this.IsPlayer() ? num2 : num4);
    this.m_zanim.SetFloat(Character.s_sidewaySpeed, num3);
    this.m_zanim.SetFloat(Character.s_turnSpeed, this.m_currentTurnVel);
    this.m_zanim.SetBool(Character.s_inWater, false);
    this.m_zanim.SetBool(Character.s_onGround, false);
    this.m_zanim.SetBool(Character.s_encumbered, false);
    this.m_zanim.SetBool(Character.s_flying, true);
  }

  private void UpdateWalking(float dt)
  {
    Vector3 moveDir = this.m_moveDir;
    bool flag = this.IsCrouching();
    this.m_running = this.CheckRun(moveDir, dt);
    float speed1 = this.m_speed * this.GetJogSpeedFactor();
    if ((this.m_walk || this.InMinorActionSlowdown()) && !flag)
    {
      speed1 = this.m_walkSpeed;
      this.m_walking = (double) moveDir.magnitude > 0.10000000149011612;
    }
    else if (this.m_running)
    {
      speed1 = this.m_runSpeed * this.GetRunSpeedFactor();
      if (this.IsPlayer() && (double) moveDir.magnitude > 0.0)
        moveDir.Normalize();
    }
    else if (flag || this.IsEncumbered())
      speed1 = this.m_crouchSpeed;
    if (!this.IsPlayer())
    {
      if (!this.InInterior())
        speed1 *= Game.m_enemySpeedSize;
      speed1 *= (float) (1.0 + (double) Game.m_worldLevel * (double) Game.instance.m_worldLevelEnemyMoveSpeedMultiplier);
    }
    this.ApplyLiquidResistance(ref speed1);
    float speed2 = speed1 * this.GetAttackSpeedFactorMovement();
    this.m_seman.ApplyStatusEffectSpeedMods(ref speed2, moveDir);
    if ((double) this.m_lavaProximity > 0.33000001311302185 && (double) this.m_lavaHeightFactor > (double) this.m_lavaSlowHeight)
    {
      float num = (float) (((double) this.m_lavaProximity - 0.33000001311302185) * 1.4925373792648315);
      speed2 *= (float) (1.0 - (double) num * (double) this.m_lavaSlowMax);
    }
    if (Terminal.m_showTests && (UnityEngine.Object) Player.m_localPlayer == (UnityEngine.Object) this)
      Terminal.m_testList["Player Speed"] = speed2.ToString("0.000");
    Vector3 vector3_1 = this.CanMove() ? moveDir * speed2 : Vector3.zero;
    Vector3 vector3_2;
    if ((double) vector3_1.magnitude > 0.0 && this.IsOnGround())
    {
      vector3_2 = Vector3.ProjectOnPlane(vector3_1, this.m_lastGroundNormal);
      vector3_1 = vector3_2.normalized * vector3_1.magnitude;
    }
    float magnitude1 = vector3_1.magnitude;
    float magnitude2 = this.m_currentVel.magnitude;
    if ((double) magnitude1 > (double) magnitude2)
    {
      float num = Mathf.MoveTowards(magnitude2, magnitude1, this.m_acceleration);
      vector3_1 = vector3_1.normalized * num;
    }
    else
    {
      float num = Mathf.MoveTowards(magnitude2, magnitude1, this.m_acceleration * 2f);
      vector3_1 = (double) vector3_1.magnitude > 0.0 ? vector3_1.normalized * num : this.m_currentVel.normalized * num;
    }
    this.m_currentVel = Vector3.Lerp(this.m_currentVel, vector3_1, 0.5f);
    Vector3 linearVelocity1 = this.m_body.linearVelocity;
    Vector3 currentVel1 = this.m_currentVel with
    {
      y = linearVelocity1.y
    };
    if (this.IsOnGround() && (UnityEngine.Object) this.m_lastAttachBody == (UnityEngine.Object) null)
    {
      this.ApplySlide(dt, ref currentVel1, linearVelocity1, this.m_running);
      currentVel1.y = Mathf.Min(currentVel1.y, 3f);
    }
    this.ApplyRootMotion(ref currentVel1);
    this.AddPushbackForce(ref currentVel1);
    this.ApplyGroundForce(ref currentVel1, vector3_1);
    Vector3 force = currentVel1 - linearVelocity1;
    if (!this.IsOnGround())
    {
      if ((double) vector3_1.magnitude > 0.10000000149011612)
        force *= this.m_airControl;
      else
        force = Vector3.zero;
    }
    if (this.IsAttached())
      force = Vector3.zero;
    if ((double) force.magnitude > 20.0)
      force = force.normalized * 20f;
    if ((double) force.magnitude > 0.0099999997764825821)
      this.m_body.AddForce(force, ForceMode.VelocityChange);
    Vector3 linearVelocity2 = this.m_body.linearVelocity;
    this.m_seman.ModifyWalkVelocity(ref linearVelocity2);
    this.m_body.linearVelocity = linearVelocity2;
    if ((bool) (UnityEngine.Object) this.m_lastGroundBody && this.m_lastGroundBody.gameObject.layer != this.gameObject.layer && (double) this.m_lastGroundBody.mass > (double) this.m_body.mass)
    {
      float num = this.m_body.mass / this.m_lastGroundBody.mass;
      this.m_lastGroundBody.AddForceAtPosition(-force * num * this.m_lastGroundBody.mass, this.transform.position, ForceMode.Impulse);
    }
    float target = 0.0f;
    if (((double) moveDir.magnitude > 0.10000000149011612 || this.AlwaysRotateCamera() || (double) this.m_standUp > 0.0) && !this.InDodge() && this.CanMove() || this.m_groundContact)
    {
      float speed3 = this.m_run ? this.m_runTurnSpeed : this.m_turnSpeed;
      this.m_seman.ApplyStatusEffectSpeedMods(ref speed3, this.m_currentVel);
      target = this.UpdateRotation(speed3, dt, false);
    }
    if (this.IsSneaking())
      this.OnSneaking(dt);
    this.UpdateEyeRotation();
    this.m_body.useGravity = true;
    Vector3 currentVel2 = this.m_currentVel;
    vector3_2 = Vector3.ProjectOnPlane(this.transform.forward, this.m_lastGroundNormal);
    Vector3 normalized1 = vector3_2.normalized;
    float a = Vector3.Dot(currentVel2, normalized1);
    float b = Vector3.Dot(this.m_body.linearVelocity, this.m_visual.transform.forward);
    if (this.IsRiding())
      a = b;
    else if (!this.IsPlayer() && !this.HaveRider())
      a = Mathf.Min(a, b);
    Vector3 currentVel3 = this.m_currentVel;
    vector3_2 = Vector3.ProjectOnPlane(this.transform.right, this.m_lastGroundNormal);
    Vector3 normalized2 = vector3_2.normalized;
    float num1 = Vector3.Dot(currentVel3, normalized2);
    this.m_currentTurnVel = Mathf.SmoothDamp(this.m_currentTurnVel, target, ref this.m_currentTurnVelChange, 0.5f, 99f);
    this.m_zanim.SetFloat(Character.s_forwardSpeed, a);
    this.m_zanim.SetFloat(Character.s_sidewaySpeed, num1);
    this.m_zanim.SetFloat(Character.s_turnSpeed, this.m_currentTurnVel);
    this.m_zanim.SetBool(Character.s_inWater, false);
    this.m_zanim.SetBool(Character.s_onGround, this.IsOnGround());
    this.m_zanim.SetBool(Character.s_encumbered, this.IsEncumbered());
    this.m_zanim.SetBool(Character.s_flying, false);
    if ((double) this.m_currentVel.magnitude <= 0.10000000149011612)
      return;
    if (this.m_running)
    {
      this.AddNoise(30f);
    }
    else
    {
      if (flag)
        return;
      this.AddNoise(15f);
    }
  }

  public bool IsSneaking()
  {
    return this.IsCrouching() && (double) this.m_currentVel.magnitude > 0.10000000149011612 && this.IsOnGround();
  }

  private float GetSlopeAngle()
  {
    return !this.IsOnGround() ? 0.0f : (float) -(90.0 - -(double) Vector3.SignedAngle(this.transform.forward, this.m_lastGroundNormal, this.transform.right));
  }

  protected void AddPushbackForce(ref Vector3 velocity)
  {
    if (!(this.m_pushForce != Vector3.zero))
      return;
    Vector3 normalized = this.m_pushForce.normalized;
    float num = Vector3.Dot(normalized, velocity);
    if ((double) num < 20.0)
      velocity += normalized * (20f - num);
    if (!this.IsSwimming() && !this.m_flying)
      return;
    velocity *= 0.5f;
  }

  private void ApplyPushback(HitData hit) => this.ApplyPushback(hit.m_dir, hit.m_pushForce);

  public void ApplyPushback(Vector3 dir, float pushForce)
  {
    if ((double) pushForce == 0.0 || !(dir != Vector3.zero))
      return;
    float num = (float) ((double) pushForce * (double) Mathf.Clamp01(1f + this.GetEquipmentMovementModifier()) / (double) this.m_body.mass * 2.5);
    dir.y = 0.0f;
    dir.Normalize();
    Vector3 vector3 = dir * num;
    if ((double) this.m_pushForce.magnitude >= (double) vector3.magnitude)
      return;
    this.m_pushForce = vector3;
  }

  private void UpdatePushback(float dt)
  {
    this.m_pushForce = Vector3.MoveTowards(this.m_pushForce, Vector3.zero, 100f * dt);
  }

  public void TimeoutGroundForce(float time) => this.m_groundForceTimer = time;

  private void ApplyGroundForce(ref Vector3 vel, Vector3 targetVel)
  {
    Vector3 vector3_1 = Vector3.zero;
    if (this.IsOnGround() && (bool) (UnityEngine.Object) this.m_lastGroundBody && (double) this.m_groundForceTimer <= 0.0)
      vector3_1 = this.m_lastGroundBody.GetPointVelocity(this.transform.position) with
      {
        y = 0.0f
      };
    Ship standingOnShip = this.GetStandingOnShip();
    if ((UnityEngine.Object) standingOnShip != (UnityEngine.Object) null)
    {
      if ((double) targetVel.magnitude > 0.0099999997764825821)
        this.m_lastAttachBody = (Rigidbody) null;
      else if ((UnityEngine.Object) this.m_lastAttachBody != (UnityEngine.Object) this.m_lastGroundBody)
      {
        this.m_lastAttachBody = this.m_lastGroundBody;
        this.m_lastAttachPos = this.m_lastAttachBody.transform.InverseTransformPoint(this.m_body.position);
      }
      if ((bool) (UnityEngine.Object) this.m_lastAttachBody)
      {
        Vector3 vector3_2 = this.m_lastAttachBody.transform.TransformPoint(this.m_lastAttachPos);
        Vector3 vector3_3 = vector3_2 - this.m_body.position;
        if ((double) vector3_3.magnitude < 4.0)
        {
          Vector3 vector3_4 = vector3_2 with
          {
            y = this.m_body.position.y
          };
          if (standingOnShip.IsOwner())
          {
            vector3_3.y = 0.0f;
            vector3_1 += vector3_3 * 10f;
          }
          else
            this.m_body.position = vector3_4;
        }
        else
          this.m_lastAttachBody = (Rigidbody) null;
      }
    }
    else
      this.m_lastAttachBody = (Rigidbody) null;
    vel += vector3_1;
  }

  private float UpdateRotation(float turnSpeed, float dt, bool smooth)
  {
    Quaternion quaternion1 = this.AlwaysRotateCamera() || this.m_moveDir == Vector3.zero ? this.m_lookYaw : Quaternion.LookRotation(this.m_moveDir);
    float yawDeltaAngle = Utils.GetYawDeltaAngle(this.transform.rotation, quaternion1);
    float num1 = 1f;
    if (!this.IsPlayer())
    {
      Mathf.Pow(Mathf.Clamp01(Mathf.Abs(yawDeltaAngle) / 90f), 0.5f);
      float target = Mathf.Pow(Mathf.Clamp01(Mathf.Abs(yawDeltaAngle) / 90f), 0.5f);
      if (smooth)
      {
        this.currentRotSpeedFactor = Mathf.MoveTowards(this.currentRotSpeedFactor, target, dt);
        num1 = this.currentRotSpeedFactor;
      }
      else
        num1 = target;
    }
    float num2 = turnSpeed * this.GetAttackSpeedFactorRotation() * num1;
    Quaternion quaternion2 = Quaternion.RotateTowards(this.transform.rotation, quaternion1, num2 * dt);
    if ((double) Mathf.Abs(yawDeltaAngle) > 1.0 / 1000.0)
      this.transform.rotation = quaternion2;
    return (float) ((double) num2 * (double) Mathf.Sign(yawDeltaAngle) * (Math.PI / 180.0));
  }

  private void UpdateGroundTilt(float dt)
  {
    if ((UnityEngine.Object) this.m_visual == (UnityEngine.Object) null || (bool) (UnityEngine.Object) this.m_baseAI && this.m_baseAI.IsSleeping())
      return;
    if (this.m_nview.IsOwner())
    {
      if (this.m_groundTilt != Character.GroundTiltType.None)
      {
        if (!this.IsFlying() && this.IsOnGround() && !this.IsAttached())
        {
          Vector3 vector = this.m_lastGroundNormal;
          if (this.m_groundTilt == Character.GroundTiltType.PitchRaycast || this.m_groundTilt == Character.GroundTiltType.FullRaycast)
          {
            Vector3 p1 = this.transform.position + this.transform.forward * this.m_collider.radius;
            Vector3 p2 = this.transform.position - this.transform.forward * this.m_collider.radius;
            Vector3 normal1;
            this.GetGroundHeight(p1, out float _, out normal1);
            Vector3 normal2;
            this.GetGroundHeight(p2, out float _, out normal2);
            vector = (vector + normal1 + normal2).normalized;
          }
          this.m_groundTiltNormal = Vector3.Lerp(this.m_groundTiltNormal, Vector3.RotateTowards(Vector3.up, this.transform.InverseTransformVector(vector), 0.87266463f, 1f), 0.05f);
          Vector3 vector3 = this.m_groundTilt == Character.GroundTiltType.Pitch || this.m_groundTilt == Character.GroundTiltType.PitchRaycast ? this.m_groundTiltNormal - Vector3.Project(this.m_groundTiltNormal, Vector3.right) : this.m_groundTiltNormal;
          this.m_visual.transform.localRotation = Quaternion.RotateTowards(this.m_visual.transform.localRotation, Quaternion.LookRotation(Vector3.Cross(vector3, Vector3.left), vector3), dt * this.m_groundTiltSpeed);
        }
        else if (this.IsFlying() && !this.IsOnGround() && this.m_groundTilt == Character.GroundTiltType.Flying && (double) this.m_currentVel.sqrMagnitude > 0.0)
        {
          this.m_groundTiltNormal = Vector3.Cross(this.transform.InverseTransformVector(this.m_currentVel.normalized), Vector3.right);
          this.m_visual.transform.localRotation = Quaternion.RotateTowards(this.m_visual.transform.localRotation, Quaternion.Lerp(Quaternion.identity, Quaternion.LookRotation(Vector3.Cross(this.m_groundTiltNormal, Vector3.left), this.m_groundTiltNormal), this.m_currentVel.magnitude * 0.33f), dt * this.m_groundTiltSpeed);
        }
        else
        {
          this.m_groundTiltNormal = Vector3.up;
          this.m_visual.transform.localRotation = !this.IsSwimming() ? Quaternion.RotateTowards(this.m_visual.transform.localRotation, Quaternion.identity, (float) ((double) dt * (double) this.m_groundTiltSpeed * 2.0)) : Quaternion.RotateTowards(this.m_visual.transform.localRotation, Quaternion.identity, dt * this.m_groundTiltSpeed);
        }
        Quaternion localRotation = this.m_visual.transform.localRotation;
        if (!localRotation.Equals(this.m_tiltRotCached))
        {
          this.m_nview.GetZDO().Set(ZDOVars.s_tiltrot, localRotation);
          this.m_tiltRotCached = localRotation;
        }
      }
      else if (this.CanWallRun())
      {
        if (this.m_wallRunning)
        {
          Vector3 vector3 = Vector3.Lerp(Vector3.up, this.m_lastGroundNormal, 0.65f);
          Vector3 forward = Vector3.ProjectOnPlane(this.transform.forward, vector3);
          forward.Normalize();
          this.m_visual.transform.rotation = Quaternion.RotateTowards(this.m_visual.transform.rotation, Quaternion.LookRotation(forward, vector3), 30f * dt);
        }
        else
          this.m_visual.transform.localRotation = Quaternion.RotateTowards(this.m_visual.transform.localRotation, Quaternion.identity, (float) ((double) dt * (double) this.m_groundTiltSpeed * 2.0));
        Quaternion localRotation = this.m_visual.transform.localRotation;
        if (!localRotation.Equals(this.m_tiltRotCached))
        {
          this.m_nview.GetZDO().Set(ZDOVars.s_tiltrot, localRotation);
          this.m_tiltRotCached = localRotation;
        }
      }
    }
    else if (this.m_groundTilt != Character.GroundTiltType.None || this.CanWallRun())
      this.m_visual.transform.localRotation = Quaternion.RotateTowards(this.m_visual.transform.localRotation, this.m_nview.GetZDO().GetQuaternion(ZDOVars.s_tiltrot, Quaternion.identity), dt * this.m_groundTiltSpeed);
    this.m_animator.SetFloat(Character.s_tilt, Vector3.Dot(this.m_visual.transform.forward, Vector3.up));
  }

  private bool GetGroundHeight(Vector3 p, out float height, out Vector3 normal)
  {
    p.y += 10f;
    RaycastHit hitInfo;
    if (Physics.Raycast(p, Vector3.down, out hitInfo, 20f, Character.s_groundRayMask))
    {
      height = hitInfo.point.y;
      normal = hitInfo.normal;
      return true;
    }
    height = p.y;
    normal = Vector3.zero;
    return false;
  }

  public bool IsWallRunning() => this.m_wallRunning;

  private bool IsOnSnow() => false;

  public void Heal(float hp, bool showText = true)
  {
    if ((double) hp <= 0.0)
      return;
    if (this.m_nview.IsOwner())
      this.RPC_Heal(0L, hp, showText);
    else
      this.m_nview.InvokeRPC("RPC_Heal", (object) hp, (object) showText);
  }

  private void RPC_Heal(long sender, float hp, bool showText)
  {
    if (!this.m_nview.IsOwner())
      return;
    float health1 = this.GetHealth();
    if ((double) health1 <= 0.0 || this.IsDead())
      return;
    float health2 = Mathf.Min(health1 + hp, this.GetMaxHealth());
    if ((double) health2 <= (double) health1)
      return;
    this.SetHealth(health2);
    if (!showText)
      return;
    DamageText.instance.ShowText(DamageText.TextType.Heal, this.GetTopPoint(), hp, this.IsPlayer());
  }

  public Vector3 GetTopPoint()
  {
    return this.transform.TransformPoint(this.m_collider.center) + this.m_visual.transform.up * (this.m_collider.height * 0.5f);
  }

  public float GetRadius()
  {
    return this.IsPlayer() || this.m_useAltStatusEffectScaling ? this.m_collider.radius : this.m_collider.radius * this.transform.localScale.magnitude;
  }

  public float GetHeight() => Mathf.Max(this.m_collider.height, this.m_collider.radius * 2f);

  public Vector3 GetHeadPoint() => this.m_head.position;

  public Vector3 GetEyePoint() => this.m_eye.position;

  public Vector3 GetCenterPoint() => this.m_collider.bounds.center;

  public DestructibleType GetDestructibleType() => DestructibleType.Character;

  private short FindWeakSpotIndex(Collider c)
  {
    if ((UnityEngine.Object) c == (UnityEngine.Object) null || this.m_weakSpots == null || this.m_weakSpots.Length == 0)
      return -1;
    for (short weakSpotIndex = 0; (int) weakSpotIndex < this.m_weakSpots.Length; ++weakSpotIndex)
    {
      if ((UnityEngine.Object) this.m_weakSpots[(int) weakSpotIndex].m_collider == (UnityEngine.Object) c)
        return weakSpotIndex;
    }
    return -1;
  }

  private WeakSpot GetWeakSpot(short index)
  {
    return index < (short) 0 || (int) index >= this.m_weakSpots.Length ? (WeakSpot) null : this.m_weakSpots[(int) index];
  }

  public void Damage(HitData hit)
  {
    if (!this.m_nview.IsValid())
      return;
    hit.m_weakSpot = this.FindWeakSpotIndex(hit.m_hitCollider);
    this.m_nview.InvokeRPC("RPC_Damage", (object) hit);
  }

  private void RPC_Damage(long sender, HitData hit)
  {
    if (this.IsDebugFlying())
      return;
    if ((UnityEngine.Object) hit.GetAttacker() == (UnityEngine.Object) Player.m_localPlayer)
    {
      Game.instance.IncrementPlayerStat(this.IsPlayer() ? PlayerStatType.PlayerHits : PlayerStatType.EnemyHits);
      this.m_localPlayerHasHit = true;
    }
    if (!this.m_nview.IsOwner())
      return;
    Character attacker = hit.GetAttacker();
    if (!this.IsDead() && !this.InCutscene() && !this.IsTeleporting() && (double) hit.m_staggerMultiplier >= 100.0)
      this.Stagger(hit.m_dir);
    if ((double) this.GetHealth() <= 0.0 || this.IsDead() || this.IsTeleporting() || this.InCutscene() || hit.m_dodgeable && this.IsDodgeInvincible() || hit.HaveAttacker() && (UnityEngine.Object) attacker == (UnityEngine.Object) null || this.IsPlayer() && !this.IsPVPEnabled() && (UnityEngine.Object) attacker != (UnityEngine.Object) null && attacker.IsPlayer() && !hit.m_ignorePVP)
      return;
    if ((UnityEngine.Object) attacker != (UnityEngine.Object) null && !attacker.IsPlayer())
    {
      float damageScalePlayer = Game.instance.GetDifficultyDamageScalePlayer(this.transform.position);
      hit.ApplyModifier(damageScalePlayer);
      hit.ApplyModifier(Game.m_enemyDamageRate);
    }
    this.m_seman.OnDamaged(hit, attacker);
    if ((UnityEngine.Object) this.m_baseAI != (UnityEngine.Object) null && this.m_baseAI.IsAggravatable() && !this.m_baseAI.IsAggravated() && (bool) (UnityEngine.Object) attacker && attacker.IsPlayer() && (double) hit.GetTotalDamage() > 0.0)
      BaseAI.AggravateAllInArea(this.transform.position, 20f, BaseAI.AggravatedReason.Damage);
    if ((UnityEngine.Object) this.m_baseAI != (UnityEngine.Object) null && !this.m_baseAI.IsAlerted() && (double) hit.m_backstabBonus > 1.0 && (double) Time.time - (double) this.m_backstabTime > 300.0 && (!ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs) || !this.m_baseAI.CanSeeTarget(attacker)))
    {
      this.m_backstabTime = Time.time;
      hit.ApplyModifier(hit.m_backstabBonus);
      this.m_backstabHitEffects.Create(hit.m_point, Quaternion.identity, this.transform);
    }
    if (this.IsStaggering() && !this.IsPlayer())
    {
      hit.ApplyModifier(2f);
      this.m_critHitEffects.Create(hit.m_point, Quaternion.identity, this.transform);
    }
    if (hit.m_blockable && this.IsBlocking())
      this.BlockAttack(hit, attacker);
    else if (this is Player player)
      this.AddAdrenaline(player.m_nonBlockDamageAdrenaline);
    this.ApplyPushback(hit);
    if (hit.m_statusEffectHash != 0)
    {
      StatusEffect statusEffect = this.m_seman.GetStatusEffect(hit.m_statusEffectHash);
      if ((UnityEngine.Object) statusEffect == (UnityEngine.Object) null)
      {
        statusEffect = this.m_seman.AddStatusEffect(hit.m_statusEffectHash, itemLevel: (int) hit.m_itemLevel, skillLevel: hit.m_skillLevel);
      }
      else
      {
        statusEffect.ResetTime();
        statusEffect.SetLevel((int) hit.m_itemLevel, hit.m_skillLevel);
      }
      if ((UnityEngine.Object) statusEffect != (UnityEngine.Object) null && (UnityEngine.Object) attacker != (UnityEngine.Object) null)
        statusEffect.SetAttacker(attacker);
    }
    WeakSpot weakSpot = this.GetWeakSpot(hit.m_weakSpot);
    if ((UnityEngine.Object) weakSpot != (UnityEngine.Object) null)
      ZLog.Log((object) ("HIT Weakspot:" + weakSpot.gameObject.name));
    HitData.DamageModifiers damageModifiers = this.GetDamageModifiers(weakSpot);
    HitData.DamageModifier significantModifier;
    hit.ApplyResistance(damageModifiers, out significantModifier);
    if (this.IsPlayer())
    {
      float bodyArmor = this.GetBodyArmor();
      hit.ApplyArmor(bodyArmor);
      this.DamageArmorDurability(hit);
    }
    else if (Game.m_worldLevel > 0)
      hit.ApplyArmor((float) (Game.m_worldLevel * Game.instance.m_worldLevelEnemyBaseAC));
    float poison = hit.m_damage.m_poison;
    float fire = hit.m_damage.m_fire;
    float spirit = hit.m_damage.m_spirit;
    hit.m_damage.m_poison = 0.0f;
    hit.m_damage.m_fire = 0.0f;
    hit.m_damage.m_spirit = 0.0f;
    this.ApplyDamage(hit, true, true, significantModifier);
    this.AddFireDamage(fire);
    this.AddSpiritDamage(spirit);
    this.AddPoisonDamage(poison);
    this.AddFrostDamage(hit.m_damage.m_frost);
    this.AddLightningDamage(hit.m_damage.m_lightning);
  }

  protected HitData.DamageModifier GetDamageModifier(HitData.DamageType damageType)
  {
    return this.GetDamageModifiers().GetModifier(damageType);
  }

  public HitData.DamageModifiers GetDamageModifiers(WeakSpot weakspot = null)
  {
    HitData.DamageModifiers mods = (bool) (UnityEngine.Object) weakspot ? weakspot.m_damageModifiers.Clone() : this.m_damageModifiers.Clone();
    this.ApplyArmorDamageMods(ref mods);
    this.m_seman.ApplyDamageMods(ref mods);
    return mods;
  }

  public void ApplyDamage(
    HitData hit,
    bool showDamageText,
    bool triggerEffects,
    HitData.DamageModifier mod = HitData.DamageModifier.Normal)
  {
    if (this.IsDebugFlying() || this.IsDead() || this.IsTeleporting() || this.InCutscene())
      return;
    float totalDamage1 = hit.GetTotalDamage();
    if (!this.IsPlayer())
    {
      float damageScaleEnemy = Game.instance.GetDifficultyDamageScaleEnemy(this.transform.position);
      hit.ApplyModifier(damageScaleEnemy);
      hit.ApplyModifier(Game.m_playerDamageRate);
    }
    else
    {
      hit.ApplyModifier(Game.m_localDamgeTakenRate);
      Game.instance.IncrementPlayerStat(hit.GetAttacker() is Player ? PlayerStatType.HitsTakenPlayers : PlayerStatType.HitsTakenEnemies);
    }
    float totalDamage2 = hit.GetTotalDamage();
    if ((double) totalDamage2 <= 0.10000000149011612)
      return;
    if (showDamageText && ((double) totalDamage2 > 0.0 || !this.IsPlayer()))
      DamageText.instance.ShowText(mod, hit.m_point, totalDamage1, this.IsPlayer() || this.IsTamed());
    this.m_lastHit = hit;
    float health = this.GetHealth() - totalDamage2;
    if ((double) health <= 0.0 && (this.InGodMode() || this.InGhostMode()))
      health = 1f;
    if (Terminal.m_showTests && Terminal.m_testList.ContainsKey("damage"))
      Terminal.Log((object) $"Damage: Character {this.m_name} took {totalDamage2} damage from {hit}");
    this.SetHealth(health);
    this.AddStaggerDamage(hit.m_damage.GetTotalStaggerDamage() * hit.m_staggerMultiplier, hit.m_dir, hit);
    if (triggerEffects && (double) totalDamage2 > (double) this.GetMaxHealth() / 10.0)
    {
      this.DoDamageCameraShake(hit);
      if ((double) hit.m_damage.GetTotalPhysicalDamage() > 0.0)
        this.m_hitEffects.Create(hit.m_point, Quaternion.identity, this.transform);
    }
    this.OnDamaged(hit);
    if ((double) health < 0.0)
      this.GetComponent<Piece>()?.DropResources(hit);
    if (this.m_onDamaged != null)
      this.m_onDamaged(totalDamage2, hit.GetAttacker());
    if (!Character.s_dpsDebugEnabled)
      return;
    Character.AddDPS(totalDamage2, this);
  }

  protected virtual void DoDamageCameraShake(HitData hit)
  {
  }

  protected virtual void DamageArmorDurability(HitData hit)
  {
  }

  private void AddFireDamage(float damage)
  {
    if ((double) damage <= 0.0)
      return;
    SE_Burning se = this.m_seman.GetStatusEffect(SEMan.s_statusEffectBurning) as SE_Burning;
    if ((UnityEngine.Object) se == (UnityEngine.Object) null)
      se = this.m_seman.AddStatusEffect(SEMan.s_statusEffectBurning) as SE_Burning;
    if (se.AddFireDamage(damage))
      return;
    this.m_seman.RemoveStatusEffect((StatusEffect) se);
  }

  private void AddSpiritDamage(float damage)
  {
    if ((double) damage <= 0.0)
      return;
    SE_Burning se = this.m_seman.GetStatusEffect(SEMan.s_statusEffectSpirit) as SE_Burning;
    if ((UnityEngine.Object) se == (UnityEngine.Object) null)
      se = this.m_seman.AddStatusEffect(SEMan.s_statusEffectSpirit) as SE_Burning;
    if (se.AddSpiritDamage(damage))
      return;
    this.m_seman.RemoveStatusEffect((StatusEffect) se);
  }

  private void AddPoisonDamage(float damage)
  {
    if ((double) damage <= 0.0)
      return;
    SE_Poison sePoison = this.m_seman.GetStatusEffect(SEMan.s_statusEffectPoison) as SE_Poison;
    if ((UnityEngine.Object) sePoison == (UnityEngine.Object) null)
      sePoison = this.m_seman.AddStatusEffect(SEMan.s_statusEffectPoison) as SE_Poison;
    sePoison.AddDamage(damage);
  }

  private void AddFrostDamage(float damage)
  {
    if ((double) damage <= 0.0)
      return;
    SE_Frost seFrost = this.m_seman.GetStatusEffect(SEMan.s_statusEffectFrost) as SE_Frost;
    if ((UnityEngine.Object) seFrost == (UnityEngine.Object) null)
      seFrost = this.m_seman.AddStatusEffect(SEMan.s_statusEffectFrost) as SE_Frost;
    seFrost.AddDamage(damage);
  }

  private void AddLightningDamage(float damage)
  {
    if ((double) damage <= 0.0)
      return;
    this.m_seman.AddStatusEffect(SEMan.s_statusEffectLightning, true);
  }

  private static void AddDPS(float damage, Character me)
  {
    if ((UnityEngine.Object) me == (UnityEngine.Object) Player.m_localPlayer)
      Character.CalculateDPS("To-you ", Character.s_playerDamage, damage);
    else
      Character.CalculateDPS("To-others ", Character.s_enemyDamage, damage);
  }

  private static void CalculateDPS(
    string name,
    List<KeyValuePair<float, float>> damages,
    float damage)
  {
    float time = Time.time;
    if (damages.Count > 0 && (double) Time.time - (double) damages[damages.Count - 1].Key > 5.0)
      damages.Clear();
    damages.Add(new KeyValuePair<float, float>(time, damage));
    float num1 = Time.time - damages[0].Key;
    if ((double) num1 < 0.0099999997764825821)
      return;
    float num2 = 0.0f;
    foreach (KeyValuePair<float, float> damage1 in damages)
      num2 += damage1.Value;
    float num3 = num2 / num1;
    string str = $"DPS {name} ( {damages.Count} attacks, {num1.ToString("0.0")}s ): {num3.ToString("0.0")}";
    ZLog.Log((object) str);
    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, str);
  }

  public float GetStaggerPercentage()
  {
    return Mathf.Clamp01(this.m_staggerDamage / this.GetStaggerTreshold());
  }

  private float GetStaggerTreshold() => this.GetMaxHealth() * this.m_staggerDamageFactor;

  protected bool AddStaggerDamage(float damage, Vector3 forceDirection, HitData hit)
  {
    if ((double) this.m_staggerDamageFactor <= 0.0)
      return false;
    this.m_seman.ModifyStagger(damage, ref damage);
    this.m_staggerDamage += damage;
    float staggerTreshold = this.GetStaggerTreshold();
    if ((double) this.m_staggerDamage < (double) staggerTreshold)
      return false;
    this.m_staggerDamage = staggerTreshold;
    this.Stagger(forceDirection);
    if (this.IsPlayer())
      Hud.instance.StaggerBarFlash();
    if (hit != null)
    {
      Character attacker = hit.GetAttacker();
      if (attacker is Player player)
      {
        if (attacker.IsOwner())
          attacker.AddAdrenaline(player.m_staggerEnemyAdrenaline * this.m_enemyAdrenalineMultiplier);
        else
          attacker.m_nview.InvokeRPC("RPC_AddAdrenaline", (object) (float) ((double) player.m_staggerEnemyAdrenaline * (double) this.m_enemyAdrenalineMultiplier));
      }
    }
    return true;
  }

  private void UpdateStagger(float dt)
  {
    if ((double) this.m_staggerDamageFactor <= 0.0 && !this.IsPlayer())
      return;
    this.m_staggerDamage -= this.GetMaxHealth() * this.m_staggerDamageFactor / 5f * dt;
    if ((double) this.m_staggerDamage >= 0.0)
      return;
    this.m_staggerDamage = 0.0f;
  }

  public void Stagger(Vector3 forceDirection)
  {
    if (this.m_nview.IsOwner())
      this.RPC_Stagger(0L, forceDirection);
    else
      this.m_nview.InvokeRPC("RPC_Stagger", (object) forceDirection);
  }

  private void RPC_Stagger(long sender, Vector3 forceDirection)
  {
    if (this.IsStaggering())
      return;
    if ((double) forceDirection.magnitude > 0.0099999997764825821)
    {
      forceDirection.y = 0.0f;
      this.transform.rotation = Quaternion.LookRotation(-forceDirection);
    }
    this.m_zanim.SetSpeed(1f);
    this.m_zanim.SetTrigger("stagger");
  }

  private void RPC_AddAdrenaline(long sender, float amount)
  {
    if (!this.m_nview.IsOwner())
      return;
    this.AddAdrenaline(amount);
  }

  protected virtual void ApplyArmorDamageMods(ref HitData.DamageModifiers mods)
  {
  }

  public virtual float GetBodyArmor() => 0.0f;

  protected virtual bool BlockAttack(HitData hit, Character attacker) => false;

  protected virtual void OnDamaged(HitData hit)
  {
  }

  private void OnCollisionStay(Collision collision)
  {
    if (!this.m_nview.IsOwner() || (double) this.m_jumpTimer < 0.10000000149011612)
      return;
    foreach (ContactPoint contact in collision.contacts)
    {
      float num = contact.point.y - this.transform.position.y;
      Vector3 normal = contact.normal;
      if ((double) normal.y < 0.0)
        normal.y *= -1f;
      if ((double) normal.y > 0.10000000149011612 && (double) num < (double) this.m_collider.radius && (double) contact.point.y < (double) this.m_collider.transform.position.y + (double) this.m_collider.center.y)
      {
        if ((double) normal.y > (double) this.m_groundContactNormal.y || !this.m_groundContact)
        {
          if ((double) this.m_standUp == -100.0)
            this.m_standUp = 2f;
          this.m_groundContact = true;
          this.m_groundContactNormal = normal;
          this.m_groundContactPoint = contact.point;
          this.m_lowestContactCollider = collision.collider;
        }
        else
        {
          Vector3 vector3 = Vector3.Normalize(this.m_groundContactNormal + normal);
          if ((double) vector3.y > (double) this.m_groundContactNormal.y)
          {
            this.m_groundContactNormal = vector3;
            this.m_groundContactPoint = (this.m_groundContactPoint + contact.point) * 0.5f;
          }
        }
      }
    }
  }

  public void StandUpOnNextGround() => this.m_standUp = -100f;

  private void UpdateGroundContact(float dt)
  {
    if (!this.m_groundContact)
      return;
    this.m_lastGroundCollider = this.m_lowestContactCollider;
    this.m_lastGroundNormal = this.m_groundContactNormal;
    this.m_lastGroundPoint = this.m_groundContactPoint;
    this.m_lastGroundBody = (bool) (UnityEngine.Object) this.m_lastGroundCollider ? this.m_lastGroundCollider.attachedRigidbody : (Rigidbody) null;
    if (!this.IsPlayer() && (UnityEngine.Object) this.m_lastGroundBody != (UnityEngine.Object) null && this.m_lastGroundBody.gameObject.layer == this.gameObject.layer)
    {
      this.m_lastGroundCollider = (Collider) null;
      this.m_lastGroundBody = (Rigidbody) null;
    }
    float num = Mathf.Max(0.0f, this.m_maxAirAltitude - this.transform.position.y);
    if ((double) num > 0.800000011920929 && this.m_onLand != null)
    {
      Vector3 lastGroundPoint = this.m_lastGroundPoint;
      if (this.InLiquid())
        lastGroundPoint.y = this.GetLiquidLevel();
      this.m_onLand(this.m_lastGroundPoint);
    }
    if (this.IsPlayer() && (double) num > 4.0)
    {
      float damage = Mathf.Clamp01((float) (((double) num - 4.0) / 16.0)) * 100f;
      this.m_seman.ModifyFallDamage(damage, ref damage);
      if ((double) damage > 0.0)
        this.Damage(new HitData()
        {
          m_damage = {
            m_damage = damage
          },
          m_point = this.m_lastGroundPoint,
          m_dir = this.m_lastGroundNormal,
          m_hitType = HitData.HitType.Fall
        });
    }
    this.ResetGroundContact();
    this.m_lastGroundTouch = 0.0f;
    this.m_maxAirAltitude = this.transform.position.y;
    if (!this.IsPlayer() || !Terminal.m_showTests)
      return;
    Dictionary<string, string> testList = Terminal.m_testList;
    Collider lastGroundCollider = Player.m_localPlayer.GetLastGroundCollider();
    string str = lastGroundCollider == null || !(bool) (UnityEngine.Object) lastGroundCollider ? "none" : LayerMask.LayerToName(lastGroundCollider.gameObject.layer);
    testList["Player.CollisionLayer"] = str;
  }

  private void ResetGroundContact()
  {
    this.m_lowestContactCollider = (Collider) null;
    this.m_groundContact = false;
    this.m_groundContactNormal = Vector3.zero;
    this.m_groundContactPoint = Vector3.zero;
  }

  public Ship GetStandingOnShip()
  {
    if (this.InNumShipVolumes == 0)
      return (Ship) null;
    if (!this.IsOnGround())
      return (Ship) null;
    return (bool) (UnityEngine.Object) this.m_lastGroundBody ? this.m_lastGroundBody.GetComponent<Ship>() : (Ship) null;
  }

  public bool IsOnGround()
  {
    return (double) this.m_lastGroundTouch < 0.20000000298023224 || this.m_body.IsSleeping();
  }

  private void CheckDeath()
  {
    if (this.IsDead() || (double) this.GetHealth() > 0.0)
      return;
    this.OnDeath();
  }

  protected virtual void OnRagdollCreated(Ragdoll ragdoll)
  {
  }

  protected virtual void OnDeath()
  {
    PlayerProfile playerProfile = Game.instance.GetPlayerProfile();
    bool flag = this.m_lastHit != null && (UnityEngine.Object) this.m_lastHit.GetAttacker() == (UnityEngine.Object) Player.m_localPlayer;
    if (flag && this.IsPlayer() && this is Player player)
    {
      playerProfile.IncrementStat(PlayerStatType.PlayerKills);
      playerProfile.m_enemyStats.IncrementOrSet<string>(player.GetPlayerName());
    }
    if (!this.IsPlayer())
    {
      if (this.m_localPlayerHasHit)
        playerProfile.IncrementStat(this.IsBoss() ? PlayerStatType.BossKills : PlayerStatType.EnemyKills);
      if (flag)
        playerProfile.IncrementStat(this.IsBoss() ? PlayerStatType.BossLastHits : PlayerStatType.EnemyKillsLastHits);
      playerProfile.m_enemyStats.IncrementOrSet<string>(this.m_name);
    }
    if (!string.IsNullOrEmpty(this.m_defeatSetGlobalKey))
      Player.m_addUniqueKeyQueue.Add(this.m_defeatSetGlobalKey);
    if ((bool) (UnityEngine.Object) this.m_nview && !this.m_nview.IsOwner())
      return;
    foreach (GameObject gameObject in this.m_deathEffects.Create(this.transform.position, this.transform.rotation, this.transform))
    {
      Ragdoll component1 = gameObject.GetComponent<Ragdoll>();
      if ((bool) (UnityEngine.Object) component1)
      {
        CharacterDrop component2 = this.GetComponent<CharacterDrop>();
        LevelEffects componentInChildren = this.GetComponentInChildren<LevelEffects>();
        Vector3 velocity = this.m_body.linearVelocity;
        if ((double) this.m_pushForce.magnitude * 0.5 > (double) velocity.magnitude)
          velocity = this.m_pushForce * 0.5f;
        float hue = 0.0f;
        float saturation = 0.0f;
        float num = 0.0f;
        if ((bool) (UnityEngine.Object) componentInChildren)
          componentInChildren.GetColorChanges(out hue, out saturation, out num);
        component1.Setup(velocity, hue, saturation, num, component2);
        this.OnRagdollCreated(component1);
        if ((bool) (UnityEngine.Object) component2 && component1.m_dropItems)
          component2.SetDropsEnabled(false);
      }
    }
    if (!string.IsNullOrEmpty(this.m_defeatSetGlobalKey))
      ZoneSystem.instance.SetGlobalKey(this.m_defeatSetGlobalKey);
    if (this.m_onDeath != null)
      this.m_onDeath();
    if (this.IsBoss() && this.m_nview.GetZDO().GetBool("bosscount"))
    {
      float num;
      ZoneSystem.instance.GetGlobalKey(GlobalKeys.activeBosses, out num);
      ZoneSystem.instance.SetGlobalKey(GlobalKeys.activeBosses, Mathf.Max(0.0f, num - 1f));
    }
    ZNetScene.instance.Destroy(this.gameObject);
    Gogan.LogEvent("Game", "Killed", this.m_name, 0L);
  }

  public float GetHealth()
  {
    ZDO zdo = this.m_nview.GetZDO();
    return zdo == null ? this.GetMaxHealth() : zdo.GetFloat(ZDOVars.s_health, this.GetMaxHealth());
  }

  public void SetHealth(float health)
  {
    if ((double) health >= (double) this.GetMaxHealth())
      this.m_localPlayerHasHit = false;
    ZDO zdo = this.m_nview.GetZDO();
    if (zdo == null || !this.m_nview.IsOwner())
      return;
    if ((double) health < 0.0)
      health = 0.0f;
    zdo.Set(ZDOVars.s_health, health);
  }

  public void UseHealth(float hp)
  {
    if ((double) hp <= 0.0)
      return;
    this.SetHealth(Mathf.Clamp(this.GetHealth() - hp, 0.0f, this.GetMaxHealth()));
    if (!this.IsPlayer())
      return;
    Hud.instance.DamageFlash();
  }

  public float GetHealthPercentage() => this.GetHealth() / this.GetMaxHealth();

  public virtual bool IsDead() => false;

  public void SetMaxHealth(float health)
  {
    if (this.m_nview.GetZDO() != null)
      this.m_nview.GetZDO().Set(ZDOVars.s_maxHealth, health);
    if ((double) this.GetHealth() <= (double) health)
      return;
    this.SetHealth(health);
  }

  public float GetMaxHealth()
  {
    return this.m_nview.GetZDO() != null ? this.m_nview.GetZDO().GetFloat(ZDOVars.s_maxHealth, this.m_health) : this.GetMaxHealthBase();
  }

  public float GetMaxHealthBase()
  {
    float health = this.m_health;
    if (!this.IsPlayer() && Game.m_worldLevel > 0)
      health *= (float) Game.m_worldLevel * Game.instance.m_worldLevelEnemyHPMultiplier;
    return health;
  }

  public virtual float GetMaxStamina() => 0.0f;

  public virtual float GetMaxAdrenaline() => this.GetEquipmentMaxAdrenaline();

  public virtual float GetMaxEitr() => 0.0f;

  public virtual float GetEitrPercentage() => 1f;

  public virtual float GetStaminaPercentage() => 1f;

  public bool IsBoss() => this.m_boss;

  public bool TryUseEitr(float eitrUse = 0.0f)
  {
    if ((double) eitrUse == 0.0)
      return true;
    if ((double) this.GetMaxEitr() == 0.0)
    {
      this.Message(MessageHud.MessageType.Center, "$hud_eitrrequired");
      return false;
    }
    if (this.HaveEitr(eitrUse + 0.1f))
      return true;
    if (this.IsPlayer())
      Hud.instance.EitrBarEmptyFlash();
    return false;
  }

  public void SetLookDir(Vector3 dir, float transitionTime = 0.0f)
  {
    if ((double) transitionTime > 0.0)
    {
      this.m_lookTransitionTime = this.m_lookTransitionTimeTotal = transitionTime;
      this.m_lookTransitionStart = this.GetLookDir();
      this.m_lookTransitionTarget = Vector3.Normalize(dir);
    }
    else
    {
      if ((double) dir.magnitude <= (double) Mathf.Epsilon)
        dir = this.transform.forward;
      else
        dir.Normalize();
      this.m_lookDir = dir;
      dir.y = 0.0f;
      this.m_lookYaw = Quaternion.LookRotation(dir);
    }
  }

  private void UpdateLookTransition(float dt)
  {
    if ((double) this.m_lookTransitionTime <= 0.0)
      return;
    this.SetLookDir(Vector3.Lerp(this.m_lookTransitionTarget, this.m_lookTransitionStart, Mathf.SmoothStep(0.0f, 1f, this.m_lookTransitionTime / this.m_lookTransitionTimeTotal)));
    this.m_lookTransitionTime -= dt;
  }

  public Vector3 GetLookDir() => this.m_eye.forward;

  public virtual void OnAttackTrigger()
  {
  }

  public virtual void OnStopMoving()
  {
  }

  public virtual void OnWeaponTrailStart()
  {
  }

  public void SetMoveDir(Vector3 dir) => this.m_moveDir = dir;

  public void SetRun(bool run) => this.m_run = run;

  public void SetWalk(bool walk) => this.m_walk = walk;

  public bool GetWalk() => this.m_walk;

  protected virtual void UpdateEyeRotation()
  {
    this.m_eye.rotation = Quaternion.LookRotation(this.m_lookDir);
  }

  public void OnAutoJump(Vector3 dir, float upVel, float forwardVel)
  {
    if (!this.m_nview.IsValid() || !this.m_nview.IsOwner() || !this.IsOnGround() || this.IsDead() || this.InAttack() || this.InDodge() || this.IsKnockedBack() || (double) Time.time - (double) this.m_lastAutoJumpTime < 0.5)
      return;
    this.m_lastAutoJumpTime = Time.time;
    if ((double) Vector3.Dot(this.m_moveDir, dir) < 0.5)
      return;
    this.m_body.linearVelocity = Vector3.zero with
    {
      y = upVel
    } + dir * forwardVel;
    this.m_lastGroundTouch = 1f;
    this.m_jumpTimer = 0.0f;
    this.m_jumpEffects.Create(this.transform.position, this.transform.rotation, this.transform);
    this.SetCrouch(false);
    this.UpdateBodyFriction();
  }

  public void Jump(bool force = false)
  {
    if (!this.IsOnGround() || this.IsDead() || !force && this.InAttack() || this.IsEncumbered() || this.InDodge() || this.IsKnockedBack() || this.IsStaggering())
      return;
    bool flag = false;
    if (!this.HaveStamina(this.m_jumpStaminaUsage))
    {
      if (this.IsPlayer())
        Hud.instance.StaminaBarEmptyFlash();
      flag = true;
    }
    float speed = this.m_speed;
    this.m_seman.ApplyStatusEffectSpeedMods(ref speed, this.m_currentVel);
    if ((double) speed <= 0.0)
      flag = true;
    float num1 = 0.0f;
    Skills skills = this.GetSkills();
    if ((UnityEngine.Object) skills != (UnityEngine.Object) null)
    {
      num1 = skills.GetSkillFactor(Skills.SkillType.Jump);
      if (!flag)
        this.RaiseSkill(Skills.SkillType.Jump);
    }
    Vector3 linearVelocity = this.m_body.linearVelocity;
    double num2 = (double) Mathf.Acos(Mathf.Clamp01(this.m_lastGroundNormal.y));
    Vector3 normalized = (this.m_lastGroundNormal + Vector3.up).normalized;
    float num3 = (float) (1.0 + (double) num1 * 0.40000000596046448);
    float num4 = this.m_jumpForce * num3;
    float num5 = Vector3.Dot(normalized, linearVelocity);
    if ((double) num5 < (double) num4)
      linearVelocity += normalized * (num4 - num5);
    Vector3 jump = !this.IsPlayer() ? linearVelocity + this.transform.forward * this.m_jumpForceForward * num3 : linearVelocity + this.m_moveDir * this.m_jumpForceForward * num3;
    if (flag)
      jump *= this.m_jumpForceTiredFactor;
    this.m_seman.ApplyStatusEffectJumpMods(ref jump);
    if ((double) jump.x <= 0.0 && (double) jump.y <= 0.0 && (double) jump.z <= 0.0)
      return;
    this.ForceJump(jump);
  }

  public void ForceJump(Vector3 vel, bool effects = true)
  {
    this.m_body.WakeUp();
    this.m_body.linearVelocity = vel;
    this.ResetGroundContact();
    this.m_lastGroundTouch = 1f;
    this.m_jumpTimer = 0.0f;
    this.AddNoise(30f);
    if (!effects)
      return;
    this.m_zanim.SetTrigger("jump");
    this.m_jumpEffects.Create(this.transform.position, this.transform.rotation, this.transform);
    this.ResetCloth();
    this.OnJump();
    this.SetCrouch(false);
    this.UpdateBodyFriction();
  }

  public void SetTempParent(Transform t)
  {
    this.oldParent = this.transform.parent;
    this.transform.parent = t;
  }

  public void ReleaseTempParent() => this.transform.parent = this.oldParent;

  private void UpdateBodyFriction()
  {
    this.m_collider.material.frictionCombine = PhysicsMaterialCombine.Multiply;
    if (this.IsDead())
    {
      this.m_collider.material.staticFriction = 1f;
      this.m_collider.material.dynamicFriction = 1f;
      this.m_collider.material.frictionCombine = PhysicsMaterialCombine.Maximum;
    }
    else if (this.IsSwimming())
    {
      this.m_collider.material.staticFriction = 0.2f;
      this.m_collider.material.dynamicFriction = 0.2f;
    }
    else if (!this.IsOnGround())
    {
      this.m_collider.material.staticFriction = 0.0f;
      this.m_collider.material.dynamicFriction = 0.0f;
    }
    else if (this.IsFlying())
    {
      this.m_collider.material.staticFriction = 0.0f;
      this.m_collider.material.dynamicFriction = 0.0f;
    }
    else if ((double) this.m_moveDir.magnitude < 0.10000000149011612)
    {
      this.m_collider.material.staticFriction = (float) (0.800000011920929 * (1.0 - (double) this.m_slippage));
      this.m_collider.material.dynamicFriction = (float) (0.800000011920929 * (1.0 - (double) this.m_slippage));
      this.m_collider.material.frictionCombine = PhysicsMaterialCombine.Maximum;
    }
    else
    {
      this.m_collider.material.staticFriction = (float) (0.40000000596046448 * (1.0 - (double) this.m_slippage));
      this.m_collider.material.dynamicFriction = (float) (0.40000000596046448 * (1.0 - (double) this.m_slippage));
    }
  }

  public virtual bool StartAttack(Character target, bool charge) => false;

  public virtual float GetTimeSinceLastAttack() => 99999f;

  public virtual void OnNearFire(Vector3 point)
  {
  }

  public ZDOID GetZDOID() => !this.m_nview.IsValid() ? ZDOID.None : this.m_nview.GetZDO().m_uid;

  public bool IsOwner() => this.m_nview.IsValid() && this.m_nview.IsOwner();

  public long GetOwner() => !this.m_nview.IsValid() ? 0L : this.m_nview.GetZDO().GetOwner();

  public virtual bool UseMeleeCamera() => false;

  protected virtual bool AlwaysRotateCamera() => true;

  public void SetLiquidLevel(float level, LiquidType type, Component liquidObj)
  {
    switch (type)
    {
      case LiquidType.Water:
        this.m_waterLevel = level;
        break;
      case LiquidType.Tar:
        this.m_tarLevel = level;
        break;
    }
    this.m_liquidLevel = Mathf.Max(this.m_waterLevel, this.m_tarLevel);
  }

  public virtual bool IsPVPEnabled() => false;

  public virtual bool InIntro() => false;

  public virtual bool InCutscene() => false;

  public virtual bool IsCrouching() => false;

  public virtual bool InBed() => false;

  public virtual bool IsAttached() => false;

  public virtual bool IsAttachedToShip() => false;

  public virtual bool IsRiding() => false;

  protected virtual void SetCrouch(bool crouch)
  {
  }

  public virtual void AttachStart(
    Transform attachPoint,
    GameObject colliderRoot,
    bool hideWeapons,
    bool isBed,
    bool onShip,
    string attachAnimation,
    Vector3 detachOffset,
    Transform cameraPos = null)
  {
  }

  public virtual void AttachStop()
  {
  }

  private void UpdateWater(float dt)
  {
    this.m_swimTimer += dt;
    float depth = this.InLiquidDepth();
    if (this.m_canSwim && this.InLiquidSwimDepth(depth))
      this.m_swimTimer = 0.0f;
    if (!this.m_nview.IsOwner() || !this.InLiquidWetDepth(depth))
      return;
    if ((double) this.m_waterLevel > (double) this.m_tarLevel)
    {
      this.m_seman.AddStatusEffect(SEMan.s_statusEffectWet, true);
    }
    else
    {
      if ((double) this.m_tarLevel <= (double) this.m_waterLevel || this.m_tolerateTar)
        return;
      this.m_seman.AddStatusEffect(SEMan.s_statusEffectTared, true);
    }
  }

  private void ApplyLiquidResistance(ref float speed)
  {
    float num1 = this.InLiquidDepth();
    if ((double) num1 <= 0.0 || this.m_seman.HaveStatusEffect(SEMan.s_statusEffectTared))
      return;
    float num2 = (double) this.m_tarLevel > (double) this.m_waterLevel ? 0.1f : 0.05f;
    float num3 = this.m_collider.height / 3f;
    float num4 = Mathf.Clamp01(num1 / num3);
    speed -= speed * speed * num4 * num2;
  }

  public bool IsSwimming() => (double) this.m_swimTimer < 0.5;

  public bool InLava() => (double) this.m_lavaTimer < 0.5;

  public bool AboveOrInLava() => (double) this.m_aboveOrInLavaTimer < 0.5;

  private bool InLiquidSwimDepth()
  {
    return (double) this.InLiquidDepth() > (double) Mathf.Max(0.0f, this.m_swimDepth - 0.4f);
  }

  private bool InLiquidSwimDepth(float depth)
  {
    return (double) depth > (double) Mathf.Max(0.0f, this.m_swimDepth - 0.4f);
  }

  private bool InLiquidKneeDepth() => (double) this.InLiquidDepth() > 0.40000000596046448;

  private bool InLiquidKneeDepth(float depth) => (double) depth > 0.40000000596046448;

  private bool InLiquidWetDepth(float depth)
  {
    if (this.InLiquidSwimDepth(depth))
      return true;
    return this.IsSitting() && this.InLiquidKneeDepth(depth);
  }

  private float InLiquidDepth() => this.m_cashedInLiquidDepth;

  private void CalculateLiquidDepth()
  {
    if (this.IsTeleporting() || (UnityEngine.Object) this.GetStandingOnShip() != (UnityEngine.Object) null || this.IsAttachedToShip())
      this.m_cashedInLiquidDepth = 0.0f;
    else
      this.m_cashedInLiquidDepth = Mathf.Max(0.0f, this.GetLiquidLevel() - this.transform.position.y);
  }

  protected void InvalidateCachedLiquidDepth() => this.m_cashedInLiquidDepth = 0.0f;

  public float GetLiquidLevel() => this.m_liquidLevel;

  public bool InLiquid() => (double) this.InLiquidDepth() > 0.0;

  private bool InTar() => (double) this.m_tarLevel > (double) this.m_waterLevel && this.InLiquid();

  public bool InWater() => (double) this.m_waterLevel > (double) this.m_tarLevel && this.InLiquid();

  protected virtual bool CheckRun(Vector3 moveDir, float dt)
  {
    return this.m_run && (double) moveDir.magnitude >= 0.10000000149011612 && !this.IsCrouching() && !this.IsEncumbered() && !this.InDodge();
  }

  public bool IsRunning() => this.m_running;

  public bool IsWalking() => this.m_walking;

  public virtual bool InPlaceMode() => false;

  public virtual void AddEitr(float v)
  {
  }

  public virtual void UseEitr(float eitr)
  {
  }

  public virtual bool HaveEitr(float amount = 0.0f) => true;

  public virtual bool HaveStamina(float amount = 0.0f) => true;

  public bool HaveHealth(float amount = 0.0f) => (double) this.GetHealth() >= (double) amount;

  public virtual void AddStamina(float v)
  {
  }

  public virtual void UseStamina(float stamina)
  {
  }

  public virtual void AddAdrenaline(float v)
  {
  }

  protected int GetNextOrCurrentAnimHash()
  {
    if (this.m_cachedAnimHashFrame == MonoUpdaters.UpdateCount)
      return this.m_cachedNextOrCurrentAnimHash;
    this.UpdateCachedAnimHashes();
    return this.m_cachedNextOrCurrentAnimHash;
  }

  protected int GetCurrentAnimHash()
  {
    if (this.m_cachedAnimHashFrame == MonoUpdaters.UpdateCount)
      return this.m_cachedCurrentAnimHash;
    this.UpdateCachedAnimHashes();
    return this.m_cachedCurrentAnimHash;
  }

  protected int GetNextAnimHash()
  {
    if (this.m_cachedAnimHashFrame == MonoUpdaters.UpdateCount)
      return this.m_cachedNextAnimHash;
    this.UpdateCachedAnimHashes();
    return this.m_cachedNextAnimHash;
  }

  private void UpdateCachedAnimHashes()
  {
    this.m_cachedAnimHashFrame = MonoUpdaters.UpdateCount;
    this.m_cachedCurrentAnimHash = this.m_animator.GetCurrentAnimatorStateInfo(0).tagHash;
    this.m_cachedNextAnimHash = 0;
    this.m_cachedNextOrCurrentAnimHash = this.m_cachedCurrentAnimHash;
    if (!this.m_animator.IsInTransition(0))
      return;
    this.m_cachedNextAnimHash = this.m_animator.GetNextAnimatorStateInfo(0).tagHash;
    this.m_cachedNextOrCurrentAnimHash = this.m_cachedNextAnimHash;
  }

  public bool IsStaggering()
  {
    return this.GetNextAnimHash() == Character.s_animatorTagStagger || this.GetCurrentAnimHash() == Character.s_animatorTagStagger;
  }

  public virtual bool CanMove()
  {
    if (this.IsStaggering())
      return false;
    int orCurrentAnimHash = this.GetNextOrCurrentAnimHash();
    return orCurrentAnimHash != Character.s_animatorTagFreeze && orCurrentAnimHash != Character.s_animatorTagSitting;
  }

  public virtual bool IsEncumbered() => false;

  public virtual bool IsTeleporting() => false;

  private bool CanWallRun() => this.IsPlayer();

  public void ShowPickupMessage(ItemDrop.ItemData item, int amount)
  {
    this.Message(MessageHud.MessageType.TopLeft, "$msg_added " + item.m_shared.m_name, amount, item.GetIcon());
  }

  public void ShowRemovedMessage(ItemDrop.ItemData item, int amount)
  {
    this.Message(MessageHud.MessageType.TopLeft, "$msg_removed " + item.m_shared.m_name, amount, item.GetIcon());
  }

  public virtual void Message(MessageHud.MessageType type, string msg, int amount = 0, Sprite icon = null)
  {
  }

  public CapsuleCollider GetCollider() => this.m_collider;

  public virtual float GetStealthFactor() => 1f;

  private void UpdateNoise(float dt)
  {
    this.m_noiseRange = Mathf.Max(0.0f, this.m_noiseRange - dt * 4f);
    this.m_syncNoiseTimer += dt;
    if ((double) this.m_syncNoiseTimer <= 0.5)
      return;
    this.m_syncNoiseTimer = 0.0f;
    this.m_nview.GetZDO().Set(ZDOVars.s_noise, this.m_noiseRange);
  }

  public void AddNoise(float range)
  {
    if (!this.m_nview.IsValid())
      return;
    if (this.m_nview.IsOwner())
      this.RPC_AddNoise(0L, range);
    else
      this.m_nview.InvokeRPC("RPC_AddNoise", (object) range);
  }

  private void RPC_AddNoise(long sender, float range)
  {
    if (!this.m_nview.IsOwner() || (double) range <= (double) this.m_noiseRange)
      return;
    this.m_noiseRange = range;
    this.m_seman.ModifyNoise(this.m_noiseRange, ref this.m_noiseRange);
  }

  public float GetNoiseRange()
  {
    if (!this.m_nview.IsValid())
      return 0.0f;
    return this.m_nview.IsOwner() ? this.m_noiseRange : this.m_nview.GetZDO().GetFloat(ZDOVars.s_noise);
  }

  public virtual bool InGodMode() => false;

  public virtual bool InGhostMode() => false;

  public virtual bool IsDebugFlying() => false;

  public virtual string GetHoverText()
  {
    Tameable component = this.GetComponent<Tameable>();
    return (bool) (UnityEngine.Object) component ? component.GetHoverText() : "";
  }

  public virtual string GetHoverName()
  {
    Tameable component = this.GetComponent<Tameable>();
    return (bool) (UnityEngine.Object) component ? component.GetHoverName() : Localization.instance.Localize(this.m_name);
  }

  public virtual bool IsDrawingBow() => false;

  public virtual bool InAttack() => false;

  protected virtual void StopEmote()
  {
  }

  public virtual bool InMinorAction() => false;

  public virtual bool InMinorActionSlowdown() => false;

  public virtual bool InDodge() => false;

  public virtual bool IsDodgeInvincible() => false;

  public virtual bool InEmote() => false;

  public virtual bool IsBlocking() => false;

  public bool IsFlying() => this.m_flying;

  public bool IsKnockedBack() => this.m_pushForce != Vector3.zero;

  private void OnDrawGizmosSelected()
  {
    if ((UnityEngine.Object) this.m_nview != (UnityEngine.Object) null && this.m_nview.GetZDO() != null)
      Gizmos.DrawWireSphere(this.transform.position, this.m_nview.GetZDO().GetFloat(ZDOVars.s_noise));
    Gizmos.color = Color.blue;
    Gizmos.DrawWireCube(this.transform.position + Vector3.up * this.m_swimDepth, new Vector3(1f, 0.05f, 1f));
    if (!this.IsOnGround())
      return;
    Gizmos.color = Color.green;
    Gizmos.DrawLine(this.m_lastGroundPoint, this.m_lastGroundPoint + this.m_lastGroundNormal);
  }

  public virtual bool TeleportTo(Vector3 pos, Quaternion rot, bool distantTeleport) => false;

  protected void RPC_TeleportTo(long sender, Vector3 pos, Quaternion rot, bool distantTeleport)
  {
    if (!this.m_nview.IsOwner())
      return;
    this.TeleportTo(pos, rot, distantTeleport);
  }

  private void SyncVelocity()
  {
    Vector3 linearVelocity = this.m_body.linearVelocity;
    if (!linearVelocity.Equals(this.m_bodyVelocityCached))
      this.m_nview.GetZDO().Set(ZDOVars.s_bodyVelocity, linearVelocity);
    this.m_bodyVelocityCached = linearVelocity;
  }

  public Vector3 GetVelocity()
  {
    if (!this.m_nview.IsValid())
      return Vector3.zero;
    return this.m_nview.IsOwner() ? this.m_body.linearVelocity : this.m_nview.GetZDO().GetVec3(ZDOVars.s_bodyVelocity, Vector3.zero);
  }

  public void AddRootMotion(Vector3 vel)
  {
    if (!this.InDodge() && !this.InAttack() && !this.InEmote())
      return;
    this.m_rootMotion += vel;
  }

  private void ApplyRootMotion(ref Vector3 vel)
  {
    Vector3 vector3 = this.m_rootMotion * 55f;
    if ((double) vector3.magnitude > (double) vel.magnitude)
      vel = vector3;
    this.m_rootMotion = Vector3.zero;
  }

  public static void GetCharactersInRange(Vector3 point, float radius, List<Character> characters)
  {
    float num = radius * radius;
    foreach (Character character in Character.s_characters)
    {
      if ((double) Utils.DistanceSqr(character.transform.position, point) < (double) num)
        characters.Add(character);
    }
  }

  public static List<Character> GetAllCharacters() => Character.s_characters;

  public static bool IsCharacterInRange(Vector3 point, float range)
  {
    foreach (Component character in Character.s_characters)
    {
      if ((double) Vector3.Distance(character.transform.position, point) < (double) range)
        return true;
    }
    return false;
  }

  public virtual void OnTargeted(bool sensed, bool alerted)
  {
  }

  public GameObject GetVisual() => this.m_visual;

  protected void UpdateLodgroup()
  {
    if ((UnityEngine.Object) this.m_lodGroup == (UnityEngine.Object) null)
      return;
    Renderer[] componentsInChildren = this.m_visual.GetComponentsInChildren<Renderer>();
    LOD[] loDs = this.m_lodGroup.GetLODs();
    loDs[0].renderers = componentsInChildren;
    this.m_lodGroup.SetLODs(loDs);
  }

  public virtual bool IsSitting() => false;

  public virtual float GetEquipmentMovementModifier() => 0.0f;

  public virtual float GetEquipmentHomeItemModifier() => 0.0f;

  public virtual float GetEquipmentHeatResistanceModifier() => 0.0f;

  public virtual float GetEquipmentJumpStaminaModifier() => 0.0f;

  public virtual float GetEquipmentAttackStaminaModifier() => 0.0f;

  public virtual float GetEquipmentBlockStaminaModifier() => 0.0f;

  public virtual float GetEquipmentDodgeStaminaModifier() => 0.0f;

  public virtual float GetEquipmentSwimStaminaModifier() => 0.0f;

  public virtual float GetEquipmentSneakStaminaModifier() => 0.0f;

  public virtual float GetEquipmentRunStaminaModifier() => 0.0f;

  protected virtual float GetJogSpeedFactor() => 1f;

  public virtual float GetEquipmentMaxAdrenaline() => 0.0f;

  protected virtual float GetRunSpeedFactor()
  {
    return this.HaveRider() ? (float) (1.0 + (double) this.m_baseAI.GetRiderSkill() * 0.25) : 1f;
  }

  protected virtual float GetAttackSpeedFactorMovement() => 1f;

  protected virtual float GetAttackSpeedFactorRotation() => 1f;

  public virtual void RaiseSkill(Skills.SkillType skill, float value = 1f)
  {
    if (!this.IsTamed())
      return;
    if (!(bool) (UnityEngine.Object) this.m_tameable)
    {
      this.m_tameable = this.GetComponent<Tameable>();
      this.m_tameableMonsterAI = this.GetComponent<MonsterAI>();
    }
    if (!(bool) (UnityEngine.Object) this.m_tameable || !(bool) (UnityEngine.Object) this.m_tameableMonsterAI)
    {
      ZLog.LogWarning((object) (this.m_name + " is tamed but missing tameable or monster AI script!"));
    }
    else
    {
      if (this.m_tameable.m_levelUpOwnerSkill == Skills.SkillType.None)
        return;
      GameObject followTarget = this.m_tameableMonsterAI.GetFollowTarget();
      if (followTarget == null || !(bool) (UnityEngine.Object) followTarget)
        return;
      Character component = followTarget.GetComponent<Character>();
      if (component == null)
        return;
      Skills skills = component.GetSkills();
      if (skills == null)
        return;
      skills.RaiseSkill(this.m_tameable.m_levelUpOwnerSkill, value * this.m_tameable.m_levelUpFactor);
      Terminal.Log((object) $"{this.name} leveling up from '{skill}' to master {component.name} skill '{this.m_tameable.m_levelUpOwnerSkill}' at factor {(ValueType) (float) ((double) value * (double) this.m_tameable.m_levelUpFactor)}");
    }
  }

  public virtual Skills GetSkills() => (Skills) null;

  public float GetSkillLevel(Skills.SkillType skillType)
  {
    Skills skills = this.GetSkills();
    return skills != null ? skills.GetSkillLevel(skillType) : 0.0f;
  }

  public virtual float GetSkillFactor(Skills.SkillType skill) => 0.0f;

  public virtual float GetRandomSkillFactor(Skills.SkillType skill)
  {
    return Mathf.Pow(UnityEngine.Random.Range(0.75f, 1f), 0.5f) * this.m_nview.GetZDO().GetFloat(ZDOVars.s_randomSkillFactor, 1f);
  }

  public bool IsMonsterFaction(float time)
  {
    if (this.IsTamed(time))
      return false;
    return this.m_faction == Character.Faction.ForestMonsters || this.m_faction == Character.Faction.Undead || this.m_faction == Character.Faction.Demon || this.m_faction == Character.Faction.PlainsMonsters || this.m_faction == Character.Faction.MountainMonsters || this.m_faction == Character.Faction.SeaMonsters || this.m_faction == Character.Faction.MistlandsMonsters;
  }

  public Transform GetTransform()
  {
    return (UnityEngine.Object) this == (UnityEngine.Object) null ? (Transform) null : this.transform;
  }

  public Collider GetLastGroundCollider() => this.m_lastGroundCollider;

  public Vector3 GetLastGroundNormal() => this.m_groundContactNormal;

  public void ResetCloth() => this.m_nview.InvokeRPC(ZNetView.Everybody, "RPC_ResetCloth");

  private void RPC_ResetCloth(long sender)
  {
    foreach (Cloth componentsInChild in this.GetComponentsInChildren<Cloth>())
    {
      if (componentsInChild.enabled)
      {
        componentsInChild.enabled = false;
        componentsInChild.enabled = true;
      }
    }
  }

  public virtual bool GetRelativePosition(
    out ZDOID parent,
    out string attachJoint,
    out Vector3 relativePos,
    out Quaternion relativeRot,
    out Vector3 relativeVel)
  {
    relativeVel = Vector3.zero;
    if (this.IsOnGround() && (bool) (UnityEngine.Object) this.m_lastGroundBody)
    {
      ZNetView component = this.m_lastGroundBody.GetComponent<ZNetView>();
      if ((bool) (UnityEngine.Object) component && component.IsValid())
      {
        parent = component.GetZDO().m_uid;
        attachJoint = "";
        relativePos = component.transform.InverseTransformPoint(this.transform.position);
        relativeRot = Quaternion.Inverse(component.transform.rotation) * this.transform.rotation;
        relativeVel = component.transform.InverseTransformVector(this.m_body.linearVelocity - this.m_lastGroundBody.linearVelocity);
        return true;
      }
    }
    parent = ZDOID.None;
    attachJoint = "";
    relativePos = Vector3.zero;
    relativeRot = Quaternion.identity;
    return false;
  }

  public Quaternion GetLookYaw() => this.m_lookYaw;

  public Vector3 GetMoveDir() => this.m_moveDir;

  public BaseAI GetBaseAI() => this.m_baseAI;

  public float GetMass() => this.m_body.mass;

  protected void SetVisible(bool visible)
  {
    if ((UnityEngine.Object) this.m_lodGroup == (UnityEngine.Object) null || this.m_lodVisible == visible)
      return;
    this.m_lodVisible = visible;
    if (this.m_lodVisible)
      this.m_lodGroup.localReferencePoint = this.m_originalLocalRef;
    else
      this.m_lodGroup.localReferencePoint = new Vector3(999999f, 999999f, 999999f);
  }

  public void SetTamed(bool tamed)
  {
    if (!this.m_nview.IsValid() || this.m_tamed == tamed)
      return;
    this.m_nview.InvokeRPC("RPC_SetTamed", (object) tamed);
  }

  private void RPC_SetTamed(long sender, bool tamed)
  {
    if (!this.m_nview.IsOwner() || this.m_tamed == tamed)
      return;
    this.m_tamed = tamed;
    this.m_nview.GetZDO().Set(ZDOVars.s_tamed, this.m_tamed);
  }

  private bool IsTamed(float time)
  {
    if (!this.m_nview.IsValid())
      return false;
    if (!this.m_nview.GetZDO().IsOwner() && (double) time - (double) this.m_lastTamedCheck > 1.0)
    {
      this.m_lastTamedCheck = time;
      this.m_tamed = this.m_nview.GetZDO().GetBool(ZDOVars.s_tamed, this.m_tamed);
    }
    return this.m_tamed;
  }

  public bool IsTamed() => this.IsTamed(Time.time);

  public ZSyncAnimation GetZAnim() => this.m_zanim;

  public SEMan GetSEMan() => this.m_seman;

  public bool InInterior() => Character.InInterior(this.transform);

  public static bool InInterior(Transform me) => Character.InInterior(me.position);

  public static bool InInterior(Vector3 position) => (double) position.y > 3000.0;

  public static void SetDPSDebug(bool enabled) => Character.s_dpsDebugEnabled = enabled;

  public static bool IsDPSDebugEnabled() => Character.s_dpsDebugEnabled;

  public void TakeOff()
  {
    this.m_flying = true;
    this.m_jumpEffects.Create(this.transform.position, Quaternion.identity);
    this.m_animator.SetTrigger("fly_takeoff");
  }

  public void Land()
  {
    this.m_flying = false;
    this.m_animator.SetTrigger("fly_land");
  }

  public void FreezeFrame(float duration)
  {
    this.m_nview.InvokeRPC(ZNetView.Everybody, "RPC_FreezeFrame", (object) duration);
  }

  private void RPC_FreezeFrame(long sender, float duration)
  {
    this.m_animEvent.FreezeFrame(duration);
  }

  public void SetExtraMass(float amount) => this.m_body.mass = this.m_originalMass + amount;

  public int InNumShipVolumes { get; set; }

  public int Increment(LiquidType type) => ++this.m_liquids[(int) type];

  public int Decrement(LiquidType type) => --this.m_liquids[(int) type];

  public static List<IMonoUpdater> Instances { get; } = new List<IMonoUpdater>();

  public enum Faction
  {
    Players,
    AnimalsVeg,
    ForestMonsters,
    Undead,
    Demon,
    MountainMonsters,
    SeaMonsters,
    PlainsMonsters,
    Boss,
    MistlandsMonsters,
    Dverger,
    PlayerSpawned,
    TrainingDummy,
  }

  public enum GroundTiltType
  {
    None,
    Pitch,
    Full,
    PitchRaycast,
    FullRaycast,
    Flying,
  }
}
