// Decompiled with JetBrains decompiler
// Type: ZoneSystem
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using SoftReferenceableAssets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

#nullable disable
public class ZoneSystem : MonoBehaviour
{
  private Dictionary<Vector3, string> tempIconList = new Dictionary<Vector3, string>();
  private List<float> s_tempVeg = new List<float>();
  private RaycastHit[] rayHits = new RaycastHit[200];
  private static readonly List<RaycastHit> s_rayHits = new List<RaycastHit>(64 /*0x40*/);
  private static readonly List<float> s_rayHitsHeight = new List<float>(64 /*0x40*/);
  private List<string> m_tempKeys = new List<string>();
  private static ZoneSystem m_instance;
  [HideInInspector]
  public List<Heightmap.Biome> m_biomeFolded = new List<Heightmap.Biome>();
  [HideInInspector]
  public List<Heightmap.Biome> m_vegetationFolded = new List<Heightmap.Biome>();
  [HideInInspector]
  public List<Heightmap.Biome> m_locationFolded = new List<Heightmap.Biome>();
  [NonSerialized]
  public bool m_drawLocations;
  [NonSerialized]
  public string m_drawLocationsFilter = "";
  [Tooltip("Zones to load around center sector")]
  public int m_activeArea = 1;
  public int m_activeDistantArea = 1;
  [Tooltip("Zone size, should match netscene sector size")]
  public float m_zoneSize = 64f;
  [Tooltip("Time before destroying inactive zone")]
  public float m_zoneTTL = 4f;
  [Tooltip("Time before spawning active zone")]
  public float m_zoneTTS = 4f;
  public GameObject m_zonePrefab;
  public GameObject m_zoneCtrlPrefab;
  public GameObject m_locationProxyPrefab;
  public float m_waterLevel = 30f;
  public const float c_WaterLevel = 30f;
  public const float c_ZoneSize = 64f;
  public const double c_ZoneSizeDouble = 64.0;
  public const float c_ZoneHalfSize = 32f;
  public const double c_ZoneHalfSizeDouble = 32.0;
  [Header("Versions")]
  public int m_pgwVersion = 53;
  public int m_locationVersion = 1;
  [Header("Generation data")]
  public List<string> m_locationScenes = new List<string>();
  public List<GameObject> m_locationLists = new List<GameObject>();
  public List<ZoneSystem.ZoneVegetation> m_vegetation = new List<ZoneSystem.ZoneVegetation>();
  public List<ZoneSystem.ZoneLocation> m_locations = new List<ZoneSystem.ZoneLocation>();
  private Dictionary<int, ZoneSystem.ZoneLocation> m_locationsByHash = new Dictionary<int, ZoneSystem.ZoneLocation>();
  private bool m_error;
  public bool m_didZoneTest;
  private int m_terrainRayMask;
  private int m_blockRayMask;
  private int m_solidRayMask;
  private int m_staticSolidRayMask;
  private float m_updateTimer;
  private float m_startTime;
  private float m_lastFixedTime;
  private Dictionary<Vector2i, ZoneSystem.ZoneData> m_zones = new Dictionary<Vector2i, ZoneSystem.ZoneData>();
  private HashSet<Vector2i> m_generatedZones = new HashSet<Vector2i>();
  private Dictionary<Vector2i, List<ZDO>> m_loadingObjectsInZones = new Dictionary<Vector2i, List<ZDO>>();
  private Coroutine m_generateLocationsCoroutine;
  private DateTime m_estimatedGenerateLocationsCompletionTime;
  private float m_timeSlicedGenerationTimeBudget = 0.01f;
  private bool m_locationsGenerated;
  private float m_generateLocationsProgress;
  [HideInInspector]
  public Dictionary<Vector2i, ZoneSystem.LocationInstance> m_locationInstances = new Dictionary<Vector2i, ZoneSystem.LocationInstance>();
  private Dictionary<Vector3, string> m_locationIcons = new Dictionary<Vector3, string>();
  private HashSet<string> m_globalKeys = new HashSet<string>();
  public HashSet<GlobalKeys> m_globalKeysEnums = new HashSet<GlobalKeys>();
  public Dictionary<string, string> m_globalKeysValues = new Dictionary<string, string>();
  private HashSet<Vector2i> m_tempGeneratedZonesSaveClone;
  private HashSet<string> m_tempGlobalKeysSaveClone;
  private List<ZoneSystem.LocationInstance> m_tempLocationsSaveClone;
  private bool m_tempLocationsGeneratedSaveClone;
  private List<ZoneSystem.ClearArea> m_tempClearAreas = new List<ZoneSystem.ClearArea>();
  private List<GameObject> m_tempSpawnedObjects = new List<GameObject>();
  private List<int> m_tempLocationPrefabsToRelease = new List<int>();
  private List<ZoneSystem.LocationPrefabLoadData> m_locationPrefabs = new List<ZoneSystem.LocationPrefabLoadData>();
  private Action m_generateLocationsCompleted;

  public static ZoneSystem instance => ZoneSystem.m_instance;

  private ZoneSystem()
  {
  }

  private void Awake()
  {
    ZoneSystem.m_instance = this;
    this.m_terrainRayMask = LayerMask.GetMask("terrain");
    this.m_blockRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
    this.m_solidRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
    this.m_staticSolidRayMask = LayerMask.GetMask("static_solid", "terrain");
    foreach (GameObject locationList in this.m_locationLists)
      UnityEngine.Object.Instantiate<GameObject>(locationList);
    ZLog.Log((object) ("Zonesystem Awake " + Time.frameCount.ToString()));
  }

  private void Start()
  {
    ZLog.Log((object) ("Zonesystem Start " + Time.frameCount.ToString()));
    this.UpdateWorldRates();
    this.SetupLocations();
    this.ValidateVegetation();
    ZRoutedRpc.instance.m_onNewPeer += new Action<long>(this.OnNewPeer);
    if (ZNet.instance.IsServer())
    {
      ZRoutedRpc.instance.Register<string>("SetGlobalKey", new Action<long, string>(this.RPC_SetGlobalKey));
      ZRoutedRpc.instance.Register<string>("RemoveGlobalKey", new Action<long, string>(this.RPC_RemoveGlobalKey));
    }
    else
    {
      ZRoutedRpc.instance.Register<List<string>>("GlobalKeys", new Action<long, List<string>>(this.RPC_GlobalKeys));
      ZRoutedRpc.instance.Register<ZPackage>("LocationIcons", new Action<long, ZPackage>(this.RPC_LocationIcons));
    }
    this.m_startTime = this.m_lastFixedTime = Time.fixedTime;
  }

  public void GenerateLocationsIfNeeded()
  {
    if (this.LocationsGenerated)
      return;
    this.GenerateLocations();
  }

  private void SendGlobalKeys(long peer)
  {
    List<string> stringList = new List<string>((IEnumerable<string>) this.m_globalKeys);
    ZRoutedRpc.instance.InvokeRoutedRPC(peer, "GlobalKeys", (object) stringList);
    Player.m_localPlayer?.UpdateEvents();
  }

  private void RPC_GlobalKeys(long sender, List<string> keys)
  {
    ZLog.Log((object) ("client got keys " + keys.Count.ToString()));
    this.ClearGlobalKeys();
    foreach (string key in keys)
      this.GlobalKeyAdd(key);
  }

  private void GlobalKeyAdd(string keyStr, bool canSaveToServerOptionKeys = true)
  {
    string str1;
    GlobalKeys gk;
    string keyValue = ZoneSystem.GetKeyValue(keyStr.ToLower(), out str1, out gk);
    bool flag = canSaveToServerOptionKeys && ZNet.World != null && gk < GlobalKeys.NonServerOption;
    string str2;
    if (this.m_globalKeysValues.TryGetValue(keyValue, out str2))
    {
      string str3 = $"{keyValue} {str2}".TrimEnd();
      this.m_globalKeys.Remove(str3);
      if (flag)
        ZNet.World.m_startingGlobalKeys.Remove(str3);
    }
    string key = $"{keyValue} {str1}".TrimEnd();
    this.m_globalKeys.Add(key);
    this.m_globalKeysValues[keyValue] = str1;
    if (gk != GlobalKeys.NonServerOption)
      this.m_globalKeysEnums.Add(gk);
    Game.instance.GetPlayerProfile().m_knownWorldKeys.IncrementOrSet<string>(key);
    if (flag)
      ZNet.World.m_startingGlobalKeys.Add(keyStr.ToLower());
    this.UpdateWorldRates();
  }

  private bool GlobalKeyRemove(string keyStr, bool canSaveToServerOptionKeys = true)
  {
    GlobalKeys gk;
    string keyValue = ZoneSystem.GetKeyValue(keyStr, out string _, out gk);
    string str1;
    if (!this.m_globalKeysValues.TryGetValue(keyValue, out str1))
      return false;
    string str2 = $"{keyValue} {str1}".TrimEnd();
    if (canSaveToServerOptionKeys && ZNet.World != null && gk < GlobalKeys.NonServerOption)
      ZNet.World.m_startingGlobalKeys.Remove(str2);
    this.m_globalKeys.Remove(str2);
    this.m_globalKeysValues.Remove(keyValue);
    if (gk != GlobalKeys.NonServerOption)
      this.m_globalKeysEnums.Remove(gk);
    this.UpdateWorldRates();
    return true;
  }

  public void UpdateWorldRates()
  {
    Game.UpdateWorldRates(this.m_globalKeys, this.m_globalKeysValues);
  }

  public void Reset()
  {
    this.ClearGlobalKeys();
    this.UpdateWorldRates();
  }

  private void ClearGlobalKeys()
  {
    this.m_globalKeys.Clear();
    this.m_globalKeysEnums.Clear();
    this.m_globalKeysValues.Clear();
  }

  public static string GetKeyValue(string key, out string value, out GlobalKeys gk)
  {
    int length = key.IndexOf(' ');
    value = "";
    string lower;
    if (length > 0)
    {
      value = key.Substring(length + 1);
      lower = key.Substring(0, length).ToLower();
    }
    else
      lower = key.ToLower();
    if (!Enum.TryParse<GlobalKeys>(lower, true, out gk))
      gk = GlobalKeys.NonServerOption;
    return lower;
  }

  private void SendLocationIcons(long peer)
  {
    ZPackage zpackage = new ZPackage();
    this.tempIconList.Clear();
    this.GetLocationIcons(this.tempIconList);
    zpackage.Write(this.tempIconList.Count);
    foreach (KeyValuePair<Vector3, string> tempIcon in this.tempIconList)
    {
      zpackage.Write(tempIcon.Key);
      zpackage.Write(tempIcon.Value);
    }
    ZRoutedRpc.instance.InvokeRoutedRPC(peer, "LocationIcons", (object) zpackage);
  }

  private void RPC_LocationIcons(long sender, ZPackage pkg)
  {
    ZLog.Log((object) "client got location icons");
    this.m_locationIcons.Clear();
    int num = pkg.ReadInt();
    for (int index = 0; index < num; ++index)
      this.m_locationIcons[pkg.ReadVector3()] = pkg.ReadString();
    ZLog.Log((object) ("Icons:" + num.ToString()));
  }

  private void OnNewPeer(long peerID)
  {
    if (!ZNet.instance.IsServer())
      return;
    ZLog.Log((object) "Server: New peer connected,sending global keys");
    this.SendGlobalKeys(peerID);
    this.SendLocationIcons(peerID);
  }

  private void SetupLocations()
  {
    List<LocationList> allLocationLists = LocationList.GetAllLocationLists();
    allLocationLists.Sort((Comparison<LocationList>) ((a, b) => a.m_sortOrder.CompareTo(b.m_sortOrder)));
    foreach (LocationList locationList in allLocationLists)
    {
      this.m_locations.AddRange((IEnumerable<ZoneSystem.ZoneLocation>) locationList.m_locations);
      this.m_vegetation.AddRange((IEnumerable<ZoneSystem.ZoneVegetation>) locationList.m_vegetation);
      foreach (EnvSetup environment in locationList.m_environments)
        EnvMan.instance.AppendEnvironment(environment);
      foreach (BiomeEnvSetup biomeEnvironment in locationList.m_biomeEnvironments)
        EnvMan.instance.AppendBiomeSetup(biomeEnvironment);
      ClutterSystem.instance.m_clutter.AddRange((IEnumerable<ClutterSystem.Clutter>) locationList.m_clutter);
      ZLog.Log((object) ($"Added {locationList.m_locations.Count} locations, {locationList.m_vegetation.Count} vegetations, {locationList.m_environments.Count} environments, {locationList.m_biomeEnvironments.Count} biome env-setups, {locationList.m_clutter.Count} clutter  from " + locationList.gameObject.scene.name));
      RandEventSystem.instance.m_events.AddRange((IEnumerable<RandomEvent>) locationList.m_events);
    }
    foreach (ZoneSystem.ZoneLocation location in this.m_locations)
    {
      if ((location.m_enable || location.m_prefab.IsValid) && Application.isPlaying)
      {
        location.m_prefabName = location.m_prefab.Name;
        int hash = location.Hash;
        if (!this.m_locationsByHash.ContainsKey(hash))
          this.m_locationsByHash.Add(hash, location);
      }
    }
    if (!Settings.AssetMemoryUsagePolicy.HasFlag((Enum) AssetMemoryUsagePolicy.KeepAsynchronousLoadedBit))
      return;
    ReferenceHolder referenceHolder = this.gameObject.AddComponent<ReferenceHolder>();
    foreach (ZoneSystem.ZoneLocation location in this.m_locations)
    {
      if (location.m_enable)
      {
        int num = (int) location.m_prefab.Load();
        referenceHolder.HoldReferenceTo((IReferenceCounted) location.m_prefab);
        location.m_prefab.Release();
      }
    }
  }

  public static void PrepareNetViews(GameObject root, List<ZNetView> views)
  {
    views.Clear();
    foreach (ZNetView componentsInChild in root.GetComponentsInChildren<ZNetView>(true))
    {
      if (Utils.IsEnabledInheirarcy(componentsInChild.gameObject, root))
        views.Add(componentsInChild);
    }
  }

  public static void PrepareRandomSpawns(GameObject root, List<RandomSpawn> randomSpawns)
  {
    randomSpawns.Clear();
    foreach (RandomSpawn componentsInChild in root.GetComponentsInChildren<RandomSpawn>(true))
    {
      if (Utils.IsEnabledInheirarcy(componentsInChild.gameObject, root))
      {
        randomSpawns.Add(componentsInChild);
        componentsInChild.Prepare();
      }
    }
  }

  private void OnDestroy()
  {
    this.ForceReleaseLoadedPrefabs();
    ZoneSystem.m_instance = (ZoneSystem) null;
  }

  private void ValidateVegetation()
  {
    foreach (ZoneSystem.ZoneVegetation zoneVegetation in this.m_vegetation)
    {
      if (zoneVegetation.m_enable && (bool) (UnityEngine.Object) zoneVegetation.m_prefab && (UnityEngine.Object) zoneVegetation.m_prefab.GetComponent<ZNetView>() == (UnityEngine.Object) null)
        ZLog.LogError((object) $"Vegetation {zoneVegetation.m_prefab.name} [ {zoneVegetation.m_name}] is missing ZNetView");
    }
  }

  public void PrepareSave()
  {
    this.m_tempGeneratedZonesSaveClone = new HashSet<Vector2i>((IEnumerable<Vector2i>) this.m_generatedZones);
    this.m_tempGlobalKeysSaveClone = new HashSet<string>((IEnumerable<string>) this.m_globalKeys);
    this.m_tempLocationsSaveClone = new List<ZoneSystem.LocationInstance>((IEnumerable<ZoneSystem.LocationInstance>) this.m_locationInstances.Values);
    this.m_tempLocationsGeneratedSaveClone = this.LocationsGenerated;
  }

  public void SaveASync(BinaryWriter writer)
  {
    writer.Write(this.m_tempGeneratedZonesSaveClone.Count);
    foreach (Vector2i vector2i in this.m_tempGeneratedZonesSaveClone)
    {
      writer.Write(vector2i.x);
      writer.Write(vector2i.y);
    }
    writer.Write(0);
    writer.Write(this.m_locationVersion);
    this.m_tempGlobalKeysSaveClone.RemoveWhere((Predicate<string>) (x =>
    {
      GlobalKeys gk;
      ZoneSystem.GetKeyValue(x, out string _, out gk);
      return gk < GlobalKeys.NonServerOption;
    }));
    writer.Write(this.m_tempGlobalKeysSaveClone.Count);
    foreach (string str in this.m_tempGlobalKeysSaveClone)
      writer.Write(str);
    writer.Write(this.m_tempLocationsGeneratedSaveClone);
    writer.Write(this.m_tempLocationsSaveClone.Count);
    foreach (ZoneSystem.LocationInstance locationInstance in this.m_tempLocationsSaveClone)
    {
      writer.Write(locationInstance.m_location.m_prefabName);
      writer.Write(locationInstance.m_position.x);
      writer.Write(locationInstance.m_position.y);
      writer.Write(locationInstance.m_position.z);
      writer.Write(locationInstance.m_placed);
    }
    this.m_tempGeneratedZonesSaveClone.Clear();
    this.m_tempGeneratedZonesSaveClone = (HashSet<Vector2i>) null;
    this.m_tempGlobalKeysSaveClone.Clear();
    this.m_tempGlobalKeysSaveClone = (HashSet<string>) null;
    this.m_tempLocationsSaveClone.Clear();
    this.m_tempLocationsSaveClone = (List<ZoneSystem.LocationInstance>) null;
  }

  public void Load(BinaryReader reader, int version)
  {
    this.m_generatedZones.Clear();
    int num1 = reader.ReadInt32();
    for (int index = 0; index < num1; ++index)
      this.m_generatedZones.Add(new Vector2i()
      {
        x = reader.ReadInt32(),
        y = reader.ReadInt32()
      });
    if (version < 13)
      return;
    reader.ReadInt32();
    int num2 = version >= 21 ? reader.ReadInt32() : 0;
    if (version >= 14)
    {
      this.ClearGlobalKeys();
      int num3 = reader.ReadInt32();
      for (int index = 0; index < num3; ++index)
        this.GlobalKeyAdd(reader.ReadString());
    }
    if (version < 18)
      return;
    if (version >= 20)
      this.LocationsGenerated = reader.ReadBoolean();
    this.m_locationInstances.Clear();
    int num4 = reader.ReadInt32();
    for (int index = 0; index < num4; ++index)
    {
      string name = reader.ReadString();
      Vector3 zero = Vector3.zero with
      {
        x = reader.ReadSingle(),
        y = reader.ReadSingle(),
        z = reader.ReadSingle()
      };
      bool generated = false;
      if (version >= 19)
        generated = reader.ReadBoolean();
      ZoneSystem.ZoneLocation location = this.GetLocation(name);
      if (location != null)
        this.RegisterLocation(location, zero, generated);
      else
        ZLog.DevLog((object) ("Failed to find location " + name));
    }
    ZLog.Log((object) $"Loaded {num4.ToString()} locations");
    if (num2 == this.m_locationVersion)
      return;
    this.LocationsGenerated = false;
  }

  private void Update()
  {
    this.m_lastFixedTime = Time.fixedTime;
    if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
      return;
    if (ZNet.instance.IsServer() && !this.LocationsGenerated)
    {
      if (TextViewer.IsShowingIntro())
        this.m_timeSlicedGenerationTimeBudget = ZoneSystem.GetGenerationTimeBudgetForTargetFrameRate(out float _);
      else
        this.m_timeSlicedGenerationTimeBudget = 0.1f;
    }
    else
    {
      if (Terminal.m_showTests)
        Terminal.m_testList["Time"] = $"{Time.fixedTime.ToString("0.00")} / {this.TimeSinceStart().ToString("0.00")}";
      this.m_updateTimer += Time.deltaTime;
      if ((double) this.m_updateTimer <= 0.10000000149011612)
        return;
      this.m_updateTimer = 0.0f;
      bool localZones = this.CreateLocalZones(ZNet.instance.GetReferencePosition());
      this.UpdateTTL(0.1f);
      if (ZNet.instance.IsServer() && !localZones)
      {
        this.CreateGhostZones(ZNet.instance.GetReferencePosition());
        foreach (ZNetPeer peer in ZNet.instance.GetPeers())
          this.CreateGhostZones(peer.GetRefPos());
      }
      this.UpdatePrefabLifetimes();
    }
  }

  private void UpdatePrefabLifetimes()
  {
    for (int index = 0; index < this.m_locationPrefabs.Count; ++index)
    {
      --this.m_locationPrefabs[index].m_iterationLifetime;
      if (this.m_locationPrefabs[index].m_iterationLifetime <= 0)
        this.m_tempLocationPrefabsToRelease.Add(index);
    }
    for (int index1 = this.m_tempLocationPrefabsToRelease.Count - 1; index1 >= 0; --index1)
    {
      int index2 = this.m_tempLocationPrefabsToRelease[index1];
      this.m_locationPrefabs[index2].Release();
      this.m_locationPrefabs.RemoveAt(index2);
    }
    this.m_tempLocationPrefabsToRelease.Clear();
  }

  private void ForceReleaseLoadedPrefabs()
  {
    foreach (ZoneSystem.LocationPrefabLoadData locationPrefab in this.m_locationPrefabs)
      locationPrefab.Release();
    this.m_locationPrefabs.Clear();
  }

  private bool CreateGhostZones(Vector3 refPoint)
  {
    Vector2i zone = ZoneSystem.GetZone(refPoint);
    if (!this.IsZoneGenerated(zone) && this.SpawnZone(zone, ZoneSystem.SpawnMode.Ghost, out GameObject _))
      return true;
    int num = this.m_activeArea + this.m_activeDistantArea;
    for (int _y = zone.y - num; _y <= zone.y + num; ++_y)
    {
      for (int _x = zone.x - num; _x <= zone.x + num; ++_x)
      {
        Vector2i zoneID = new Vector2i(_x, _y);
        if (!this.IsZoneGenerated(zoneID) && this.SpawnZone(zoneID, ZoneSystem.SpawnMode.Ghost, out GameObject _))
          return true;
      }
    }
    return false;
  }

  private bool CreateLocalZones(Vector3 refPoint)
  {
    Vector2i zone = ZoneSystem.GetZone(refPoint);
    if (this.PokeLocalZone(zone))
      return true;
    for (int _y = zone.y - this.m_activeArea; _y <= zone.y + this.m_activeArea; ++_y)
    {
      for (int _x = zone.x - this.m_activeArea; _x <= zone.x + this.m_activeArea; ++_x)
      {
        Vector2i zoneID = new Vector2i(_x, _y);
        if (!(zoneID == zone) && this.PokeLocalZone(zoneID))
          return true;
      }
    }
    return false;
  }

  private bool PokeLocalZone(Vector2i zoneID)
  {
    ZoneSystem.ZoneData zoneData;
    if (this.m_zones.TryGetValue(zoneID, out zoneData))
    {
      zoneData.m_ttl = 0.0f;
      return false;
    }
    ZoneSystem.SpawnMode mode = !ZNet.instance.IsServer() || this.IsZoneGenerated(zoneID) ? ZoneSystem.SpawnMode.Client : ZoneSystem.SpawnMode.Full;
    GameObject root;
    if (!this.SpawnZone(zoneID, mode, out root))
      return false;
    this.m_zones.Add(zoneID, new ZoneSystem.ZoneData()
    {
      m_root = root
    });
    return true;
  }

  public bool IsZoneLoaded(Vector3 point) => this.IsZoneLoaded(ZoneSystem.GetZone(point));

  public bool IsZoneLoaded(Vector2i zoneID)
  {
    return this.m_zones.ContainsKey(zoneID) && !this.m_loadingObjectsInZones.ContainsKey(zoneID);
  }

  public bool IsActiveAreaLoaded()
  {
    Vector2i zone = ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
    for (int _y = zone.y - this.m_activeArea; _y <= zone.y + this.m_activeArea; ++_y)
    {
      for (int _x = zone.x - this.m_activeArea; _x <= zone.x + this.m_activeArea; ++_x)
      {
        if (!this.m_zones.ContainsKey(new Vector2i(_x, _y)))
          return false;
      }
    }
    return true;
  }

  private bool SpawnZone(Vector2i zoneID, ZoneSystem.SpawnMode mode, out GameObject root)
  {
    Vector3 zonePos = ZoneSystem.GetZonePos(zoneID);
    Heightmap componentInChildren1 = this.m_zonePrefab.GetComponentInChildren<Heightmap>();
    if (!HeightmapBuilder.instance.IsTerrainReady(zonePos, componentInChildren1.m_width, componentInChildren1.m_scale, componentInChildren1.IsDistantLod, WorldGenerator.instance))
    {
      root = (GameObject) null;
      return false;
    }
    ZoneSystem.LocationInstance locationInstance;
    if (this.m_locationInstances.TryGetValue(zoneID, out locationInstance) && !locationInstance.m_placed && !this.PokeCanSpawnLocation(locationInstance.m_location, true))
    {
      root = (GameObject) null;
      return false;
    }
    root = UnityEngine.Object.Instantiate<GameObject>(this.m_zonePrefab, zonePos, Quaternion.identity);
    if ((mode == ZoneSystem.SpawnMode.Ghost || mode == ZoneSystem.SpawnMode.Full) && !this.IsZoneGenerated(zoneID))
    {
      Heightmap componentInChildren2 = root.GetComponentInChildren<Heightmap>();
      this.m_tempClearAreas.Clear();
      this.m_tempSpawnedObjects.Clear();
      this.PlaceLocations(zoneID, zonePos, root.transform, componentInChildren2, this.m_tempClearAreas, mode, this.m_tempSpawnedObjects);
      this.PlaceVegetation(zoneID, zonePos, root.transform, componentInChildren2, this.m_tempClearAreas, mode, this.m_tempSpawnedObjects);
      this.PlaceZoneCtrl(zoneID, zonePos, mode, this.m_tempSpawnedObjects);
      if (mode == ZoneSystem.SpawnMode.Ghost)
      {
        foreach (UnityEngine.Object tempSpawnedObject in this.m_tempSpawnedObjects)
          UnityEngine.Object.Destroy(tempSpawnedObject);
        this.m_tempSpawnedObjects.Clear();
        UnityEngine.Object.Destroy((UnityEngine.Object) root);
        root = (GameObject) null;
      }
      this.SetZoneGenerated(zoneID);
    }
    return true;
  }

  private void PlaceZoneCtrl(
    Vector2i zoneID,
    Vector3 zoneCenterPos,
    ZoneSystem.SpawnMode mode,
    List<GameObject> spawnedObjects)
  {
    if (mode != ZoneSystem.SpawnMode.Full && mode != ZoneSystem.SpawnMode.Ghost)
      return;
    if (mode == ZoneSystem.SpawnMode.Ghost)
      ZNetView.StartGhostInit();
    GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(this.m_zoneCtrlPrefab, zoneCenterPos, Quaternion.identity);
    gameObject.GetComponent<ZNetView>();
    if (mode != ZoneSystem.SpawnMode.Ghost)
      return;
    spawnedObjects.Add(gameObject);
    ZNetView.FinishGhostInit();
  }

  private Vector3 GetRandomPointInRadius(Vector3 center, float radius)
  {
    float f = (float) ((double) UnityEngine.Random.value * 3.1415927410125732 * 2.0);
    float num = UnityEngine.Random.Range(0.0f, radius);
    return center + new Vector3(Mathf.Sin(f) * num, 0.0f, Mathf.Cos(f) * num);
  }

  private void PlaceVegetation(
    Vector2i zoneID,
    Vector3 zoneCenterPos,
    Transform parent,
    Heightmap hmap,
    List<ZoneSystem.ClearArea> clearAreas,
    ZoneSystem.SpawnMode mode,
    List<GameObject> spawnedObjects)
  {
    UnityEngine.Random.State state = UnityEngine.Random.state;
    int seed = WorldGenerator.instance.GetSeed();
    int num1 = 1;
    foreach (ZoneSystem.ZoneVegetation zoneVegetation in this.m_vegetation)
    {
      ++num1;
      if (zoneVegetation.m_enable && hmap.HaveBiome(zoneVegetation.m_biome))
      {
        UnityEngine.Random.InitState(seed + zoneID.x * 4271 + zoneID.y * 9187 + zoneVegetation.m_prefab.name.GetStableHashCode());
        int num2 = 1;
        if ((double) zoneVegetation.m_max < 1.0)
        {
          if ((double) UnityEngine.Random.value > (double) zoneVegetation.m_max)
            continue;
        }
        else
          num2 = UnityEngine.Random.Range((int) zoneVegetation.m_min, (int) zoneVegetation.m_max + 1);
        bool flag1 = (UnityEngine.Object) zoneVegetation.m_prefab.GetComponent<ZNetView>() != (UnityEngine.Object) null;
        float num3 = Mathf.Cos((float) Math.PI / 180f * zoneVegetation.m_maxTilt);
        float num4 = Mathf.Cos((float) Math.PI / 180f * zoneVegetation.m_minTilt);
        float num5 = 32f - zoneVegetation.m_groupRadius;
        this.s_tempVeg.Clear();
        int num6 = zoneVegetation.m_forcePlacement ? num2 * 50 : num2;
        int num7 = 0;
        for (int index1 = 0; index1 < num6; ++index1)
        {
          Vector3 center = new Vector3(UnityEngine.Random.Range(zoneCenterPos.x - num5, zoneCenterPos.x + num5), 0.0f, UnityEngine.Random.Range(zoneCenterPos.z - num5, zoneCenterPos.z + num5));
          int num8 = UnityEngine.Random.Range(zoneVegetation.m_groupSizeMin, zoneVegetation.m_groupSizeMax + 1);
          bool flag2 = false;
          for (int index2 = 0; index2 < num8; ++index2)
          {
            Vector3 p = index2 == 0 ? center : this.GetRandomPointInRadius(center, zoneVegetation.m_groupRadius);
            float y = (float) UnityEngine.Random.Range(0, 360);
            float num9 = UnityEngine.Random.Range(zoneVegetation.m_scaleMin, zoneVegetation.m_scaleMax);
            float x = UnityEngine.Random.Range(-zoneVegetation.m_randTilt, zoneVegetation.m_randTilt);
            float z = UnityEngine.Random.Range(-zoneVegetation.m_randTilt, zoneVegetation.m_randTilt);
            if (!zoneVegetation.m_blockCheck || !this.IsBlocked(p))
            {
              Vector3 normal1;
              Heightmap.Biome biome;
              Heightmap.BiomeArea biomeArea;
              Heightmap hmap1;
              this.GetGroundData(ref p, out normal1, out biome, out biomeArea, out hmap1);
              if ((zoneVegetation.m_biome & biome) != Heightmap.Biome.None && (zoneVegetation.m_biomeArea & biomeArea) != (Heightmap.BiomeArea) 0)
              {
                float height;
                Vector3 normal2;
                if (zoneVegetation.m_snapToStaticSolid && this.GetStaticSolidHeight(p, out height, out normal2))
                {
                  p.y = height;
                  normal1 = normal2;
                }
                float num10 = p.y - 30f;
                if ((double) num10 >= (double) zoneVegetation.m_minAltitude && (double) num10 <= (double) zoneVegetation.m_maxAltitude)
                {
                  if ((double) zoneVegetation.m_minVegetation != (double) zoneVegetation.m_maxVegetation)
                  {
                    float vegetationMask = hmap1.GetVegetationMask(p);
                    if ((double) vegetationMask > (double) zoneVegetation.m_maxVegetation || (double) vegetationMask < (double) zoneVegetation.m_minVegetation)
                      continue;
                  }
                  if ((double) zoneVegetation.m_minOceanDepth != (double) zoneVegetation.m_maxOceanDepth)
                  {
                    float oceanDepth = hmap1.GetOceanDepth(p);
                    if ((double) oceanDepth < (double) zoneVegetation.m_minOceanDepth || (double) oceanDepth > (double) zoneVegetation.m_maxOceanDepth)
                      continue;
                  }
                  if ((double) normal1.y >= (double) num3 && (double) normal1.y <= (double) num4)
                  {
                    if ((double) zoneVegetation.m_terrainDeltaRadius > 0.0)
                    {
                      float delta;
                      this.GetTerrainDelta(p, zoneVegetation.m_terrainDeltaRadius, out delta, out Vector3 _);
                      if ((double) delta > (double) zoneVegetation.m_maxTerrainDelta || (double) delta < (double) zoneVegetation.m_minTerrainDelta)
                        continue;
                    }
                    if ((double) zoneVegetation.m_minDistanceFromCenter > 0.0 || (double) zoneVegetation.m_maxDistanceFromCenter > 0.0)
                    {
                      float num11 = Utils.LengthXZ(p);
                      if ((double) zoneVegetation.m_minDistanceFromCenter > 0.0 && (double) num11 < (double) zoneVegetation.m_minDistanceFromCenter || (double) zoneVegetation.m_maxDistanceFromCenter > 0.0 && (double) num11 > (double) zoneVegetation.m_maxDistanceFromCenter)
                        continue;
                    }
                    if (zoneVegetation.m_inForest)
                    {
                      float forestFactor = WorldGenerator.GetForestFactor(p);
                      if ((double) forestFactor < (double) zoneVegetation.m_forestTresholdMin || (double) forestFactor > (double) zoneVegetation.m_forestTresholdMax)
                        continue;
                    }
                    if (zoneVegetation.m_surroundCheckVegetation)
                    {
                      float num12 = 0.0f;
                      for (int index3 = 0; index3 < zoneVegetation.m_surroundCheckLayers; ++index3)
                      {
                        float num13 = (float) (index3 + 1) / (float) zoneVegetation.m_surroundCheckLayers * zoneVegetation.m_surroundCheckDistance;
                        for (int index4 = 0; index4 < 6; ++index4)
                        {
                          float f = (float) ((double) index4 / 6.0 * 3.1415927410125732 * 2.0);
                          float vegetationMask = hmap1.GetVegetationMask(p + new Vector3(Mathf.Sin(f) * num13, 0.0f, Mathf.Cos(f) * num13));
                          float num14 = (float) ((1.0 - (double) num13) / ((double) zoneVegetation.m_surroundCheckDistance * 2.0));
                          num12 += vegetationMask * num14;
                        }
                      }
                      this.s_tempVeg.Add(num12);
                      if (this.s_tempVeg.Count >= 10)
                      {
                        float num15 = this.s_tempVeg.Max();
                        float num16 = this.s_tempVeg.Average();
                        float num17 = num16 + (num15 - num16) * zoneVegetation.m_surroundBetterThanAverage;
                        if ((double) num12 < (double) num17)
                          continue;
                      }
                      else
                        continue;
                    }
                    if (!this.InsideClearArea(clearAreas, p))
                    {
                      if (zoneVegetation.m_snapToWater)
                        p.y = 30f;
                      p.y += zoneVegetation.m_groundOffset;
                      Quaternion identity = Quaternion.identity;
                      Quaternion rotation;
                      if ((double) zoneVegetation.m_chanceToUseGroundTilt > 0.0 && (double) UnityEngine.Random.value <= (double) zoneVegetation.m_chanceToUseGroundTilt)
                      {
                        Quaternion quaternion = Quaternion.Euler(0.0f, y, 0.0f);
                        rotation = Quaternion.LookRotation(Vector3.Cross(normal1, quaternion * Vector3.forward), normal1);
                      }
                      else
                        rotation = Quaternion.Euler(x, y, z);
                      if (flag1)
                      {
                        if (mode == ZoneSystem.SpawnMode.Full || mode == ZoneSystem.SpawnMode.Ghost)
                        {
                          if (mode == ZoneSystem.SpawnMode.Ghost)
                            ZNetView.StartGhostInit();
                          GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(zoneVegetation.m_prefab, p, rotation);
                          ZNetView component = gameObject.GetComponent<ZNetView>();
                          if ((double) num9 != (double) gameObject.transform.localScale.x)
                          {
                            component.SetLocalScale(new Vector3(num9, num9, num9));
                            foreach (Collider componentsInChild in gameObject.GetComponentsInChildren<Collider>())
                            {
                              componentsInChild.enabled = false;
                              componentsInChild.enabled = true;
                            }
                          }
                          if (mode == ZoneSystem.SpawnMode.Ghost)
                          {
                            spawnedObjects.Add(gameObject);
                            ZNetView.FinishGhostInit();
                          }
                        }
                      }
                      else
                      {
                        GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(zoneVegetation.m_prefab, p, rotation);
                        gameObject.transform.localScale = new Vector3(num9, num9, num9);
                        gameObject.transform.SetParent(parent, true);
                      }
                      flag2 = true;
                    }
                  }
                }
              }
            }
          }
          if (flag2)
            ++num7;
          if (num7 >= num2)
            break;
        }
      }
    }
    UnityEngine.Random.state = state;
  }

  private bool InsideClearArea(List<ZoneSystem.ClearArea> areas, Vector3 point)
  {
    foreach (ZoneSystem.ClearArea area in areas)
    {
      if ((double) point.x > (double) area.m_center.x - (double) area.m_radius && (double) point.x < (double) area.m_center.x + (double) area.m_radius && (double) point.z > (double) area.m_center.z - (double) area.m_radius && (double) point.z < (double) area.m_center.z + (double) area.m_radius)
        return true;
    }
    return false;
  }

  private ZoneSystem.ZoneLocation GetLocation(int hash)
  {
    ZoneSystem.ZoneLocation zoneLocation;
    return this.m_locationsByHash.TryGetValue(hash, out zoneLocation) ? zoneLocation : (ZoneSystem.ZoneLocation) null;
  }

  private ZoneSystem.ZoneLocation GetLocation(string name)
  {
    foreach (ZoneSystem.ZoneLocation location in this.m_locations)
    {
      if (!location.m_prefab.IsValid)
      {
        if (location.m_enable)
          throw new NullReferenceException($"Location in list of locations was invalid! Asset: {location.m_prefab.m_assetID}");
      }
      else if (location.m_prefab.Name == name)
        return location;
    }
    return (ZoneSystem.ZoneLocation) null;
  }

  private void ClearNonPlacedLocations()
  {
    Dictionary<Vector2i, ZoneSystem.LocationInstance> dictionary = new Dictionary<Vector2i, ZoneSystem.LocationInstance>();
    foreach (KeyValuePair<Vector2i, ZoneSystem.LocationInstance> locationInstance in this.m_locationInstances)
    {
      if (locationInstance.Value.m_placed)
        dictionary.Add(locationInstance.Key, locationInstance.Value);
    }
    this.m_locationInstances = dictionary;
  }

  private void CheckLocationDuplicates()
  {
    ZLog.Log((object) "Checking for location duplicates");
    Stopwatch stopwatch = new Stopwatch();
    stopwatch.Start();
    for (int index1 = 0; index1 < this.m_locations.Count; ++index1)
    {
      ZoneSystem.ZoneLocation location1 = this.m_locations[index1];
      if (location1.m_enable)
      {
        for (int index2 = index1 + 1; index2 < this.m_locations.Count; ++index2)
        {
          ZoneSystem.ZoneLocation location2 = this.m_locations[index2];
          if (location2.m_enable)
          {
            if (location1.m_prefab.Name == location2.m_prefab.Name)
              ZLog.LogWarning((object) ("Two locations have the same location prefab name " + location1.m_prefab.ToString()));
            if (location1.m_prefab == location2.m_prefab)
              ZLog.LogWarning((object) $"Locations {location1.m_prefab} and {location2.m_prefab} point to the same location prefab");
          }
        }
      }
    }
    stopwatch.Stop();
    ZLog.Log((object) $"Location duplicate check took {stopwatch.Elapsed.TotalMilliseconds} ms");
  }

  public void GenerateLocations()
  {
    if (this.m_generateLocationsCoroutine != null)
      return;
    if (!Application.isPlaying)
    {
      ZLog.Log((object) "Setting up locations");
      this.SetupLocations();
    }
    this.m_generateLocationsCoroutine = this.StartCoroutine(this.GenerateLocationsTimeSliced());
  }

  private IEnumerator GenerateLocationsTimeSliced()
  {
    this.m_estimatedGenerateLocationsCompletionTime = DateTime.MaxValue;
    yield return (object) null;
    Stopwatch timeSliceStopwatch = Stopwatch.StartNew();
    ZLog.Log((object) "Generating locations");
    DateTime startTime = DateTime.UtcNow;
    this.ClearNonPlacedLocations();
    List<ZoneSystem.ZoneLocation> ordered = this.m_locations.OrderByDescending<ZoneSystem.ZoneLocation, bool>((Func<ZoneSystem.ZoneLocation, bool>) (a => a.m_prioritized)).ToList<ZoneSystem.ZoneLocation>();
    int totalEstimatedIterationsLeft = 0;
    for (int index = ordered.Count - 1; index >= 0; --index)
    {
      ZoneSystem.ZoneLocation zoneLocation = ordered[index];
      if (!zoneLocation.m_enable || zoneLocation.m_quantity == 0)
        ordered.RemoveAt(index);
      else
        totalEstimatedIterationsLeft += (zoneLocation.m_prioritized ? 200000 : 100000) * 20 / 2;
    }
    int runIterations = 0;
    for (int i = 0; i < ordered.Count; ++i)
    {
      ZoneSystem.ZoneLocation location = ordered[i];
      if (timeSliceStopwatch.Elapsed.TotalSeconds >= (double) this.m_timeSlicedGenerationTimeBudget)
      {
        yield return (object) null;
        timeSliceStopwatch.Restart();
      }
      ZPackage iterationsPkg = new ZPackage();
      yield return (object) this.GenerateLocationsTimeSliced(location, timeSliceStopwatch, iterationsPkg);
      runIterations += iterationsPkg.ReadInt();
      totalEstimatedIterationsLeft -= (location.m_prioritized ? 200000 : 100000) * 20 / 2;
      DateTime utcNow = DateTime.UtcNow;
      double totalSeconds = (utcNow - startTime).TotalSeconds;
      double d = (runIterations != 0 ? totalSeconds / (double) runIterations : double.MaxValue) * (double) totalEstimatedIterationsLeft;
      this.m_estimatedGenerateLocationsCompletionTime = !double.IsInfinity(d) ? utcNow.AddSeconds(d) : DateTime.MaxValue;
      this.m_generateLocationsProgress = (float) (i + 1) / (float) ordered.Count;
      location = (ZoneSystem.ZoneLocation) null;
      iterationsPkg = (ZPackage) null;
    }
    this.LocationsGenerated = true;
    ZLog.Log((object) $" Done generating locations, duration:{(DateTime.UtcNow - startTime).TotalMilliseconds.ToString()} ms");
    this.m_generateLocationsCoroutine = (Coroutine) null;
  }

  private int CountNrOfLocation(ZoneSystem.ZoneLocation location)
  {
    int num = 0;
    foreach (ZoneSystem.LocationInstance locationInstance in this.m_locationInstances.Values)
    {
      if (locationInstance.m_location.m_prefab.Name == location.m_prefab.Name)
        ++num;
    }
    if (num > 0)
      ZLog.Log((object) $"Old location found {location.m_prefab.ToString()} x {num.ToString()}");
    return num;
  }

  private IEnumerator GenerateLocationsTimeSliced(
    ZoneSystem.ZoneLocation location,
    Stopwatch timeSliceStopwatch,
    ZPackage iterationsPkg)
  {
    DateTime t = DateTime.Now;
    int seed = WorldGenerator.instance.GetSeed() + location.m_prefab.Name.GetStableHashCode();
    UnityEngine.Random.State state = UnityEngine.Random.state;
    UnityEngine.Random.InitState(seed);
    int errorLocationInZone = 0;
    int errorCenterDistance = 0;
    int errorBiome = 0;
    int errorBiomeArea = 0;
    int errorAlt = 0;
    int errorForest = 0;
    int errorSimilar = 0;
    int errorNotSimilar = 0;
    int errorTerrainDelta = 0;
    int errorVegetation = 0;
    float maxRadius = Mathf.Max(location.m_exteriorRadius, location.m_interiorRadius);
    int attempts = location.m_prioritized ? 200000 : 100000;
    int iterations = 0;
    int placed = this.CountNrOfLocation(location);
    float maxRange = 10000f;
    if (location.m_centerFirst)
      maxRange = location.m_minDistance;
    if (!location.m_unique || placed <= 0)
    {
      this.s_tempVeg.Clear();
      for (int i = 0; i < attempts && placed < location.m_quantity; ++i)
      {
        UnityEngine.Random.State insideState;
        if (timeSliceStopwatch.Elapsed.TotalSeconds >= (double) this.m_timeSlicedGenerationTimeBudget)
        {
          insideState = UnityEngine.Random.state;
          UnityEngine.Random.state = state;
          yield return (object) null;
          timeSliceStopwatch.Restart();
          state = UnityEngine.Random.state;
          UnityEngine.Random.state = insideState;
        }
        Vector2i zoneID = ZoneSystem.GetRandomZone(maxRange);
        if (location.m_centerFirst)
          ++maxRange;
        if (this.m_locationInstances.ContainsKey(zoneID))
          ++errorLocationInZone;
        else if (!this.IsZoneGenerated(zoneID))
        {
          if ((location.m_biomeArea & WorldGenerator.instance.GetBiomeArea(ZoneSystem.GetZonePos(zoneID))) == (Heightmap.BiomeArea) 0)
          {
            ++errorBiomeArea;
          }
          else
          {
            for (int j = 0; j < 20; ++j)
            {
              if (timeSliceStopwatch.Elapsed.TotalSeconds >= (double) this.m_timeSlicedGenerationTimeBudget)
              {
                insideState = UnityEngine.Random.state;
                UnityEngine.Random.state = state;
                yield return (object) null;
                timeSliceStopwatch.Restart();
                state = UnityEngine.Random.state;
                UnityEngine.Random.state = insideState;
              }
              ++iterations;
              Vector3 randomPointInZone = ZoneSystem.GetRandomPointInZone(zoneID, maxRadius);
              float magnitude = randomPointInZone.magnitude;
              if ((double) location.m_minDistance != 0.0 && (double) magnitude < (double) location.m_minDistance)
                ++errorCenterDistance;
              else if ((double) location.m_maxDistance != 0.0 && (double) magnitude > (double) location.m_maxDistance)
                ++errorCenterDistance;
              else if ((location.m_biome & WorldGenerator.instance.GetBiome(randomPointInZone)) == Heightmap.Biome.None)
              {
                ++errorBiome;
              }
              else
              {
                Color mask1;
                randomPointInZone.y = WorldGenerator.instance.GetHeight(randomPointInZone.x, randomPointInZone.z, out mask1);
                float num1 = randomPointInZone.y - 30f;
                if ((double) num1 < (double) location.m_minAltitude || (double) num1 > (double) location.m_maxAltitude)
                {
                  ++errorAlt;
                }
                else
                {
                  if (location.m_inForest)
                  {
                    float forestFactor = WorldGenerator.GetForestFactor(randomPointInZone);
                    if ((double) forestFactor < (double) location.m_forestTresholdMin || (double) forestFactor > (double) location.m_forestTresholdMax)
                    {
                      ++errorForest;
                      continue;
                    }
                  }
                  if ((double) location.m_minDistanceFromCenter > 0.0 || (double) location.m_maxDistanceFromCenter > 0.0)
                  {
                    float num2 = Utils.LengthXZ(randomPointInZone);
                    if ((double) location.m_minDistanceFromCenter > 0.0 && (double) num2 < (double) location.m_minDistanceFromCenter || (double) location.m_maxDistanceFromCenter > 0.0 && (double) num2 > (double) location.m_maxDistanceFromCenter)
                      continue;
                  }
                  float delta;
                  WorldGenerator.instance.GetTerrainDelta(randomPointInZone, location.m_exteriorRadius, out delta, out Vector3 _);
                  if ((double) delta > (double) location.m_maxTerrainDelta || (double) delta < (double) location.m_minTerrainDelta)
                    ++errorTerrainDelta;
                  else if ((double) location.m_minDistanceFromSimilar > 0.0 && this.HaveLocationInRange(location.m_prefab.Name, location.m_group, randomPointInZone, location.m_minDistanceFromSimilar))
                    ++errorSimilar;
                  else if ((double) location.m_maxDistanceFromSimilar > 0.0 && !this.HaveLocationInRange(location.m_prefabName, location.m_groupMax, randomPointInZone, location.m_maxDistanceFromSimilar, true))
                  {
                    ++errorNotSimilar;
                  }
                  else
                  {
                    float a = mask1.a;
                    if ((double) location.m_minimumVegetation > 0.0 && (double) a <= (double) location.m_minimumVegetation)
                      ++errorVegetation;
                    else if ((double) location.m_maximumVegetation < 1.0 && (double) a >= (double) location.m_maximumVegetation)
                    {
                      ++errorVegetation;
                    }
                    else
                    {
                      if (location.m_surroundCheckVegetation)
                      {
                        float num3 = 0.0f;
                        for (int index1 = 0; index1 < location.m_surroundCheckLayers; ++index1)
                        {
                          float num4 = (float) (index1 + 1) / (float) location.m_surroundCheckLayers * location.m_surroundCheckDistance;
                          for (int index2 = 0; index2 < 6; ++index2)
                          {
                            float f = (float) ((double) index2 / 6.0 * 3.1415927410125732 * 2.0);
                            Vector3 vector3 = randomPointInZone + new Vector3(Mathf.Sin(f) * num4, 0.0f, Mathf.Cos(f) * num4);
                            Color mask2;
                            double height = (double) WorldGenerator.instance.GetHeight(vector3.x, vector3.z, out mask2);
                            float num5 = (float) (((double) location.m_surroundCheckDistance - (double) num4) / ((double) location.m_surroundCheckDistance * 2.0));
                            num3 += mask2.a * num5;
                          }
                        }
                        this.s_tempVeg.Add(num3);
                        if (this.s_tempVeg.Count >= 10)
                        {
                          float num6 = this.s_tempVeg.Max();
                          float num7 = this.s_tempVeg.Average();
                          float num8 = num7 + (num6 - num7) * location.m_surroundBetterThanAverage;
                          if ((double) num3 >= (double) num8)
                            ZLog.DevLog((object) $"Surround check passed with a value of {num3}, cutoff was {num8}, max: {num6}, average: {num7}.");
                          else
                            continue;
                        }
                        else
                          continue;
                      }
                      this.RegisterLocation(location, randomPointInZone, false);
                      ++placed;
                      break;
                    }
                  }
                }
              }
            }
            zoneID = new Vector2i();
          }
        }
      }
      if (placed < location.m_quantity)
      {
        ZLog.LogWarning((object) $"Failed to place all {location.m_prefab.Name}, placed {placed.ToString()} out of {location.m_quantity.ToString()}");
        ZLog.DevLog((object) ("errorLocationInZone " + errorLocationInZone.ToString()));
        ZLog.DevLog((object) ("errorCenterDistance " + errorCenterDistance.ToString()));
        ZLog.DevLog((object) ("errorBiome " + errorBiome.ToString()));
        ZLog.DevLog((object) ("errorBiomeArea " + errorBiomeArea.ToString()));
        ZLog.DevLog((object) ("errorAlt " + errorAlt.ToString()));
        ZLog.DevLog((object) ("errorForest " + errorForest.ToString()));
        ZLog.DevLog((object) ("errorSimilar " + errorSimilar.ToString()));
        ZLog.DevLog((object) ("errorNotSimilar " + errorNotSimilar.ToString()));
        ZLog.DevLog((object) ("errorTerrainDelta " + errorTerrainDelta.ToString()));
        ZLog.DevLog((object) ("errorVegetation " + errorVegetation.ToString()));
      }
    }
    UnityEngine.Random.state = state;
    TimeSpan timeSpan = DateTime.Now - t;
    iterationsPkg.Write(iterations);
    iterationsPkg.SetPos(0);
  }

  public float GetEstimatedGenerationCompletionTimeFromNow()
  {
    return this.m_generateLocationsCoroutine == null ? 0.0f : (float) (this.m_estimatedGenerateLocationsCompletionTime - DateTime.UtcNow).TotalSeconds;
  }

  public static float GetGenerationTimeBudgetForTargetFrameRate(out float targetFrameTime)
  {
    float num1;
    if (QualitySettings.vSyncCount > 0)
    {
      Resolution currentResolution = Screen.currentResolution;
      double denominator = (double) currentResolution.refreshRateRatio.denominator;
      currentResolution = Screen.currentResolution;
      double numerator = (double) currentResolution.refreshRateRatio.numerator;
      num1 = (float) (denominator / numerator) * (float) QualitySettings.vSyncCount;
    }
    else
    {
      num1 = 1f / (float) Application.targetFrameRate;
      if ((double) num1 < 0.0)
        num1 = 0.0f;
    }
    targetFrameTime = Mathf.Clamp(num1, 0.0166666675f, 0.0333333351f);
    float num2 = 0.006666667f;
    return targetFrameTime - num2;
  }

  private static Vector2i GetRandomZone(float range)
  {
    int maxExclusive = (int) range / 64 /*0x40*/;
    Vector2i id;
    do
    {
      id = new Vector2i(UnityEngine.Random.Range(-maxExclusive, maxExclusive), UnityEngine.Random.Range(-maxExclusive, maxExclusive));
    }
    while ((double) ZoneSystem.GetZonePos(id).magnitude >= 10000.0);
    return id;
  }

  private static Vector3 GetRandomPointInZone(Vector2i zone, float locationRadius)
  {
    return ZoneSystem.GetZonePos(zone) + new Vector3(UnityEngine.Random.Range(locationRadius - 32f, 32f - locationRadius), 0.0f, UnityEngine.Random.Range(locationRadius - 32f, 32f - locationRadius));
  }

  private static Vector3 GetRandomPointInZone(float locationRadius)
  {
    Vector3 zonePos = ZoneSystem.GetZonePos(ZoneSystem.GetZone(new Vector3(UnityEngine.Random.Range(-10000f, 10000f), 0.0f, UnityEngine.Random.Range(-10000f, 10000f))));
    return new Vector3(UnityEngine.Random.Range(zonePos.x - 32f + locationRadius, zonePos.x + 32f - locationRadius), 0.0f, UnityEngine.Random.Range(zonePos.z - 32f + locationRadius, zonePos.z + 32f - locationRadius));
  }

  private void PlaceLocations(
    Vector2i zoneID,
    Vector3 zoneCenterPos,
    Transform parent,
    Heightmap hmap,
    List<ZoneSystem.ClearArea> clearAreas,
    ZoneSystem.SpawnMode mode,
    List<GameObject> spawnedObjects)
  {
    DateTime now = DateTime.Now;
    ZoneSystem.LocationInstance locationInstance;
    if (!this.m_locationInstances.TryGetValue(zoneID, out locationInstance) || locationInstance.m_placed)
      return;
    Vector3 position = locationInstance.m_position;
    this.GetGroundData(ref position, out Vector3 _, out Heightmap.Biome _, out Heightmap.BiomeArea _, out Heightmap _);
    if (locationInstance.m_location.m_snapToWater)
      position.y = 30f;
    if (locationInstance.m_location.m_clearArea)
    {
      ZoneSystem.ClearArea clearArea = new ZoneSystem.ClearArea(position, locationInstance.m_location.m_exteriorRadius);
      clearAreas.Add(clearArea);
    }
    Quaternion rot = Quaternion.identity;
    if (locationInstance.m_location.m_slopeRotation)
    {
      Vector3 slopeDirection;
      this.GetTerrainDelta(position, locationInstance.m_location.m_exteriorRadius, out float _, out slopeDirection);
      Vector3 forward = new Vector3(slopeDirection.x, 0.0f, slopeDirection.z);
      forward.Normalize();
      rot = Quaternion.LookRotation(forward);
      Vector3 eulerAngles = rot.eulerAngles;
      eulerAngles.y = Mathf.Round(eulerAngles.y / 22.5f) * 22.5f;
      rot.eulerAngles = eulerAngles;
    }
    else if (locationInstance.m_location.m_randomRotation)
      rot = Quaternion.Euler(0.0f, (float) UnityEngine.Random.Range(0, 16 /*0x10*/) * 22.5f, 0.0f);
    int seed = WorldGenerator.instance.GetSeed() + zoneID.x * 4271 + zoneID.y * 9187;
    this.SpawnLocation(locationInstance.m_location, seed, position, rot, mode, spawnedObjects);
    locationInstance.m_placed = true;
    this.m_locationInstances[zoneID] = locationInstance;
    TimeSpan timeSpan = DateTime.Now - now;
    ZLog.Log((object) $"Placed locations in zone {zoneID.ToString()}  duration {timeSpan.TotalMilliseconds.ToString()} ms");
    if (locationInstance.m_location.m_unique)
      this.RemoveUnplacedLocations(locationInstance.m_location);
    if (!locationInstance.m_location.m_iconPlaced)
      return;
    this.SendLocationIcons(ZRoutedRpc.Everybody);
  }

  private void RemoveUnplacedLocations(ZoneSystem.ZoneLocation location)
  {
    List<Vector2i> vector2iList = new List<Vector2i>();
    foreach (KeyValuePair<Vector2i, ZoneSystem.LocationInstance> locationInstance in this.m_locationInstances)
    {
      if (locationInstance.Value.m_location == location && !locationInstance.Value.m_placed)
        vector2iList.Add(locationInstance.Key);
    }
    foreach (Vector2i key in vector2iList)
      this.m_locationInstances.Remove(key);
    ZLog.DevLog((object) $"Removed {vector2iList.Count.ToString()} unplaced locations of type {location.m_prefab.Name}");
  }

  public bool TestSpawnLocation(string name, Vector3 pos, bool disableSave = true)
  {
    if (!ZNet.instance.IsServer())
      return false;
    ZoneSystem.ZoneLocation location = this.GetLocation(name);
    if (location == null)
    {
      ZLog.Log((object) ("Missing location:" + name));
      Console.instance.Print("Missing location:" + name);
      return false;
    }
    if (!location.m_prefab.IsValid)
    {
      ZLog.Log((object) ("Missing prefab in location:" + name));
      Console.instance.Print("Missing location:" + name);
      return false;
    }
    float num = Mathf.Max(location.m_exteriorRadius, location.m_interiorRadius);
    Vector3 zonePos = ZoneSystem.GetZonePos(ZoneSystem.GetZone(pos));
    pos.x = Mathf.Clamp(pos.x, zonePos.x - 32f + num, zonePos.x + 32f - num);
    pos.z = Mathf.Clamp(pos.z, zonePos.z - 32f + num, zonePos.z + 32f - num);
    string[] strArray = new string[6]
    {
      "radius ",
      num.ToString(),
      "  ",
      null,
      null,
      null
    };
    Vector3 vector3 = zonePos;
    strArray[3] = vector3.ToString();
    strArray[4] = " ";
    vector3 = pos;
    strArray[5] = vector3.ToString();
    ZLog.Log((object) string.Concat(strArray));
    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Location spawned, " + (disableSave ? "world saving DISABLED until restart" : "CAUTION! world saving is ENABLED, use normal location command to disable it!"));
    this.m_didZoneTest = disableSave;
    float y = (float) UnityEngine.Random.Range(0, 16 /*0x10*/) * 22.5f;
    List<GameObject> spawnedGhostObjects = new List<GameObject>();
    this.SpawnLocation(location, UnityEngine.Random.Range(0, 99999), pos, Quaternion.Euler(0.0f, y, 0.0f), ZoneSystem.SpawnMode.Full, spawnedGhostObjects);
    return true;
  }

  private bool PokeCanSpawnLocation(ZoneSystem.ZoneLocation location, bool isFirstSpawn)
  {
    ZoneSystem.LocationPrefabLoadData locationPrefabLoadData = (ZoneSystem.LocationPrefabLoadData) null;
    for (int index = 0; index < this.m_locationPrefabs.Count; ++index)
    {
      if (this.m_locationPrefabs[index].PrefabAssetID == location.m_prefab.m_assetID)
      {
        locationPrefabLoadData = this.m_locationPrefabs[index];
        break;
      }
    }
    if (locationPrefabLoadData == null)
    {
      locationPrefabLoadData = new ZoneSystem.LocationPrefabLoadData(location.m_prefab, isFirstSpawn);
      this.m_locationPrefabs.Add(locationPrefabLoadData);
    }
    locationPrefabLoadData.m_iterationLifetime = this.GetLocationPrefabLifetime();
    return locationPrefabLoadData.IsLoaded;
  }

  public int GetLocationPrefabLifetime()
  {
    int num = 2 * (this.m_activeArea + this.m_activeDistantArea) + 1;
    return num * num * (ZNet.instance.IsServer() ? ZNet.instance.GetPeers().Count + 1 : 1);
  }

  public bool ShouldDelayProxyLocationSpawning(int hash)
  {
    ZoneSystem.ZoneLocation location = this.GetLocation(hash);
    if (location != null)
      return !this.PokeCanSpawnLocation(location, false);
    ZLog.LogWarning((object) ("Missing location:" + hash.ToString()));
    return false;
  }

  public GameObject SpawnProxyLocation(int hash, int seed, Vector3 pos, Quaternion rot)
  {
    ZoneSystem.ZoneLocation location = this.GetLocation(hash);
    if (location == null)
    {
      ZLog.LogWarning((object) ("Missing location:" + hash.ToString()));
      return (GameObject) null;
    }
    List<GameObject> spawnedGhostObjects = new List<GameObject>();
    return this.SpawnLocation(location, seed, pos, rot, ZoneSystem.SpawnMode.Client, spawnedGhostObjects);
  }

  private GameObject SpawnLocation(
    ZoneSystem.ZoneLocation location,
    int seed,
    Vector3 pos,
    Quaternion rot,
    ZoneSystem.SpawnMode mode,
    List<GameObject> spawnedGhostObjects)
  {
    int num = (int) location.m_prefab.Load();
    ZNetView[] componentsInChildren1 = Utils.GetEnabledComponentsInChildren<ZNetView>(location.m_prefab.Asset);
    RandomSpawn[] componentsInChildren2 = Utils.GetEnabledComponentsInChildren<RandomSpawn>(location.m_prefab.Asset);
    for (int index = 0; index < componentsInChildren2.Length; ++index)
      componentsInChildren2[index].Prepare();
    Location component1 = location.m_prefab.Asset.GetComponent<Location>();
    Vector3 vector3_1 = Vector3.zero;
    Vector3 vector3_2 = Vector3.zero;
    if ((bool) (UnityEngine.Object) component1.m_interiorTransform && (bool) (UnityEngine.Object) component1.m_generator)
    {
      vector3_1 = component1.m_interiorTransform.localPosition;
      vector3_2 = component1.m_generator.transform.localPosition;
    }
    Vector3 position1 = location.m_prefab.Asset.transform.position;
    Quaternion rotation1 = location.m_prefab.Asset.transform.rotation;
    location.m_prefab.Asset.transform.position = Vector3.zero;
    location.m_prefab.Asset.transform.rotation = Quaternion.identity;
    UnityEngine.Random.InitState(seed);
    bool flag = (bool) (UnityEngine.Object) component1 && component1.m_useCustomInteriorTransform && (bool) (UnityEngine.Object) component1.m_interiorTransform && (bool) (UnityEngine.Object) component1.m_generator;
    Vector3 vector3_3 = Vector3.zero;
    Vector3 vector3_4 = Vector3.zero;
    Quaternion quaternion = Quaternion.identity;
    if (flag)
    {
      vector3_3 = component1.m_generator.transform.localPosition;
      vector3_4 = component1.m_interiorTransform.localPosition;
      quaternion = component1.m_interiorTransform.localRotation;
      Vector3 zonePos = ZoneSystem.GetZonePos(ZoneSystem.GetZone(pos));
      component1.m_generator.transform.localPosition = Vector3.zero;
      Vector3 vector3_5 = vector3_1;
      Vector3 vector = zonePos + vector3_5 + vector3_2 - pos;
      Vector3 column = (Vector3) (Matrix4x4.Rotate(Quaternion.Inverse(rot)) * Matrix4x4.Translate(vector)).GetColumn(3) with
      {
        y = component1.m_interiorTransform.localPosition.y
      };
      component1.m_interiorTransform.localPosition = column;
      component1.m_interiorTransform.localRotation = Quaternion.Inverse(rot);
    }
    if ((bool) (UnityEngine.Object) component1 && (bool) (UnityEngine.Object) component1.m_generator && component1.m_useCustomInteriorTransform != component1.m_generator.m_useCustomInteriorTransform)
      ZLog.LogWarning((object) $"{component1.name} & {component1.m_generator.name} don't have matching m_useCustomInteriorTransform()! If one has it the other should as well!");
    GameObject gameObject = (GameObject) null;
    if (mode == ZoneSystem.SpawnMode.Full || mode == ZoneSystem.SpawnMode.Ghost)
    {
      UnityEngine.Random.InitState(seed);
      foreach (RandomSpawn randomSpawn in componentsInChildren2)
      {
        Vector3 position2 = randomSpawn.gameObject.transform.position;
        randomSpawn.Randomize(pos + rot * position2, component1);
      }
      WearNTear.m_randomInitialDamage = component1.m_applyRandomDamage;
      foreach (ZNetView znetView in componentsInChildren1)
      {
        if (znetView.gameObject.activeSelf)
        {
          Vector3 position3 = znetView.gameObject.transform.position;
          Vector3 position4 = pos + rot * position3;
          Quaternion rotation2 = znetView.gameObject.transform.rotation;
          Quaternion rotation3 = rot * rotation2;
          if (mode == ZoneSystem.SpawnMode.Ghost)
            ZNetView.StartGhostInit();
          GameObject instance = UnityEngine.Object.Instantiate<GameObject>(znetView.gameObject, position4, rotation3);
          instance.HoldReferenceTo(location.m_prefab);
          DungeonGenerator component2 = instance.GetComponent<DungeonGenerator>();
          if ((bool) (UnityEngine.Object) component2)
          {
            if (flag)
              component2.m_originalPosition = vector3_2;
            component2.Generate(mode);
          }
          if (mode == ZoneSystem.SpawnMode.Ghost)
          {
            spawnedGhostObjects.Add(instance);
            ZNetView.FinishGhostInit();
          }
        }
      }
      WearNTear.m_randomInitialDamage = false;
      foreach (RandomSpawn randomSpawn in componentsInChildren2)
        randomSpawn.Reset();
      foreach (Component component3 in componentsInChildren1)
        component3.gameObject.SetActive(true);
      location.m_prefab.Asset.transform.position = position1;
      location.m_prefab.Asset.transform.rotation = rotation1;
      if (flag)
      {
        component1.m_generator.transform.localPosition = vector3_3;
        component1.m_interiorTransform.localPosition = vector3_4;
        component1.m_interiorTransform.localRotation = quaternion;
      }
      this.CreateLocationProxy(location, seed, pos, rot, mode, spawnedGhostObjects);
    }
    else
    {
      UnityEngine.Random.InitState(seed);
      foreach (RandomSpawn randomSpawn in componentsInChildren2)
      {
        Vector3 position5 = randomSpawn.gameObject.transform.position;
        randomSpawn.Randomize(pos + rot * position5, component1);
      }
      foreach (Component component4 in componentsInChildren1)
        component4.gameObject.SetActive(false);
      gameObject = SoftReferenceableAssets.Utils.Instantiate(location.m_prefab, pos, rot);
      gameObject.SetActive(true);
      foreach (RandomSpawn randomSpawn in componentsInChildren2)
        randomSpawn.Reset();
      foreach (Component component5 in componentsInChildren1)
        component5.gameObject.SetActive(true);
      location.m_prefab.Asset.transform.position = position1;
      location.m_prefab.Asset.transform.rotation = rotation1;
      if (flag)
      {
        component1.m_generator.transform.localPosition = vector3_3;
        component1.m_interiorTransform.localPosition = vector3_4;
        component1.m_interiorTransform.localRotation = quaternion;
      }
    }
    location.m_prefab.Release();
    SnapToGround.SnappAll();
    return gameObject;
  }

  private void CreateLocationProxy(
    ZoneSystem.ZoneLocation location,
    int seed,
    Vector3 pos,
    Quaternion rotation,
    ZoneSystem.SpawnMode mode,
    List<GameObject> spawnedGhostObjects)
  {
    if (mode == ZoneSystem.SpawnMode.Ghost)
      ZNetView.StartGhostInit();
    GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(this.m_locationProxyPrefab, pos, rotation);
    LocationProxy component = gameObject.GetComponent<LocationProxy>();
    bool flag = mode == ZoneSystem.SpawnMode.Full;
    string name = location.m_prefab.Name;
    int seed1 = seed;
    int num = flag ? 1 : 0;
    component.SetLocation(name, seed1, num != 0);
    if (mode != ZoneSystem.SpawnMode.Ghost)
      return;
    spawnedGhostObjects.Add(gameObject);
    ZNetView.FinishGhostInit();
  }

  private void RegisterLocation(ZoneSystem.ZoneLocation location, Vector3 pos, bool generated)
  {
    ZoneSystem.LocationInstance locationInstance = new ZoneSystem.LocationInstance();
    locationInstance.m_location = location;
    locationInstance.m_position = pos;
    locationInstance.m_placed = generated;
    Vector2i zone = ZoneSystem.GetZone(pos);
    if (this.m_locationInstances.ContainsKey(zone))
      ZLog.LogWarning((object) ("Location already exist in zone " + zone.ToString()));
    else
      this.m_locationInstances.Add(zone, locationInstance);
  }

  private bool HaveLocationInRange(
    string prefabName,
    string group,
    Vector3 p,
    float radius,
    bool maxGroup = false)
  {
    foreach (ZoneSystem.LocationInstance locationInstance in this.m_locationInstances.Values)
    {
      if ((locationInstance.m_location.m_prefab.Name == prefabName || !maxGroup && group.Length > 0 && group == locationInstance.m_location.m_group || maxGroup && group.Length > 0 && group == locationInstance.m_location.m_groupMax) && (double) Vector3.Distance(locationInstance.m_position, p) < (double) radius)
        return true;
    }
    return false;
  }

  public bool GetLocationIcon(string name, out Vector3 pos)
  {
    if (ZNet.instance.IsServer())
    {
      foreach (KeyValuePair<Vector2i, ZoneSystem.LocationInstance> locationInstance in this.m_locationInstances)
      {
        if ((locationInstance.Value.m_location.m_iconAlways || locationInstance.Value.m_location.m_iconPlaced && locationInstance.Value.m_placed) && locationInstance.Value.m_location.m_prefab.Name == name)
        {
          pos = locationInstance.Value.m_position;
          return true;
        }
      }
    }
    else
    {
      foreach (KeyValuePair<Vector3, string> locationIcon in this.m_locationIcons)
      {
        if (locationIcon.Value == name)
        {
          pos = locationIcon.Key;
          return true;
        }
      }
    }
    pos = Vector3.zero;
    return false;
  }

  public void GetLocationIcons(Dictionary<Vector3, string> icons)
  {
    if (ZNet.instance.IsServer())
    {
      foreach (ZoneSystem.LocationInstance locationInstance in this.m_locationInstances.Values)
      {
        if (locationInstance.m_location.m_iconAlways || locationInstance.m_location.m_iconPlaced && locationInstance.m_placed)
          icons[locationInstance.m_position] = locationInstance.m_location.m_prefab.Name;
      }
    }
    else
    {
      foreach (KeyValuePair<Vector3, string> locationIcon in this.m_locationIcons)
        icons.Add(locationIcon.Key, locationIcon.Value);
    }
  }

  private void GetTerrainDelta(
    Vector3 center,
    float radius,
    out float delta,
    out Vector3 slopeDirection)
  {
    int num1 = 10;
    float num2 = -999999f;
    float num3 = 999999f;
    Vector3 vector3_1 = center;
    Vector3 vector3_2 = center;
    for (int index = 0; index < num1; ++index)
    {
      Vector2 vector2 = UnityEngine.Random.insideUnitCircle * radius;
      Vector3 p = center + new Vector3(vector2.x, 0.0f, vector2.y);
      float groundHeight = this.GetGroundHeight(p);
      if ((double) groundHeight < (double) num3)
      {
        num3 = groundHeight;
        vector3_2 = p;
      }
      if ((double) groundHeight > (double) num2)
      {
        num2 = groundHeight;
        vector3_1 = p;
      }
    }
    delta = num2 - num3;
    slopeDirection = Vector3.Normalize(vector3_2 - vector3_1);
  }

  public bool IsBlocked(Vector3 p)
  {
    p.y += 2000f;
    return Physics.Raycast(p, Vector3.down, 10000f, this.m_blockRayMask);
  }

  public float GetGroundHeight(Vector3 p)
  {
    RaycastHit hitInfo;
    return Physics.Raycast(p with { y = 6000f }, Vector3.down, out hitInfo, 10000f, this.m_terrainRayMask) ? hitInfo.point.y : p.y;
  }

  public bool GetGroundHeight(Vector3 p, out float height)
  {
    p.y = 6000f;
    RaycastHit hitInfo;
    if (Physics.Raycast(p, Vector3.down, out hitInfo, 10000f, this.m_terrainRayMask))
    {
      height = hitInfo.point.y;
      return true;
    }
    height = 0.0f;
    return false;
  }

  public float GetSolidHeight(Vector3 p)
  {
    Vector3 origin = p;
    origin.y += 1000f;
    RaycastHit hitInfo;
    return Physics.Raycast(origin, Vector3.down, out hitInfo, 2000f, this.m_solidRayMask) ? hitInfo.point.y : p.y;
  }

  public bool GetSolidHeight(Vector3 p, out float height, int heightMargin = 1000)
  {
    p.y += (float) heightMargin;
    RaycastHit hitInfo;
    if (Physics.Raycast(p, Vector3.down, out hitInfo, 2000f, this.m_solidRayMask) && !(bool) (UnityEngine.Object) hitInfo.collider.attachedRigidbody)
    {
      height = hitInfo.point.y;
      return true;
    }
    height = 0.0f;
    return false;
  }

  public bool GetSolidHeight(Vector3 p, float radius, out float height, Transform ignore)
  {
    height = p.y - 1000f;
    p.y += 1000f;
    int num = (double) radius > 0.0 ? Physics.SphereCastNonAlloc(p, radius, Vector3.down, this.rayHits, 2000f, this.m_solidRayMask) : Physics.RaycastNonAlloc(p, Vector3.down, this.rayHits, 2000f, this.m_solidRayMask);
    ZoneSystem.s_rayHits.Clear();
    ZoneSystem.s_rayHitsHeight.Clear();
    for (int index = 0; index < num; ++index)
    {
      RaycastHit rayHit = this.rayHits[index];
      float y = rayHit.point.y;
      Utils.InsertSortNoAlloc<RaycastHit>(ZoneSystem.s_rayHits, rayHit, ZoneSystem.s_rayHitsHeight, y);
    }
    for (int index = ZoneSystem.s_rayHits.Count - 1; index >= 0; --index)
    {
      Collider collider = this.rayHits[index].collider;
      if (!((UnityEngine.Object) collider.attachedRigidbody != (UnityEngine.Object) null) && (!((UnityEngine.Object) ignore != (UnityEngine.Object) null) || !Utils.IsParent(collider.transform, ignore)))
      {
        height = Mathf.Max(height, ZoneSystem.s_rayHitsHeight[index]);
        return true;
      }
    }
    return false;
  }

  public bool GetSolidHeight(Vector3 p, out float height, out Vector3 normal, out GameObject go)
  {
    p.y += 1000f;
    RaycastHit hitInfo;
    if (Physics.Raycast(p, Vector3.down, out hitInfo, 2000f, this.m_solidRayMask) && !(bool) (UnityEngine.Object) hitInfo.collider.attachedRigidbody)
    {
      height = hitInfo.point.y;
      normal = hitInfo.normal;
      go = hitInfo.collider.gameObject;
      return true;
    }
    height = 0.0f;
    normal = Vector3.zero;
    go = (GameObject) null;
    return false;
  }

  public bool GetStaticSolidHeight(Vector3 p, out float height, out Vector3 normal)
  {
    p.y += 1000f;
    RaycastHit hitInfo;
    if (Physics.Raycast(p, Vector3.down, out hitInfo, 2000f, this.m_staticSolidRayMask) && !(bool) (UnityEngine.Object) hitInfo.collider.attachedRigidbody)
    {
      height = hitInfo.point.y;
      normal = hitInfo.normal;
      return true;
    }
    height = 0.0f;
    normal = Vector3.zero;
    return false;
  }

  public bool FindFloor(Vector3 p, out float height)
  {
    RaycastHit hitInfo;
    if (Physics.Raycast(p + Vector3.up * 1f, Vector3.down, out hitInfo, 1000f, this.m_solidRayMask))
    {
      height = hitInfo.point.y;
      return true;
    }
    height = 0.0f;
    return false;
  }

  public float GetGroundOffset(Vector3 position)
  {
    Heightmap hmap;
    this.GetGroundData(ref position, out Vector3 _, out Heightmap.Biome _, out Heightmap.BiomeArea _, out hmap);
    return (bool) (UnityEngine.Object) hmap ? hmap.GetHeightOffset(position) : 0.0f;
  }

  public static bool IsLavaPreHeightmap(Vector3 position, float lavaValue = 0.6f)
  {
    if (WorldGenerator.instance.GetBiome(position.x, position.z) != Heightmap.Biome.AshLands)
      return false;
    Color mask;
    double biomeHeight = (double) WorldGenerator.instance.GetBiomeHeight(Heightmap.Biome.AshLands, position.x, position.z, out mask);
    return (double) mask.a > (double) lavaValue;
  }

  public bool IsLava(Vector3 position, bool defaultTrue = false)
  {
    Heightmap hmap;
    this.GetGroundData(ref position, out Vector3 _, out Heightmap.Biome _, out Heightmap.BiomeArea _, out hmap);
    return !(bool) (UnityEngine.Object) hmap ? defaultTrue : hmap.IsLava(position);
  }

  public bool IsLava(ref Vector3 position, bool defaultTrue = false)
  {
    Heightmap hmap;
    this.GetGroundData(ref position, out Vector3 _, out Heightmap.Biome _, out Heightmap.BiomeArea _, out hmap);
    return !(bool) (UnityEngine.Object) hmap ? defaultTrue : hmap.IsLava(position);
  }

  public void GetGroundData(
    ref Vector3 p,
    out Vector3 normal,
    out Heightmap.Biome biome,
    out Heightmap.BiomeArea biomeArea,
    out Heightmap hmap)
  {
    biome = Heightmap.Biome.None;
    biomeArea = Heightmap.BiomeArea.Everything;
    hmap = (Heightmap) null;
    RaycastHit hitInfo;
    if (Physics.Raycast(p + Vector3.up * 5000f, Vector3.down, out hitInfo, 10000f, this.m_terrainRayMask))
    {
      p.y = hitInfo.point.y;
      normal = hitInfo.normal;
      Heightmap component = hitInfo.collider.GetComponent<Heightmap>();
      if (!(bool) (UnityEngine.Object) component)
        return;
      biome = component.GetBiome(hitInfo.point);
      biomeArea = component.GetBiomeArea();
      hmap = component;
    }
    else
      normal = Vector3.up;
  }

  private void UpdateTTL(float dt)
  {
    foreach (KeyValuePair<Vector2i, ZoneSystem.ZoneData> zone in this.m_zones)
      zone.Value.m_ttl += dt;
    foreach (KeyValuePair<Vector2i, ZoneSystem.ZoneData> zone in this.m_zones)
    {
      if ((double) zone.Value.m_ttl > (double) this.m_zoneTTL && !ZNetScene.instance.HaveInstanceInSector(zone.Key))
      {
        UnityEngine.Object.Destroy((UnityEngine.Object) zone.Value.m_root);
        this.m_zones.Remove(zone.Key);
        break;
      }
    }
  }

  public bool FindClosestLocation(
    string name,
    Vector3 point,
    out ZoneSystem.LocationInstance closest)
  {
    float num1 = 999999f;
    closest = new ZoneSystem.LocationInstance();
    bool closestLocation = false;
    foreach (ZoneSystem.LocationInstance locationInstance in this.m_locationInstances.Values)
    {
      float num2 = Vector3.Distance(locationInstance.m_position, point);
      if (locationInstance.m_location.m_prefab.Name == name && (double) num2 < (double) num1)
      {
        num1 = num2;
        closest = locationInstance;
        closestLocation = true;
      }
    }
    return closestLocation;
  }

  public bool FindLocations(string name, ref List<ZoneSystem.LocationInstance> locations)
  {
    locations.Clear();
    foreach (ZoneSystem.LocationInstance locationInstance in this.m_locationInstances.Values)
    {
      if (locationInstance.m_location.m_prefab.Name == name)
        locations.Add(locationInstance);
    }
    return locations.Count > 0;
  }

  public static Vector2i GetZone(Vector3 point)
  {
    return new Vector2i(Utils.FloorToInt((float) (((double) point.x + 32.0) / 64.0)), Utils.FloorToInt((float) (((double) point.z + 32.0) / 64.0)));
  }

  public static Vector3 GetZonePos(Vector2i id)
  {
    return new Vector3((float) id.x * 64f, 0.0f, (float) id.y * 64f);
  }

  private void SetZoneGenerated(Vector2i zoneID) => this.m_generatedZones.Add(zoneID);

  private bool IsZoneGenerated(Vector2i zoneID) => this.m_generatedZones.Contains(zoneID);

  public bool IsZoneReadyForType(Vector2i zoneID, ZDO.ObjectType objectType)
  {
    if (this.m_loadingObjectsInZones.Count <= 0 || !this.m_loadingObjectsInZones.ContainsKey(zoneID))
      return true;
    foreach (ZDO zdo in this.m_loadingObjectsInZones[zoneID])
    {
      if (objectType < zdo.Type)
        return false;
    }
    return true;
  }

  public void SetLoadingInZone(ZDO zdo)
  {
    Vector2i sector = zdo.GetSector();
    if (this.m_loadingObjectsInZones.ContainsKey(sector))
      this.m_loadingObjectsInZones[sector].Add(zdo);
    else
      this.m_loadingObjectsInZones.Add(sector, new List<ZDO>()
      {
        zdo
      });
  }

  public void UnsetLoadingInZone(ZDO zdo)
  {
    Vector2i sector = zdo.GetSector();
    this.m_loadingObjectsInZones[sector].Remove(zdo);
    if (this.m_loadingObjectsInZones[sector].Count > 0)
      return;
    this.m_loadingObjectsInZones.Remove(sector);
  }

  public bool SkipSaving() => this.m_error || this.m_didZoneTest;

  public float TimeSinceStart() => this.m_lastFixedTime - this.m_startTime;

  public void ResetGlobalKeys()
  {
    this.ClearGlobalKeys();
    this.SetStartingGlobalKeys(false);
    this.SendGlobalKeys(ZRoutedRpc.Everybody);
  }

  public void ResetWorldKeys()
  {
    for (int index = 0; index < 32 /*0x20*/; ++index)
      this.RemoveGlobalKey(((GlobalKeys) index).ToString());
  }

  public void SetStartingGlobalKeys(bool send = true)
  {
    for (int index = 0; index < 32 /*0x20*/; ++index)
      this.GlobalKeyRemove(((GlobalKeys) index).ToString(), false);
    string combinedString = (string) null;
    this.m_tempKeys.Clear();
    this.m_tempKeys.AddRange((IEnumerable<string>) ZNet.World.m_startingGlobalKeys);
    foreach (string tempKey in this.m_tempKeys)
    {
      string str;
      GlobalKeys gk;
      ZoneSystem.GetKeyValue(tempKey.ToLower(), out str, out gk);
      if (gk == GlobalKeys.Preset)
        combinedString = str;
      this.GlobalKeyAdd(tempKey, false);
    }
    if (combinedString != null)
      ServerOptionsGUI.m_instance.SetPreset(ZNet.World, combinedString);
    if (!send)
      return;
    this.SendGlobalKeys(ZRoutedRpc.Everybody);
  }

  public void SetGlobalKey(GlobalKeys key, float value)
  {
    this.SetGlobalKey($"{key} {value.ToString((IFormatProvider) CultureInfo.InvariantCulture)}");
  }

  public void SetGlobalKey(GlobalKeys key) => this.SetGlobalKey(key.ToString());

  public void SetGlobalKey(string name)
  {
    ZRoutedRpc.instance.InvokeRoutedRPC(nameof (SetGlobalKey), (object) name);
  }

  public bool GetGlobalKey(GlobalKeys key) => this.m_globalKeysEnums.Contains(key);

  public bool GetGlobalKey(GlobalKeys key, out string value)
  {
    return this.m_globalKeysValues.TryGetValue(key.ToString().ToLower(), out value);
  }

  public bool GetGlobalKey(GlobalKeys key, out float value)
  {
    string s;
    if (this.m_globalKeysValues.TryGetValue(key.ToString().ToLower(), out s) && float.TryParse(s, NumberStyles.Any, (IFormatProvider) CultureInfo.InvariantCulture, out value))
      return true;
    value = 0.0f;
    return false;
  }

  public bool GetGlobalKey(string name) => this.GetGlobalKey(name, out string _);

  public bool GetGlobalKey(string name, out string value)
  {
    return this.m_globalKeysValues.TryGetValue(name.ToLower(), out value);
  }

  public bool GetGlobalKeyExact(string fullLine) => this.m_globalKeys.Contains(fullLine);

  public bool CheckKey(string key, GameKeyType type = GameKeyType.Global, bool trueWhenKeySet = true)
  {
    switch (type)
    {
      case GameKeyType.Global:
        return ZoneSystem.instance.GetGlobalKey(key) == trueWhenKeySet;
      case GameKeyType.Player:
        return (bool) (UnityEngine.Object) Player.m_localPlayer && Player.m_localPlayer.HaveUniqueKey(key) == trueWhenKeySet;
      default:
        ZLog.LogError((object) "Unknown GameKeyType type");
        return false;
    }
  }

  private void RPC_SetGlobalKey(long sender, string name)
  {
    if (this.m_globalKeys.Contains(name))
      return;
    this.GlobalKeyAdd(name);
    this.SendGlobalKeys(ZRoutedRpc.Everybody);
  }

  public void RemoveGlobalKey(GlobalKeys key) => this.RemoveGlobalKey(key.ToString());

  public void RemoveGlobalKey(string name)
  {
    ZRoutedRpc.instance.InvokeRoutedRPC(nameof (RemoveGlobalKey), (object) name);
  }

  private void RPC_RemoveGlobalKey(long sender, string name)
  {
    if (!this.GlobalKeyRemove(name))
      return;
    this.SendGlobalKeys(ZRoutedRpc.Everybody);
  }

  public List<string> GetGlobalKeys() => new List<string>((IEnumerable<string>) this.m_globalKeys);

  public Dictionary<Vector2i, ZoneSystem.LocationInstance>.ValueCollection GetLocationList()
  {
    return this.m_locationInstances.Values;
  }

  public bool LocationsGenerated
  {
    get => this.m_locationsGenerated;
    private set
    {
      this.m_locationsGenerated = value;
      if (!this.m_locationsGenerated)
        return;
      Action locationsCompleted = this.m_generateLocationsCompleted;
      if (locationsCompleted != null)
        locationsCompleted();
      this.m_generateLocationsCompleted = (Action) null;
    }
  }

  public float GenerateLocationsProgress => this.m_generateLocationsProgress;

  public event Action GenerateLocationsCompleted
  {
    add
    {
      if (this.m_locationsGenerated)
      {
        if (value == null)
          return;
        value();
      }
      else
        this.m_generateLocationsCompleted += value;
    }
    remove => this.m_generateLocationsCompleted -= value;
  }

  private class ZoneData
  {
    public GameObject m_root;
    public float m_ttl;
  }

  private class ClearArea
  {
    public Vector3 m_center;
    public float m_radius;

    public ClearArea(Vector3 p, float r)
    {
      this.m_center = p;
      this.m_radius = r;
    }
  }

  [Serializable]
  public class ZoneVegetation
  {
    public string m_name = "veg";
    public GameObject m_prefab;
    public bool m_enable = true;
    public float m_min;
    public float m_max = 10f;
    public bool m_forcePlacement;
    public float m_scaleMin = 1f;
    public float m_scaleMax = 1f;
    public float m_randTilt;
    public float m_chanceToUseGroundTilt;
    [BitMask(typeof (Heightmap.Biome))]
    public Heightmap.Biome m_biome;
    [BitMask(typeof (Heightmap.BiomeArea))]
    public Heightmap.BiomeArea m_biomeArea = Heightmap.BiomeArea.Everything;
    public bool m_blockCheck = true;
    public bool m_snapToStaticSolid;
    public float m_minAltitude = -1000f;
    public float m_maxAltitude = 1000f;
    public float m_minVegetation;
    public float m_maxVegetation;
    [Header("Samples points around and choses the highest vegetation")]
    [Tooltip("Samples points around the placement point and choses the point with most total vegetation value")]
    public bool m_surroundCheckVegetation;
    [Tooltip("How far to check surroundings")]
    public float m_surroundCheckDistance = 20f;
    [Tooltip("How many layers of circles to sample. (If distance is large you should have more layers)")]
    public int m_surroundCheckLayers = 2;
    [Tooltip("How much better than the average an accepted point will be. (Procentually between average and best)")]
    public float m_surroundBetterThanAverage;
    [Space(10f)]
    public float m_minOceanDepth;
    public float m_maxOceanDepth;
    public float m_minTilt;
    public float m_maxTilt = 90f;
    public float m_terrainDeltaRadius;
    public float m_maxTerrainDelta = 2f;
    public float m_minTerrainDelta;
    public bool m_snapToWater;
    public float m_groundOffset;
    public int m_groupSizeMin = 1;
    public int m_groupSizeMax = 1;
    public float m_groupRadius;
    [Header("Distance from center")]
    public float m_minDistanceFromCenter;
    public float m_maxDistanceFromCenter;
    [Header("Forest fractal 0-1 inside forest")]
    public bool m_inForest;
    public float m_forestTresholdMin;
    public float m_forestTresholdMax = 1f;
    [HideInInspector]
    public bool m_foldout;

    public ZoneSystem.ZoneVegetation Clone() => this.MemberwiseClone() as ZoneSystem.ZoneVegetation;
  }

  [Serializable]
  public class ZoneLocation
  {
    public string m_name;
    public bool m_enable = true;
    [HideInInspector]
    public string m_prefabName;
    public SoftReference<GameObject> m_prefab;
    [BitMask(typeof (Heightmap.Biome))]
    public Heightmap.Biome m_biome;
    [BitMask(typeof (Heightmap.BiomeArea))]
    public Heightmap.BiomeArea m_biomeArea = Heightmap.BiomeArea.Everything;
    public int m_quantity;
    public bool m_prioritized;
    public bool m_centerFirst;
    public bool m_unique;
    public string m_group = "";
    public float m_minDistanceFromSimilar;
    public string m_groupMax = "";
    public float m_maxDistanceFromSimilar;
    public bool m_iconAlways;
    public bool m_iconPlaced;
    public bool m_randomRotation = true;
    public bool m_slopeRotation;
    public bool m_snapToWater;
    public float m_interiorRadius;
    public float m_exteriorRadius;
    public bool m_clearArea;
    public float m_minTerrainDelta;
    public float m_maxTerrainDelta = 2f;
    public float m_minimumVegetation;
    public float m_maximumVegetation = 1f;
    [Header("Samples points around and choses the highest vegetation")]
    [Tooltip("Samples points around the placement point and choses the point with most total vegetation value")]
    public bool m_surroundCheckVegetation;
    [Tooltip("How far to check surroundings")]
    public float m_surroundCheckDistance = 20f;
    [Tooltip("How many layers of circles to sample. (If distance is large you should have more layers)")]
    public int m_surroundCheckLayers = 2;
    [Tooltip("How much better than the average an accepted point will be. (Procentually between average and best)")]
    public float m_surroundBetterThanAverage;
    [Header("Forest fractal 0-1 inside forest")]
    public bool m_inForest;
    public float m_forestTresholdMin;
    public float m_forestTresholdMax = 1f;
    [Header("Distance from center")]
    public float m_minDistanceFromCenter;
    public float m_maxDistanceFromCenter;
    [Space(10f)]
    public float m_minDistance;
    public float m_maxDistance;
    public float m_minAltitude = -1000f;
    public float m_maxAltitude = 1000f;
    [HideInInspector]
    public bool m_foldout;

    public ZoneSystem.ZoneLocation Clone() => this.MemberwiseClone() as ZoneSystem.ZoneLocation;

    public int Hash => this.m_prefab.Name.GetStableHashCode();
  }

  public struct LocationInstance
  {
    public ZoneSystem.ZoneLocation m_location;
    public Vector3 m_position;
    public bool m_placed;
  }

  private class LocationPrefabLoadData
  {
    private SoftReference<GameObject> m_prefab;
    private SoftReference<GameObject>[] m_possibleRooms;
    private int m_roomsToLoad;
    private bool m_isFirstSpawn;
    public int m_iterationLifetime;

    public bool IsLoaded { get; private set; }

    public AssetID PrefabAssetID => this.m_prefab.m_assetID;

    public LocationPrefabLoadData(SoftReference<GameObject> prefab, bool isFirstSpawn)
    {
      this.m_prefab = prefab;
      this.m_isFirstSpawn = isFirstSpawn;
      this.m_roomsToLoad = 0;
      this.m_prefab.LoadAsync(new LoadedHandler(this.OnPrefabLoaded));
    }

    public void Release()
    {
      if (!this.m_prefab.IsValid)
        return;
      this.m_prefab.Release();
      this.m_prefab.m_assetID = new AssetID();
      if (this.m_possibleRooms == null)
        return;
      for (int index = 0; index < this.m_possibleRooms.Length; ++index)
        this.m_possibleRooms[index].Release();
      this.m_possibleRooms = (SoftReference<GameObject>[]) null;
    }

    private void OnPrefabLoaded(AssetID assetID, LoadResult result)
    {
      if (result != LoadResult.Succeeded || !this.m_prefab.IsValid)
        return;
      if (!this.m_isFirstSpawn)
      {
        this.IsLoaded = true;
      }
      else
      {
        DungeonGenerator[] componentsInChildren = Utils.GetEnabledComponentsInChildren<DungeonGenerator>(this.m_prefab.Asset);
        if (componentsInChildren.Length == 0)
        {
          this.IsLoaded = true;
        }
        else
        {
          if (componentsInChildren.Length > 1)
            ZLog.LogWarning((object) $"Location {this.m_prefab.Asset.name} has more than one dungeon generator! The preloading code only works for one dungeon generator per location.");
          this.m_possibleRooms = componentsInChildren[0].GetAvailableRoomPrefabs();
          this.m_roomsToLoad = this.m_possibleRooms.Length;
          for (int index = 0; index < this.m_possibleRooms.Length; ++index)
            this.m_possibleRooms[index].LoadAsync(new LoadedHandler(this.OnRoomLoaded));
        }
      }
    }

    private void OnRoomLoaded(AssetID assetID, LoadResult result)
    {
      if (result != LoadResult.Succeeded)
        return;
      --this.m_roomsToLoad;
      if (this.m_possibleRooms == null || this.m_roomsToLoad > 0)
        return;
      this.IsLoaded = true;
    }
  }

  public enum SpawnMode
  {
    Full,
    Client,
    Ghost,
  }
}
