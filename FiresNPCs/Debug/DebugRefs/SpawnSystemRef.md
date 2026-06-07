// Decompiled with JetBrains decompiler
// Type: SpawnSystem
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using System;
using System.Collections.Generic;
using UnityEngine;

#nullable disable
public class SpawnSystem : MonoBehaviour
{
  public static bool m_nospawn = false;
  private static List<SpawnSystem> m_instances = new List<SpawnSystem>();
  private const float m_spawnDistanceMin = 40f;
  private const float m_spawnDistanceMax = 80f;
  private const float m_levelupChance = 10f;
  public List<SpawnSystemList> m_spawnLists = new List<SpawnSystemList>();
  [HideInInspector]
  public List<Heightmap.Biome> m_biomeFolded = new List<Heightmap.Biome>();
  private static List<Player> m_tempNearPlayers = new List<Player>();
  private ZNetView m_nview;
  private Heightmap m_heightmap;
  private List<SE_Stats> m_pheromoneList = new List<SE_Stats>();

  private void Awake()
  {
    SpawnSystem.m_instances.Add(this);
    this.m_nview = this.GetComponent<ZNetView>();
    this.m_heightmap = Heightmap.FindHeightmap(this.transform.position);
    this.InvokeRepeating("UpdateSpawning", 10f, 1f);
  }

  private void OnDestroy() => SpawnSystem.m_instances.Remove(this);

  private void UpdateSpawning()
  {
    if (!this.m_nview.IsValid() || !this.m_nview.IsOwner() || (UnityEngine.Object) Player.m_localPlayer == (UnityEngine.Object) null)
      return;
    SpawnSystem.m_tempNearPlayers.Clear();
    this.GetPlayersInZone(SpawnSystem.m_tempNearPlayers);
    if (SpawnSystem.m_tempNearPlayers.Count == 0)
      return;
    DateTime time = ZNet.instance.GetTime();
    foreach (SpawnSystemList spawnList in this.m_spawnLists)
      this.UpdateSpawnList(spawnList.m_spawners, time, false);
    List<SpawnSystem.SpawnData> currentSpawners = RandEventSystem.instance.GetCurrentSpawners();
    if (currentSpawners == null)
      return;
    this.UpdateSpawnList(currentSpawners, time, true);
  }

  private void UpdateSpawnList(
    List<SpawnSystem.SpawnData> spawners,
    DateTime currentTime,
    bool eventSpawners)
  {
    string str = eventSpawners ? "e_" : "b_";
    this.m_pheromoneList.Clear();
    foreach (Character allPlayer in Player.GetAllPlayers())
    {
      foreach (StatusEffect statusEffect in allPlayer.GetSEMan().GetStatusEffects())
      {
        if (statusEffect is SE_Stats seStats && (UnityEngine.Object) seStats.m_pheromoneTarget != (UnityEngine.Object) null)
          this.m_pheromoneList.Add(seStats);
      }
    }
    int num1 = 0;
    foreach (SpawnSystem.SpawnData spawner in spawners)
    {
      ++num1;
      if (spawner.m_enabled && this.m_heightmap.HaveBiome(spawner.m_biome))
      {
        int stableHashCode = (str + spawner.m_prefab.name + num1.ToString()).GetStableHashCode();
        DateTime dateTime = new DateTime(this.m_nview.GetZDO().GetLong(stableHashCode));
        TimeSpan timeSpan = currentTime - dateTime;
        int num2 = Mathf.Min(spawner.m_maxSpawned == 0 ? 1 : spawner.m_maxSpawned, (int) (timeSpan.TotalSeconds / (double) spawner.m_spawnInterval));
        if (num2 > 0)
          this.m_nview.GetZDO().Set(stableHashCode, currentTime.Ticks);
        for (int index1 = 0; index1 < num2; ++index1)
        {
          Vector3 spawnCenter;
          if (this.FindBaseSpawnPoint(spawner, SpawnSystem.m_tempNearPlayers, out spawnCenter, out Player _))
          {
            int num3 = spawner.m_maxSpawned;
            float num4 = spawner.m_spawnChance;
            int minLevelOverride = -1;
            float levelUpMultiplier = 1f;
            foreach (SE_Stats pheromone in this.m_pheromoneList)
            {
              if ((UnityEngine.Object) pheromone.m_pheromoneTarget == (UnityEngine.Object) spawner.m_prefab && (UnityEngine.Object) pheromone.m_character != (UnityEngine.Object) null && (double) Vector3.Distance(spawnCenter, pheromone.m_character.transform.position) < 100.0)
              {
                if ((double) pheromone.m_pheromoneSpawnChanceOverride > 0.0)
                  num4 = pheromone.m_pheromoneSpawnChanceOverride;
                if (pheromone.m_pheromoneMaxInstanceOverride > 0)
                  num3 = pheromone.m_pheromoneMaxInstanceOverride;
                if (pheromone.m_pheromoneSpawnMinLevel > 0)
                  minLevelOverride = pheromone.m_pheromoneSpawnMinLevel;
                if ((double) pheromone.m_pheromoneLevelUpMultiplier != 1.0)
                  levelUpMultiplier *= pheromone.m_pheromoneLevelUpMultiplier;
              }
            }
            if ((double) UnityEngine.Random.Range(0.0f, 100f) <= (double) num4)
            {
              if ((string.IsNullOrEmpty(spawner.m_requiredGlobalKey) || ZoneSystem.instance.GetGlobalKey(spawner.m_requiredGlobalKey)) && (spawner.m_requiredEnvironments.Count <= 0 || EnvMan.instance.IsEnvironment(spawner.m_requiredEnvironments)) && (spawner.m_spawnAtDay || !EnvMan.IsDay()) && (spawner.m_spawnAtNight || !EnvMan.IsNight()))
              {
                int num5 = 0;
                if (num3 > 0)
                {
                  num5 = SpawnSystem.GetNrOfInstances(spawner.m_prefab, Vector3.zero, 0.0f, eventSpawners);
                  if (num5 >= num3)
                    break;
                }
                if ((double) spawner.m_spawnDistance <= 0.0 || !SpawnSystem.HaveInstanceInRange(spawner.m_prefab, spawnCenter, spawner.m_spawnDistance))
                {
                  int num6 = Mathf.Min(UnityEngine.Random.Range(spawner.m_groupSizeMin, spawner.m_groupSizeMax + 1), spawner.m_maxSpawned > 0 ? spawner.m_maxSpawned - num5 : 100);
                  float num7 = num6 > 1 ? spawner.m_groupRadius : 0.0f;
                  int num8 = 0;
                  for (int index2 = 0; index2 < num6 * 2; ++index2)
                  {
                    Vector2 insideUnitCircle = UnityEngine.Random.insideUnitCircle;
                    Vector3 spawnPoint = spawnCenter + new Vector3(insideUnitCircle.x, 0.0f, insideUnitCircle.y) * num7;
                    if (this.IsSpawnPointGood(spawner, ref spawnPoint))
                    {
                      this.Spawn(spawner, spawnPoint + Vector3.up * (spawner.m_groundOffset + UnityEngine.Random.Range(0.0f, spawner.m_groundOffsetRandom)), eventSpawners, minLevelOverride, levelUpMultiplier);
                      ++num8;
                      if (num8 >= num6)
                        break;
                    }
                  }
                  ZLog.Log((object) $"Spawned {spawner.m_prefab.name} x {num8.ToString()}");
                }
              }
              else
                break;
            }
          }
        }
      }
    }
  }

  private void Spawn(
    SpawnSystem.SpawnData critter,
    Vector3 spawnPoint,
    bool eventSpawner,
    int minLevelOverride = -1,
    float levelUpMultiplier = 1f)
  {
    if (SpawnSystem.m_nospawn)
      return;
    GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(critter.m_prefab, spawnPoint, Quaternion.identity);
    if (Terminal.m_showTests && Terminal.m_testList.ContainsKey("spawns"))
    {
      Terminal.Log((object) $"Spawning {critter.m_prefab.name} at {spawnPoint}");
      Chat.instance.SendPing(spawnPoint);
    }
    BaseAI component = gameObject.GetComponent<BaseAI>();
    if ((UnityEngine.Object) component != (UnityEngine.Object) null && critter.m_huntPlayer && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs))
      component.SetHuntPlayer(true);
    if ((double) critter.m_levelUpMinCenterDistance <= 0.0 || (double) spawnPoint.magnitude > (double) critter.m_levelUpMinCenterDistance)
    {
      int num = critter.m_minLevel;
      float levelUpChance = SpawnSystem.GetLevelUpChance(critter);
      if (minLevelOverride >= 0)
        num = minLevelOverride;
      if ((double) levelUpMultiplier != 1.0)
        levelUpChance *= levelUpMultiplier;
      while (num < critter.m_maxLevel && (double) UnityEngine.Random.Range(0.0f, 100f) <= (double) levelUpChance)
        ++num;
      if (num > 1)
      {
        gameObject.GetComponent<Character>()?.SetLevel(num);
        if (gameObject.GetComponent<Fish>() != null)
          gameObject.GetComponent<ItemDrop>()?.SetQuality(num);
      }
    }
    if (!(component is MonsterAI monsterAi))
      return;
    if (!critter.m_spawnAtDay)
      monsterAi.SetDespawnInDay(true);
    if (!eventSpawner)
      return;
    monsterAi.SetEventCreature(true);
  }

  private bool IsSpawnPointGood(SpawnSystem.SpawnData spawn, ref Vector3 spawnPoint)
  {
    Vector3 normal;
    Heightmap.Biome biome;
    Heightmap.BiomeArea biomeArea;
    Heightmap hmap;
    ZoneSystem.instance.GetGroundData(ref spawnPoint, out normal, out biome, out biomeArea, out hmap);
    if ((spawn.m_biome & biome) == Heightmap.Biome.None || (spawn.m_biomeArea & biomeArea) == (Heightmap.BiomeArea) 0 || ZoneSystem.instance.IsBlocked(spawnPoint))
      return false;
    float num1 = spawnPoint.y - 30f;
    if ((double) num1 < (double) spawn.m_minAltitude || (double) num1 > (double) spawn.m_maxAltitude)
      return false;
    float num2 = Mathf.Cos((float) Math.PI / 180f * spawn.m_maxTilt);
    float num3 = Mathf.Cos((float) Math.PI / 180f * spawn.m_minTilt);
    if ((double) normal.y < (double) num2 || (double) normal.y > (double) num3)
      return false;
    if ((double) spawn.m_minDistanceFromCenter > 0.0 || (double) spawn.m_maxDistanceFromCenter > 0.0)
    {
      float num4 = Utils.LengthXZ(spawnPoint);
      if ((double) spawn.m_minDistanceFromCenter > 0.0 && (double) num4 < (double) spawn.m_minDistanceFromCenter || (double) spawn.m_maxDistanceFromCenter > 0.0 && (double) num4 > (double) spawn.m_maxDistanceFromCenter)
        return false;
    }
    float range = (double) spawn.m_spawnRadiusMin > 0.0 ? spawn.m_spawnRadiusMin : 40f;
    if (!spawn.m_canSpawnCloseToPlayer && Player.IsPlayerInRange(spawnPoint, range) || !spawn.m_insidePlayerBase && (bool) (UnityEngine.Object) EffectArea.IsPointInsideArea(spawnPoint, EffectArea.Type.PlayerBase))
      return false;
    if (!spawn.m_inForest || !spawn.m_outsideForest)
    {
      bool flag = WorldGenerator.InForest(spawnPoint);
      if (!spawn.m_inForest & flag || !spawn.m_outsideForest && !flag)
        return false;
    }
    if ((!spawn.m_inLava || !spawn.m_outsideLava) && (!spawn.m_inLava && ZoneSystem.instance.IsLava(spawnPoint, true) || !spawn.m_outsideLava && !ZoneSystem.instance.IsLava(spawnPoint)))
      return false;
    if ((double) spawn.m_minOceanDepth != (double) spawn.m_maxOceanDepth && (UnityEngine.Object) hmap != (UnityEngine.Object) null)
    {
      float oceanDepth = hmap.GetOceanDepth(spawnPoint);
      if ((double) oceanDepth < (double) spawn.m_minOceanDepth || (double) oceanDepth > (double) spawn.m_maxOceanDepth)
        return false;
    }
    return true;
  }

  private bool FindBaseSpawnPoint(
    SpawnSystem.SpawnData spawn,
    List<Player> allPlayers,
    out Vector3 spawnCenter,
    out Player targetPlayer)
  {
    float minInclusive = (double) spawn.m_spawnRadiusMin > 0.0 ? spawn.m_spawnRadiusMin : 40f;
    float maxInclusive = (double) spawn.m_spawnRadiusMax > 0.0 ? spawn.m_spawnRadiusMax : 80f;
    for (int index = 0; index < 20; ++index)
    {
      Player allPlayer = allPlayers[UnityEngine.Random.Range(0, allPlayers.Count)];
      Vector3 vector3 = Quaternion.Euler(0.0f, (float) UnityEngine.Random.Range(0, 360), 0.0f) * Vector3.forward;
      Vector3 spawnPoint = allPlayer.transform.position + vector3 * UnityEngine.Random.Range(minInclusive, maxInclusive);
      if (this.IsSpawnPointGood(spawn, ref spawnPoint))
      {
        spawnCenter = spawnPoint;
        targetPlayer = allPlayer;
        return true;
      }
    }
    spawnCenter = Vector3.zero;
    targetPlayer = (Player) null;
    return false;
  }

  private int GetNrOfInstances(string prefabName)
  {
    List<Character> allCharacters = Character.GetAllCharacters();
    int nrOfInstances = 0;
    foreach (Character character in allCharacters)
    {
      if (character.gameObject.name.CustomStartsWith(prefabName) && this.InsideZone(character.transform.position))
        ++nrOfInstances;
    }
    return nrOfInstances;
  }

  private void GetPlayersInZone(List<Player> players)
  {
    foreach (Player allPlayer in Player.GetAllPlayers())
    {
      if (this.InsideZone(allPlayer.transform.position))
        players.Add(allPlayer);
    }
  }

  private void GetPlayersNearZone(List<Player> players, float marginDistance)
  {
    foreach (Player allPlayer in Player.GetAllPlayers())
    {
      if (this.InsideZone(allPlayer.transform.position, marginDistance))
        players.Add(allPlayer);
    }
  }

  private bool IsPlayerTooClose(List<Player> players, Vector3 point, float minDistance)
  {
    foreach (Component player in players)
    {
      if ((double) Vector3.Distance(player.transform.position, point) < (double) minDistance)
        return true;
    }
    return false;
  }

  private bool InPlayerRange(
    List<Player> players,
    Vector3 point,
    float minDistance,
    float maxDistance)
  {
    bool flag = false;
    foreach (Component player in players)
    {
      float num = Utils.DistanceXZ(player.transform.position, point);
      if ((double) num < (double) minDistance)
        return false;
      if ((double) num < (double) maxDistance)
        flag = true;
    }
    return flag;
  }

  private static bool HaveInstanceInRange(
    GameObject prefab,
    Vector3 centerPoint,
    float minDistance)
  {
    string name = prefab.name;
    if ((UnityEngine.Object) prefab.GetComponent<BaseAI>() != (UnityEngine.Object) null)
    {
      foreach (BaseAI baseAiInstance in BaseAI.BaseAIInstances)
      {
        if (baseAiInstance.gameObject.name.CustomStartsWith(name) && (double) Utils.DistanceXZ(baseAiInstance.transform.position, centerPoint) < (double) minDistance)
          return true;
      }
      return false;
    }
    foreach (GameObject gameObject in GameObject.FindGameObjectsWithTag("spawned"))
    {
      if (gameObject.gameObject.name.CustomStartsWith(name) && (double) Utils.DistanceXZ(gameObject.transform.position, centerPoint) < (double) minDistance)
        return true;
    }
    return false;
  }

  public static int GetNrOfInstances(GameObject prefab)
  {
    return SpawnSystem.GetNrOfInstances(prefab, Vector3.zero, 0.0f);
  }

  public static int GetNrOfInstances(
    GameObject prefab,
    Vector3 center,
    float maxRange,
    bool eventCreaturesOnly = false,
    bool procreationOnly = false)
  {
    string b = prefab.name + "(Clone)";
    if ((UnityEngine.Object) prefab.GetComponent<BaseAI>() != (UnityEngine.Object) null)
    {
      List<BaseAI> baseAiInstances = BaseAI.BaseAIInstances;
      int nrOfInstances = 0;
      foreach (BaseAI baseAi in baseAiInstances)
      {
        if (!(baseAi.gameObject.name != b) && ((double) maxRange <= 0.0 || (double) Vector3.Distance(center, baseAi.transform.position) <= (double) maxRange))
        {
          if (eventCreaturesOnly)
          {
            MonsterAI monsterAi = baseAi as MonsterAI;
            if ((bool) (UnityEngine.Object) monsterAi && !monsterAi.IsEventCreature())
              continue;
          }
          if (procreationOnly)
          {
            Procreation component = baseAi.GetComponent<Procreation>();
            if ((bool) (UnityEngine.Object) component && !component.ReadyForProcreation())
              continue;
          }
          ++nrOfInstances;
        }
      }
      return nrOfInstances;
    }
    GameObject[] gameObjectsWithTag = GameObject.FindGameObjectsWithTag("spawned");
    int nrOfInstances1 = 0;
    foreach (GameObject gameObject in gameObjectsWithTag)
    {
      if (gameObject.name.CustomStartsWith(b) && ((double) maxRange <= 0.0 || (double) Vector3.Distance(center, gameObject.transform.position) <= (double) maxRange))
        ++nrOfInstances1;
    }
    return nrOfInstances1;
  }

  private bool InsideZone(Vector3 point, float extra = 0.0f)
  {
    float num = 32f + extra;
    Vector3 position = this.transform.position;
    return (double) point.x >= (double) position.x - (double) num && (double) point.x <= (double) position.x + (double) num && (double) point.z >= (double) position.z - (double) num && (double) point.z <= (double) position.z + (double) num;
  }

  private bool HaveGlobalKeys(SpawnSystem.SpawnData ev)
  {
    return string.IsNullOrEmpty(ev.m_requiredGlobalKey) || ZoneSystem.instance.GetGlobalKey(ev.m_requiredGlobalKey);
  }

  public static float GetLevelUpChance(SpawnSystem.SpawnData creature)
  {
    return SpawnSystem.GetLevelUpChance((double) creature.m_overrideLevelupChance >= 0.0 ? creature.m_overrideLevelupChance : 0.0f);
  }

  public static float GetLevelUpChance(float levelUpChanceOverride = 0.0f)
  {
    float f = (double) levelUpChanceOverride > 0.0 ? levelUpChanceOverride : 10f;
    return Game.m_worldLevel > 0 && (double) Game.instance.m_worldLevelEnemyLevelUpExponent > 0.0 ? Mathf.Min(70f, Mathf.Pow(f, (float) Game.m_worldLevel * Game.instance.m_worldLevelEnemyLevelUpExponent)) : f * Game.m_enemyLevelUpRate;
  }

  [Serializable]
  public class SpawnData
  {
    public string m_name = "";
    public bool m_enabled = true;
    public bool m_devDisabled;
    public GameObject m_prefab;
    [BitMask(typeof (Heightmap.Biome))]
    public Heightmap.Biome m_biome;
    [BitMask(typeof (Heightmap.BiomeArea))]
    public Heightmap.BiomeArea m_biomeArea = Heightmap.BiomeArea.Everything;
    [Header("Total nr of instances (if near player is set, only instances within the max spawn radius is counted)")]
    public int m_maxSpawned = 1;
    [Header("How often do we spawn")]
    public float m_spawnInterval = 4f;
    [Header("Chanse to spawn each spawn interval")]
    [Range(0.0f, 100f)]
    public float m_spawnChance = 100f;
    [Header("Minimum distance to another instance")]
    public float m_spawnDistance = 10f;
    [Header("Spawn range ( 0 = use global setting )")]
    public float m_spawnRadiusMin;
    public float m_spawnRadiusMax;
    [Header("Only spawn if this key is set")]
    public string m_requiredGlobalKey = "";
    [Header("Only spawn if this environment is active")]
    public List<string> m_requiredEnvironments = new List<string>();
    [Header("Group spawning")]
    public int m_groupSizeMin = 1;
    public int m_groupSizeMax = 1;
    public float m_groupRadius = 3f;
    [Header("Time of day & Environment")]
    public bool m_spawnAtNight = true;
    public bool m_spawnAtDay = true;
    [Header("Altitude")]
    public float m_minAltitude = -1000f;
    public float m_maxAltitude = 1000f;
    [Header("Terrain tilt")]
    public float m_minTilt;
    public float m_maxTilt = 35f;
    [Header("Areas")]
    public bool m_inForest = true;
    public bool m_outsideForest = true;
    public bool m_inLava;
    public bool m_outsideLava = true;
    public bool m_canSpawnCloseToPlayer;
    public bool m_insidePlayerBase;
    [Header("Ocean depth ")]
    public float m_minOceanDepth;
    public float m_maxOceanDepth;
    [Header("States")]
    public bool m_huntPlayer;
    public float m_groundOffset = 0.5f;
    public float m_groundOffsetRandom;
    [Header("Distance from center")]
    public float m_minDistanceFromCenter;
    public float m_maxDistanceFromCenter;
    [Header("Level")]
    public int m_maxLevel = 1;
    public int m_minLevel = 1;
    public float m_levelUpMinCenterDistance;
    public float m_overrideLevelupChance = -1f;
    [HideInInspector]
    public bool m_foldout;

    public SpawnSystem.SpawnData Clone()
    {
      SpawnSystem.SpawnData spawnData = this.MemberwiseClone() as SpawnSystem.SpawnData;
      spawnData.m_requiredEnvironments = new List<string>((IEnumerable<string>) this.m_requiredEnvironments);
      return spawnData;
    }
  }
}



// Decompiled with JetBrains decompiler
// Type: SpawnSystemList
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using System.Collections.Generic;
using UnityEngine;

#nullable disable
public class SpawnSystemList : MonoBehaviour
{
  public List<SpawnSystem.SpawnData> m_spawners = new List<SpawnSystem.SpawnData>();
  [HideInInspector]
  public List<Heightmap.Biome> m_biomeFolded = new List<Heightmap.Biome>();

  public void GetSpawners(Heightmap.Biome biome, List<SpawnSystem.SpawnData> spawners)
  {
    foreach (SpawnSystem.SpawnData spawner in this.m_spawners)
    {
      if ((spawner.m_biome & biome) != Heightmap.Biome.None || spawner.m_biome == biome)
        spawners.Add(spawner);
    }
  }
}


// Decompiled with JetBrains decompiler
// Type: SpawnOnDamaged
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using System;
using UnityEngine;

#nullable disable
public class SpawnOnDamaged : MonoBehaviour
{
  public GameObject m_spawnOnDamage;

  private void Start()
  {
    WearNTear component1 = this.GetComponent<WearNTear>();
    if ((bool) (UnityEngine.Object) component1)
      component1.m_onDamaged += new Action(this.OnDamaged);
    Destructible component2 = this.GetComponent<Destructible>();
    if (!(bool) (UnityEngine.Object) component2)
      return;
    component2.m_onDamaged += new Action(this.OnDamaged);
  }

  private void OnDamaged()
  {
    if (!(bool) (UnityEngine.Object) this.m_spawnOnDamage)
      return;
    UnityEngine.Object.Instantiate<GameObject>(this.m_spawnOnDamage, this.transform.position, Quaternion.identity);
  }
}


// Decompiled with JetBrains decompiler
// Type: SpawnArea
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using System;
using System.Collections.Generic;
using UnityEngine;

#nullable disable
public class SpawnArea : MonoBehaviour
{
  private const float dt = 2f;
  public List<SpawnArea.SpawnData> m_prefabs = new List<SpawnArea.SpawnData>();
  public float m_levelupChance = 15f;
  public float m_spawnIntervalSec = 30f;
  public float m_triggerDistance = 256f;
  public bool m_setPatrolSpawnPoint = true;
  public float m_spawnRadius = 2f;
  public float m_nearRadius = 10f;
  public float m_farRadius = 1000f;
  public int m_maxNear = 3;
  public int m_maxTotal = 20;
  public bool m_onGroundOnly;
  public EffectList m_spawnEffects = new EffectList();
  private ZNetView m_nview;
  private float m_spawnTimer;

  private void Awake()
  {
    this.m_nview = this.GetComponent<ZNetView>();
    this.InvokeRepeating("UpdateSpawn", 2f, 2f);
  }

  private void UpdateSpawn()
  {
    if (!this.m_nview.IsOwner() || ZNetScene.instance.OutsideActiveArea(this.transform.position) || !Player.IsPlayerInRange(this.transform.position, this.m_triggerDistance))
      return;
    this.m_spawnTimer += 2f;
    if ((double) this.m_spawnTimer <= (double) this.m_spawnIntervalSec)
      return;
    this.m_spawnTimer = 0.0f;
    this.SpawnOne();
  }

  private bool SpawnOne()
  {
    if (SpawnSystem.m_nospawn)
      return false;
    int near;
    int total;
    this.GetInstances(out near, out total);
    if (near >= this.m_maxNear || total >= this.m_maxTotal)
      return false;
    SpawnArea.SpawnData spawnData = this.SelectWeightedPrefab();
    Vector3 point;
    if (spawnData == null || !this.FindSpawnPoint(spawnData.m_prefab, out point))
      return false;
    GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(spawnData.m_prefab, point, Quaternion.Euler(0.0f, (float) UnityEngine.Random.Range(0, 360), 0.0f));
    if (this.m_setPatrolSpawnPoint)
    {
      BaseAI component = gameObject.GetComponent<BaseAI>();
      if ((UnityEngine.Object) component != (UnityEngine.Object) null)
        component.SetPatrolPoint();
    }
    Character component1 = gameObject.GetComponent<Character>();
    if (spawnData.m_maxLevel > 1)
    {
      int minLevel = spawnData.m_minLevel;
      while (minLevel < spawnData.m_maxLevel && (double) UnityEngine.Random.Range(0.0f, 100f) <= (double) this.GetLevelUpChance())
        ++minLevel;
      if (minLevel > 1)
        component1.SetLevel(minLevel);
    }
    this.m_spawnEffects.Create(component1.GetCenterPoint(), Quaternion.identity);
    return true;
  }

  private bool FindSpawnPoint(GameObject prefab, out Vector3 point)
  {
    prefab.GetComponent<BaseAI>();
    for (int index = 0; index < 10; ++index)
    {
      Vector3 p = this.transform.position + Quaternion.Euler(0.0f, (float) UnityEngine.Random.Range(0, 360), 0.0f) * Vector3.forward * UnityEngine.Random.Range(0.0f, this.m_spawnRadius);
      float height;
      if (ZoneSystem.instance.FindFloor(p, out height) && (!this.m_onGroundOnly || !ZoneSystem.instance.IsBlocked(p)))
      {
        p.y = height + 0.1f;
        point = p;
        return true;
      }
    }
    point = Vector3.zero;
    return false;
  }

  private SpawnArea.SpawnData SelectWeightedPrefab()
  {
    if (this.m_prefabs.Count == 0)
      return (SpawnArea.SpawnData) null;
    float maxInclusive = 0.0f;
    foreach (SpawnArea.SpawnData prefab in this.m_prefabs)
      maxInclusive += prefab.m_weight;
    float num1 = UnityEngine.Random.Range(0.0f, maxInclusive);
    float num2 = 0.0f;
    foreach (SpawnArea.SpawnData prefab in this.m_prefabs)
    {
      num2 += prefab.m_weight;
      if ((double) num1 <= (double) num2)
        return prefab;
    }
    return this.m_prefabs[this.m_prefabs.Count - 1];
  }

  private void GetInstances(out int near, out int total)
  {
    near = 0;
    total = 0;
    Vector3 position = this.transform.position;
    foreach (BaseAI baseAiInstance in BaseAI.BaseAIInstances)
    {
      if (this.IsSpawnPrefab(baseAiInstance.gameObject))
      {
        double num = (double) Utils.DistanceXZ(baseAiInstance.transform.position, position);
        if (num < (double) this.m_nearRadius)
          ++near;
        if (num < (double) this.m_farRadius)
          ++total;
      }
    }
  }

  private bool IsSpawnPrefab(GameObject go)
  {
    string name = go.name;
    Character component = go.GetComponent<Character>();
    foreach (SpawnArea.SpawnData prefab in this.m_prefabs)
    {
      if (name.CustomStartsWith(prefab.m_prefab.name) && (!(bool) (UnityEngine.Object) component || !component.IsTamed()))
        return true;
    }
    return false;
  }

  public float GetLevelUpChance()
  {
    return Game.m_worldLevel > 0 && (double) Game.instance.m_worldLevelEnemyLevelUpExponent > 0.0 ? Mathf.Min(70f, Mathf.Pow(this.m_levelupChance, (float) Game.m_worldLevel * Game.instance.m_worldLevelEnemyLevelUpExponent)) : this.m_levelupChance * Game.m_enemyLevelUpRate;
  }

  private void OnDrawGizmosSelected()
  {
    Gizmos.color = Color.red;
    Gizmos.DrawWireSphere(this.transform.position, this.m_spawnRadius);
    Gizmos.color = Color.yellow;
    Gizmos.DrawWireSphere(this.transform.position, this.m_nearRadius);
  }

  [Serializable]
  public class SpawnData
  {
    public GameObject m_prefab;
    public float m_weight;
    [Header("Level")]
    public int m_maxLevel = 1;
    public int m_minLevel = 1;
  }
}


// Decompiled with JetBrains decompiler
// Type: SpawnAbility
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#nullable disable
public class SpawnAbility : MonoBehaviour, IProjectile
{
  [Header("Spawn")]
  public GameObject[] m_spawnPrefab;
  public string m_maxSummonReached = "$hud_maxsummonsreached";
  public bool m_spawnOnAwake;
  public bool m_alertSpawnedCreature = true;
  public bool m_passiveAggressive;
  public bool m_spawnAtTarget = true;
  public int m_minToSpawn = 1;
  public int m_maxToSpawn = 1;
  public int m_maxSpawned = 3;
  public float m_spawnRadius = 3f;
  public bool m_circleSpawn;
  public bool m_snapToTerrain = true;
  [Tooltip("Used to give random Y rotations to things like AOEs that aren't circular")]
  public bool m_randomYRotation;
  public float m_spawnGroundOffset;
  public int m_getSolidHeightMargin = 1000;
  public float m_initialSpawnDelay;
  public float m_spawnDelay;
  public float m_preSpawnDelay;
  public bool m_commandOnSpawn;
  public bool m_wakeUpAnimation;
  public Skills.SkillType m_copySkill;
  public float m_copySkillToRandomFactor;
  public bool m_setMaxInstancesFromWeaponLevel;
  public List<SpawnAbility.LevelUpSettings> m_levelUpSettings;
  public SpawnAbility.TargetType m_targetType;
  public Pathfinding.AgentType m_targetWhenPathfindingType = Pathfinding.AgentType.Humanoid;
  public float m_maxTargetRange = 40f;
  public EffectList m_spawnEffects = new EffectList();
  public EffectList m_preSpawnEffects = new EffectList();
  [Tooltip("Used for the troll summoning staff, to spawn an AOE that's friendly to the spawned creature.")]
  public GameObject m_aoePrefab;
  [Header("Projectile")]
  public float m_projectileVelocity = 10f;
  public float m_projectileVelocityMax;
  public float m_projectileAccuracy = 10f;
  public bool m_randomDirection;
  public float m_randomAngleMin;
  public float m_randomAngleMax;
  private Character m_owner;
  private ItemDrop.ItemData m_weapon;

  public void Awake()
  {
    if (!this.m_spawnOnAwake)
      return;
    this.StartCoroutine("Spawn");
  }

  public void Setup(
    Character owner,
    Vector3 velocity,
    float hitNoise,
    HitData hitData,
    ItemDrop.ItemData item,
    ItemDrop.ItemData ammo)
  {
    this.m_owner = owner;
    this.m_weapon = item;
    this.StartCoroutine("Spawn");
  }

  public string GetTooltipString(int itemQuality) => "";

  private IEnumerator Spawn()
  {
    SpawnAbility spawnAbility = this;
    if ((double) spawnAbility.m_initialSpawnDelay > 0.0)
      yield return (object) new WaitForSeconds(spawnAbility.m_initialSpawnDelay);
    int toSpawn = UnityEngine.Random.Range(spawnAbility.m_minToSpawn, spawnAbility.m_maxToSpawn);
    Skills skills = (bool) (UnityEngine.Object) spawnAbility.m_owner ? spawnAbility.m_owner.GetSkills() : (Skills) null;
    for (int i = 0; i < toSpawn; ++i)
    {
      Vector3 targetPosition = spawnAbility.transform.position;
      bool foundSpawnPoint = false;
      int tries = spawnAbility.m_targetType == SpawnAbility.TargetType.RandomPathfindablePosition ? 5 : 1;
      for (int k = 0; k < tries && !(foundSpawnPoint = spawnAbility.FindTarget(out targetPosition, i, toSpawn)); ++k)
      {
        if (spawnAbility.m_targetType == SpawnAbility.TargetType.RandomPathfindablePosition)
        {
          if (k == tries - 1)
          {
            Terminal.LogWarning((object) $"SpawnAbility failed to pathfindable target after {tries} tries, defaulting to transform position.");
            targetPosition = spawnAbility.transform.position;
            foundSpawnPoint = true;
          }
          else
          {
            Terminal.Log((object) "SpawnAbility failed to pathfindable target, waiting before retry.");
            yield return (object) new WaitForSeconds(0.2f);
          }
        }
      }
      if (!foundSpawnPoint)
      {
        Terminal.LogWarning((object) "SpawnAbility failed to find spawn point, aborting spawn.");
      }
      else
      {
        Vector3 spawnPoint = targetPosition;
        if (spawnAbility.m_targetType != SpawnAbility.TargetType.RandomPathfindablePosition)
        {
          Vector3 vector3 = spawnAbility.m_spawnAtTarget ? targetPosition : spawnAbility.transform.position;
          Vector2 vector2 = UnityEngine.Random.insideUnitCircle * spawnAbility.m_spawnRadius;
          if (spawnAbility.m_circleSpawn)
            vector2 = spawnAbility.GetCirclePoint(i, toSpawn) * spawnAbility.m_spawnRadius;
          spawnPoint = vector3 + new Vector3(vector2.x, 0.0f, vector2.y);
          if (spawnAbility.m_snapToTerrain)
          {
            float height;
            ZoneSystem.instance.GetSolidHeight(spawnPoint, out height, spawnAbility.m_getSolidHeightMargin);
            spawnPoint.y = height;
          }
          spawnPoint.y += spawnAbility.m_spawnGroundOffset;
          if ((double) Mathf.Abs(spawnPoint.y - vector3.y) > 100.0)
            continue;
        }
        GameObject prefab = spawnAbility.m_spawnPrefab[UnityEngine.Random.Range(0, spawnAbility.m_spawnPrefab.Length)];
        if (spawnAbility.m_maxSpawned > 0 && SpawnSystem.GetNrOfInstances(prefab) >= spawnAbility.m_maxSpawned)
        {
          if (spawnAbility.m_owner is Player owner1)
            owner1.Message(MessageHud.MessageType.Center, spawnAbility.m_maxSummonReached, 0, (Sprite) null);
        }
        else
        {
          spawnAbility.m_preSpawnEffects.Create(spawnPoint, Quaternion.identity);
          if ((double) spawnAbility.m_preSpawnDelay > 0.0)
            yield return (object) new WaitForSeconds(spawnAbility.m_preSpawnDelay);
          Terminal.Log((object) ("SpawnAbility spawning a " + prefab.name));
          GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(prefab, spawnPoint, Quaternion.Euler(0.0f, (float) ((double) UnityEngine.Random.value * 3.1415927410125732 * 2.0), 0.0f));
          ZNetView component1 = gameObject.GetComponent<ZNetView>();
          Projectile component2 = gameObject.GetComponent<Projectile>();
          if ((bool) (UnityEngine.Object) component2)
            spawnAbility.SetupProjectile(component2, targetPosition);
          if (spawnAbility.m_randomYRotation)
            gameObject.transform.Rotate(Vector3.up, (float) UnityEngine.Random.Range(-180, 180));
          if ((bool) (UnityEngine.Object) skills)
          {
            if (spawnAbility.m_copySkill != Skills.SkillType.None && (double) spawnAbility.m_copySkillToRandomFactor > 0.0)
              component1.GetZDO().Set(ZDOVars.s_randomSkillFactor, (float) (1.0 + (double) skills.GetSkillLevel(spawnAbility.m_copySkill) * (double) spawnAbility.m_copySkillToRandomFactor));
            if (spawnAbility.m_levelUpSettings.Count > 0)
            {
              Character component3 = gameObject.GetComponent<Character>();
              if (component3 != null)
              {
                for (int index = spawnAbility.m_levelUpSettings.Count - 1; index >= 0; --index)
                {
                  SpawnAbility.LevelUpSettings levelUpSetting = spawnAbility.m_levelUpSettings[index];
                  if ((double) skills.GetSkillLevel(levelUpSetting.m_skill) >= (double) levelUpSetting.m_skillLevel)
                  {
                    component3.SetLevel(levelUpSetting.m_setLevel);
                    int num = spawnAbility.m_setMaxInstancesFromWeaponLevel ? spawnAbility.m_weapon.m_quality : levelUpSetting.m_maxSpawns;
                    if (num > 0)
                    {
                      component1.GetZDO().Set(ZDOVars.s_maxInstances, num);
                      break;
                    }
                    break;
                  }
                }
              }
            }
          }
          if (spawnAbility.m_commandOnSpawn)
          {
            Tameable component4 = gameObject.GetComponent<Tameable>();
            if (component4 != null && spawnAbility.m_owner is Humanoid owner2)
            {
              component4.Command(owner2, false);
              if ((UnityEngine.Object) owner2 == (UnityEngine.Object) Player.m_localPlayer)
                Game.instance.IncrementPlayerStat(PlayerStatType.SkeletonSummons);
            }
          }
          if (spawnAbility.m_wakeUpAnimation)
            gameObject.GetComponent<ZSyncAnimation>()?.SetBool("wakeup", true);
          BaseAI component5 = gameObject.GetComponent<BaseAI>();
          if ((UnityEngine.Object) component5 != (UnityEngine.Object) null)
          {
            if (spawnAbility.m_alertSpawnedCreature)
              component5.Alert();
            BaseAI baseAi = spawnAbility.m_owner.GetBaseAI();
            if (component5.m_aggravatable && (bool) (UnityEngine.Object) baseAi && baseAi.m_aggravatable)
              component5.SetAggravated(baseAi.IsAggravated(), BaseAI.AggravatedReason.Damage);
            if (spawnAbility.m_passiveAggressive)
              component5.m_passiveAggresive = true;
          }
          spawnAbility.SetupAoe(gameObject.GetComponent<Character>(), spawnPoint);
          spawnAbility.m_spawnEffects.Create(spawnPoint, Quaternion.identity);
          if ((double) spawnAbility.m_spawnDelay > 0.0)
            yield return (object) new WaitForSeconds(spawnAbility.m_spawnDelay);
          targetPosition = new Vector3();
          spawnPoint = new Vector3();
          prefab = (GameObject) null;
        }
      }
    }
    if (!spawnAbility.m_spawnOnAwake)
      UnityEngine.Object.Destroy((UnityEngine.Object) spawnAbility.gameObject);
  }

  private Vector3 GetRandomConeDirection()
  {
    int angle = UnityEngine.Random.Range(0, 360);
    float f = UnityEngine.Random.Range(this.m_randomAngleMin, this.m_randomAngleMax);
    return Quaternion.AngleAxis((float) angle, Vector3.up) * new Vector3(Mathf.Sin(f), Mathf.Cos(f), 0.0f);
  }

  private void SetupProjectile(Projectile projectile, Vector3 targetPoint)
  {
    Vector3 lhs = this.m_randomDirection ? this.GetRandomConeDirection() : (targetPoint - projectile.transform.position).normalized;
    Vector3 axis = Vector3.Cross(lhs, Vector3.up);
    Vector3 vector3 = Quaternion.AngleAxis(UnityEngine.Random.Range(-this.m_projectileAccuracy, this.m_projectileAccuracy), Vector3.up) * (Quaternion.AngleAxis(UnityEngine.Random.Range(-this.m_projectileAccuracy, this.m_projectileAccuracy), axis) * lhs);
    float num = (double) this.m_projectileVelocityMax > 0.0 ? UnityEngine.Random.Range(this.m_projectileVelocity, this.m_projectileVelocityMax) : this.m_projectileVelocity;
    projectile.Setup(this.m_owner, vector3 * num, -1f, (HitData) null, (ItemDrop.ItemData) null, (ItemDrop.ItemData) null);
  }

  private void SetupAoe(Character owner, Vector3 targetPoint)
  {
    if ((UnityEngine.Object) this.m_aoePrefab == (UnityEngine.Object) null || (UnityEngine.Object) owner == (UnityEngine.Object) null)
      return;
    Aoe component = UnityEngine.Object.Instantiate<GameObject>(this.m_aoePrefab, targetPoint, Quaternion.identity).GetComponent<Aoe>();
    if ((UnityEngine.Object) component == (UnityEngine.Object) null)
      return;
    component.Setup(owner, Vector3.zero, -1f, (HitData) null, (ItemDrop.ItemData) null, (ItemDrop.ItemData) null);
  }

  private bool FindTarget(out Vector3 point, int i, int spawnCount)
  {
    point = Vector3.zero;
    switch (this.m_targetType)
    {
      case SpawnAbility.TargetType.ClosestEnemy:
        if ((UnityEngine.Object) this.m_owner == (UnityEngine.Object) null)
          return false;
        Character closestEnemy = BaseAI.FindClosestEnemy(this.m_owner, this.transform.position, this.m_maxTargetRange);
        if (!((UnityEngine.Object) closestEnemy != (UnityEngine.Object) null))
          return false;
        point = closestEnemy.transform.position;
        return true;
      case SpawnAbility.TargetType.RandomEnemy:
        if ((UnityEngine.Object) this.m_owner == (UnityEngine.Object) null)
          return false;
        Character randomEnemy = BaseAI.FindRandomEnemy(this.m_owner, this.transform.position, this.m_maxTargetRange);
        if (!((UnityEngine.Object) randomEnemy != (UnityEngine.Object) null))
          return false;
        point = randomEnemy.transform.position;
        return true;
      case SpawnAbility.TargetType.Caster:
        if ((UnityEngine.Object) this.m_owner == (UnityEngine.Object) null)
          return false;
        point = this.m_owner.transform.position;
        return true;
      case SpawnAbility.TargetType.Position:
        point = this.transform.position;
        return true;
      case SpawnAbility.TargetType.RandomPathfindablePosition:
        if ((UnityEngine.Object) this.m_owner == (UnityEngine.Object) null)
          return false;
        List<Vector3> path = new List<Vector3>();
        Vector2 vector2 = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(this.m_spawnRadius / 2f, this.m_spawnRadius);
        point = this.transform.position + new Vector3(vector2.x, 2f, vector2.y);
        float height;
        ZoneSystem.instance.GetSolidHeight(point, out height, 2);
        point.y = height;
        if (!Pathfinding.instance.GetPath(this.m_owner.transform.position, point, path, this.m_targetWhenPathfindingType, true, false, true))
          return false;
        Terminal.Log((object) $"SpawnAbility found path target, distance: {Vector3.Distance(this.transform.position, path[0])}");
        point = path[path.Count - 1];
        return true;
      default:
        return false;
    }
  }

  private Vector2 GetCirclePoint(int i, int spawnCount)
  {
    double num = (double) i / (double) spawnCount;
    return new Vector2(Mathf.Sin((float) (num * 3.1415927410125732 * 2.0)), Mathf.Cos((float) (num * 3.1415927410125732 * 2.0)));
  }

  public enum TargetType
  {
    ClosestEnemy,
    RandomEnemy,
    Caster,
    Position,
    RandomPathfindablePosition,
  }

  [Serializable]
  public class LevelUpSettings
  {
    public Skills.SkillType m_skill;
    public int m_skillLevel;
    public int m_setLevel;
    public int m_maxSpawns;
  }
}


// Decompiled with JetBrains decompiler
// Type: SoftReferencePrefabSpawner
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using SoftReferenceableAssets;
using UnityEngine;

#nullable disable
public class SoftReferencePrefabSpawner : MonoBehaviour
{
  [SerializeField]
  private SoftReference<GameObject> m_prefab;
  [SerializeField]
  private bool m_spawnAsynchronously;
  private bool m_currentlyLoading;

  private void Awake()
  {
    if (this.m_spawnAsynchronously)
    {
      this.m_currentlyLoading = true;
      this.m_prefab.LoadAsync((LoadedHandler) ((assetID, result) =>
      {
        this.m_currentlyLoading = false;
        if (result == LoadResult.Succeeded)
          this.SpawnPrefab();
        this.m_prefab.Release();
        Object.Destroy((Object) this.gameObject);
      }));
    }
    else
    {
      this.SpawnPrefab();
      Object.Destroy((Object) this.gameObject);
    }
  }

  private void OnDestroy()
  {
    if (!this.m_currentlyLoading)
      return;
    int num = (int) this.m_prefab.Load();
    this.m_prefab.Release();
  }

  private void SpawnPrefab()
  {
    SoftReferenceableAssets.Utils.Instantiate(this.m_prefab, this.transform.parent).name = this.m_prefab.Name;
  }
}


