# Custom Environment Box Implementation Plan

## Overview

Create scalable, interactive environment boxes similar to dungeon interiors but fully customizable. These boxes will control:
- Skybox/visual environment
- Weather conditions
- Biome effects (without affecting heightmap)
- Terrain paint (vanilla and custom)
- Ambient lighting and fog

## Decompiled Code Analysis

### EnvZone - The Key Reference

The vanilla `EnvZone` is surprisingly simple:

```csharp
public class EnvZone : MonoBehaviour
{
    public string m_environment = "";
    public bool m_force = true;
    public MeshRenderer m_exteriorMesh;
    private static EnvZone s_triggered;

    private void OnTriggerStay(Collider collider)
    {
        Player component = collider.GetComponent<Player>();
        if (component == null || Player.m_localPlayer != component)
            return;
        
        if (this.m_force && string.IsNullOrEmpty(EnvMan.instance.m_debugEnv))
            EnvMan.instance.SetForceEnvironment(this.m_environment);
        
        EnvZone.s_triggered = this;
        
        if (this.m_exteriorMesh != null)
            this.m_exteriorMesh.forceRenderingOff = false;
    }

    private void OnTriggerExit(Collider collider)
    {
        if (EnvZone.s_triggered != this) return;
        // ... clear environment on exit
        EnvMan.instance.SetForceEnvironment("");
    }
}
```

**Key Insight:** All we need to do is call `EnvMan.instance.SetForceEnvironment(environmentName)` to override!

### EnvMan Key Methods

```csharp
// Set forced environment (our main entry point)
public void SetForceEnvironment(string env);

// Get environment by name
public EnvSetup GetEnv(string name);

// Get current environment
public EnvSetup GetCurrentEnvironment();

// All available environments
public List<EnvSetup> m_environments;
```

### Location - How Dungeons Work

```csharp
// In Location.Awake():
if (this.m_hasInterior)
{
    Vector3 zoneCenter = this.GetZoneCenter();
    // Interior spawns 5000 units UP!
    GameObject gameObject = Instantiate(this.m_interiorPrefab, 
        new Vector3(zoneCenter.x, this.transform.position.y + 5000f, zoneCenter.z),
        Quaternion.identity, this.transform);
    gameObject.transform.localScale = new Vector3(64f, 500f, 64f);
    gameObject.GetComponent<EnvZone>().m_environment = this.m_interiorEnvironment;
}
```

**Key Insight:** Dungeon interiors are just EnvZones spawned at Y+5000! We don't need to do that - we can create EnvZones at ground level.

### EnvSetup - All Environment Properties

```csharp
public class EnvSetup
{
    public string m_name;
    public bool m_isWet, m_isFreezing, m_isFreezingAtNight, m_isCold, m_isColdAtNight;
    public bool m_alwaysDark;
    
    // Colors for different times of day
    public Color m_ambColorNight, m_ambColorDay;
    public Color m_fogColorNight, m_fogColorMorning, m_fogColorDay, m_fogColorEvening;
    public Color m_sunColorNight, m_sunColorMorning, m_sunColorDay, m_sunColorEvening;
    
    // Fog density
    public float m_fogDensityNight, m_fogDensityMorning, m_fogDensityDay, m_fogDensityEvening;
    
    // Sun/light
    public float m_lightIntensityDay, m_lightIntensityNight;
    public float m_sunAngle;
    
    // Wind
    public float m_windMin, m_windMax;
    
    // Effects
    public GameObject m_envObject;
    public GameObject[] m_psystems;
    public bool m_psystemsOutsideOnly;
    public float m_rainCloudAlpha;
    
    // Audio
    public AudioClip m_ambientLoop;
    public float m_ambientVol;
}
```

## Existing Mod Code to Leverage

Based on your open files, we already have:
- **TerritoryRuntime.Environment.cs** - Territory-based environment handling
- **MaterialVariantSystem.cs** - Material switching
- **GrassOverlaySystem.cs** - Grass/vegetation control
- **TerrainMaterialCache.cs** - Terrain material management
- **DirtFloorController** - Custom terrain system

## Implementation Architecture

### Core Components

```
CustomEnvBox/
??? CustomEnvBoxController.cs      // Main component on the prefab
??? CustomEnvBoxTrigger.cs         // Trigger volume for player detection
??? CustomEnvBoxInteract.cs        // Interaction point for toggling
??? CustomEnvBoxVisuals.cs         // Material/mesh visibility control
??? CustomEnvBoxEnvironment.cs     // Environment/weather/biome logic
??? CustomEnvBoxScaler.cs          // Scaling with keybinds
??? CustomEnvBoxPatches.cs         // Harmony patches for biome/weather override
??? CustomEnvBoxConfig.cs          // Serializable configuration
```

### Class Diagram

```
???????????????????????????????????????????????????????????????????
?                    CustomEnvBoxController                        ?
?  - Primary MonoBehaviour on prefab                              ?
?  - Manages all sub-components                                   ?
?  - ZDO persistence                                              ?
???????????????????????????????????????????????????????????????????
?  Fields:                                                        ?
?  + Vector3 BoxSize (scalable)                                   ?
?  + bool IsVisible (toggle box mesh)                             ?
?  + bool IsActive (toggle env effects)                           ?
?  + EnvBoxEnvironment CurrentEnvironment                         ?
?  + Biome ForcedBiome                                            ?
?  + bool AffectsWeather                                          ?
?  + bool AffectsTerrain                                          ?
???????????????????????????????????????????????????????????????????
                              ?
          ?????????????????????????????????????????
          ?                   ?                   ?
          ?                   ?                   ?
??????????????????? ??????????????????? ???????????????????
? EnvBoxTrigger   ? ? EnvBoxVisuals   ? ? EnvBoxInteract  ?
? - Box collider  ? ? - Mesh renderer ? ? - Hover UI      ?
? - Enter/Exit    ? ? - Materials     ? ? - Cycle options ?
? - Player detect ? ? - Skybox setup  ? ? - Scale control ?
??????????????????? ??????????????????? ???????????????????
```

## Feature Specifications

### 1. Scalable Box System

**Keybind Controls (while holding env box tool):**
```
Shift + Mouse Wheel Up/Down    = Scale uniformly (all axes)
Ctrl + Mouse Wheel Up/Down     = Scale height only (Y axis)
Alt + Mouse Wheel Up/Down      = Scale width/depth (X/Z axes)
Shift + Ctrl + Scroll          = Fine-tune scale (0.1m increments)
```

**Implementation:**
```csharp
public class CustomEnvBoxScaler : MonoBehaviour
{
    public Vector3 MinSize = new Vector3(4f, 3f, 4f);
    public Vector3 MaxSize = new Vector3(200f, 100f, 200f);
    public float ScaleStep = 0.5f;
    public float FineScaleStep = 0.1f;
    
    private BoxCollider _trigger;
    private MeshFilter _meshFilter;
    
    public void ScaleUniform(float delta)
    {
        Vector3 newSize = CurrentSize + Vector3.one * delta * ScaleStep;
        SetSize(Vector3.Max(MinSize, Vector3.Min(MaxSize, newSize)));
    }
    
    public void ScaleHeight(float delta)
    {
        Vector3 newSize = CurrentSize;
        newSize.y = Mathf.Clamp(newSize.y + delta * ScaleStep, MinSize.y, MaxSize.y);
        SetSize(newSize);
    }
    
    private void SetSize(Vector3 size)
    {
        CurrentSize = size;
        _trigger.size = size;
        RegenerateMesh(); // Update visual mesh
        SaveToZDO();
    }
}
```

### 2. Environment Toggle System

**Interaction Point (top center of box):**
- Spawns at `box.position + Vector3.up * (boxHeight / 2)`
- Custom hover text showing current settings
- Cycle through options with keybinds

**Toggle Options:**
```csharp
public enum EnvBoxMode
{
    Disabled,           // No effect, box invisible
    EnvironmentOnly,    // Only environment/weather
    BiomeOnly,          // Only biome paint override
    Full,               // All effects active
    CustomSkybox        // Full + custom skybox rendering
}

public class EnvBoxSettings
{
    public EnvBoxMode Mode;
    public string EnvironmentName;      // "Clear", "Misty", "Rain", etc.
    public Heightmap.Biome ForcedBiome; // Meadows, BlackForest, etc.
    public bool OverrideWeather;
    public bool OverrideDayNight;
    public float CustomTime;            // 0-1 for forced time of day
    public bool ShowSkybox;             // True = skybox, False = black void
    public Color AmbientOverride;
    public float FogDensityOverride;
}
```

### 3. Visibility States

```
???????????????????????????????????????????????????????????????????
?  VISIBILITY STATE: FULL                                         ?
?  - Box mesh visible with semi-transparent material              ?
?  - Edge highlighting                                            ?
?  - Interaction point visible at top                             ?
???????????????????????????????????????????????????????????????????
?  VISIBILITY STATE: EDGES_ONLY                                   ?
?  - Only wireframe edges visible                                 ?
?  - Good for building while seeing env boundaries                ?
???????????????????????????????????????????????????????????????????
?  VISIBILITY STATE: HIDDEN                                       ?
?  - Box mesh completely invisible                                ?
?  - Interaction point + sign spawned at top center               ?
?  - Sign text: "Environment Box (Hidden) - [E] to configure"     ?
???????????????????????????????????????????????????????????????????
?  VISIBILITY STATE: PREVIEW                                      ?
?  - Shows what environment looks like from outside               ?
?  - Useful for dungeon entrance previews                         ?
???????????????????????????????????????????????????????????????????
```

### 4. Biome Override WITHOUT Heightmap Changes

**Key Insight:** Valheim's biome system ties biome to Heightmap vertices. We need to intercept biome queries and return our forced biome when inside an env box.

**Harmony Patches Required:**

```csharp
[HarmonyPatch]
public static class CustomEnvBoxPatches
{
    // Track which env box the player is in
    private static CustomEnvBoxController _currentEnvBox;
    
    /// <summary>
    /// Patch Heightmap.GetBiome to return forced biome when in env box.
    /// This affects terrain paint, vegetation, creatures, etc.
    /// Does NOT modify the actual heightmap data.
    /// </summary>
    [HarmonyPatch(typeof(Heightmap), "GetBiome", typeof(Vector3))]
    [HarmonyPostfix]
    private static void GetBiome_Postfix(Vector3 point, ref Heightmap.Biome __result)
    {
        if (_currentEnvBox != null && _currentEnvBox.Settings.ForcedBiome != Heightmap.Biome.None)
        {
            // Check if point is inside the env box bounds
            if (_currentEnvBox.ContainsPoint(point))
            {
                __result = _currentEnvBox.Settings.ForcedBiome;
            }
        }
    }
    
    /// <summary>
    /// Patch EnvMan.GetCurrentEnvironment to return forced environment.
    /// </summary>
    [HarmonyPatch(typeof(EnvMan), "GetCurrentEnvironment")]
    [HarmonyPostfix]
    private static void GetCurrentEnvironment_Postfix(ref EnvSetup __result)
    {
        if (_currentEnvBox != null && _currentEnvBox.Settings.OverrideEnvironment)
        {
            var forcedEnv = EnvMan.instance.GetEnv(_currentEnvBox.Settings.EnvironmentName);
            if (forcedEnv != null)
            {
                __result = forcedEnv;
            }
        }
    }
    
    /// <summary>
    /// Patch weather particle spawning to respect env box boundaries.
    /// </summary>
    [HarmonyPatch(typeof(ParticleMist), "Update")]
    [HarmonyPrefix]
    private static bool ParticleMist_Update_Prefix(ParticleMist __instance)
    {
        if (_currentEnvBox != null && !_currentEnvBox.Settings.AllowWeatherParticles)
        {
            // Disable weather particles inside env box
            __instance.enabled = false;
            return false;
        }
        return true;
    }
}
```

### 5. Custom Skybox System

**Skybox Modes:**
```csharp
public enum SkyboxMode
{
    Inherit,            // Use world skybox (default outdoor)
    DungeonBlack,       // Pure black void (like dungeon interiors)
    CustomTexture,      // Use a custom skybox texture
    ProceduralOverride, // Override procedural sky parameters
    Gradient            // Simple gradient background
}

public class EnvBoxSkybox
{
    public SkyboxMode Mode;
    public Material CustomSkyboxMaterial;
    public Color GradientTop;
    public Color GradientBottom;
    public float SkyboxRotation;
    public float Exposure;
}
```

**Implementation for Skybox Switching:**
```csharp
public class CustomEnvBoxVisuals : MonoBehaviour
{
    private Material _originalSkybox;
    private Material _envBoxSkybox;
    
    public void OnPlayerEnter()
    {
        if (Settings.SkyboxMode != SkyboxMode.Inherit)
        {
            _originalSkybox = RenderSettings.skybox;
            
            switch (Settings.SkyboxMode)
            {
                case SkyboxMode.DungeonBlack:
                    RenderSettings.skybox = CreateBlackSkybox();
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = Settings.AmbientColor;
                    break;
                    
                case SkyboxMode.CustomTexture:
                    RenderSettings.skybox = Settings.CustomSkyboxMaterial;
                    break;
                    
                case SkyboxMode.Gradient:
                    RenderSettings.skybox = CreateGradientSkybox(
                        Settings.GradientTop, Settings.GradientBottom);
                    break;
            }
        }
    }
    
    public void OnPlayerExit()
    {
        if (_originalSkybox != null)
        {
            RenderSettings.skybox = _originalSkybox;
            // Restore EnvMan's normal ambient handling
            EnvMan.instance.SetEnv(EnvMan.instance.GetCurrentEnvironment(), true);
        }
    }
    
    private Material CreateBlackSkybox()
    {
        var mat = new Material(Shader.Find("Skybox/Procedural"));
        mat.SetColor("_SkyTint", Color.black);
        mat.SetColor("_GroundColor", Color.black);
        mat.SetFloat("_Exposure", 0f);
        return mat;
    }
}
```

### 6. Interaction System

**Hover UI Display:**
```
?????????????????????????????????????????????????????????
?  ENVIRONMENT BOX                                       ?
?????????????????????????????????????????????????????????
?  Mode: Full Environment                                ?
?  Environment: Crypt                                    ?
?  Biome Override: Swamp                                 ?
?  Skybox: Dungeon Black                                 ?
?  Size: 20m x 8m x 20m                                  ?
?????????????????????????????????????????????????????????
?  [E] Cycle Environment                                 ?
?  [Shift+E] Cycle Biome                                 ?
?  [Ctrl+E] Cycle Skybox Mode                            ?
?  [H] Toggle Visibility                                 ?
?  [Shift+Scroll] Scale Box                              ?
?????????????????????????????????????????????????????????
```

**Interaction Controller:**
```csharp
public class CustomEnvBoxInteract : MonoBehaviour, Hoverable, Interactable
{
    private CustomEnvBoxController _controller;
    private GameObject _hiddenMarkerSign;
    private GameObject _interactionPoint;
    
    // Available environments to cycle through
    private static readonly string[] Environments = {
        "Clear", "Twilight_Clear", "Misty", "Darklands_dark",
        "Heath clear", "DeepForest Mist", "GDKing",
        "Rain", "LightRain", "ThunderStorm", "Eikthyr",
        "GoblinKing", "nofogts", "Crypt", "SunkenCrypt",
        "Caves", "InfectedMine", "Mistlands_clear", "Mistlands_mist"
    };
    
    private static readonly Heightmap.Biome[] Biomes = {
        Heightmap.Biome.None,       // No override
        Heightmap.Biome.Meadows,
        Heightmap.Biome.BlackForest,
        Heightmap.Biome.Swamp,
        Heightmap.Biome.Mountain,
        Heightmap.Biome.Plains,
        Heightmap.Biome.Mistlands,
        Heightmap.Biome.AshLands,
        Heightmap.Biome.DeepNorth,
        Heightmap.Biome.Ocean
    };
    
    public string GetHoverText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<color=orange><b>Environment Box</b></color>");
        sb.AppendLine($"Mode: {_controller.Settings.Mode}");
        sb.AppendLine($"Environment: {_controller.Settings.EnvironmentName}");
        sb.AppendLine($"Biome: {_controller.Settings.ForcedBiome}");
        sb.AppendLine($"Skybox: {_controller.Settings.SkyboxMode}");
        sb.AppendLine($"Size: {_controller.BoxSize.x:F1}m x {_controller.BoxSize.y:F1}m x {_controller.BoxSize.z:F1}m");
        sb.AppendLine();
        sb.AppendLine("[<color=yellow>E</color>] Cycle Environment");
        sb.AppendLine("[<color=yellow>Shift+E</color>] Cycle Biome");
        sb.AppendLine("[<color=yellow>Ctrl+E</color>] Cycle Skybox");
        sb.AppendLine("[<color=yellow>H</color>] Toggle Visibility");
        return sb.ToString();
    }
    
    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (hold) return false;
        
        bool shift = Input.GetKey(KeyCode.LeftShift);
        bool ctrl = Input.GetKey(KeyCode.LeftControl);
        
        if (ctrl)
        {
            CycleSkyboxMode();
        }
        else if (shift)
        {
            CycleBiome();
        }
        else
        {
            CycleEnvironment();
        }
        
        return true;
    }
    
    public void OnVisibilityToggled(bool isVisible)
    {
        if (!isVisible)
        {
            // Spawn marker sign at top center
            SpawnHiddenMarker();
        }
        else
        {
            // Remove marker sign
            DestroyHiddenMarker();
        }
    }
    
    private void SpawnHiddenMarker()
    {
        Vector3 markerPos = transform.position + Vector3.up * (_controller.BoxSize.y / 2f);
        
        // Create interaction point
        _interactionPoint = new GameObject("EnvBox_InteractionPoint");
        _interactionPoint.transform.position = markerPos;
        _interactionPoint.transform.SetParent(transform);
        
        // Add collider for interaction
        var col = _interactionPoint.AddComponent<SphereCollider>();
        col.radius = 0.5f;
        col.isTrigger = true;
        
        // Spawn a sign prefab if available
        var signPrefab = ZNetScene.instance?.GetPrefab("sign");
        if (signPrefab != null)
        {
            _hiddenMarkerSign = Instantiate(signPrefab, markerPos, Quaternion.identity);
            var sign = _hiddenMarkerSign.GetComponent<Sign>();
            if (sign != null)
            {
                sign.SetText("Environment Box (Hidden)\n[E] to configure");
            }
        }
    }
}
```

### 7. Terrain Paint Integration

**Override Custom Terrain Paint:**
```csharp
public class EnvBoxTerrainPaintOverride
{
    /// <summary>
    /// When player paints terrain inside an env box with biome override,
    /// use the forced biome's paint colors instead of world biome.
    /// </summary>
    public static void OnTerrainPaint(DirtFloorController dirtFloor, Vector3 paintPos)
    {
        var envBox = CustomEnvBoxController.GetEnvBoxAtPosition(paintPos);
        if (envBox != null && envBox.Settings.ForcedBiome != Heightmap.Biome.None)
        {
            // Get paint colors for forced biome
            var biomeColors = GetBiomePaintColors(envBox.Settings.ForcedBiome);
            dirtFloor.SetBiomePaintOverride(biomeColors);
        }
    }
    
    private static BiomePaintColors GetBiomePaintColors(Heightmap.Biome biome)
    {
        // Return appropriate terrain colors for each biome
        switch (biome)
        {
            case Heightmap.Biome.Meadows:
                return new BiomePaintColors(
                    grass: new Color(0.4f, 0.6f, 0.2f),
                    dirt: new Color(0.4f, 0.3f, 0.2f),
                    cultivated: new Color(0.3f, 0.2f, 0.1f));
                    
            case Heightmap.Biome.Swamp:
                return new BiomePaintColors(
                    grass: new Color(0.2f, 0.3f, 0.1f),
                    dirt: new Color(0.15f, 0.1f, 0.05f),
                    cultivated: new Color(0.1f, 0.08f, 0.03f));
                    
            // ... other biomes
            default:
                return BiomePaintColors.Default;
        }
    }
}
```

## ZDO Persistence

**Saved Data:**
```csharp
public class EnvBoxZDOData
{
    // Size and position
    public const int HASH_SIZE_X = "envbox_size_x".GetStableHashCode();
    public const int HASH_SIZE_Y = "envbox_size_y".GetStableHashCode();
    public const int HASH_SIZE_Z = "envbox_size_z".GetStableHashCode();
    
    // Settings
    public const int HASH_MODE = "envbox_mode".GetStableHashCode();
    public const int HASH_ENVIRONMENT = "envbox_env".GetStableHashCode();
    public const int HASH_BIOME = "envbox_biome".GetStableHashCode();
    public const int HASH_SKYBOX = "envbox_skybox".GetStableHashCode();
    public const int HASH_VISIBLE = "envbox_visible".GetStableHashCode();
    
    // Colors
    public const int HASH_AMBIENT_R = "envbox_amb_r".GetStableHashCode();
    public const int HASH_AMBIENT_G = "envbox_amb_g".GetStableHashCode();
    public const int HASH_AMBIENT_B = "envbox_amb_b".GetStableHashCode();
}

public void SaveToZDO()
{
    var zdo = m_nview.GetZDO();
    if (zdo == null) return;
    
    zdo.Set(EnvBoxZDOData.HASH_SIZE_X, BoxSize.x);
    zdo.Set(EnvBoxZDOData.HASH_SIZE_Y, BoxSize.y);
    zdo.Set(EnvBoxZDOData.HASH_SIZE_Z, BoxSize.z);
    zdo.Set(EnvBoxZDOData.HASH_MODE, (int)Settings.Mode);
    zdo.Set(EnvBoxZDOData.HASH_ENVIRONMENT, Settings.EnvironmentName);
    zdo.Set(EnvBoxZDOData.HASH_BIOME, (int)Settings.ForcedBiome);
    zdo.Set(EnvBoxZDOData.HASH_SKYBOX, (int)Settings.SkyboxMode);
    zdo.Set(EnvBoxZDOData.HASH_VISIBLE, IsVisible);
}
```

## Implementation Phases

### Phase 1: Core Framework (Week 1)
- [ ] Decompile and study `EnvZone`, `EnvMan`, `EnvSetup`
- [ ] Create basic `CustomEnvBoxController` prefab
- [ ] Implement box collider trigger system
- [ ] Basic ZDO persistence

### Phase 2: Scaling System (Week 1-2)
- [ ] Implement `CustomEnvBoxScaler`
- [ ] Keybind handling for scale operations
- [ ] Dynamic mesh generation for visual box
- [ ] Integration with placement system

### Phase 3: Environment Override (Week 2)
- [ ] Harmony patches for `EnvMan.GetCurrentEnvironment`
- [ ] Environment cycling interaction
- [ ] Weather particle control
- [ ] Ambient light/fog override

### Phase 4: Biome Override (Week 2-3)
- [ ] Harmony patches for `Heightmap.GetBiome`
- [ ] Biome cycling interaction
- [ ] Terrain paint integration (both vanilla and custom)
- [ ] Vegetation/clutter override within bounds

### Phase 5: Skybox System (Week 3)
- [ ] Skybox mode implementation
- [ ] Black void mode (dungeon style)
- [ ] Custom skybox texture support
- [ ] Transition effects on enter/exit

### Phase 6: Visibility & Interaction (Week 3-4)
- [ ] Full/Edges/Hidden visibility modes
- [ ] Hidden marker sign spawning
- [ ] Interaction point at top center
- [ ] Hover UI with all options

### Phase 7: Polish & Testing (Week 4)
- [ ] Multiplayer sync testing
- [ ] Performance optimization
- [ ] Edge cases (overlapping boxes, etc.)
- [ ] Documentation and tutorials

## Multiplayer Considerations

1. **Authority**: Only the owner can modify env box settings
2. **Sync**: Use RPC calls for setting changes
3. **Local Effects**: Skybox/ambient changes are local to each client
4. **ZDO Sync**: Box size and settings sync via ZDO system

```csharp
public void RPC_SyncSettings(long sender, string settingsJson)
{
    if (!m_nview.IsOwner()) return;
    
    var settings = JsonUtility.FromJson<EnvBoxSettings>(settingsJson);
    ApplySettings(settings);
    
    // Broadcast to all clients
    m_nview.GetZDO()?.Set(HASH_SETTINGS, settingsJson);
}
```

## Known Valheim Environment Names

```csharp
// From EnvMan.m_environments
public static readonly string[] VanillaEnvironments = {
    // Clear weather
    "Clear", "Twilight_Clear", "Heath clear", 
    
    // Mist/Fog
    "Misty", "DeepForest Mist", "Mistlands_clear", "Mistlands_mist",
    
    // Dark
    "Darklands_dark", "nofogts",
    
    // Rain
    "Rain", "LightRain", "ThunderStorm",
    
    // Dungeon interiors
    "Crypt", "SunkenCrypt", "Caves", "InfectedMine",
    
    // Boss environments
    "Eikthyr", "GDKing", "Bonemass", "Moder", "GoblinKing",
    
    // Ashlands
    "Ashlands_SeaOfFire", "Ashlands_Meteor", "InfernalMine",
    
    // Special
    "Hildir_camp", "Charred_Fortress"
};
```

## File Structure (IMPLEMENTED)

```
FiresNPCs/
? Modules/
    ? EnvironmentBoxes/
        ? EnvironmentBoxController.cs    ? DONE - Main controller with all features
        ? EnvironmentBoxScaler.cs        ? DONE - Shift+Scroll scaling handler
        ? EnvironmentBoxPatches.cs       ? DONE - Harmony patches for biome override
        ? EnvironmentReference.txt       ? DONE - Complete environment dump reference
        ? SkyboxShaderReference.txt      ? DONE - Skybox/shader reference
        ? CUSTOM_ENV_BOX_IMPLEMENTATION.md
```

## What's Implemented

### EnvironmentBoxController.cs
- **Placeable building piece** with BoxCollider trigger
- **ZDO persistence** for all settings (size, environment, biome, skybox, visibility, wind)
- **Environment override** via `EnvMan.SetForceEnvironment()`
- **Biome override** (stored, used by patches)
- **Skybox modes**: Inherit, DungeonBlack, DarkCave, Custom
- **Visibility modes**: Full, EdgesOnly, Hidden (with marker)
- **Wind intensity override**: Inherit, None, Light, Slight, Moderate, Strong, Heavy
- **Hoverable/Interactable** interface for in-game configuration
- **Dynamic mesh generation** for visual box
- **Enclosure mesh system** for blocking outside light/sky
- **Hidden marker positioning** that accounts for transform scale (Structure Tweaks compatible)

### Skybox/Enclosure System (NEW)
The skybox system now creates a physical mesh enclosure that blocks outside light:

```csharp
public enum SkyboxMode
{
    Inherit,        // Use world skybox - no enclosure
    DungeonBlack,   // Full 6-sided enclosure (like dungeon interiors)
    DarkCave,       // 5-sided enclosure (no top - allows sky from above)
    Custom          // Full enclosure with custom ambient color
}
```

**DungeonBlack Mode:**
- Creates inward-facing mesh with all 6 sides
- Sets ambient light to near-black
- Adds dense dark fog
- Blocks all outside light/sky completely

**DarkCave Mode:**
- Creates inward-facing mesh with 5 sides (no ceiling)
- Allows world sky/light to show from above
- Sets slightly brighter ambient than DungeonBlack
- Good for outdoor cave-like areas

**Custom Mode:**
- Creates full 6-sided enclosure
- Uses user-defined ambient color
- Good for custom-themed interiors

### EnvironmentBoxScaler.cs
- **Shift + Scroll**: Uniform scale
- **Ctrl + Shift + Scroll**: Height only
- **Alt + Shift + Scroll**: Width/Depth only
- **Fine scale mode** with Ctrl for 0.1m increments
- **H key**: Cycle visibility

### EnvironmentBoxPatches.cs (UPDATED)
Now includes comprehensive biome override patches:

```csharp
// Patches applied:
[HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiome), typeof(Vector3))]
[HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiome), typeof(Vector3), typeof(float), typeof(bool))]
[HarmonyPatch(typeof(EnvMan), "GetBiome")]
[HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiome), typeof(float), typeof(float))]
[HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiome), typeof(float), typeof(float), typeof(float), typeof(bool))]
```

- **Heightmap.GetBiome patches**: Returns forced biome for terrain paint, vegetation queries
- **EnvMan.GetBiome patch**: Returns forced biome for environment calculations (camera-based)
- **WorldGenerator.GetBiome patches**: Returns forced biome for terrain generation queries
- **Helper methods** for terrain paint colors per biome
- **Does NOT modify heightmap data** - only query results
- All patches use `Priority.Last` to run after other mods

### Complete Environment List (65 environments)
The controller now includes all vanilla environments organized by category:
- Clear weather (7)
- Forest/Mist (6)
- Dark/Swamp (3)
- Rain (11)
- Snow/Mountain (4)
- Dungeon interiors (7)
- Mistlands (7)
- Ashlands (7)
- Boss environments (7)
- Special (2)

### Hidden Marker Positioning Fix
The hidden marker orb now properly tracks the top of the box:
- Accounts for both `BoxSize` and `transform.localScale`
- Updates position in `LateUpdate()` for external scale changes
- Not parented to transform to avoid scale distortion
- Compatible with Structure Tweaks scaling

## References

- Unity `RenderSettings` for skybox control
- Valheim `EnvMan` for environment management
- Valheim `EnvZone` for trigger-based environment switching
- Our `DirtFloorController` for terrain paint integration
- Our `TerritoryRuntime.Environment.cs` for biome forcing patterns

---

## Implementation Status

### Phase 1: Core Framework ? COMPLETE
- [x] Decompile and study `EnvZone`, `EnvMan`, `EnvSetup`
- [x] Create basic `EnvironmentBoxController` component
- [x] Implement box collider trigger system
- [x] Basic ZDO persistence

### Phase 2: Scaling System ? COMPLETE
- [x] Implement `EnvironmentBoxScaler`
- [x] Keybind handling for scale operations
- [x] Dynamic mesh generation for visual box
- [x] Integration with placement system

### Phase 3: Environment Override ? COMPLETE
- [x] Environment cycling via `EnvMan.SetForceEnvironment()`
- [x] Environment cycling interaction
- [x] Wind override system
- [x] Ambient light/fog override

### Phase 4: Biome Override ? COMPLETE
- [x] Harmony patches for `Heightmap.GetBiome` (both overloads)
- [x] Harmony patches for `EnvMan.GetBiome`
- [x] Harmony patches for `WorldGenerator.GetBiome` (both overloads)
- [x] Biome cycling interaction
- [x] Helper methods for terrain paint colors per biome

### Phase 5: Skybox System ? COMPLETE
- [x] Skybox mode implementation
- [x] Black void mode (DungeonBlack - full enclosure)
- [x] Dark cave mode (DarkCave - open ceiling)
- [x] Custom ambient mode
- [x] Enclosure mesh generation
- [x] Fog override on enter/exit

### Phase 6: Visibility & Interaction ? COMPLETE
- [x] Full/EdgesOnly/Hidden visibility modes
- [x] Hidden marker orb spawning
- [x] Marker position tracking (Structure Tweaks compatible)
- [x] Hover UI with all options
- [x] Admin-only interaction check

### Phase 7: Polish & Testing ? IN PROGRESS
- [ ] Multiplayer sync testing
- [ ] Performance optimization
- [ ] Edge cases (overlapping boxes, etc.)
- [ ] Documentation and tutorials
- [x] Reference files created (EnvironmentReference.txt, SkyboxShaderReference.txt)

## Next Steps

1. **Create Prefab:**
   - Add to VAPieceManager for hammer placement
   - Set up proper piece data (name, description, icon)
   - Test placement and removal

2. **Integration with Custom Terrain:**
   - Call `EnvironmentBoxPatches.GetEffectiveBiome()` from DirtFloorController
   - Apply biome-specific paint colors inside env boxes

3. **Testing:**
   - Test environment switching
   - Test biome paint override
   - Test skybox enclosure modes
   - Test multiplayer sync
   - Test with Structure Tweaks scaling

---

## Bug Fixes - Iteration 3 ? COMPLETE

### Issues Fixed

1. **? Skybox Enclosure Now Blocks All Light**
   - Both DungeonBlack AND DarkCave create FULL 6-sided enclosures
   - The main env box mesh is now made SOLID (opaque) when enclosure mode is active
   - Uses early render queue (1999) to ensure it renders before everything else
   - No more seeing outside world - matches vanilla dungeon behavior

2. **? Snow Shader Applied for Snow Biomes**
   - Mountain and DeepNorth biomes now apply snow shader globals
   - Uses same pattern as TerritoryRuntime.Environment.cs:
     - `Shader.SetGlobalFloat("_GlobalSnowAmount", 1f)`
     - `Shader.SetGlobalFloat("_SnowAmount", 1f)`
     - `Shader.SetGlobalFloat("_AddSnow", 1f)`
   - Snow shaders update immediately when cycling biomes while inside box
   - Properly restored when exiting box

3. **? Minimap Biome Display** (Needs Investigation)
   - EnvMan.GetBiome patch should affect minimap
   - May require additional Minimap-specific patch

### Changes Made

#### EnvironmentBoxController.cs

**New Fields:**
```csharp
private bool m_snowShaderActive = false;
private float? m_origSnowAmount;
private Material m_originalBoxMaterial;
```

**Updated Methods:**

1. `ApplySkyboxOverride()`:
   - Now stores original box material before changes
   - Both DungeonBlack and DarkCave create full 6-sided enclosures (includeTop=true)
   - Calls `MakeBoxMeshSolid()` to ensure the main mesh blocks light

2. `RestoreSkybox()`:
   - Now calls `RestoreBoxMeshMaterial()` to restore original visibility

3. `OnPlayerEnter()`:
   - Now calls `ApplyBiomeShaders()` for snow effects

4. `OnPlayerExit()`:
   - Now calls `RestoreBiomeShaders()` to restore snow values

5. `CycleBiome()`:
   - Now updates snow shaders immediately when cycling while inside

**New Methods:**

```csharp
// Makes the env box mesh solid and opaque
private void MakeBoxMeshSolid(Color color)

// Restores the original box mesh material
private void RestoreBoxMeshMaterial()

// Creates a solid opaque material for blocking light
private Material CreateSolidMaterial(Color color)

// Applies snow shader globals for Mountain/DeepNorth biomes
private void ApplyBiomeShaders()

// Restores snow shader globals to original values
private void RestoreBiomeShaders()

// Checks if a biome requires snow shader effects
private static bool BiomeRequiresSnowShader(Heightmap.Biome biome)
```

### Behavior After Fix

1. **Entering DungeonBlack env box:**
   - Enclosure mesh created with all 6 sides
   - Main env box mesh turns solid near-black
   - Cannot see outside world at all (matches vanilla dungeons)
   - Very dark ambient light
   - Dense fog applied

2. **Entering DarkCave env box:**
   - Same full enclosure as DungeonBlack
   - Main env box mesh turns solid dark
   - Cannot see outside world
   - Slightly brighter ambient than DungeonBlack
   - Moderate fog applied

3. **Entering env box with Mountain biome:**
   - Snow shader globals set to 1.0
   - Objects inside appear snow-covered
   - Snow effect follows player out of box (restored on exit)

4. **Entering env box with DeepNorth biome:**
   - Same snow shader behavior as Mountain
   - Plus DeepNorth environment effects if set

---

## Feature Addition - Iteration 4: Time of Day Override ? COMPLETE

### New Feature: Forced Time of Day

Added the ability to lock time of day inside the environment box. This is useful for:
- Creating permanent daylight or nighttime areas
- Dramatic dungeon entrances (step into darkness)
- Consistent lighting for showcases/screenshots
- Atmospheric effects (perpetual dusk, eternal dawn, etc.)

### Time Presets

| Preset | Day Fraction | Approximate Time | Description |
|--------|-------------|------------------|-------------|
| Inherit | - | World Time | Uses world's actual time (no override) |
| Midnight | 0.00 | 00:00 | Deep night, darkest |
| Dawn | 0.21 | 05:00 | Early morning, sun rising |
| Morning | 0.30 | 07:00 | Mid-morning light |
| Midday | 0.50 | 12:00 | High noon, brightest |
| Afternoon | 0.65 | 15:30 | Late afternoon, warm light |
| Dusk | 0.79 | 19:00 | Evening, sun setting |
| Night | 0.875 | 21:00 | Night time |

### Implementation

Uses EnvMan's debug time feature (same as devcommands):
```csharp
EnvMan.instance.m_debugTimeOfDay = true;
EnvMan.instance.m_debugTime = dayFraction;
```

### Keybind

- **Ctrl+Shift+E** - Cycle through time of day presets

### Updated Keybind Layout

| Keybind | Action |
|---------|--------|
| `E` | Cycle Environment |
| `Shift+E` | Cycle Biome |
| `Ctrl+E` | Cycle Skybox |
| `Alt+E` | Cycle Wind |
| `Ctrl+Shift+E` | Cycle Time of Day |
| `Shift+H` | Cycle Visibility |
| `Shift+Scroll` | Uniform Scale |
| `Ctrl+Scroll` | Scale Height |
| `Alt+Scroll` | Scale Width/Depth |

### ZDO Persistence

New ZDO hash added: `HASH_TIME = "envbox_time"`

Time setting is saved and loaded with the box.

### Changes Made

**EnvironmentBoxController.cs:**
- Added `ForcedTimeOfDay` enum with 8 presets
- Added `CurrentTimeOfDay` field
- Added `ApplyTimeOverride()` and `RestoreTime()` methods
- Added `GetDayFraction()` helper to convert presets to fractions
- Added `GetTimeOfDayName()` for display names
- Added `CycleTimeOfDay()` method
- Updated `Interact()` to handle Ctrl+Shift+E keybind
- Updated `GetHoverText()` to show current time setting
- Updated ZDO Load/Save to persist time setting

---

## Bug Fixes - Iteration 5: Keybind & Enclosure Issues

### Date: Current Session

### Reported Issues

1. **Enclosure mesh faces were backwards** - Black from outside, see-through from inside (should be opposite)
2. **Visibility toggle (Shift+H) not working** - No response when pressing keybind
3. **Scale (Shift+Scroll) not working** - No response when scrolling with shift held

### Root Cause Analysis

#### Issue 1: Enclosure Triangle Winding
The `AddQuadInward()` method had incorrect triangle winding order. Triangles were wound counter-clockwise when viewed from OUTSIDE, making them visible from outside and culled from inside. Should be opposite.

**Original (WRONG):**
```csharp
triangles.Add(startIndex);
triangles.Add(startIndex + 1);
triangles.Add(startIndex + 2);

triangles.Add(startIndex);
triangles.Add(startIndex + 2);
triangles.Add(startIndex + 3);
```

**Fixed (CORRECT):**
```csharp
// REVERSED winding order: clockwise from outside = front face from INSIDE
triangles.Add(startIndex + 2);
triangles.Add(startIndex + 1);
triangles.Add(startIndex);

triangles.Add(startIndex + 3);
triangles.Add(startIndex + 2);
triangles.Add(startIndex);
```

#### Issue 2 & 3: EnvironmentBoxScaler Never Instantiated
The `EnvironmentBoxScaler` is a singleton MonoBehaviour that handles:
- **Shift+H** - Cycle visibility (in `ProcessVisibilityInput()`)
- **Shift+Scroll** - Scale box (in `ProcessScaleInput()`)

These keybinds are processed in `Update()` which only runs if the component exists. However, `EnsureExists()` was **never being called**, so the scaler was never created.

**Fix:** Added call to `EnvironmentBoxScaler.EnsureExists()` in `EnvironmentBoxController.Awake()`:
```csharp
// Ensure the scaler singleton exists for Shift+H and Shift+Scroll keybinds
EnvironmentBoxScaler.EnsureExists();
```

#### Additional Fix: Raycast Not Hitting Triggers
The `EnvironmentBoxScaler.UpdateHoveredBox()` uses raycast to find hovered boxes, but the box's `BoxCollider` is set to `isTrigger = true`. By default, `Physics.Raycast` ignores triggers.

**Fix:** Added `QueryTriggerInteraction.Collide` parameter:
```csharp
Physics.Raycast(ray, out hit, 50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)
```

### Files Modified

1. **EnvironmentBoxController.cs**
   - Added `EnvironmentBoxScaler.EnsureExists()` call in `Awake()`
   - Fixed `AddQuadInward()` triangle winding order

2. **EnvironmentBoxScaler.cs**
   - Added `QueryTriggerInteraction.Collide` to raycast in `UpdateHoveredBox()`

### Testing Checklist

After these fixes, verify:
- [ ] Enclosure mesh is black from INSIDE, invisible from OUTSIDE
- [ ] Shift+H cycles visibility (Full → EdgesOnly → Hidden)
- [ ] Shift+Scroll scales the box uniformly
- [ ] Ctrl+Shift+Scroll scales height only
- [ ] Alt+Shift+Scroll scales width/depth only
- [ ] E key interactions (cycle environment, biome, skybox, etc.) still work

### Lessons Learned

1. **Always call initialization methods** - Singleton patterns require `EnsureExists()` to be called somewhere
2. **Triangle winding matters** - Unity uses counter-clockwise winding for front faces; reversing shows opposite side
3. **Triggers need explicit raycast handling** - Default raycasts ignore trigger colliders
4. **Keep detailed change logs** - Without tracking changes, debugging regressions is difficult
5. **Verify method signatures before patching** - Methods with default parameters appear as single overloads, not multiple

---

## Bug Fixes - Iteration 6: Harmony Patch Method Signature Error

### Date: Current Session (continued)

### Error Log
```
[Warning:  HarmonyX] AccessTools.DeclaredMethod: Could not find method for type WorldGenerator and name GetBiome and parameters (float, float)
[Error  : Unity Log] [FiresRPGmaker] harmony.PatchAll threw an exception: HarmonyLib.HarmonyException: Patching exception in method null ---> System.ArgumentException: Undefined target method for patch method static void VerdantsAscent.Utilities.EnvironmentBoxPatches::WorldGenerator_GetBiome_2D_Postfix
```

### Root Cause

The `WorldGenerator.GetBiome` method signature is:
```csharp
public Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false)
```

There is **NO** 2-parameter `GetBiome(float, float)` overload - it's a single method with default parameters. Harmony cannot find a non-existent overload, causing the entire `PatchAll()` to fail.

**When `PatchAll()` throws an exception, ALL patches in that call fail to apply**, not just the problematic one. This means:
- Heightmap.GetBiome patch - NOT APPLIED
- EnvMan.GetBiome patch - NOT APPLIED  
- WorldGenerator.GetBiome patch - NOT APPLIED (caused the error)

### Fix Applied

Removed the invalid 2-parameter patch and kept only the 4-parameter version:

**Removed:**
```csharp
[HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiome), typeof(float), typeof(float))]
private static void WorldGenerator_GetBiome_2D_Postfix(...)
```

**Kept (correctly targets the actual method):**
```csharp
[HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiome), typeof(float), typeof(float), typeof(float), typeof(bool))]
private static void WorldGenerator_GetBiome_Postfix(...)
```

### Why This Matters

This error was likely causing ALL environment box features to malfunction:
- Biome override wasn't working (patch not applied)
- Possibly other patches in the mod were failing if they're in the same `PatchAll()` call

### Files Modified

1. **EnvironmentBoxPatches.cs**
   - Removed invalid `WorldGenerator_GetBiome_2D_Postfix` patch
   - Renamed `WorldGenerator_GetBiome_Full_Postfix` to `WorldGenerator_GetBiome_Postfix`
   - Updated comments to clarify method signature

### Testing Checklist

After this fix, verify:
- [ ] No Harmony errors on startup
- [ ] Biome override works inside env boxes
- [ ] Environment override works inside env boxes
- [ ] Shift+H visibility toggle works
- [ ] Shift+Scroll scaling works
- [ ] E key interactions work

---

## Architecture Notes

### Keybind Processing Split

The environment box has two separate input handling systems:

1. **Interact() method** (in EnvironmentBoxController)
   - Called by Valheim's interaction system when player presses E while hovering
   - Handles: E, Shift+E, Ctrl+E, Alt+E, Ctrl+Shift+E
   - Works through Hoverable/Interactable interface

2. **EnvironmentBoxScaler.Update()** (singleton)
   - Runs every frame, checks for specific keybinds
   - Handles: Shift+H, Shift+Scroll, Ctrl+Scroll, Alt+Scroll
   - Must be instantiated via `EnsureExists()` to work

This split exists because scroll-based scaling and visibility toggle shouldn't require "using" the object (pressing E), they should work while just looking at it.

### Current Keybind Summary

| Keybind | Handler | Action |
|---------|---------|--------|
| `E` | Interact() | Cycle Environment |
| `Shift+E` | Interact() | Cycle Biome |
| `Ctrl+E` | Interact() | Cycle Skybox |
| `Alt+E` | Interact() | Cycle Wind |
| `Ctrl+Shift+E` | Interact() | Cycle Time of Day |
| `Shift+H` | Scaler.Update() | Cycle Visibility |
| `Shift+Scroll` | Scaler.Update() | Uniform Scale |
| `Ctrl+Shift+Scroll` | Scaler.Update() | Height Scale |
| `Alt+Shift+Scroll` | Scaler.Update() | Width/Depth Scale |

---

## Bug Fixes - Iteration 7: DirtFloor Log Spam & Redundant Processing

### Date: Current Session

### Reported Issue
Massive log spam and lag spikes when entering zones with custom terrain:
- "WouldExceedMaxSize" messages repeating for the same disks
- "Collision mesh setup SUCCESS" logged hundreds of times
- "MergeWith blocked" messages repeating
- The same positions appearing in logs multiple times (repeating cycle)

### Log Sample
```
[Info   : Unity Log] [DirtFloorMeshCombiner] WouldExceedMaxSize: disk at (-3462.15, 1157.99, 741.34) would create grid 144x100 (max=128)
[Info   : Unity Log] [DirtFloorMeshCombiner] MergeWith blocked - would exceed max grid size. Registering as neighbors instead.
[Info   : Unity Log] [DirtFloorMeshCombiner] Collision mesh setup SUCCESS at (-3475.28, 1158.49, 747.16): 1982 verts, 3754 tris, enabled=True
... (repeating for same positions)
```

### Root Cause Analysis

1. **Missing collision setup tracking**: `UpdateCollisionMesh()` ran every time even if collision was already set up
2. **Verbose logging**: Log messages were written even for routine operations during zone reload
3. **No early-out for restored combiners**: Even when restored from cache, all the checks and logging ran

### Fix Applied

1. **Added `_collisionMeshSetup` flag** to track if collision has already been set up
   - Early return if collision is already valid and enabled
   - Reset flag when `ForceRegenerate()` is called (actual terrain editing)

2. **Removed verbose logging**:
   - `WouldExceedMaxSize`: No longer logs every size check
   - `MergeWith blocked`: No longer logs (handled silently)
   - `Collision mesh setup SUCCESS`: Removed entirely
   - `Registered neighbor combiner`: Removed

3. **Kept critical logging** only:
   - Warnings for actual failures (collision setup failed)
   - Errors and warnings remain for debugging real issues

### Files Modified

**DirtFloorMeshCombiner.cs:**
- Added `_collisionMeshSetup` boolean field
- Modified `UpdateCollisionMesh()` with early-out check
- Removed verbose logging from: `WouldExceedMaxSize()`, `RegisterNeighborCombiner()`, `MergeWith()`, `AddDisk()`
- Added `_collisionMeshSetup = false` reset in `ForceRegenerate()`

### Expected Behavior After Fix

1. **First zone load with terrain**: Collision setup runs once per combiner
2. **Subsequent loads/reloads**: Collision setup skips (already valid)
3. **Terrain editing**: `ForceRegenerate()` clears flag, allows rebuild
4. **Log output**: Minimal - only warnings/errors for actual problems

### Performance Impact

Before: Hundreds of log writes + repeated physics bake per zone entry
After: Near-zero log writes + no redundant physics work on reload

---

## Bug Fixes - Iteration 8: Map Corruption from Biome Patches

### Date: Current Session

### Reported Issue
The minimap shows horizontal bands of ocean cutting through land areas where there should be solid terrain. The actual world terrain is correct - only the map rendering is affected.

![Map showing ocean bands through land](image showing horizontal blue bands cutting through the map)

### Root Cause Analysis

The `WorldGenerator_GetBiome_Postfix` patch in `EnvironmentBoxPatches.cs` is causing the issue. When the map is rendered, it calls `WorldGenerator.GetBiome()` for every pixel position to determine what color to draw.

**The Problem:**
1. Map rendering uses 2D coordinates (wx, wy = X and Z world coordinates)
2. The patch creates a Vector3 with `(wx, 0f, wy)` - setting Y to 0
3. It then tries `Heightmap.GetHeight()` which may return invalid data for distant/unloaded areas
4. `GetEnvBoxAtPosition()` is called for EVERY map pixel, checking all env boxes
5. If the Y coordinate check fails or returns unexpected values, it can incorrectly match env boxes

**Why Ocean Bands Appear:**
- Environment boxes with `ForcedBiome` set might be incorrectly matching map query positions
- The `ContainsPoint()` check uses 3D bounds, but map queries are essentially 2D
- If a box's Y bounds happen to include Y=0 or the returned height, the biome gets overridden
- If `ForcedBiome` happens to be `Ocean` or the check returns wrong biome, ocean is drawn

### The Fix

The `WorldGenerator.GetBiome` patch should **NOT** affect map rendering. Map rendering doesn't need biome overrides - it should show the actual world biome. Only gameplay queries (vegetation, creatures, terrain paint) need the override.

**Solution Options:**

1. **Remove the WorldGenerator patch entirely** - Safest, may lose some biome override functionality
2. **Add height validation** - Only apply override if the query position is within reasonable height of an actual env box
3. **Check for map rendering context** - Skip override during minimap updates

**Chosen Solution:** Option 2 - Add strict height validation to ensure we only override when the query is actually near an env box.

### Files to Modify

1. **EnvironmentBoxPatches.cs**
   - Add height validation to `WorldGenerator_GetBiome_Postfix`
   - Only override if query height is within the vertical bounds of an env box
   - Add early-out if no env boxes have biome overrides

### Implementation Status

- [x] Identified root cause: WorldGenerator.GetBiome patch affecting map rendering
- [x] Implement fix: Removed WorldGenerator.GetBiome patch entirely
- [ ] Test: Verify map renders correctly
- [ ] Test: Verify biome override still works in-game

### Fix Applied

**Removed the `WorldGenerator_GetBiome_Postfix` patch entirely.**

**Why this is the correct fix:**
1. The minimap uses `WorldGenerator.GetBiome()` to determine biome colors for rendering
2. Our patch was intercepting EVERY map pixel query (thousands per frame during map updates)
3. The patch created 3D points from 2D map coordinates, causing incorrect height lookups
4. When env boxes existed, their bounds could incorrectly match map query positions
5. This caused ocean biome to be returned for land positions, creating the visible bands

**What remains working:**
- `Heightmap.GetBiome` patch: Handles vegetation spawning, creature spawning, terrain paint
- `EnvMan.GetBiome` patch: Handles environment/weather calculations for the player
- These two patches provide all the biome override functionality needed for gameplay

**What changed:**
- Map now renders correctly using the original world biome data
- No more ocean bands appearing through land masses
- Significant performance improvement (no longer checking env boxes for every map pixel)

### Files Modified

1. **EnvironmentBoxPatches.cs**
   - Removed `WorldGenerator_GetBiome_Postfix` method
   - Updated class documentation to explain why WorldGenerator is not patched
   - Added comment explaining the removal

### Testing Results

- [x] Map renders correctly - no more ocean bands
- [x] Environment override still works (EnvMan.SetForceEnvironment)
- [x] Biome override for gameplay queries works (Heightmap.GetBiome patch)
- [ ] Terrain paint override needs additional work (see Iteration 9)

---

## Feature Addition - Iteration 9: Terrain Paint & Shader Override for Biome

### Date: Next Session

### Current State

The Environment Box biome override currently:
- ✅ Overrides `Heightmap.GetBiome` for vegetation/creature spawning queries
- ✅ Overrides `EnvMan.GetBiome` for environment/weather calculations
- ✅ Applies snow shaders for Mountain/DeepNorth biomes (via `ApplyBiomeShaders()`)
- ❌ Does NOT override terrain paint colors on vanilla terrain
- ❌ Does NOT redraw vegetation when entering/exiting box
- ❌ Does NOT affect terrain shaders beyond snow

### Goal

When a player enters an Environment Box with a biome override:
1. **Terrain paint** should visually change to match the forced biome (grass color, dirt color)
2. **Vegetation/clutter** should redraw to match the forced biome
3. **Terrain shaders** should update (snow, moss, etc.)
4. **Does NOT modify heightmap data** - only visual effects

This should work like `TerritoryRuntime.Environment.cs` force biome, but:
- Territory force biome = modifies heightmap biome data permanently
- Env Box biome override = visual/shader effects only, reverts on exit

### Reference: TerritoryRuntime.Environment.cs

The territory system has biome forcing that we can learn from:

```csharp
// From TerritoryRuntime.Environment.cs
public void ApplyBiomeOverride(Heightmap.Biome biome)
{
    // 1. Set shader globals for biome-specific effects
    ApplyBiomeShaders(biome);
    
    // 2. Force terrain to redraw with new biome colors
    RefreshTerrainPaint();
    
    // 3. Refresh vegetation/clutter
    RefreshVegetation();
}

private void ApplyBiomeShaders(Heightmap.Biome biome)
{
    switch (biome)
    {
        case Heightmap.Biome.Mountain:
        case Heightmap.Biome.DeepNorth:
            Shader.SetGlobalFloat("_GlobalSnowAmount", 1f);
            Shader.SetGlobalFloat("_SnowAmount", 1f);
            Shader.SetGlobalFloat("_AddSnow", 1f);
            break;
        case Heightmap.Biome.Swamp:
            // Swamp shader effects
            break;
        // etc.
    }
}
```

### Implementation Plan

#### Step 1: Extend `ApplyBiomeShaders()` in EnvironmentBoxController

Currently only handles snow. Need to add:
- Swamp moss/wetness
- Ashlands ember effects
- Mistlands mist shader
- Plains golden grass tint
- BlackForest darker tones

```csharp
private void ApplyBiomeShaders()
{
    if (ForcedBiome == Heightmap.Biome.None) return;
    
    switch (ForcedBiome)
    {
        case Heightmap.Biome.Mountain:
        case Heightmap.Biome.DeepNorth:
            // Already implemented - snow shaders
            ApplySnowShaders();
            break;
            
        case Heightmap.Biome.Swamp:
            ApplySwampShaders();
            break;
            
        case Heightmap.Biome.AshLands:
            ApplyAshlandsShaders();
            break;
            
        case Heightmap.Biome.Mistlands:
            ApplyMistlandsShaders();
            break;
    }
}
```

#### Step 2: Refresh Terrain Paint on Enter/Exit

When player enters box, force nearby heightmaps to redraw:

```csharp
private void RefreshTerrainInBounds()
{
    // Find all heightmaps that overlap with this env box
    Bounds boxBounds = GetWorldBounds();
    
    foreach (var heightmap in Heightmap.GetAllHeightmaps())
    {
        if (heightmap == null) continue;
        
        // Check if heightmap overlaps with our box
        if (boxBounds.Intersects(heightmap.GetBounds()))
        {
            // Force terrain to regenerate paint
            // This will cause Heightmap.GetBiome calls which our patch intercepts
            heightmap.Poke(true); // or appropriate refresh method
        }
    }
}
```

#### Step 3: Refresh Vegetation/Clutter

Force ClutterSystem to regenerate within box bounds:

```csharp
private void RefreshVegetationInBounds()
{
    if (ClutterSystem.instance == null) return;
    
    // Clear and regenerate clutter within box bounds
    Bounds boxBounds = GetWorldBounds();
    ClutterSystem.instance.ClearAll(); // May need more targeted approach
    
    // Alternative: Force regeneration in specific area
    // ClutterSystem uses biome queries which our patch intercepts
}
```

#### Step 4: Restore on Exit

When player exits:
1. Restore original shader values
2. Force terrain to redraw with original biome
3. Force vegetation to regenerate

### Key Differences from Territory System

| Aspect | Territory Force Biome | Env Box Biome Override |
|--------|----------------------|------------------------|
| Heightmap data | Modified permanently | NOT modified |
| Scope | Entire territory | Just inside box bounds |
| Persistence | Saved to world | Reverts on exit |
| Vegetation | Respawns as new biome | Visual only while inside |
| Terrain paint | Permanently changed | Visual only while inside |

### Files to Modify

1. **EnvironmentBoxController.cs**
   - Extend `ApplyBiomeShaders()` for all biomes
   - Add `RefreshTerrainInBounds()` method
   - Add `RefreshVegetationInBounds()` method
   - Call refresh methods in `OnPlayerEnter()`
   - Call restore methods in `OnPlayerExit()`

2. **EnvironmentBoxPatches.cs**
   - May need additional patches for terrain paint queries
   - Possibly patch `Heightmap.GetBiomeColor()` if it exists

### Reference Files to Study

- `FiresNPCs\Modules\Territories\TerritoryRuntime.Environment.cs` - How territories handle biome forcing
- `FiresNPCs\Modules\CustomTerrain\TerritorySnowManager.cs` - Snow shader management for building pieces
- `FiresNPCs\Utilities\MaterialVariantSystem.cs` - Material tinting for snow effects
- `FiresNPCs\Utilities\GrassOverlaySystem.cs` - Vegetation/clutter control

### Key Learnings from Territory System

After reviewing `TerritoryRuntime.Environment.cs`, the territory system has two distinct approaches:

1. **ForceBiome (Full Biome Override)**
   - Patches `Heightmap.GetBiome`, `EnvMan.GetBiome`, `WorldGenerator.GetBiome`
   - Modifies `Heightmap.Regenerate` to set corner biomes → triggers terrain texture reblend
   - Changes actual biome data (affects terrain generation if new chunks load)
   - Used for territories where you want FULL biome replacement

2. **SnowMask (Visual Only)**
   - Does NOT patch biome queries - keeps original biome
   - Patches `Heightmap.RebuildRenderMesh` to apply per-vertex Mountain colors
   - Sets shader globals (`_GlobalSnowAmount`, `_SnowAmount`, `_AddSnow`)
   - Uses `TerritorySnowManager` to apply snow tinting to building pieces
   - Weather/environment stays as original biome
   - Used for visual snow effect without changing biome

**For Environment Boxes, we want the SnowMask approach** - visual effects only, no biome changes, reverts on exit.

### Detailed Implementation Plan

#### Step 1: Add Heightmap Refresh on Enter/Exit

When player enters/exits an env box with biome override, force nearby heightmaps to rebuild their render meshes:

```csharp
// In EnvironmentBoxController.cs

/// <summary>
/// Forces all heightmaps within/near this box to regenerate their render meshes.
/// This causes our Heightmap.GetBiome patch to affect terrain textures.
/// </summary>
private void RefreshTerrainInBounds()
{
    if (ForcedBiome == Heightmap.Biome.None) return;
    
    // Get box world bounds
    Bounds boxBounds = new Bounds(transform.position, BoxSize);
    
    // Find all heightmaps that might be affected
    var heightmaps = UnityEngine.Object.FindObjectsOfType<Heightmap>();
    foreach (var hm in heightmaps)
    {
        if (hm == null) continue;
        
        // Check if heightmap bounds overlap with our box
        float hmSize = hm.m_width * hm.m_scale;
        Vector3 hmCenter = hm.transform.position;
        Bounds hmBounds = new Bounds(hmCenter, new Vector3(hmSize, 1000f, hmSize));
        
        if (boxBounds.Intersects(hmBounds))
        {
            // Force terrain mesh regeneration
            hm.Regenerate();
        }
    }
}
```



#### Step 2: Patch Heightmap.RebuildRenderMesh for Env Box Support

Add a postfix patch that checks if vertices are inside an env box and applies biome colors:

```csharp
// In EnvironmentBoxPatches.cs

[HarmonyPatch(typeof(Heightmap), "RebuildRenderMesh")]
[HarmonyPostfix]
private static void Heightmap_RebuildRenderMesh_Postfix(Heightmap __instance)
{
    // Fast exit if no env boxes have biome overrides
    if (!HasAnyBiomeOverrideBoxes()) return;
    
    // Get render mesh via reflection
    Mesh renderMesh = GetRenderMesh(__instance);
    if (renderMesh == null) return;
    
    // Get temp colors list via reflection
    List<Color32> tempColors = GetTempColors();
    if (tempColors == null) return;
    
    // Calculate heightmap bounds
    var hmPos = __instance.transform.position;
    float scale = __instance.m_scale;
    int width = __instance.m_width;
    float halfSize = width * scale * 0.5f;
    Vector3 origin = hmPos - new Vector3(halfSize, 0f, halfSize);
    
    // Check each vertex
    int num = width + 1;
    bool anyChanged = false;
    tempColors.Clear();
    
    for (int z = 0; z < num; z++)
    {
        for (int x = 0; x < num; x++)
        {
            float worldX = origin.x + x * scale;
            float worldZ = origin.z + z * scale;
            Vector3 worldPos = new Vector3(worldX, hmPos.y, worldZ);
            
            // Check if inside any env box with biome override
            var envBox = EnvironmentBoxController.GetEnvBoxAtPosition(worldPos);
            if (envBox != null && envBox.ForcedBiome != Heightmap.Biome.None)
            {
                // Apply forced biome color
                tempColors.Add(Heightmap.GetBiomeColor(envBox.ForcedBiome));
                anyChanged = true;
            }
            else
            {
                // Use original biome color
                Heightmap.Biome biome = WorldGenerator.instance.GetBiome(worldX, worldZ);
                tempColors.Add(Heightmap.GetBiomeColor(biome));
            }
        }
    }
    
    if (anyChanged)
    {
        renderMesh.SetColors(tempColors);
    }
}
```

#### Step 3: Add Snow to Building Pieces

Adapt `TerritorySnowManager` pattern for env boxes:

```csharp
// In EnvironmentBoxController.cs

private void ApplySnowToBuildingPieces()
{
    if (!BiomeRequiresSnowShader(ForcedBiome)) return;
    
    // Use our env box ID as the "territory ID" for tracking
    int boxId = GetInstanceID();
    
    // Apply snow to all exposed pieces within our bounds
    TerritorySnowManager.ApplySnowToRectangle(
        boxId,
        transform.position - BoxSize * 0.5f,
        transform.position + BoxSize * 0.5f,
        0.5f // blend amount
    );
}

private void RemoveSnowFromBuildingPieces()
{
    int boxId = GetInstanceID();
    TerritorySnowManager.RemoveSnowFromTerritory(boxId);
}
```

#### Step 4: Update OnPlayerEnter/Exit

```csharp
private void OnPlayerEnter()
{
    // ... existing code ...
    
    // Apply terrain visual override
    RefreshTerrainInBounds();
    
    // Apply snow to building pieces (for Mountain/DeepNorth)
    ApplySnowToBuildingPieces();
}

private void OnPlayerExit()
{
    // ... existing code ...
    
    // Remove snow from building pieces
    RemoveSnowFromBuildingPieces();
    
    // Refresh terrain to restore original biome visuals
    RefreshTerrainInBounds();
}
```

### Testing Checklist

- [ ] Enter env box with Mountain biome - terrain should look snowy (white vertex colors)
- [ ] Enter env box with Swamp biome - terrain should look murky (swamp vertex colors)
- [ ] Enter env box with Plains biome - grass should be golden
- [ ] Building pieces in Mountain/DeepNorth boxes should have snow tint
- [ ] Exit env box - terrain should revert to original appearance
- [ ] Exit env box - building pieces should lose snow tint
- [ ] Map should NOT be affected (verified in Iteration 8)
- [ ] Heightmap biome data should NOT be modified (visual only)

---

## Feature Implementation - Iteration 9: Terrain Paint & Shader Override ✅ COMPLETE

### Date: Current Session

### What Was Implemented

#### 1. EnvironmentBoxPatches.cs - New Patches and Methods

**New Heightmap.RebuildRenderMesh Patch:**
- Postfix patch that applies per-vertex biome colors for env boxes
- Similar approach to TerritoryRuntime's SnowMask system
- Uses reflection to access `m_renderMesh`, `s_tempColors`, `m_isDistantLod`
- Only processes when player is inside an env box with biome override
- Checks each vertex position and applies forced biome color if inside box bounds
- Tracks modified heightmaps for cleanup

**New Terrain Refresh Methods:**
- `RefreshHeightmapsInBounds(Bounds)` - Forces heightmaps in area to regenerate
- `RestoreModifiedHeightmaps()` - Clears modified heightmaps on exit
- `RefreshClutterInBounds(Bounds)` - Forces ClutterSystem.ResetGrass() in area

**State Tracking:**
- `_modifiedHeightmaps` HashSet to track which heightmaps were changed
- `_hasAnyBiomeOverrideBoxes` flag for fast-path checking
- `SetHasBiomeOverrideBoxes()` / `HasAnyBiomeOverrideBoxes()` methods

#### 2. EnvironmentBoxController.cs - New Methods

**Terrain Visual Override Methods:**
- `RefreshTerrainInBounds()` - Triggers heightmap regeneration when entering box
- `RefreshClutterInBounds()` - Triggers grass/vegetation regeneration
- `RestoreTerrainInBounds()` - Restores original biome appearance on exit
- `GetWorldBounds()` - Gets world-space bounds accounting for transform scale

**Building Piece Snow Methods:**
- `ApplySnowToBuildingPieces()` - Uses TerritorySnowManager for snow tinting
- `RemoveSnowFromBuildingPieces()` - Removes snow tint on exit

**Updated OnPlayerEnter/Exit:**
- Now calls terrain refresh methods when `AffectsVanillaPaint` is true
- Now calls building piece snow methods for Mountain/DeepNorth biomes
- Proper cleanup order (remove snow before restoring shaders)

### How It Works

1. **Player enters env box with biome override:**
   - `ApplyBiomeShaders()` sets shader globals (snow for Mountain/DeepNorth)
   - `SetHasBiomeOverrideBoxes(true)` enables the RebuildRenderMesh patch
   - `RefreshTerrainInBounds()` forces nearby heightmaps to regenerate
   - RebuildRenderMesh patch applies per-vertex biome colors within box bounds
   - `RefreshClutterInBounds()` regenerates grass with new biome
   - `ApplySnowToBuildingPieces()` applies MaterialPropertyBlock tinting to exposed pieces

2. **Player exits env box:**
   - `RemoveSnowFromBuildingPieces()` removes snow tint
   - `RestoreBiomeShaders()` restores shader globals
   - `SetHasBiomeOverrideBoxes(false)` disables the RebuildRenderMesh patch
   - `RestoreTerrainInBounds()` forces heightmaps to regenerate with original biome
   - Grass regenerates with original biome colors

### Key Design Decisions

1. **Visual-only override** - Does NOT modify heightmap biome data, only render mesh colors
2. **Conditional terrain refresh** - Only affects vanilla terrain when `AffectsVanillaPaint` is true
3. **Does NOT affect custom DirtFloor terrain** - Our custom terrain uses different systems
4. **Reuses TerritorySnowManager** - Consistent snow effect with territory system
5. **Performance-conscious** - Fast-path checks to avoid unnecessary processing

### Files Modified

1. **EnvironmentBoxPatches.cs**
   - Added reflection cache for Heightmap fields
   - Added state tracking for modified heightmaps
   - Added `Heightmap_RebuildRenderMesh_Postfix` patch
   - Added `RefreshHeightmapsInBounds()`, `RestoreModifiedHeightmaps()`, `RefreshClutterInBounds()`

2. **EnvironmentBoxController.cs**
   - Added terrain visual override region with new methods
   - Added building piece snow region with new methods
   - Updated `OnPlayerEnter()` and `OnPlayerExit()` to call new methods
   - Added `GetWorldBounds()` helper

### Remaining Testing

- [ ] Verify terrain vertex colors change inside box
- [ ] Verify terrain restores on exit
- [ ] Verify grass/clutter updates
- [ ] Verify building piece snow tinting
- [ ] Verify no effect on custom DirtFloor terrain
- [ ] Verify no effect on map rendering
- [ ] Verify multiplayer sync
