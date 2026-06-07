// Decompiled with JetBrains decompiler
// Type: Humanoid
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#nullable disable
public class Humanoid : Character
{
  private static List<ItemDrop.ItemData> optimalWeapons = new List<ItemDrop.ItemData>();
  private static List<ItemDrop.ItemData> outofRangeWeapons = new List<ItemDrop.ItemData>();
  private static List<ItemDrop.ItemData> allWeapons = new List<ItemDrop.ItemData>();
  [Header("Humanoid")]
  public float m_blockStaminaDrain = 25f;
  public float m_perfectBlockStaminaDrain;
  public StatusEffect m_perfectBlockStatusEffect;
  [Header("Default items")]
  public GameObject[] m_defaultItems;
  public GameObject[] m_randomWeapon;
  public GameObject[] m_randomArmor;
  public GameObject[] m_randomShield;
  public Humanoid.ItemSet[] m_randomSets;
  public Humanoid.RandomItem[] m_randomItems;
  public ItemDrop m_unarmedWeapon;
  private bool[] m_randomItemSlotFilled;
  [Header("Effects")]
  public EffectList m_pickupEffects = new EffectList();
  public EffectList m_dropEffects = new EffectList();
  public EffectList m_consumeItemEffects = new EffectList();
  public EffectList m_equipEffects = new EffectList();
  public EffectList m_perfectBlockEffect = new EffectList();
  protected readonly Inventory m_inventory = new Inventory("Inventory", (Sprite) null, 8, 4);
  protected ItemDrop.ItemData m_rightItem;
  protected ItemDrop.ItemData m_leftItem;
  protected ItemDrop.ItemData m_chestItem;
  protected ItemDrop.ItemData m_legItem;
  protected ItemDrop.ItemData m_ammoItem;
  protected ItemDrop.ItemData m_helmetItem;
  protected ItemDrop.ItemData m_shoulderItem;
  protected ItemDrop.ItemData m_utilityItem;
  protected ItemDrop.ItemData m_trinketItem;
  protected string m_beardItem = "";
  protected string m_hairItem = "";
  protected Attack m_currentAttack;
  protected bool m_currentAttackIsSecondary;
  protected float m_attackDrawTime;
  protected float m_lastCombatTimer = 999f;
  protected VisEquipment m_visEquipment;
  private Attack m_previousAttack;
  private ItemDrop.ItemData m_hiddenLeftItem;
  private ItemDrop.ItemData m_hiddenRightItem;
  private int m_lastEquipEffectFrame;
  private float m_timeSinceLastAttack;
  private bool m_internalBlockingState;
  private float m_blockTimer = 9999f;
  private const float m_perfectBlockInterval = 0.25f;
  private int m_blockCharges;
  private float m_blockChargeRemoveTimer;
  private readonly HashSet<StatusEffect> m_equipmentStatusEffects = new HashSet<StatusEffect>();
  private int m_seed;
  private int m_useItemBlockMessage;
  private int m_lastGroundColliderOnAttackStart = -1;
  private float m_useItemTime;
  private ItemDrop.ItemData.SharedData m_useItemVisual;
  private bool m_hidHandsOnEat;
  private static readonly int s_statef = ZSyncAnimation.GetHash("statef");
  private static readonly int s_statei = ZSyncAnimation.GetHash("statei");
  private static readonly int s_blocking = ZSyncAnimation.GetHash("blocking");
  protected static readonly int s_animatorTagAttack = ZSyncAnimation.GetHash("attack");

  protected override void Awake()
  {
    base.Awake();
    this.m_visEquipment = this.GetComponent<VisEquipment>();
    if (!this.m_nview.IsValid())
      return;
    this.m_seed = this.m_nview.GetZDO().GetInt(ZDOVars.s_seed);
    if (this.m_seed != 0)
      return;
    this.m_seed = this.m_nview.GetZDO().m_uid.GetHashCode();
    this.m_nview.GetZDO().Set(ZDOVars.s_seed, this.m_seed, true);
  }

  protected override void Start()
  {
    if (this.IsPlayer())
      return;
    this.GiveDefaultItems();
  }

  protected override void OnDestroy() => base.OnDestroy();

  public void GiveDefaultItems()
  {
    foreach (GameObject defaultItem in this.m_defaultItems)
      this.GiveDefaultItem(defaultItem);
    if (this.m_randomWeapon.Length == 0 && this.m_randomArmor.Length == 0 && this.m_randomShield.Length == 0 && this.m_randomSets.Length == 0 && this.m_randomItems.Length == 0)
      return;
    UnityEngine.Random.State state = UnityEngine.Random.state;
    UnityEngine.Random.InitState(this.m_seed);
    if (this.m_randomShield.Length != 0)
    {
      GameObject prefab = this.m_randomShield[UnityEngine.Random.Range(0, this.m_randomShield.Length)];
      if ((bool) (UnityEngine.Object) prefab)
        this.GiveDefaultItem(prefab);
    }
    if (this.m_randomWeapon.Length != 0)
    {
      GameObject prefab = this.m_randomWeapon[UnityEngine.Random.Range(0, this.m_randomWeapon.Length)];
      if ((bool) (UnityEngine.Object) prefab)
        this.GiveDefaultItem(prefab);
    }
    if (this.m_randomArmor.Length != 0)
    {
      GameObject prefab = this.m_randomArmor[UnityEngine.Random.Range(0, this.m_randomArmor.Length)];
      if ((bool) (UnityEngine.Object) prefab)
        this.GiveDefaultItem(prefab);
    }
    if (this.m_randomSets.Length != 0)
    {
      foreach (GameObject prefab in this.m_randomSets[UnityEngine.Random.Range(0, this.m_randomSets.Length)].m_items)
        this.GiveDefaultItem(prefab);
    }
    if (this.m_randomItems.Length != 0)
    {
      this.m_randomItemSlotFilled = new bool[(int) Enum.GetValues(typeof (ItemDrop.ItemData.ItemType)).Cast<ItemDrop.ItemData.ItemType>().Max<ItemDrop.ItemData.ItemType>()];
      foreach (Humanoid.RandomItem randomItem in this.m_randomItems)
      {
        if ((bool) (UnityEngine.Object) randomItem.m_prefab && (double) UnityEngine.Random.value > (double) randomItem.m_chance)
        {
          int itemType = (int) randomItem.m_prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_itemType;
          if (!this.m_randomItemSlotFilled[itemType])
          {
            this.m_randomItemSlotFilled[itemType] = true;
            this.GiveDefaultItem(randomItem.m_prefab);
          }
        }
      }
    }
    UnityEngine.Random.state = state;
  }

  private void GiveDefaultItem(GameObject prefab)
  {
    ItemDrop.ItemData itemData = this.PickupPrefab(prefab, autoequip: false);
    if (itemData == null || itemData.IsWeapon())
      return;
    this.EquipItem(itemData, false);
  }

  public override void CustomFixedUpdate(float fixedDeltaTime)
  {
    if (!this.m_nview.IsValid())
      return;
    if (this.m_nview.IsOwner())
    {
      this.UpdateAttack(fixedDeltaTime);
      this.UpdateEquipment(fixedDeltaTime);
      this.UpdateBlock(fixedDeltaTime);
    }
    this.UpdateUseVisual(fixedDeltaTime);
    base.CustomFixedUpdate(fixedDeltaTime);
  }

  public override bool InAttack()
  {
    return this.GetNextAnimHash() == Humanoid.s_animatorTagAttack || this.GetCurrentAnimHash() == Humanoid.s_animatorTagAttack;
  }

  public override bool StartAttack(Character target, bool secondaryAttack)
  {
    if (this.InAttack() && !this.HaveQueuedChain() || this.InDodge() || !this.CanMove() || this.IsKnockedBack() || this.IsStaggering() || this.InMinorAction())
      return false;
    ItemDrop.ItemData currentWeapon = this.GetCurrentWeapon();
    if (currentWeapon == null || secondaryAttack && !currentWeapon.HaveSecondaryAttack() || !secondaryAttack && !currentWeapon.HavePrimaryAttack())
      return false;
    if (this.m_currentAttack != null)
    {
      this.m_currentAttack.Stop();
      this.m_previousAttack = this.m_currentAttack;
      this.m_currentAttack = (Attack) null;
    }
    Attack attack1;
    Attack attack2;
    if (!secondaryAttack)
      attack2 = attack1 = currentWeapon.m_shared.m_attack.Clone();
    else
      attack1 = attack2 = currentWeapon.m_shared.m_secondaryAttack.Clone();
    Attack attack3 = attack2;
    if (!attack3.Start(this, this.m_body, this.m_zanim, this.m_animEvent, this.m_visEquipment, currentWeapon, this.m_previousAttack, this.m_timeSinceLastAttack, this.GetAttackDrawPercentage()))
      return false;
    this.ClearActionQueue();
    this.StartAttackGroundCheck();
    this.m_currentAttack = attack3;
    this.m_currentAttackIsSecondary = secondaryAttack;
    this.m_lastCombatTimer = 0.0f;
    return true;
  }

  private void StartAttackGroundCheck()
  {
    if (!this.IsPlayer())
      return;
    Collider lastGroundCollider = this.GetLastGroundCollider();
    if (!(bool) (UnityEngine.Object) lastGroundCollider)
      return;
    int layer = lastGroundCollider.gameObject.layer;
    int num = Character.s_groundRayMask | Character.s_characterLayerMask;
    if (num != (num | 1 << layer))
      return;
    this.m_lastGroundColliderOnAttackStart = layer;
  }

  private IEnumerator EndAttackGroundCheck()
  {
    Humanoid humanoid = this;
    if (humanoid.IsPlayer())
    {
      yield return (object) new WaitForSeconds(0.03f);
      Collider lastGroundCollider = humanoid.GetLastGroundCollider();
      if ((bool) (UnityEngine.Object) lastGroundCollider)
      {
        int layer = lastGroundCollider.gameObject.layer;
        bool flag1 = Character.s_characterLayerMask == (Character.s_characterLayerMask | 1 << layer);
        bool flag2 = Character.s_characterLayerMask == (Character.s_characterLayerMask | 1 << humanoid.m_lastGroundColliderOnAttackStart);
        if (humanoid.m_lastGroundColliderOnAttackStart != layer && !(flag1 & flag2) && Character.s_characterLayerMask == (Character.s_characterLayerMask | 1 << layer))
          humanoid.TimeoutGroundForce(2f);
      }
    }
  }

  public override float GetTimeSinceLastAttack() => this.m_timeSinceLastAttack;

  public float GetAttackDrawPercentage()
  {
    ItemDrop.ItemData currentWeapon = this.GetCurrentWeapon();
    if (currentWeapon == null || !currentWeapon.m_shared.m_attack.m_bowDraw || (double) this.m_attackDrawTime <= 0.0)
      return 0.0f;
    float skillFactor = this.GetSkillFactor(currentWeapon.m_shared.m_skillType);
    float num = Mathf.Lerp(currentWeapon.m_shared.m_attack.m_drawDurationMin, currentWeapon.m_shared.m_attack.m_drawDurationMin * 0.2f, skillFactor);
    return (double) num <= 0.0 ? 1f : Mathf.Clamp01(this.m_attackDrawTime / num);
  }

  private void UpdateEquipment(float dt)
  {
    if (!this.IsPlayer())
      return;
    if (this.IsSwimming() && !this.IsOnGround())
      this.HideHandItems();
    if (this.m_rightItem != null && this.m_rightItem.m_shared.m_useDurability)
      this.DrainEquipedItemDurability(this.m_rightItem, dt);
    if (this.m_leftItem != null && this.m_leftItem.m_shared.m_useDurability)
      this.DrainEquipedItemDurability(this.m_leftItem, dt);
    if (this.m_chestItem != null && this.m_chestItem.m_shared.m_useDurability)
      this.DrainEquipedItemDurability(this.m_chestItem, dt);
    if (this.m_legItem != null && this.m_legItem.m_shared.m_useDurability)
      this.DrainEquipedItemDurability(this.m_legItem, dt);
    if (this.m_helmetItem != null && this.m_helmetItem.m_shared.m_useDurability)
      this.DrainEquipedItemDurability(this.m_helmetItem, dt);
    if (this.m_shoulderItem != null && this.m_shoulderItem.m_shared.m_useDurability)
      this.DrainEquipedItemDurability(this.m_shoulderItem, dt);
    if (this.m_utilityItem != null && this.m_utilityItem.m_shared.m_useDurability)
      this.DrainEquipedItemDurability(this.m_utilityItem, dt);
    if (this.m_trinketItem == null || !this.m_trinketItem.m_shared.m_useDurability)
      return;
    this.DrainEquipedItemDurability(this.m_trinketItem, dt);
  }

  private void UpdateUseVisual(float dt)
  {
    if ((double) this.m_useItemTime <= 0.0)
      return;
    this.m_useItemTime -= dt;
    if ((double) this.m_useItemTime > 0.0)
      return;
    if (this.m_useItemVisual != null)
      this.m_useItemVisual.m_equipEffect.Create(this.m_visEquipment.m_rightHand.position, this.m_visEquipment.m_rightHand.rotation);
    this.m_visEquipment.SetRightItemVisual((string) null);
    this.m_useItemVisual = (ItemDrop.ItemData.SharedData) null;
    if (!this.m_hidHandsOnEat)
      return;
    this.ShowHandItems(true, false);
  }

  private void DrainEquipedItemDurability(ItemDrop.ItemData item, float dt)
  {
    item.m_durability -= item.m_shared.m_durabilityDrain * dt;
    if ((double) item.m_durability > 0.0)
      return;
    this.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$msg_broke", item.m_shared.m_name), icon: item.GetIcon());
    this.UnequipItem(item, false);
    if (!item.m_shared.m_destroyBroken)
      return;
    this.m_inventory.RemoveItem(item);
  }

  protected override void OnDamaged(HitData hit) => this.SetCrouch(false);

  public ItemDrop.ItemData GetCurrentWeapon()
  {
    if (this.m_rightItem != null && this.m_rightItem.IsWeapon())
      return this.m_rightItem;
    if (this.m_leftItem != null && this.m_leftItem.IsWeapon() && this.m_leftItem.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch)
      return this.m_leftItem;
    return (bool) (UnityEngine.Object) this.m_unarmedWeapon ? this.m_unarmedWeapon.m_itemData : (ItemDrop.ItemData) null;
  }

  private ItemDrop.ItemData GetCurrentBlocker()
  {
    return this.m_leftItem != null ? this.m_leftItem : this.GetCurrentWeapon();
  }

  private void UpdateAttack(float dt)
  {
    this.m_lastCombatTimer += dt;
    if (this.m_currentAttack != null && this.GetCurrentWeapon() != null)
      this.m_currentAttack.Update(dt);
    if (this.InAttack())
      this.m_timeSinceLastAttack = 0.0f;
    else
      this.m_timeSinceLastAttack += dt;
  }

  protected override float GetAttackSpeedFactorMovement()
  {
    return this.InAttack() && this.m_currentAttack != null && (this.IsFlying() || this.IsOnGround()) ? this.m_currentAttack.m_speedFactor : 1f;
  }

  protected override float GetAttackSpeedFactorRotation()
  {
    return this.InAttack() && this.m_currentAttack != null ? this.m_currentAttack.m_speedFactorRotation : 1f;
  }

  protected virtual bool HaveQueuedChain() => false;

  public override void OnWeaponTrailStart()
  {
    if (!this.m_nview.IsValid() || !this.m_nview.IsOwner() || this.m_currentAttack == null || this.GetCurrentWeapon() == null)
      return;
    this.m_currentAttack.OnTrailStart();
  }

  public override void OnAttackTrigger()
  {
    if (!this.m_nview.IsValid() || !this.m_nview.IsOwner() || this.m_currentAttack == null || this.GetCurrentWeapon() == null)
      return;
    this.StartCoroutine(this.EndAttackGroundCheck());
    this.m_currentAttack.OnAttackTrigger();
  }

  public override void OnStopMoving()
  {
    if (!this.m_nview.IsValid() || !this.m_nview.IsOwner() || this.m_currentAttack != null || !this.InAttack() || this.GetCurrentWeapon() == null)
      return;
    this.m_currentAttack.m_speedFactor = 0.0f;
    this.m_currentAttack.m_speedFactorRotation = 0.0f;
  }

  public virtual Vector3 GetAimDir(Vector3 fromPoint) => this.GetLookDir();

  public ItemDrop.ItemData PickupPrefab(GameObject prefab, int stackSize = 0, bool autoequip = true)
  {
    ZNetView.m_forceDisableInit = true;
    GameObject go = UnityEngine.Object.Instantiate<GameObject>(prefab);
    ZNetView.m_forceDisableInit = false;
    if (stackSize > 0)
    {
      ItemDrop component = go.GetComponent<ItemDrop>();
      component.m_itemData.m_stack = Mathf.Clamp(stackSize, 1, component.m_itemData.m_shared.m_maxStackSize);
    }
    if (this.Pickup(go, autoequip))
      return go.GetComponent<ItemDrop>().m_itemData;
    UnityEngine.Object.Destroy((UnityEngine.Object) go);
    return (ItemDrop.ItemData) null;
  }

  public virtual bool HaveUniqueKey(string name) => false;

  public virtual void AddUniqueKey(string name)
  {
  }

  public virtual bool RemoveUniqueKey(string name) => false;

  public bool Pickup(GameObject go, bool autoequip = true, bool autoPickupDelay = true)
  {
    if (this.IsTeleporting())
      return false;
    ItemDrop component = go.GetComponent<ItemDrop>();
    if ((UnityEngine.Object) component == (UnityEngine.Object) null)
      return false;
    component.Load();
    if (this.IsPlayer() && (component.m_itemData.m_shared.m_icons == null || component.m_itemData.m_shared.m_icons.Length == 0 || component.m_itemData.m_variant >= component.m_itemData.m_shared.m_icons.Length) || !component.CanPickup(autoPickupDelay) || this.m_inventory.ContainsItem(component.m_itemData))
      return false;
    if (component.m_itemData.m_shared.m_questItem && this.HaveUniqueKey(component.m_itemData.m_shared.m_name))
    {
      this.Message(MessageHud.MessageType.Center, "$msg_cantpickup");
      return false;
    }
    int stack = component.m_itemData.m_stack;
    bool flag = this.m_inventory.AddItem(component.m_itemData);
    if (this.m_nview.GetZDO() == null)
    {
      UnityEngine.Object.Destroy((UnityEngine.Object) go);
      return true;
    }
    if (!flag)
    {
      this.Message(MessageHud.MessageType.Center, "$msg_noroom");
      return false;
    }
    if (component.m_itemData.m_shared.m_questItem)
      this.AddUniqueKey(component.m_itemData.m_shared.m_name);
    ZNetScene.instance.Destroy(go);
    if (autoequip && flag && this.IsPlayer() && component.m_itemData.IsWeapon() && this.m_rightItem == null && this.m_hiddenRightItem == null && (this.m_leftItem == null || !this.m_leftItem.IsTwoHanded()) && (this.m_hiddenLeftItem == null || !this.m_hiddenLeftItem.IsTwoHanded()))
      this.EquipItem(component.m_itemData);
    this.m_pickupEffects.Create(this.transform.position, Quaternion.identity);
    if (this.IsPlayer())
    {
      this.ShowPickupMessage(component.m_itemData, stack);
      if ((UnityEngine.Object) Player.m_localPlayer == (UnityEngine.Object) (this as Player) && Hud.instance.m_radialMenu.Active)
        Hud.instance.m_radialMenu.OnAddItem(component.m_itemData);
    }
    return flag;
  }

  public void EquipBestWeapon(
    Character targetCreature,
    StaticTarget targetStatic,
    Character hurtFriend,
    Character friend)
  {
    List<ItemDrop.ItemData> allItems = this.m_inventory.GetAllItems();
    if (allItems.Count == 0 || this.InAttack())
      return;
    float num = 0.0f;
    if ((bool) (UnityEngine.Object) targetCreature)
    {
      float radius = targetCreature.GetRadius();
      num = Vector3.Distance(targetCreature.transform.position, this.transform.position) - radius;
    }
    else if ((bool) (UnityEngine.Object) targetStatic)
      num = Vector3.Distance(targetStatic.transform.position, this.transform.position);
    float time = Time.time;
    this.IsFlying();
    this.IsSwimming();
    Humanoid.optimalWeapons.Clear();
    Humanoid.outofRangeWeapons.Clear();
    Humanoid.allWeapons.Clear();
    foreach (ItemDrop.ItemData itemData in allItems)
    {
      if (itemData.IsWeapon() && this.m_baseAI.CanUseAttack(itemData))
      {
        if (itemData.m_shared.m_aiTargetType == ItemDrop.ItemData.AiTarget.Enemy)
        {
          if ((double) num >= (double) itemData.m_shared.m_aiAttackRangeMin)
          {
            Humanoid.allWeapons.Add(itemData);
            if ((!((UnityEngine.Object) targetCreature == (UnityEngine.Object) null) || !((UnityEngine.Object) targetStatic == (UnityEngine.Object) null)) && (double) time - (double) itemData.m_lastAttackTime >= (double) itemData.m_shared.m_aiAttackInterval)
            {
              if ((double) num > (double) itemData.m_shared.m_aiAttackRange)
              {
                Humanoid.outofRangeWeapons.Add(itemData);
              }
              else
              {
                if (itemData.m_shared.m_aiPrioritized)
                {
                  this.EquipItem(itemData);
                  return;
                }
                Humanoid.optimalWeapons.Add(itemData);
              }
            }
          }
        }
        else if (itemData.m_shared.m_aiTargetType == ItemDrop.ItemData.AiTarget.FriendHurt)
        {
          if (!((UnityEngine.Object) hurtFriend == (UnityEngine.Object) null) && (double) time - (double) itemData.m_lastAttackTime >= (double) itemData.m_shared.m_aiAttackInterval)
          {
            if (itemData.m_shared.m_aiPrioritized)
            {
              this.EquipItem(itemData);
              return;
            }
            Humanoid.optimalWeapons.Add(itemData);
          }
        }
        else if (itemData.m_shared.m_aiTargetType == ItemDrop.ItemData.AiTarget.Friend && !((UnityEngine.Object) friend == (UnityEngine.Object) null) && (double) time - (double) itemData.m_lastAttackTime >= (double) itemData.m_shared.m_aiAttackInterval)
        {
          if (itemData.m_shared.m_aiPrioritized)
          {
            this.EquipItem(itemData);
            return;
          }
          Humanoid.optimalWeapons.Add(itemData);
        }
      }
    }
    if (Humanoid.optimalWeapons.Count > 0)
    {
      foreach (ItemDrop.ItemData optimalWeapon in Humanoid.optimalWeapons)
      {
        if (optimalWeapon.m_shared.m_aiPrioritized)
        {
          this.EquipItem(optimalWeapon);
          return;
        }
      }
      this.EquipItem(Humanoid.optimalWeapons[UnityEngine.Random.Range(0, Humanoid.optimalWeapons.Count)]);
    }
    else if (Humanoid.outofRangeWeapons.Count > 0)
    {
      foreach (ItemDrop.ItemData outofRangeWeapon in Humanoid.outofRangeWeapons)
      {
        if (outofRangeWeapon.m_shared.m_aiPrioritized)
        {
          this.EquipItem(outofRangeWeapon);
          return;
        }
      }
      this.EquipItem(Humanoid.outofRangeWeapons[UnityEngine.Random.Range(0, Humanoid.outofRangeWeapons.Count)]);
    }
    else if (Humanoid.allWeapons.Count > 0)
    {
      foreach (ItemDrop.ItemData allWeapon in Humanoid.allWeapons)
      {
        if (allWeapon.m_shared.m_aiPrioritized)
        {
          this.EquipItem(allWeapon);
          return;
        }
      }
      this.EquipItem(Humanoid.allWeapons[UnityEngine.Random.Range(0, Humanoid.allWeapons.Count)]);
    }
    else
      this.UnequipItem(this.GetCurrentWeapon(), false);
  }

  public bool DropItem(Inventory inventory, ItemDrop.ItemData item, int amount)
  {
    if (inventory == null)
      inventory = this.m_inventory;
    if (amount == 0)
      return false;
    if (item.m_shared.m_questItem)
    {
      this.Message(MessageHud.MessageType.Center, "$msg_cantdrop");
      return false;
    }
    if (amount > item.m_stack)
      amount = item.m_stack;
    this.RemoveEquipAction(item);
    this.UnequipItem(item, false);
    if (this.m_hiddenLeftItem == item)
    {
      this.m_hiddenLeftItem = (ItemDrop.ItemData) null;
      this.SetupVisEquipment(this.m_visEquipment, false);
    }
    if (this.m_hiddenRightItem == item)
    {
      this.m_hiddenRightItem = (ItemDrop.ItemData) null;
      this.SetupVisEquipment(this.m_visEquipment, false);
    }
    if (amount == item.m_stack)
    {
      ZLog.Log((object) $"drop all {amount.ToString()}  {item.m_stack.ToString()}");
      if (!inventory.RemoveItem(item))
      {
        ZLog.Log((object) "Was not removed");
        return false;
      }
    }
    else
    {
      ZLog.Log((object) $"drop some {amount.ToString()}  {item.m_stack.ToString()}");
      inventory.RemoveItem(item, amount);
    }
    ItemDrop itemDrop = ItemDrop.DropItem(item, amount, this.transform.position + this.transform.forward + this.transform.up, this.transform.rotation);
    if (this.IsPlayer())
      itemDrop.OnPlayerDrop();
    float num = 5f;
    if ((double) item.GetWeight() >= 300.0)
      num = 0.5f;
    itemDrop.GetComponent<Rigidbody>().linearVelocity = (this.transform.forward + Vector3.up) * num;
    this.m_zanim.SetTrigger("interact");
    this.m_dropEffects.Create(this.transform.position, Quaternion.identity);
    this.Message(MessageHud.MessageType.TopLeft, "$msg_dropped " + itemDrop.m_itemData.m_shared.m_name, itemDrop.m_itemData.m_stack, itemDrop.m_itemData.GetIcon());
    return true;
  }

  protected virtual void SetPlaceMode(PieceTable buildPieces)
  {
  }

  public Inventory GetInventory() => this.m_inventory;

  public void UseIemBlockkMessage() => this.m_useItemBlockMessage = 1;

  public void UseItem(Inventory inventory, ItemDrop.ItemData item, bool fromInventoryGui)
  {
    if (inventory == null)
      inventory = this.m_inventory;
    if (!inventory.ContainsItem(item))
      return;
    GameObject hoverObject = this.GetHoverObject();
    Hoverable componentInParent1 = (bool) (UnityEngine.Object) hoverObject ? hoverObject.GetComponentInParent<Hoverable>() : (Hoverable) null;
    if (componentInParent1 != null && !fromInventoryGui)
    {
      Interactable componentInParent2 = hoverObject.GetComponentInParent<Interactable>();
      if (componentInParent2 != null && componentInParent2.UseItem(this, item))
      {
        this.DoInteractAnimation(hoverObject);
        return;
      }
    }
    if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
    {
      if (!this.ConsumeItem(inventory, item, true))
        return;
      this.m_consumeItemEffects.Create(Player.m_localPlayer.transform.position, Quaternion.identity);
      this.m_zanim.SetTrigger("eat");
      GameObject prefab;
      if (!ObjectDB.instance.TryGetItemPrefab(item.m_shared, out prefab))
        return;
      this.SetUseHandVisual(prefab, item.m_shared.m_foodEatAnimTime);
    }
    else
    {
      if (inventory == this.m_inventory && this.ToggleEquipped(item))
        return;
      if (!fromInventoryGui && this.m_useItemBlockMessage == 0)
      {
        if (componentInParent1 != null)
          this.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_cantuseon", item.m_shared.m_name, componentInParent1.GetHoverName()));
        else
          this.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_useonwhat", item.m_shared.m_name));
      }
      this.m_useItemBlockMessage = 0;
    }
  }

  public void TryUseItemOnInteractable(
    ItemDrop.ItemData item,
    GameObject hoverObject,
    bool fromInventoryGui)
  {
    Hoverable componentInParent1 = (bool) (UnityEngine.Object) hoverObject ? hoverObject.GetComponentInParent<Hoverable>() : (Hoverable) null;
    if (componentInParent1 == null | fromInventoryGui)
    {
      if (fromInventoryGui)
        return;
      this.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_useonwhat", item.m_shared.m_name));
    }
    else
    {
      Interactable componentInParent2 = hoverObject.GetComponentInParent<Interactable>();
      if (componentInParent2 != null && componentInParent2.UseItem(this, item))
        return;
      if (this.m_useItemBlockMessage == 0)
        this.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_cantuseon", item.m_shared.m_name, componentInParent1.GetHoverName()));
      this.m_useItemBlockMessage = 0;
    }
  }

  protected void DoInteractAnimation(GameObject obj)
  {
    Vector3 forward = (obj.transform.position - this.transform.position) with
    {
      y = 0.0f
    };
    forward.Normalize();
    this.transform.rotation = Quaternion.LookRotation(forward);
    Physics.SyncTransforms();
    ItemDrop component = obj.GetComponent<ItemDrop>();
    if (component != null && component.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && component.IsPiece())
    {
      this.SetUseHandVisual(obj, component.m_itemData.m_shared.m_foodEatAnimTime);
      this.m_zanim.SetTrigger("eat");
    }
    else
      this.m_zanim.SetTrigger("interact");
  }

  protected void SetUseHandVisual(GameObject obj, float seconds = 1f)
  {
    if (!(bool) (UnityEngine.Object) this.m_nview || !this.m_nview.IsValid() || !(bool) (UnityEngine.Object) this.m_visEquipment)
      return;
    this.m_hidHandsOnEat = this.HideHandItems(true, false);
    this.m_useItemVisual = obj.GetComponent<ItemDrop>()?.m_itemData.m_shared;
    this.m_useItemTime = seconds;
    this.m_visEquipment.SetRightItemVisual(Utils.GetPrefabName(obj.name));
  }

  protected virtual void ClearActionQueue()
  {
  }

  public virtual void RemoveEquipAction(ItemDrop.ItemData item)
  {
  }

  public virtual void ResetLoadedWeapon()
  {
  }

  public virtual bool IsWeaponLoaded() => false;

  protected virtual bool ToggleEquipped(ItemDrop.ItemData item)
  {
    if (!item.IsEquipable())
      return false;
    if (this.InAttack())
      return true;
    if (this.IsItemEquiped(item))
      this.UnequipItem(item);
    else
      this.EquipItem(item);
    return true;
  }

  public virtual bool CanConsumeItem(ItemDrop.ItemData item, bool checkWorldLevel = false)
  {
    if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable)
      return false;
    if (!checkWorldLevel || Game.m_worldLevel <= 0 || item.m_worldLevel >= Game.m_worldLevel)
      return true;
    this.Message(MessageHud.MessageType.Center, "$msg_ng_item_too_low");
    return false;
  }

  public virtual bool ConsumeItem(
    Inventory inventory,
    ItemDrop.ItemData item,
    bool checkWorldLevel = false)
  {
    this.CanConsumeItem(item, checkWorldLevel);
    return false;
  }

  public bool EquipItem(ItemDrop.ItemData item, bool triggerEquipEffects = true)
  {
    if (this.IsItemEquiped(item) || !this.m_inventory.ContainsItem(item) || this.InAttack() || this.InDodge() || this.IsPlayer() && !this.IsDead() && this.IsSwimming() && !this.IsOnGround() || item.m_shared.m_useDurability && (double) item.m_durability <= 0.0)
      return false;
    if (item.m_shared.m_dlc.Length > 0 && !DLCMan.instance.IsDLCInstalled(item.m_shared.m_dlc))
    {
      this.Message(MessageHud.MessageType.Center, "$msg_dlcrequired");
      return false;
    }
    if (Game.m_worldLevel > 0 && item.m_worldLevel < Game.m_worldLevel && (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket))
    {
      this.Message(MessageHud.MessageType.Center, "$msg_ng_item_too_low");
      return false;
    }
    if (Application.isEditor)
      item.m_shared = item.m_dropPrefab.GetComponent<ItemDrop>().m_itemData.m_shared;
    if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Tool)
    {
      this.UnequipItem(this.m_rightItem, triggerEquipEffects);
      this.UnequipItem(this.m_leftItem, triggerEquipEffects);
      this.m_rightItem = item;
      this.m_hiddenRightItem = (ItemDrop.ItemData) null;
      this.m_hiddenLeftItem = (ItemDrop.ItemData) null;
      if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
        item.m_shared.m_equipEffect.Create(this.m_visEquipment.m_rightHand.position, this.m_visEquipment.m_rightHand.rotation);
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch)
    {
      if (this.m_rightItem != null && this.m_leftItem == null && this.m_rightItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon)
      {
        this.m_leftItem = item;
        if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
          item.m_shared.m_equipEffect.Create(this.m_visEquipment.m_leftHand.position, this.m_visEquipment.m_leftHand.rotation);
      }
      else
      {
        this.UnequipItem(this.m_rightItem, triggerEquipEffects);
        if (this.m_leftItem != null && this.m_leftItem.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield)
          this.UnequipItem(this.m_leftItem, triggerEquipEffects);
        this.m_rightItem = item;
        if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
          item.m_shared.m_equipEffect.Create(this.m_visEquipment.m_rightHand.position, this.m_visEquipment.m_rightHand.rotation);
      }
      this.m_hiddenRightItem = (ItemDrop.ItemData) null;
      this.m_hiddenLeftItem = (ItemDrop.ItemData) null;
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon)
    {
      if (this.m_rightItem != null && this.m_rightItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch && this.m_leftItem == null)
      {
        ItemDrop.ItemData rightItem = this.m_rightItem;
        this.UnequipItem(this.m_rightItem, triggerEquipEffects);
        this.m_leftItem = rightItem;
        this.m_leftItem.m_equipped = true;
      }
      this.UnequipItem(this.m_rightItem, triggerEquipEffects);
      if (this.m_leftItem != null && this.m_leftItem.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield && this.m_leftItem.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch)
        this.UnequipItem(this.m_leftItem, triggerEquipEffects);
      this.m_rightItem = item;
      if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
        item.m_shared.m_equipEffect.Create(this.m_visEquipment.m_rightHand.position, this.m_visEquipment.m_rightHand.rotation);
      this.m_hiddenRightItem = (ItemDrop.ItemData) null;
      this.m_hiddenLeftItem = (ItemDrop.ItemData) null;
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield)
    {
      this.UnequipItem(this.m_leftItem, triggerEquipEffects);
      if (this.m_rightItem != null && this.m_rightItem.m_shared.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon && this.m_rightItem.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch)
        this.UnequipItem(this.m_rightItem, triggerEquipEffects);
      this.m_leftItem = item;
      if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
        item.m_shared.m_equipEffect.Create(this.m_visEquipment.m_leftHand.position, this.m_visEquipment.m_leftHand.rotation);
      this.m_hiddenRightItem = (ItemDrop.ItemData) null;
      this.m_hiddenLeftItem = (ItemDrop.ItemData) null;
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow)
    {
      this.UnequipItem(this.m_leftItem, triggerEquipEffects);
      this.UnequipItem(this.m_rightItem, triggerEquipEffects);
      this.m_leftItem = item;
      if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
        item.m_shared.m_equipEffect.Create(this.m_visEquipment.m_leftHand.position, this.m_visEquipment.m_leftHand.rotation);
      this.m_hiddenRightItem = (ItemDrop.ItemData) null;
      this.m_hiddenLeftItem = (ItemDrop.ItemData) null;
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon)
    {
      this.UnequipItem(this.m_leftItem, triggerEquipEffects);
      this.UnequipItem(this.m_rightItem, triggerEquipEffects);
      this.m_rightItem = item;
      if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
        item.m_shared.m_equipEffect.Create(this.m_visEquipment.m_rightHand.position, this.m_visEquipment.m_rightHand.rotation);
      this.m_hiddenRightItem = (ItemDrop.ItemData) null;
      this.m_hiddenLeftItem = (ItemDrop.ItemData) null;
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft)
    {
      this.UnequipItem(this.m_leftItem, triggerEquipEffects);
      this.UnequipItem(this.m_rightItem, triggerEquipEffects);
      this.m_leftItem = item;
      item.m_shared.m_equipEffect.Create(this.m_visEquipment.m_leftHand.position, this.m_visEquipment.m_leftHand.rotation);
      this.m_hiddenRightItem = (ItemDrop.ItemData) null;
      this.m_hiddenLeftItem = (ItemDrop.ItemData) null;
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest)
    {
      this.UnequipItem(this.m_chestItem, triggerEquipEffects);
      this.m_chestItem = item;
      if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
        item.m_shared.m_equipEffect.Create(this.transform.position + Vector3.up, this.transform.rotation);
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Legs)
    {
      this.UnequipItem(this.m_legItem, triggerEquipEffects);
      this.m_legItem = item;
      if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
        item.m_shared.m_equipEffect.Create(this.transform.position, this.transform.rotation);
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.AmmoNonEquipable)
    {
      this.UnequipItem(this.m_ammoItem, triggerEquipEffects);
      this.m_ammoItem = item;
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet)
    {
      this.UnequipItem(this.m_helmetItem, triggerEquipEffects);
      this.m_helmetItem = item;
      if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
        item.m_shared.m_equipEffect.Create(this.m_visEquipment.m_helmet.position, this.m_visEquipment.m_helmet.rotation);
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shoulder)
    {
      this.UnequipItem(this.m_shoulderItem, triggerEquipEffects);
      this.m_shoulderItem = item;
      if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
        item.m_shared.m_equipEffect.Create(this.transform.position + Vector3.up, this.transform.rotation);
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility)
    {
      this.UnequipItem(this.m_utilityItem, triggerEquipEffects);
      this.m_utilityItem = item;
      if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
        item.m_shared.m_equipEffect.Create(this.transform.position + Vector3.up, this.transform.rotation);
    }
    else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket)
    {
      this.UnequipItem(this.m_trinketItem, triggerEquipEffects);
      this.m_trinketItem = item;
      if ((bool) (UnityEngine.Object) this.m_visEquipment && this.m_visEquipment.m_isPlayer)
        item.m_shared.m_equipEffect.Create(this.transform.position + Vector3.up, this.transform.rotation);
    }
    if (this.IsItemEquiped(item))
      item.m_equipped = true;
    this.SetupEquipment();
    if (triggerEquipEffects)
      this.TriggerEquipEffect(item);
    return true;
  }

  public void UnequipItem(ItemDrop.ItemData item, bool triggerEquipEffects = true)
  {
    if (item == null)
      return;
    if (this.m_hiddenLeftItem == item)
    {
      this.m_hiddenLeftItem = (ItemDrop.ItemData) null;
      this.SetupVisEquipment(this.m_visEquipment, false);
    }
    if (this.m_hiddenRightItem == item)
    {
      this.m_hiddenRightItem = (ItemDrop.ItemData) null;
      this.SetupVisEquipment(this.m_visEquipment, false);
    }
    if (!this.IsItemEquiped(item))
      return;
    if (item.IsWeapon())
    {
      if (this.m_currentAttack != null && this.m_currentAttack.GetWeapon() == item)
      {
        this.m_currentAttack.Stop();
        this.m_previousAttack = this.m_currentAttack;
        this.m_currentAttack = (Attack) null;
      }
      if (!string.IsNullOrEmpty(item.m_shared.m_attack.m_drawAnimationState))
        this.m_zanim.SetBool(item.m_shared.m_attack.m_drawAnimationState, false);
      this.m_attackDrawTime = 0.0f;
      this.ResetLoadedWeapon();
    }
    if (this.m_rightItem == item)
      this.m_rightItem = (ItemDrop.ItemData) null;
    else if (this.m_leftItem == item)
      this.m_leftItem = (ItemDrop.ItemData) null;
    else if (this.m_chestItem == item)
      this.m_chestItem = (ItemDrop.ItemData) null;
    else if (this.m_legItem == item)
      this.m_legItem = (ItemDrop.ItemData) null;
    else if (this.m_ammoItem == item)
      this.m_ammoItem = (ItemDrop.ItemData) null;
    else if (this.m_helmetItem == item)
      this.m_helmetItem = (ItemDrop.ItemData) null;
    else if (this.m_shoulderItem == item)
      this.m_shoulderItem = (ItemDrop.ItemData) null;
    else if (this.m_utilityItem == item)
      this.m_utilityItem = (ItemDrop.ItemData) null;
    else if (this.m_trinketItem == item)
      this.m_trinketItem = (ItemDrop.ItemData) null;
    item.m_equipped = false;
    this.SetupEquipment();
    item.m_shared.m_unequipEffect.Create(this.transform.position, Quaternion.identity);
    if (!triggerEquipEffects)
      return;
    this.TriggerEquipEffect(item);
  }

  private void TriggerEquipEffect(ItemDrop.ItemData item)
  {
    if (this.m_nview.GetZDO() == null || MonoUpdaters.UpdateCount == this.m_lastEquipEffectFrame)
      return;
    this.m_lastEquipEffectFrame = MonoUpdaters.UpdateCount;
    this.m_equipEffects.Create(this.transform.position, Quaternion.identity);
  }

  public override bool IsAttached()
  {
    return this.m_currentAttack != null && this.InAttack() && this.m_currentAttack.IsAttached() && !this.m_currentAttack.IsDone() || base.IsAttached();
  }

  public override bool GetRelativePosition(
    out ZDOID parent,
    out string attachJoint,
    out Vector3 relativePos,
    out Quaternion relativeRot,
    out Vector3 relativeVel)
  {
    return this.m_currentAttack != null && this.InAttack() && this.m_currentAttack.IsAttached() && !this.m_currentAttack.IsDone() ? this.m_currentAttack.GetAttachData(out parent, out attachJoint, out relativePos, out relativeRot, out relativeVel) : base.GetRelativePosition(out parent, out attachJoint, out relativePos, out relativeRot, out relativeVel);
  }

  public void UnequipAllItems()
  {
    this.UnequipItem(this.m_rightItem, false);
    this.UnequipItem(this.m_leftItem, false);
    this.UnequipItem(this.m_chestItem, false);
    this.UnequipItem(this.m_legItem, false);
    this.UnequipItem(this.m_helmetItem, false);
    this.UnequipItem(this.m_ammoItem, false);
    this.UnequipItem(this.m_shoulderItem, false);
    this.UnequipItem(this.m_utilityItem, false);
    this.UnequipItem(this.m_trinketItem, false);
  }

  protected override void OnRagdollCreated(Ragdoll ragdoll)
  {
    VisEquipment component = ragdoll.GetComponent<VisEquipment>();
    if (!(bool) (UnityEngine.Object) component)
      return;
    this.SetupVisEquipment(component, true);
  }

  protected virtual void SetupVisEquipment(VisEquipment visEq, bool isRagdoll)
  {
    if (!isRagdoll)
    {
      visEq.SetLeftItem(this.m_leftItem != null ? this.m_leftItem.m_dropPrefab.name : "", this.m_leftItem != null ? this.m_leftItem.m_variant : 0);
      visEq.SetRightItem(this.m_rightItem != null ? this.m_rightItem.m_dropPrefab.name : "");
      if (this.IsPlayer())
      {
        visEq.SetLeftBackItem(this.m_hiddenLeftItem != null ? this.m_hiddenLeftItem.m_dropPrefab.name : "", this.m_hiddenLeftItem != null ? this.m_hiddenLeftItem.m_variant : 0);
        visEq.SetRightBackItem(this.m_hiddenRightItem != null ? this.m_hiddenRightItem.m_dropPrefab.name : "");
      }
    }
    visEq.SetChestItem(this.m_chestItem != null ? this.m_chestItem.m_dropPrefab.name : "");
    visEq.SetLegItem(this.m_legItem != null ? this.m_legItem.m_dropPrefab.name : "");
    visEq.SetHelmetItem(this.m_helmetItem != null ? this.m_helmetItem.m_dropPrefab.name : "");
    visEq.SetShoulderItem(this.m_shoulderItem != null ? this.m_shoulderItem.m_dropPrefab.name : "", this.m_shoulderItem != null ? this.m_shoulderItem.m_variant : 0);
    visEq.SetUtilityItem(this.m_utilityItem != null ? this.m_utilityItem.m_dropPrefab.name : "");
    visEq.SetTrinketItem(this.m_trinketItem != null ? this.m_trinketItem.m_dropPrefab.name : "");
    if (!this.IsPlayer())
      return;
    visEq.SetBeardItem(this.m_beardItem);
    visEq.SetHairItem(this.m_hairItem);
  }

  private void SetupEquipment()
  {
    if ((bool) (UnityEngine.Object) this.m_visEquipment && (this.m_nview.GetZDO() == null || this.m_nview.IsOwner()))
      this.SetupVisEquipment(this.m_visEquipment, false);
    if (this.m_nview.GetZDO() == null)
      return;
    this.UpdateEquipmentStatusEffects();
    if (this.m_rightItem != null && (bool) (UnityEngine.Object) this.m_rightItem.m_shared.m_buildPieces)
      this.SetPlaceMode(this.m_rightItem.m_shared.m_buildPieces);
    else
      this.SetPlaceMode((PieceTable) null);
    this.SetupAnimationState();
  }

  private void SetupAnimationState()
  {
    if (this.m_leftItem != null)
    {
      if (this.m_leftItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch)
        this.SetAnimationState(ItemDrop.ItemData.AnimationState.LeftTorch);
      else
        this.SetAnimationState(this.m_leftItem.m_shared.m_animationState);
    }
    else if (this.m_rightItem != null)
    {
      this.SetAnimationState(this.m_rightItem.m_shared.m_animationState);
    }
    else
    {
      if (!((UnityEngine.Object) this.m_unarmedWeapon != (UnityEngine.Object) null))
        return;
      this.SetAnimationState(this.m_unarmedWeapon.m_itemData.m_shared.m_animationState);
    }
  }

  private void SetAnimationState(ItemDrop.ItemData.AnimationState state)
  {
    this.m_zanim.SetFloat(Humanoid.s_statef, (float) state);
    this.m_zanim.SetInt(Humanoid.s_statei, (int) state);
  }

  public override bool IsSitting() => this.GetCurrentAnimHash() == Character.s_animatorTagSitting;

  private void UpdateEquipmentStatusEffects()
  {
    HashSet<StatusEffect> other = new HashSet<StatusEffect>();
    if (this.m_leftItem != null && (bool) (UnityEngine.Object) this.m_leftItem.m_shared.m_equipStatusEffect)
      other.Add(this.m_leftItem.m_shared.m_equipStatusEffect);
    if (this.m_rightItem != null && (bool) (UnityEngine.Object) this.m_rightItem.m_shared.m_equipStatusEffect)
      other.Add(this.m_rightItem.m_shared.m_equipStatusEffect);
    if (this.m_chestItem != null && (bool) (UnityEngine.Object) this.m_chestItem.m_shared.m_equipStatusEffect)
      other.Add(this.m_chestItem.m_shared.m_equipStatusEffect);
    if (this.m_legItem != null && (bool) (UnityEngine.Object) this.m_legItem.m_shared.m_equipStatusEffect)
      other.Add(this.m_legItem.m_shared.m_equipStatusEffect);
    if (this.m_helmetItem != null && (bool) (UnityEngine.Object) this.m_helmetItem.m_shared.m_equipStatusEffect)
      other.Add(this.m_helmetItem.m_shared.m_equipStatusEffect);
    if (this.m_shoulderItem != null && (bool) (UnityEngine.Object) this.m_shoulderItem.m_shared.m_equipStatusEffect)
      other.Add(this.m_shoulderItem.m_shared.m_equipStatusEffect);
    if (this.m_utilityItem != null && (bool) (UnityEngine.Object) this.m_utilityItem.m_shared.m_equipStatusEffect)
      other.Add(this.m_utilityItem.m_shared.m_equipStatusEffect);
    if (this.m_trinketItem != null && (bool) (UnityEngine.Object) this.m_trinketItem.m_shared.m_equipStatusEffect)
      other.Add(this.m_trinketItem.m_shared.m_equipStatusEffect);
    if (this.HaveSetEffect(this.m_leftItem))
      other.Add(this.m_leftItem.m_shared.m_setStatusEffect);
    if (this.HaveSetEffect(this.m_rightItem))
      other.Add(this.m_rightItem.m_shared.m_setStatusEffect);
    if (this.HaveSetEffect(this.m_chestItem))
      other.Add(this.m_chestItem.m_shared.m_setStatusEffect);
    if (this.HaveSetEffect(this.m_legItem))
      other.Add(this.m_legItem.m_shared.m_setStatusEffect);
    if (this.HaveSetEffect(this.m_helmetItem))
      other.Add(this.m_helmetItem.m_shared.m_setStatusEffect);
    if (this.HaveSetEffect(this.m_shoulderItem))
      other.Add(this.m_shoulderItem.m_shared.m_setStatusEffect);
    if (this.HaveSetEffect(this.m_utilityItem))
      other.Add(this.m_utilityItem.m_shared.m_setStatusEffect);
    foreach (StatusEffect equipmentStatusEffect in this.m_equipmentStatusEffects)
    {
      if (!other.Contains(equipmentStatusEffect))
        this.m_seman.RemoveStatusEffect(equipmentStatusEffect.NameHash());
    }
    foreach (StatusEffect statusEffect in other)
    {
      if (!this.m_equipmentStatusEffects.Contains(statusEffect))
        this.m_seman.AddStatusEffect(statusEffect);
    }
    this.m_equipmentStatusEffects.Clear();
    this.m_equipmentStatusEffects.UnionWith((IEnumerable<StatusEffect>) other);
  }

  private bool HaveSetEffect(ItemDrop.ItemData item)
  {
    return item != null && !((UnityEngine.Object) item.m_shared.m_setStatusEffect == (UnityEngine.Object) null) && item.m_shared.m_setName.Length != 0 && item.m_shared.m_setSize > 1 && this.GetSetCount(item.m_shared.m_setName) >= item.m_shared.m_setSize;
  }

  private int GetSetCount(string setName)
  {
    int setCount = 0;
    if (this.m_leftItem != null && this.m_leftItem.m_shared.m_setName == setName)
      ++setCount;
    if (this.m_rightItem != null && this.m_rightItem.m_shared.m_setName == setName)
      ++setCount;
    if (this.m_chestItem != null && this.m_chestItem.m_shared.m_setName == setName)
      ++setCount;
    if (this.m_legItem != null && this.m_legItem.m_shared.m_setName == setName)
      ++setCount;
    if (this.m_helmetItem != null && this.m_helmetItem.m_shared.m_setName == setName)
      ++setCount;
    if (this.m_shoulderItem != null && this.m_shoulderItem.m_shared.m_setName == setName)
      ++setCount;
    if (this.m_utilityItem != null && this.m_utilityItem.m_shared.m_setName == setName)
      ++setCount;
    if (this.m_trinketItem != null && this.m_trinketItem.m_shared.m_setName == setName)
      ++setCount;
    return setCount;
  }

  public void SetBeard(string name)
  {
    this.m_beardItem = name;
    this.SetupEquipment();
  }

  public string GetBeard() => this.m_beardItem;

  public void SetHair(string hair)
  {
    this.m_hairItem = hair;
    this.SetupEquipment();
  }

  public string GetHair() => this.m_hairItem;

  public bool IsItemEquiped(ItemDrop.ItemData item)
  {
    return this.m_rightItem == item || this.m_leftItem == item || this.m_chestItem == item || this.m_legItem == item || this.m_ammoItem == item || this.m_helmetItem == item || this.m_shoulderItem == item || this.m_utilityItem == item || this.m_trinketItem == item;
  }

  protected ItemDrop.ItemData GetRightItem() => this.m_rightItem;

  protected ItemDrop.ItemData GetLeftItem() => this.m_leftItem;

  protected override bool CheckRun(Vector3 moveDir, float dt)
  {
    return !this.IsDrawingBow() && base.CheckRun(moveDir, dt) && !this.IsBlocking();
  }

  public override bool IsDrawingBow()
  {
    if ((double) this.m_attackDrawTime <= 0.0)
      return false;
    ItemDrop.ItemData currentWeapon = this.GetCurrentWeapon();
    return currentWeapon != null && currentWeapon.m_shared.m_attack.m_bowDraw;
  }

  protected override bool BlockAttack(HitData hit, Character attacker)
  {
    if ((double) Vector3.Dot(hit.m_dir, this.transform.forward) > 0.0)
      return false;
    ItemDrop.ItemData currentBlocker = this.GetCurrentBlocker();
    if (currentBlocker == null)
      return false;
    bool flag1 = (double) currentBlocker.m_shared.m_timedBlockBonus > 1.0 && (double) this.m_blockTimer != -1.0 && (double) this.m_blockTimer < 0.25;
    float skillFactor = this.GetSkillFactor(Skills.SkillType.Blocking);
    float blockPower = currentBlocker.GetBlockPower(skillFactor);
    if (flag1)
    {
      blockPower *= currentBlocker.m_shared.m_timedBlockBonus;
      this.GetSEMan().ModifyTimedBlockBonus(ref blockPower);
    }
    if (currentBlocker.m_shared.m_damageModifiers.Count > 0)
    {
      HitData.DamageModifiers modifiers = new HitData.DamageModifiers();
      modifiers.Apply(currentBlocker.m_shared.m_damageModifiers);
      hit.ApplyResistance(modifiers, out HitData.DamageModifier _);
    }
    HitData.DamageTypes damageTypes = hit.m_damage.Clone();
    damageTypes.ApplyArmor(blockPower);
    float totalBlockableDamage1 = hit.GetTotalBlockableDamage();
    float totalBlockableDamage2 = damageTypes.GetTotalBlockableDamage();
    float num1 = totalBlockableDamage1 - totalBlockableDamage2;
    float num2 = Mathf.Clamp01(num1 / blockPower);
    float num3 = flag1 ? this.m_perfectBlockStaminaDrain : this.m_blockStaminaDrain * num2;
    float staminaUse1 = num3 + num3 * this.GetEquipmentBlockStaminaModifier();
    this.m_seman.ModifyBlockStaminaUsage(staminaUse1, ref staminaUse1, false);
    if ((double) staminaUse1 > 0.0)
      this.UseStamina(staminaUse1);
    else
      this.AddStamina(-staminaUse1);
    bool flag2 = this.AddStaggerDamage(damageTypes.GetTotalStaggerDamage(), hit.m_dir, (HitData) null);
    int num4 = this.HaveStamina() ? 1 : 0;
    bool flag3 = num4 != 0 && !flag2;
    if (num4 != 0 && !flag2)
    {
      hit.m_statusEffectHash = 0;
      hit.BlockDamage(num1);
      DamageText.instance.ShowText(DamageText.TextType.Blocked, hit.m_point + Vector3.up * 0.5f, num1);
    }
    if (currentBlocker.m_shared.m_useDurability)
    {
      float num5 = currentBlocker.m_shared.m_useDurabilityDrain * (totalBlockableDamage1 / blockPower);
      currentBlocker.m_durability -= num5;
    }
    this.RaiseSkill(Skills.SkillType.Blocking, flag1 ? 2f : 1f);
    currentBlocker.m_shared.m_blockEffect.Create(hit.m_point, Quaternion.identity);
    if (currentBlocker.m_shared.m_buildBlockCharges)
    {
      ++this.m_blockCharges;
      this.m_blockChargeRemoveTimer = 0.0f;
      currentBlocker.m_shared.m_blockChargeEffects.Create(this.m_visEquipment.m_leftHand.position, this.transform.rotation, variant: this.m_blockCharges);
      if (this.m_blockCharges >= currentBlocker.m_shared.m_maxBlockCharges)
      {
        currentBlocker.m_shared.m_attack.StartWithoutAnimation(this, this.m_body, this.m_visEquipment, currentBlocker);
        this.m_blockCharges = 0;
      }
    }
    if ((bool) (UnityEngine.Object) attacker && !flag1)
      this.AddAdrenaline(currentBlocker.m_shared.m_blockAdrenaline);
    if ((bool) (UnityEngine.Object) attacker & flag1 & flag3)
    {
      this.m_perfectBlockEffect.Create(hit.m_point, Quaternion.identity);
      this.AddAdrenaline(currentBlocker.m_shared.m_perfectBlockAdrenaline);
      if (attacker.m_staggerWhenBlocked)
        attacker.Stagger(-hit.m_dir);
      float blockStaminaRegen = currentBlocker.m_shared.m_perfectBlockStaminaRegen;
      if ((double) blockStaminaRegen > 0.0)
      {
        this.AddStamina(blockStaminaRegen);
      }
      else
      {
        float blockStaminaDrain = this.m_perfectBlockStaminaDrain;
        float staminaUse2 = blockStaminaDrain - blockStaminaDrain * this.GetEquipmentBlockStaminaModifier();
        this.m_seman.ModifyBlockStaminaUsage(staminaUse2, ref staminaUse2, false);
        if ((double) staminaUse2 > 0.0)
          this.UseStamina(staminaUse2);
        else
          this.AddStamina(-staminaUse2);
      }
      if ((bool) (UnityEngine.Object) currentBlocker.m_shared.m_perfectBlockStatusEffect)
        this.GetSEMan().AddStatusEffect(currentBlocker.m_shared.m_perfectBlockStatusEffect, true, currentBlocker.m_worldLevel, this.GetSkillLevel(Skills.SkillType.Blocking));
      else if ((bool) (UnityEngine.Object) this.m_perfectBlockStatusEffect)
        this.GetSEMan().AddStatusEffect(this.m_perfectBlockStatusEffect, true, currentBlocker.m_worldLevel, this.GetSkillLevel(Skills.SkillType.Blocking));
    }
    if (flag3)
    {
      hit.m_pushForce *= num2;
      if ((bool) (UnityEngine.Object) attacker && !hit.m_ranged)
      {
        float num6 = 1f - Mathf.Clamp01(num2 * 0.5f);
        HitData hit1 = new HitData()
        {
          m_pushForce = currentBlocker.GetDeflectionForce() * num6,
          m_dir = attacker.transform.position - this.transform.position
        };
        hit1.m_dir.y = 0.0f;
        hit1.m_dir.Normalize();
        attacker.Damage(hit1);
      }
    }
    return true;
  }

  public override bool IsBlocking()
  {
    if (this.m_nview.IsValid() && !this.m_nview.IsOwner())
      return this.m_nview.GetZDO().GetBool(ZDOVars.s_isBlockingHash);
    return this.m_blocking && !this.InAttack() && !this.InDodge() && !this.InPlaceMode() && !this.IsEncumbered() && !this.InMinorAction() && !this.IsStaggering();
  }

  private void UpdateBlock(float dt)
  {
    ItemDrop.ItemData currentBlocker = this.GetCurrentBlocker();
    float num = dt;
    if (this.IsBlocking())
    {
      num *= currentBlocker.m_shared.m_blockChargeBlockingDecayMult;
      if (!this.m_internalBlockingState)
      {
        this.m_internalBlockingState = true;
        this.m_nview.GetZDO().Set(ZDOVars.s_isBlockingHash, true);
        this.m_zanim.SetBool(Humanoid.s_blocking, true);
      }
      if ((double) this.m_blockTimer < 0.0)
        this.m_blockTimer = 0.0f;
      else
        this.m_blockTimer += dt;
    }
    else
    {
      this.m_blockChargeRemoveTimer += dt;
      if (this.m_internalBlockingState)
      {
        this.m_internalBlockingState = false;
        this.m_nview.GetZDO().Set(ZDOVars.s_isBlockingHash, false);
        this.m_zanim.SetBool(Humanoid.s_blocking, false);
      }
      this.m_blockTimer = -1f;
    }
    if (currentBlocker != null && currentBlocker.m_shared.m_buildBlockCharges)
    {
      this.m_blockChargeRemoveTimer += num;
      if ((double) this.m_blockChargeRemoveTimer < (double) currentBlocker.m_shared.m_blockChargeDecayTime)
        return;
      this.m_blockCharges = 0;
    }
    else
    {
      if (currentBlocker != null)
        return;
      this.m_blockCharges = 0;
    }
  }

  public bool HideHandItems(bool onlyRightHand = false, bool animation = true)
  {
    if (this.m_leftItem == null && this.m_rightItem == null)
      return false;
    if (!onlyRightHand)
    {
      ItemDrop.ItemData leftItem = this.m_leftItem;
      this.UnequipItem(this.m_leftItem);
      this.m_hiddenLeftItem = leftItem;
    }
    else
      this.m_hiddenLeftItem = (ItemDrop.ItemData) null;
    ItemDrop.ItemData rightItem = this.m_rightItem;
    this.UnequipItem(this.m_rightItem);
    this.m_hiddenRightItem = rightItem;
    this.SetupVisEquipment(this.m_visEquipment, false);
    if (animation)
      this.m_zanim.SetTrigger("equip_hip");
    return true;
  }

  protected void ShowHandItems(bool onlyRightHand = false, bool animation = true)
  {
    ItemDrop.ItemData hiddenLeftItem = this.m_hiddenLeftItem;
    ItemDrop.ItemData hiddenRightItem = this.m_hiddenRightItem;
    if (hiddenLeftItem == null && hiddenRightItem == null)
      return;
    if (!onlyRightHand)
    {
      this.m_hiddenLeftItem = (ItemDrop.ItemData) null;
      if (hiddenLeftItem != null)
        this.EquipItem(hiddenLeftItem);
    }
    this.m_hiddenRightItem = (ItemDrop.ItemData) null;
    if (hiddenRightItem != null)
      this.EquipItem(hiddenRightItem);
    if (!animation)
      return;
    this.m_zanim.SetTrigger("equip_hip");
  }

  public ItemDrop.ItemData GetAmmoItem() => this.m_ammoItem;

  public virtual GameObject GetHoverObject() => (GameObject) null;

  public bool IsTeleportable() => this.m_inventory.IsTeleportable();

  public override bool UseMeleeCamera()
  {
    ItemDrop.ItemData currentWeapon = this.GetCurrentWeapon();
    return currentWeapon != null && currentWeapon.m_shared.m_centerCamera;
  }

  public float GetEquipmentWeight()
  {
    float equipmentWeight = 0.0f;
    if (this.m_rightItem != null)
      equipmentWeight += this.m_rightItem.m_shared.m_weight;
    if (this.m_leftItem != null)
      equipmentWeight += this.m_leftItem.m_shared.m_weight;
    if (this.m_chestItem != null)
      equipmentWeight += this.m_chestItem.m_shared.m_weight;
    if (this.m_legItem != null)
      equipmentWeight += this.m_legItem.m_shared.m_weight;
    if (this.m_helmetItem != null)
      equipmentWeight += this.m_helmetItem.m_shared.m_weight;
    if (this.m_shoulderItem != null)
      equipmentWeight += this.m_shoulderItem.m_shared.m_weight;
    if (this.m_utilityItem != null)
      equipmentWeight += this.m_utilityItem.m_shared.m_weight;
    if (this.m_trinketItem != null)
      equipmentWeight += this.m_trinketItem.m_shared.m_weight;
    return equipmentWeight;
  }

  public ItemDrop.ItemData RightItem => this.m_rightItem;

  public ItemDrop.ItemData LeftItem => this.m_leftItem;

  [Serializable]
  public class ItemSet
  {
    public string m_name = "";
    public GameObject[] m_items = Array.Empty<GameObject>();
  }

  [Serializable]
  public class RandomItem
  {
    public GameObject m_prefab;
    [Range(0.0f, 1f)]
    public float m_chance = 0.5f;
  }
}
