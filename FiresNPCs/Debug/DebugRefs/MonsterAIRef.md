// Decompiled with JetBrains decompiler
// Type: MonsterAI
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using System;
using System.Collections.Generic;
using UnityEngine;

#nullable disable
public class MonsterAI : BaseAI
{
  private float m_lastDespawnInDayCheck = -9999f;
  private float m_lastEventCreatureCheck = -9999f;
  public Action<ItemDrop> m_onConsumedItem;
  private const float m_giveUpTime = 30f;
  private const float m_updateTargetFarRange = 50f;
  private const float m_updateTargetIntervalNear = 2f;
  private const float m_updateTargetIntervalFar = 6f;
  private const float m_updateWeaponInterval = 1f;
  private const float m_unableToAttackTargetDuration = 15f;
  [Header("Monster AI")]
  public float m_alertRange = 9999f;
  public bool m_fleeIfHurtWhenTargetCantBeReached = true;
  public float m_fleeUnreachableSinceAttacking = 30f;
  public float m_fleeUnreachableSinceHurt = 20f;
  public bool m_fleeIfNotAlerted;
  public float m_fleeIfLowHealth;
  public float m_fleeTimeSinceHurt = 20f;
  public bool m_fleeInLava = true;
  public float m_fleePheromoneMin = 3f;
  public float m_fleePheromoneMax = 8f;
  public bool m_circulateWhileCharging;
  public bool m_circulateWhileChargingFlying;
  public bool m_enableHuntPlayer;
  public bool m_attackPlayerObjects = true;
  public int m_privateAreaTriggerTreshold = 4;
  public float m_interceptTimeMax;
  public float m_interceptTimeMin;
  public float m_maxChaseDistance;
  public float m_minAttackInterval;
  [Header("Circle target")]
  public float m_circleTargetInterval;
  public float m_circleTargetDuration = 5f;
  public float m_circleTargetDistance = 10f;
  [Header("Sleep")]
  public bool m_sleeping;
  public float m_wakeupRange = 5f;
  public bool m_noiseWakeup;
  public float m_maxNoiseWakeupRange = 50f;
  public EffectList m_wakeupEffects = new EffectList();
  public EffectList m_sleepEffects = new EffectList();
  public float m_wakeUpDelayMin;
  public float m_wakeUpDelayMax;
  public float m_fallAsleepDistance;
  [Header("Other")]
  public bool m_avoidLand;
  [Header("Consume items")]
  public List<ItemDrop> m_consumeItems;
  public float m_consumeRange = 2f;
  public float m_consumeSearchRange = 5f;
  public float m_consumeSearchInterval = 10f;
  private ItemDrop m_consumeTarget;
  private float m_consumeSearchTimer;
  private static int m_itemMask = 0;
  private bool m_despawnInDay;
  private bool m_eventCreature;
  private Character m_targetCreature;
  private Vector3 m_lastKnownTargetPos = Vector3.zero;
  private bool m_beenAtLastPos;
  private StaticTarget m_targetStatic;
  private float m_timeSinceAttacking;
  private float m_timeSinceSensedTargetCreature;
  private float m_updateTargetTimer;
  private float m_updateWeaponTimer;
  private float m_interceptTime;
  private float m_sleepDelay = 0.5f;
  private float m_pauseTimer;
  private float m_sleepTimer;
  private float m_unableToAttackTargetTimer;
  private GameObject m_follow;
  private int m_privateAreaAttacks;
  private static readonly int s_sleeping = ZSyncAnimation.GetHash("sleeping");

  protected override void Awake()
  {
    base.Awake();
    ZDO zdo = this.m_nview.GetZDO();
    if (zdo != null)
    {
      this.m_despawnInDay = zdo.GetBool(ZDOVars.s_despawnInDay, this.m_despawnInDay);
      this.m_eventCreature = zdo.GetBool(ZDOVars.s_eventCreature, this.m_eventCreature);
      this.m_sleeping = zdo.GetBool(ZDOVars.s_sleeping, this.m_sleeping);
      this.m_animator.SetBool(MonsterAI.s_sleeping, this.IsSleeping());
    }
    this.m_interceptTime = UnityEngine.Random.Range(this.m_interceptTimeMin, this.m_interceptTimeMax);
    this.m_pauseTimer = UnityEngine.Random.Range(0.0f, this.m_circleTargetInterval);
    this.m_updateTargetTimer = UnityEngine.Random.Range(0.0f, 2f);
    if ((double) this.m_wakeUpDelayMin > 0.0 || (double) this.m_wakeUpDelayMax > 0.0)
      this.m_sleepDelay = UnityEngine.Random.Range(this.m_wakeUpDelayMin, this.m_wakeUpDelayMax);
    if (this.m_enableHuntPlayer)
      this.SetHuntPlayer(true);
    this.m_nview.Register("RPC_Wakeup", new Action<long>(this.RPC_Wakeup));
    this.m_nview.Register("RPC_Sleep", new Action<long>(this.RPC_Sleep));
  }

  private void Start()
  {
    if (!(bool) (UnityEngine.Object) this.m_nview || !this.m_nview.IsValid() || !this.m_nview.IsOwner())
      return;
    Humanoid character = this.m_character as Humanoid;
    if (!(bool) (UnityEngine.Object) character)
      return;
    character.EquipBestWeapon((Character) null, (StaticTarget) null, (Character) null, (Character) null);
  }

  protected override void OnDamaged(float damage, Character attacker)
  {
    base.OnDamaged(damage, attacker);
    this.Wakeup();
    this.SetAlerted(true);
    this.SetTarget(attacker);
  }

  private void SetTarget(Character attacker)
  {
    if (!((UnityEngine.Object) attacker != (UnityEngine.Object) null) || !((UnityEngine.Object) this.m_targetCreature == (UnityEngine.Object) null) || attacker.IsPlayer() && this.m_character.IsTamed())
      return;
    this.m_targetCreature = attacker;
    this.m_lastKnownTargetPos = attacker.transform.position;
    this.m_beenAtLastPos = false;
    this.m_targetStatic = (StaticTarget) null;
  }

  protected override void RPC_OnNearProjectileHit(
    long sender,
    Vector3 center,
    float range,
    ZDOID attackerID)
  {
    if (!this.m_nview.IsOwner() || ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs))
      return;
    this.SetAlerted(true);
    if (this.m_fleeIfNotAlerted)
      return;
    GameObject instance = ZNetScene.instance.FindInstance(attackerID);
    if (!((UnityEngine.Object) instance != (UnityEngine.Object) null))
      return;
    Character component = instance.GetComponent<Character>();
    if (!(bool) (UnityEngine.Object) component)
      return;
    this.SetTarget(component);
  }

  public void MakeTame()
  {
    this.m_character.SetTamed(true);
    this.SetAlerted(false);
    this.m_targetCreature = (Character) null;
    this.m_targetStatic = (StaticTarget) null;
  }

  private void UpdateTarget(
    Humanoid humanoid,
    float dt,
    out bool canHearTarget,
    out bool canSeeTarget)
  {
    this.m_unableToAttackTargetTimer -= dt;
    this.m_updateTargetTimer -= dt;
    if ((double) this.m_updateTargetTimer <= 0.0 && !this.m_character.InAttack())
    {
      this.m_updateTargetTimer = Player.IsPlayerInRange(this.transform.position, 50f) ? 2f : 6f;
      Character enemy = this.FindEnemy();
      if ((bool) (UnityEngine.Object) enemy)
      {
        this.m_targetCreature = enemy;
        this.m_targetStatic = (StaticTarget) null;
      }
      bool flag1 = (UnityEngine.Object) this.m_targetCreature != (UnityEngine.Object) null && this.m_targetCreature.IsPlayer();
      bool flag2 = (UnityEngine.Object) this.m_targetCreature != (UnityEngine.Object) null && (double) this.m_unableToAttackTargetTimer > 0.0 && !this.HavePath(this.m_targetCreature.transform.position);
      if ((!this.m_attackPlayerObjects || this.m_aggravatable && !this.IsAggravated() ? 0 : (!ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs) ? 1 : 0)) != 0 && (UnityEngine.Object) this.m_targetCreature == (UnityEngine.Object) null | flag2 && !this.m_character.IsTamed())
      {
        StaticTarget staticPriorityTarget = this.FindClosestStaticPriorityTarget();
        if ((bool) (UnityEngine.Object) staticPriorityTarget)
        {
          this.m_targetStatic = staticPriorityTarget;
          this.m_targetCreature = (Character) null;
        }
        bool flag3 = false;
        if ((UnityEngine.Object) this.m_targetStatic != (UnityEngine.Object) null)
          flag3 = this.HavePath(this.m_targetStatic.FindClosestPoint(this.m_character.transform.position));
        if ((((UnityEngine.Object) this.m_targetStatic == (UnityEngine.Object) null || !flag3 ? (this.IsAlerted() ? 1 : 0) : 0) & (flag1 ? 1 : 0)) != 0)
        {
          StaticTarget randomStaticTarget = this.FindRandomStaticTarget(10f);
          if ((bool) (UnityEngine.Object) randomStaticTarget)
          {
            this.m_targetStatic = randomStaticTarget;
            this.m_targetCreature = (Character) null;
          }
        }
      }
    }
    if ((bool) (UnityEngine.Object) this.m_targetCreature && this.m_character.IsTamed())
    {
      Vector3 point;
      if (this.GetPatrolPoint(out point))
      {
        if ((double) Vector3.Distance(this.m_targetCreature.transform.position, point) > (double) this.m_alertRange)
          this.m_targetCreature = (Character) null;
      }
      else if ((bool) (UnityEngine.Object) this.m_follow && (double) Vector3.Distance(this.m_targetCreature.transform.position, this.m_follow.transform.position) > (double) this.m_alertRange)
        this.m_targetCreature = (Character) null;
    }
    if ((bool) (UnityEngine.Object) this.m_targetCreature)
    {
      if (this.m_targetCreature.IsDead())
        this.m_targetCreature = (Character) null;
      else if (!this.IsEnemy(this.m_targetCreature))
        this.m_targetCreature = (Character) null;
      else if (this.m_skipLavaTargets && this.m_targetCreature.AboveOrInLava())
        this.m_targetCreature = (Character) null;
    }
    canHearTarget = false;
    canSeeTarget = false;
    if ((bool) (UnityEngine.Object) this.m_targetCreature)
    {
      canHearTarget = this.CanHearTarget(this.m_targetCreature);
      canSeeTarget = this.CanSeeTarget(this.m_targetCreature);
      if (canSeeTarget | canHearTarget)
        this.m_timeSinceSensedTargetCreature = 0.0f;
      if (this.m_targetCreature.IsPlayer())
        this.m_targetCreature.OnTargeted(canSeeTarget | canHearTarget, this.IsAlerted());
      this.SetTargetInfo(this.m_targetCreature.GetZDOID());
    }
    else
      this.SetTargetInfo(ZDOID.None);
    this.m_timeSinceSensedTargetCreature += dt;
    if (!this.IsAlerted() && !((UnityEngine.Object) this.m_targetCreature != (UnityEngine.Object) null))
      return;
    this.m_timeSinceAttacking += dt;
    float num1 = 60f;
    float num2 = Vector3.Distance(this.m_spawnPoint, this.transform.position);
    bool flag = this.HuntPlayer() && (bool) (UnityEngine.Object) this.m_targetCreature && this.m_targetCreature.IsPlayer();
    if ((double) this.m_timeSinceSensedTargetCreature <= 30.0 && (flag || (double) this.m_timeSinceAttacking <= (double) num1 && ((double) this.m_maxChaseDistance <= 0.0 || (double) this.m_timeSinceSensedTargetCreature <= 1.0 || (double) num2 <= (double) this.m_maxChaseDistance)))
      return;
    this.SetAlerted(false);
    this.m_targetCreature = (Character) null;
    this.m_targetStatic = (StaticTarget) null;
    this.m_timeSinceAttacking = 0.0f;
    this.m_updateTargetTimer = 5f;
  }

  public override bool UpdateAI(float dt)
  {
    if (!base.UpdateAI(dt))
      return false;
    this.UpdateSleep(dt);
    if (this.IsSleeping())
      return true;
    Humanoid character = this.m_character as Humanoid;
    if (this.HuntPlayer())
      this.SetAlerted(true);
    bool canHearTarget;
    bool canSeeTarget;
    this.UpdateTarget(character, dt, out canHearTarget, out canSeeTarget);
    if ((bool) (UnityEngine.Object) this.m_tamable && (bool) (UnityEngine.Object) this.m_tamable.m_saddle && this.m_tamable.m_saddle.UpdateRiding(dt))
      return true;
    if (this.m_avoidLand && !this.m_character.IsSwimming())
    {
      this.MoveToWater(dt, 20f);
      return true;
    }
    if (this.DespawnInDay() && EnvMan.IsDay() && ((UnityEngine.Object) this.m_targetCreature == (UnityEngine.Object) null || !canSeeTarget))
    {
      this.MoveAwayAndDespawn(dt, true);
      return true;
    }
    if (this.IsEventCreature() && !RandEventSystem.HaveActiveEvent())
    {
      this.SetHuntPlayer(false);
      if ((UnityEngine.Object) this.m_targetCreature == (UnityEngine.Object) null && !this.IsAlerted())
      {
        this.MoveAwayAndDespawn(dt, false);
        return true;
      }
    }
    if (this.m_fleeIfNotAlerted && !this.HuntPlayer() && (bool) (UnityEngine.Object) this.m_targetCreature && !this.IsAlerted() && (double) Vector3.Distance(this.m_targetCreature.transform.position, this.transform.position) - (double) this.m_targetCreature.GetRadius() > (double) this.m_alertRange)
    {
      this.Flee(dt, this.m_targetCreature.transform.position);
      return true;
    }
    if ((double) this.m_fleeIfLowHealth > 0.0 && (double) this.m_timeSinceHurt < (double) this.m_fleeTimeSinceHurt && (UnityEngine.Object) this.m_targetCreature != (UnityEngine.Object) null && (double) this.m_character.GetHealthPercentage() < (double) this.m_fleeIfLowHealth)
    {
      this.Flee(dt, this.m_targetCreature.transform.position);
      return true;
    }
    if (this.m_fleeInLava && this.m_character.InLava() && ((UnityEngine.Object) this.m_targetCreature == (UnityEngine.Object) null || this.m_targetCreature.AboveOrInLava()))
    {
      this.Flee(dt, this.m_character.transform.position - this.m_character.transform.forward);
      return true;
    }
    if ((this.m_afraidOfFire || this.m_avoidFire) && this.AvoidFire(dt, this.m_targetCreature, this.m_afraidOfFire))
    {
      if (this.m_afraidOfFire)
      {
        this.m_targetStatic = (StaticTarget) null;
        this.m_targetCreature = (Character) null;
      }
      return true;
    }
    if (!this.m_character.IsTamed())
    {
      if ((UnityEngine.Object) this.m_targetCreature != (UnityEngine.Object) null)
      {
        if ((bool) (UnityEngine.Object) EffectArea.IsPointInsideNoMonsterArea(this.m_targetCreature.transform.position))
        {
          this.Flee(dt, this.m_targetCreature.transform.position);
          return true;
        }
      }
      else
      {
        EffectArea noMonsterArea = EffectArea.IsPointCloseToNoMonsterArea(this.transform.position);
        if ((UnityEngine.Object) noMonsterArea != (UnityEngine.Object) null)
        {
          this.Flee(dt, noMonsterArea.transform.position);
          return true;
        }
      }
    }
    if (this.m_fleeIfHurtWhenTargetCantBeReached && (UnityEngine.Object) this.m_targetCreature != (UnityEngine.Object) null && (double) this.m_timeSinceAttacking > 30.0 && (double) this.m_timeSinceHurt < 20.0)
    {
      this.Flee(dt, this.m_targetCreature.transform.position);
      this.m_lastKnownTargetPos = this.transform.position;
      this.m_updateTargetTimer = 1f;
      return true;
    }
    if ((!this.IsAlerted() || (UnityEngine.Object) this.m_targetStatic == (UnityEngine.Object) null && (UnityEngine.Object) this.m_targetCreature == (UnityEngine.Object) null) && this.UpdateConsumeItem(character, dt))
      return true;
    if ((double) this.m_circleTargetInterval > 0.0 && (bool) (UnityEngine.Object) this.m_targetCreature)
    {
      this.m_pauseTimer += dt;
      if ((double) this.m_pauseTimer > (double) this.m_circleTargetInterval)
      {
        if ((double) this.m_pauseTimer > (double) this.m_circleTargetInterval + (double) this.m_circleTargetDuration)
          this.m_pauseTimer = UnityEngine.Random.Range(0.0f, this.m_circleTargetInterval / 10f);
        this.RandomMovementArroundPoint(dt, this.m_targetCreature.transform.position, this.m_circleTargetDistance, this.IsAlerted());
        return true;
      }
    }
    ItemDrop.ItemData itemData = this.SelectBestAttack(character, dt);
    bool flag1 = itemData != null && (double) Time.time - (double) itemData.m_lastAttackTime > (double) itemData.m_shared.m_aiAttackInterval;
    bool flag2 = (double) this.m_character.GetTimeSinceLastAttack() >= (double) this.m_minAttackInterval;
    bool flag3 = itemData != null & flag1 & flag2 && !this.IsTakingOff();
    if (((this.IsCharging() || !((UnityEngine.Object) this.m_targetStatic != (UnityEngine.Object) null) && !((UnityEngine.Object) this.m_targetCreature != (UnityEngine.Object) null) ? 0 : (itemData != null ? 1 : 0)) & (flag3 ? 1 : 0)) != 0 && !this.m_character.InAttack() && itemData.m_shared.m_attack != null && !itemData.m_shared.m_attack.IsDone() && !string.IsNullOrEmpty(itemData.m_shared.m_attack.m_chargeAnimationBool))
      this.ChargeStart(itemData.m_shared.m_attack.m_chargeAnimationBool);
    if ((this.m_character.IsFlying() ? (this.m_circulateWhileChargingFlying ? 1 : 0) : (this.m_circulateWhileCharging ? 1 : 0)) != 0 && ((UnityEngine.Object) this.m_targetStatic != (UnityEngine.Object) null || (UnityEngine.Object) this.m_targetCreature != (UnityEngine.Object) null) && itemData != null && !flag3 && !this.m_character.InAttack())
    {
      Vector3 point = (bool) (UnityEngine.Object) this.m_targetCreature ? this.m_targetCreature.transform.position : this.m_targetStatic.transform.position;
      this.RandomMovementArroundPoint(dt, point, this.m_randomMoveRange, this.IsAlerted());
      return true;
    }
    if ((UnityEngine.Object) this.m_targetStatic == (UnityEngine.Object) null && (UnityEngine.Object) this.m_targetCreature == (UnityEngine.Object) null || itemData == null)
    {
      if ((bool) (UnityEngine.Object) this.m_follow)
        this.Follow(this.m_follow, dt);
      else
        this.IdleMovement(dt);
      this.ChargeStop();
      return true;
    }
    if (itemData.m_shared.m_aiTargetType == ItemDrop.ItemData.AiTarget.Enemy)
    {
      if ((bool) (UnityEngine.Object) this.m_targetStatic)
      {
        Vector3 closestPoint = this.m_targetStatic.FindClosestPoint(this.transform.position);
        if ((double) Vector3.Distance(closestPoint, this.transform.position) < (double) itemData.m_shared.m_aiAttackRange && this.CanSeeTarget(this.m_targetStatic))
        {
          this.LookAt(this.m_targetStatic.GetCenter());
          if ((double) itemData.m_shared.m_aiAttackMaxAngle == 0.0)
            ZLog.LogError((object) $"AI Attack Max Angle for {itemData.m_shared.m_name} is 0!");
          if (this.IsLookingAt(this.m_targetStatic.GetCenter(), itemData.m_shared.m_aiAttackMaxAngle, itemData.m_shared.m_aiInvertAngleCheck) & flag3)
            this.DoAttack((Character) null, false);
          else
            this.StopMoving();
        }
        else
        {
          this.MoveTo(dt, closestPoint, 0.0f, this.IsAlerted());
          this.ChargeStop();
        }
      }
      else if ((bool) (UnityEngine.Object) this.m_targetCreature)
      {
        if (canHearTarget | canSeeTarget || this.HuntPlayer() && this.m_targetCreature.IsPlayer())
        {
          this.m_beenAtLastPos = false;
          this.m_lastKnownTargetPos = this.m_targetCreature.transform.position;
          float num1 = Vector3.Distance(this.m_lastKnownTargetPos, this.transform.position) - this.m_targetCreature.GetRadius();
          float num2 = this.m_alertRange * this.m_targetCreature.GetStealthFactor();
          if (canSeeTarget && (double) num1 < (double) num2)
            this.SetAlerted(true);
          int num3 = (double) num1 < (double) itemData.m_shared.m_aiAttackRange ? 1 : 0;
          if (num3 == 0 || !canSeeTarget || (double) itemData.m_shared.m_aiAttackRangeMin < 0.0 || !this.IsAlerted())
          {
            Vector3 velocity = this.m_targetCreature.GetVelocity();
            Vector3 vector3 = velocity * this.m_interceptTime;
            Vector3 lastKnownTargetPos = this.m_lastKnownTargetPos;
            if ((double) num1 > (double) vector3.magnitude / 4.0)
              lastKnownTargetPos += velocity * this.m_interceptTime;
            this.MoveTo(dt, lastKnownTargetPos, 0.0f, this.IsAlerted());
            if ((double) this.m_timeSinceAttacking > 15.0)
              this.m_unableToAttackTargetTimer = 15f;
          }
          else
            this.StopMoving();
          if ((num3 & (canSeeTarget ? 1 : 0)) != 0 && this.IsAlerted())
          {
            if (this.PheromoneFleeCheck(this.m_targetCreature))
            {
              this.Flee(dt, this.m_targetCreature.transform.position);
              this.m_updateTargetTimer = UnityEngine.Random.Range(this.m_fleePheromoneMin, this.m_fleePheromoneMax);
              this.m_targetCreature = (Character) null;
            }
            else
            {
              this.LookAt(this.m_targetCreature.GetTopPoint());
              if (flag3 && this.IsLookingAt(this.m_lastKnownTargetPos, itemData.m_shared.m_aiAttackMaxAngle, itemData.m_shared.m_aiInvertAngleCheck))
                this.DoAttack(this.m_targetCreature, false);
            }
          }
        }
        else
        {
          this.ChargeStop();
          if (this.m_beenAtLastPos)
          {
            this.RandomMovement(dt, this.m_lastKnownTargetPos);
            if ((double) this.m_timeSinceAttacking > 15.0)
              this.m_unableToAttackTargetTimer = 15f;
          }
          else if (this.MoveTo(dt, this.m_lastKnownTargetPos, 0.0f, this.IsAlerted()))
            this.m_beenAtLastPos = true;
        }
      }
    }
    else if (itemData.m_shared.m_aiTargetType == ItemDrop.ItemData.AiTarget.FriendHurt || itemData.m_shared.m_aiTargetType == ItemDrop.ItemData.AiTarget.Friend)
    {
      Character target = itemData.m_shared.m_aiTargetType == ItemDrop.ItemData.AiTarget.FriendHurt ? this.HaveHurtFriendInRange(this.m_viewRange) : this.HaveFriendInRange(this.m_viewRange);
      if ((bool) (UnityEngine.Object) target)
      {
        if ((double) Vector3.Distance(target.transform.position, this.transform.position) < (double) itemData.m_shared.m_aiAttackRange)
        {
          if (flag3)
          {
            this.StopMoving();
            this.LookAt(target.transform.position);
            this.DoAttack(target, true);
          }
          else
            this.RandomMovement(dt, target.transform.position);
        }
        else
          this.MoveTo(dt, target.transform.position, 0.0f, this.IsAlerted());
      }
      else
        this.RandomMovement(dt, this.transform.position, true);
    }
    return true;
  }

  private bool PheromoneFleeCheck(Character target)
  {
    if (target is Player player)
    {
      foreach (StatusEffect statusEffect in player.GetSEMan().GetStatusEffects())
      {
        if (statusEffect is SE_Stats seStats && seStats.m_pheromoneFlee)
        {
          Character component = seStats.m_pheromoneTarget.GetComponent<Character>();
          if (component != null && component.m_name == this.m_character.m_name)
            return true;
        }
      }
    }
    return false;
  }

  private bool UpdateConsumeItem(Humanoid humanoid, float dt)
  {
    if (this.m_consumeItems == null || this.m_consumeItems.Count == 0)
      return false;
    this.m_consumeSearchTimer += dt;
    if ((double) this.m_consumeSearchTimer > (double) this.m_consumeSearchInterval)
    {
      this.m_consumeSearchTimer = 0.0f;
      if ((bool) (UnityEngine.Object) this.m_tamable && !this.m_tamable.IsHungry())
        return false;
      this.m_consumeTarget = this.FindClosestConsumableItem(this.m_consumeSearchRange);
    }
    if (!(bool) (UnityEngine.Object) this.m_consumeTarget)
      return false;
    if (this.MoveTo(dt, this.m_consumeTarget.transform.position, this.m_consumeRange, false))
    {
      this.LookAt(this.m_consumeTarget.transform.position);
      if (this.IsLookingAt(this.m_consumeTarget.transform.position, 20f) && this.m_consumeTarget.RemoveOne())
      {
        if (this.m_onConsumedItem != null)
          this.m_onConsumedItem(this.m_consumeTarget);
        humanoid.m_consumeItemEffects.Create(this.transform.position, Quaternion.identity);
        this.m_animator.SetTrigger("consume");
        this.m_consumeTarget = (ItemDrop) null;
      }
    }
    return true;
  }

  private ItemDrop FindClosestConsumableItem(float maxRange)
  {
    if (MonsterAI.m_itemMask == 0)
      MonsterAI.m_itemMask = LayerMask.GetMask("item");
    Collider[] colliderArray = Physics.OverlapSphere(this.transform.position, maxRange, MonsterAI.m_itemMask);
    ItemDrop itemDrop = (ItemDrop) null;
    float num1 = 999999f;
    foreach (Collider collider in colliderArray)
    {
      if ((bool) (UnityEngine.Object) collider.attachedRigidbody)
      {
        ItemDrop component = collider.attachedRigidbody.GetComponent<ItemDrop>();
        if (!((UnityEngine.Object) component == (UnityEngine.Object) null) && component.GetComponent<ZNetView>().IsValid() && this.CanConsume(component.m_itemData))
        {
          float num2 = Vector3.Distance(component.transform.position, this.transform.position);
          if ((UnityEngine.Object) itemDrop == (UnityEngine.Object) null || (double) num2 < (double) num1)
          {
            itemDrop = component;
            num1 = num2;
          }
        }
      }
    }
    return (bool) (UnityEngine.Object) itemDrop && this.HavePath(itemDrop.transform.position) ? itemDrop : (ItemDrop) null;
  }

  private bool CanConsume(ItemDrop.ItemData item)
  {
    foreach (ItemDrop consumeItem in this.m_consumeItems)
    {
      if (consumeItem.m_itemData.m_shared.m_name == item.m_shared.m_name)
        return true;
    }
    return false;
  }

  private ItemDrop.ItemData SelectBestAttack(Humanoid humanoid, float dt)
  {
    if ((bool) (UnityEngine.Object) this.m_targetCreature || (bool) (UnityEngine.Object) this.m_targetStatic)
    {
      this.m_updateWeaponTimer -= dt;
      if ((double) this.m_updateWeaponTimer <= 0.0 && !this.m_character.InAttack())
      {
        this.m_updateWeaponTimer = 1f;
        Character hurtFriend;
        Character friend;
        this.HaveFriendsInRange(this.m_viewRange, out hurtFriend, out friend);
        humanoid.EquipBestWeapon(this.m_targetCreature, this.m_targetStatic, hurtFriend, friend);
      }
    }
    return humanoid.GetCurrentWeapon();
  }

  private bool DoAttack(Character target, bool isFriend)
  {
    ItemDrop.ItemData currentWeapon = (this.m_character as Humanoid).GetCurrentWeapon();
    if (currentWeapon == null || !this.CanUseAttack(currentWeapon))
      return false;
    int num = this.m_character.StartAttack(target, false) ? 1 : 0;
    if (num == 0)
      return num != 0;
    this.m_timeSinceAttacking = 0.0f;
    return num != 0;
  }

  public void SetDespawnInDay(bool despawn)
  {
    this.m_despawnInDay = despawn;
    this.m_nview.GetZDO().Set(ZDOVars.s_despawnInDay, despawn);
  }

  public bool DespawnInDay()
  {
    if ((double) Time.time - (double) this.m_lastDespawnInDayCheck > 4.0)
    {
      this.m_lastDespawnInDayCheck = Time.time;
      this.m_despawnInDay = this.m_nview.GetZDO().GetBool(ZDOVars.s_despawnInDay, this.m_despawnInDay);
    }
    return this.m_despawnInDay;
  }

  public void SetEventCreature(bool despawn)
  {
    this.m_eventCreature = despawn;
    this.m_nview.GetZDO().Set(ZDOVars.s_eventCreature, despawn);
  }

  public bool IsEventCreature()
  {
    if ((double) Time.time - (double) this.m_lastEventCreatureCheck > 4.0)
    {
      this.m_lastEventCreatureCheck = Time.time;
      this.m_eventCreature = this.m_nview.GetZDO().GetBool(ZDOVars.s_eventCreature, this.m_eventCreature);
    }
    return this.m_eventCreature;
  }

  protected override void OnDrawGizmosSelected()
  {
    base.OnDrawGizmosSelected();
    this.DrawAILabel();
  }

  private void OnDrawGizmos()
  {
    if (!Terminal.m_showTests)
      return;
    this.DrawAILabel();
  }

  private void DrawAILabel()
  {
  }

  public override Character GetTargetCreature() => this.m_targetCreature;

  public StaticTarget GetStaticTarget() => this.m_targetStatic;

  private void UpdateSleep(float dt)
  {
    Player player = (Player) null;
    if ((double) this.m_wakeupRange > 0.0 || (double) this.m_fallAsleepDistance > 0.0)
    {
      player = Player.GetClosestPlayer(this.transform.position, this.m_wakeupRange);
      if ((UnityEngine.Object) player != (UnityEngine.Object) null && (player.InGhostMode() || player.IsDebugFlying()))
        player = (Player) null;
    }
    if (!this.IsSleeping())
    {
      if ((double) this.m_fallAsleepDistance <= 0.0 || !((UnityEngine.Object) player == (UnityEngine.Object) null) && (double) Vector3.Distance(player.transform.position, this.transform.position) <= (double) this.m_fallAsleepDistance)
        return;
      this.Sleep();
    }
    else
    {
      this.m_sleepTimer += dt;
      if ((double) this.m_sleepTimer < (double) this.m_sleepDelay)
        return;
      if (this.HuntPlayer())
        this.Wakeup();
      else if ((double) this.m_wakeupRange > 0.0 && (bool) (UnityEngine.Object) player)
      {
        this.Wakeup();
      }
      else
      {
        if (!this.m_noiseWakeup)
          return;
        Player playerNoiseRange = Player.GetPlayerNoiseRange(this.transform.position, this.m_maxNoiseWakeupRange);
        if (!(bool) (UnityEngine.Object) playerNoiseRange || playerNoiseRange.InGhostMode() || playerNoiseRange.IsDebugFlying())
          return;
        this.Wakeup();
      }
    }
  }

  public void OnPrivateAreaAttacked(Character attacker, bool destroyed)
  {
    if (!attacker.IsPlayer() || !this.IsAggravatable() || this.IsAggravated())
      return;
    ++this.m_privateAreaAttacks;
    if (!(this.m_privateAreaAttacks > this.m_privateAreaTriggerTreshold | destroyed))
      return;
    this.SetAggravated(true, BaseAI.AggravatedReason.Damage);
  }

  private void RPC_Wakeup(long sender)
  {
    if (this.m_nview.GetZDO().IsOwner())
      return;
    this.m_sleeping = false;
  }

  private void Wakeup()
  {
    if (!this.IsSleeping())
      return;
    this.m_animator.SetBool(MonsterAI.s_sleeping, false);
    this.m_nview.GetZDO().Set(ZDOVars.s_sleeping, false);
    this.m_wakeupEffects.Create(this.transform.position, this.transform.rotation);
    this.m_sleeping = false;
    this.m_nview.InvokeRPC(ZNetView.Everybody, "RPC_Wakeup");
  }

  private void RPC_Sleep(long sender)
  {
    if (this.m_nview.GetZDO().IsOwner())
      return;
    this.m_sleeping = true;
  }

  private void Sleep()
  {
    if (this.IsSleeping())
      return;
    this.SetAlerted(false);
    this.m_sleepTimer = 0.0f;
    this.m_targetCreature = (Character) null;
    this.m_animator.SetBool(MonsterAI.s_sleeping, true);
    this.m_nview.GetZDO().Set(ZDOVars.s_sleeping, true);
    this.m_sleepEffects.Create(this.transform.position, this.transform.rotation);
    this.m_sleeping = true;
    this.m_nview.InvokeRPC(ZNetView.Everybody, "RPC_Sleep");
  }

  public override bool IsSleeping() => this.m_sleeping;

  protected override void SetAlerted(bool alert)
  {
    if (alert)
      this.m_timeSinceSensedTargetCreature = 0.0f;
    base.SetAlerted(alert);
  }

  public override bool HuntPlayer()
  {
    return base.HuntPlayer() && (!this.IsEventCreature() || RandEventSystem.InEvent()) && (!this.DespawnInDay() || !EnvMan.IsDay());
  }

  public GameObject GetFollowTarget() => this.m_follow;

  public void SetFollowTarget(GameObject go) => this.m_follow = go;
}
