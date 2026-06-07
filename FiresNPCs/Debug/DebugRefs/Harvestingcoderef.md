// Decompiled with JetBrains decompiler
// Type: Companions.FarmController
// Assembly: Companions, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 0A9D6DFD-4997-4979-8571-F0AB8638B627
// Assembly location: C:\Users\travi\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\an test\BepInEx\plugins\ProfMags-Offline_Companions\Companions.dll

using UnityEngine;

#nullable disable
namespace Companions;

public class FarmController : MonoBehaviour
{
  internal enum FarmPhase : XXX
  {
  }

  internal struct SeedPlantInfo
  {
  }
}


// Decompiled with JetBrains decompiler
// Type: Companions.FarmZone
// Assembly: Companions, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 0A9D6DFD-4997-4979-8571-F0AB8638B627
// Assembly location: C:\Users\travi\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\an test\BepInEx\plugins\ProfMags-Offline_Companions\Companions.dll

#nullable disable
namespace Companions;

internal struct FarmZone
{
}


// Decompiled with JetBrains decompiler
// Type: Companions.FarmZoneCropPicker
// Assembly: Companions, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 0A9D6DFD-4997-4979-8571-F0AB8638B627
// Assembly location: C:\Users\travi\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\an test\BepInEx\plugins\ProfMags-Offline_Companions\Companions.dll

using HarmonyLib;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

#nullable disable
namespace Companions;

public class FarmZoneCropPicker : MonoBehaviour
{
  private static FarmZoneCropPicker _instance;
  private Action<string> _onSelect;
  private Action _onCancel;
  private Canvas _canvas;
  private GameObject _root;
  private TMP_FontAsset _font;
  private static readonly Color BgColor = new Color(0.08f, 0.07f, 0.06f, 0.94f);
  private static readonly Color ButtonColor = new Color(0.15f, 0.13f, 0.1f, 0.9f);
  private static readonly Color ButtonHover = new Color(0.25f, 0.22f, 0.16f, 0.9f);
  private static readonly Color LabelColor = new Color(1f, 0.9f, 0.5f, 1f);
  private static readonly Color TextColor = new Color(0.85f, 0.8f, 0.65f, 1f);
  private const float ButtonSize = 80f;
  private const float Spacing = 6f;
  private const int Columns = 5;

  internal static bool IsOpen
  {
    get
    {
      return (UnityEngine.Object) FarmZoneCropPicker._instance != (UnityEngine.Object) null && (UnityEngine.Object) FarmZoneCropPicker._instance._root != (UnityEngine.Object) null && FarmZoneCropPicker._instance._root.activeSelf;
    }
  }

  internal static void Show(FarmZone zone, Action<string> onSelect, Action onCancel)
  {
    if ((UnityEngine.Object) FarmZoneCropPicker._instance == (UnityEngine.Object) null)
    {
      GameObject target = new GameObject("HC_FarmZoneCropPicker");
      UnityEngine.Object.DontDestroyOnLoad((UnityEngine.Object) target);
      FarmZoneCropPicker._instance = target.AddComponent<FarmZoneCropPicker>();
    }
    FarmZoneCropPicker._instance._onSelect = onSelect;
    FarmZoneCropPicker._instance._onCancel = onCancel;
    FarmZoneCropPicker._instance.Build();
  }

  private void OnDestroy()
  {
    if (!((UnityEngine.Object) FarmZoneCropPicker._instance == (UnityEngine.Object) this))
      return;
    FarmZoneCropPicker._instance = (FarmZoneCropPicker) null;
  }

  private void Update()
  {
    if ((UnityEngine.Object) this._root == (UnityEngine.Object) null || !this._root.activeSelf)
      return;
    Cursor.lockState = CursorLockMode.None;
    Cursor.visible = true;
    if (!Input.GetKeyDown(KeyCode.Escape) && !ZInput.GetButtonDown("JoyButtonB"))
      return;
    this.Cancel();
  }

  private void Build()
  {
    if ((UnityEngine.Object) this._root != (UnityEngine.Object) null)
      UnityEngine.Object.Destroy((UnityEngine.Object) this._root);
    if ((UnityEngine.Object) this._canvas != (UnityEngine.Object) null)
      UnityEngine.Object.Destroy((UnityEngine.Object) this._canvas.gameObject);
    this._font = FarmZoneCropPicker.ResolveFont();
    GameObject gameObject1 = new GameObject("CropPickerCanvas", new System.Type[3]
    {
      typeof (Canvas),
      typeof (CanvasScaler),
      typeof (GraphicRaycaster)
    });
    gameObject1.transform.SetParent(this.transform, false);
    this._canvas = gameObject1.GetComponent<Canvas>();
    this._canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    this._canvas.sortingOrder = 31 /*0x1F*/;
    CanvasScaler component1 = gameObject1.GetComponent<CanvasScaler>();
    component1.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    component1.referenceResolution = new Vector2(1920f, 1080f);
    FarmController.BuildSeedMappingPublic();
    List<FarmZoneCropPicker.CropEntry> cropEntryList = new List<FarmZoneCropPicker.CropEntry>();
    if (FarmController.s_seedToPlant != null)
    {
      foreach (KeyValuePair<string, FarmController.SeedPlantInfo> keyValuePair in FarmController.s_seedToPlant)
      {
        if (!FarmZoneCropPicker.IsSeedMultiplicationEntry(keyValuePair.Key, keyValuePair.Value))
        {
          FarmZoneCropPicker.CropEntry cropEntry = new FarmZoneCropPicker.CropEntry()
          {
            SeedPrefabName = keyValuePair.Key,
            DisplayName = FarmZoneCropPicker.GetCropDisplayName(keyValuePair.Value),
            Icon = FarmZoneCropPicker.GetCropIcon(keyValuePair.Key)
          };
          cropEntryList.Add(cropEntry);
        }
      }
    }
    cropEntryList.Sort((Comparison<FarmZoneCropPicker.CropEntry>) ((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase)));
    int num = Mathf.CeilToInt((float) (cropEntryList.Count + 1) / 5f);
    float x1 = 436f;
    float y1 = (float) ((double) num * 86.0 + 6.0);
    float x2 = x1 + 20f;
    float y2 = y1 + 60f;
    this._root = new GameObject("CropPickerRoot", new System.Type[2]
    {
      typeof (RectTransform),
      typeof (Image)
    });
    this._root.transform.SetParent(gameObject1.transform, false);
    RectTransform component2 = this._root.GetComponent<RectTransform>();
    component2.anchorMin = new Vector2(0.5f, 0.5f);
    component2.anchorMax = new Vector2(0.5f, 0.5f);
    component2.pivot = new Vector2(0.5f, 0.5f);
    component2.sizeDelta = new Vector2(x2, y2);
    this._root.GetComponent<Image>().color = FarmZoneCropPicker.BgColor;
    GameObject gameObject2 = new GameObject("Title", new System.Type[1]
    {
      typeof (RectTransform)
    });
    gameObject2.transform.SetParent(this._root.transform, false);
    TextMeshProUGUI textMeshProUgui = gameObject2.AddComponent<TextMeshProUGUI>();
    if ((UnityEngine.Object) this._font != (UnityEngine.Object) null)
      textMeshProUgui.font = this._font;
    textMeshProUgui.text = ModLocalization.Loc("hc_farmzone_title");
    textMeshProUgui.fontSize = 18f;
    textMeshProUgui.color = FarmZoneCropPicker.LabelColor;
    textMeshProUgui.fontStyle = FontStyles.Bold;
    textMeshProUgui.alignment = TextAlignmentOptions.Center;
    textMeshProUgui.raycastTarget = false;
    RectTransform component3 = gameObject2.GetComponent<RectTransform>();
    component3.anchorMin = new Vector2(0.0f, 1f);
    component3.anchorMax = new Vector2(1f, 1f);
    component3.pivot = new Vector2(0.5f, 1f);
    component3.sizeDelta = new Vector2(0.0f, 36f);
    component3.anchoredPosition = new Vector2(0.0f, -4f);
    this.BuildCloseButton(this._root.transform);
    GameObject gameObject3 = new GameObject("Content", new System.Type[1]
    {
      typeof (RectTransform)
    });
    gameObject3.transform.SetParent(this._root.transform, false);
    RectTransform component4 = gameObject3.GetComponent<RectTransform>();
    component4.anchorMin = new Vector2(0.5f, 1f);
    component4.anchorMax = new Vector2(0.5f, 1f);
    component4.pivot = new Vector2(0.5f, 1f);
    component4.sizeDelta = new Vector2(x1, y1);
    component4.anchoredPosition = new Vector2(0.0f, -44f);
    this.BuildCropButton(gameObject3.transform, 0, "", ModLocalization.Loc("hc_farmzone_any"), (Sprite) null);
    for (int index = 0; index < cropEntryList.Count; ++index)
    {
      FarmZoneCropPicker.CropEntry cropEntry = cropEntryList[index];
      this.BuildCropButton(gameObject3.transform, index + 1, cropEntry.SeedPrefabName, cropEntry.DisplayName, cropEntry.Icon);
    }
    Cursor.lockState = CursorLockMode.None;
    Cursor.visible = true;
  }

  private void BuildCropButton(
    Transform parent,
    int index,
    string seedName,
    string displayName,
    Sprite icon)
  {
    int num1 = index % 5;
    int num2 = index / 5;
    float x = (float) (6.0 + (double) num1 * 86.0);
    float y = (float) -(6.0 + (double) num2 * 86.0);
    GameObject gameObject1 = new GameObject("Crop_" + seedName, new System.Type[3]
    {
      typeof (RectTransform),
      typeof (Image),
      typeof (Button)
    });
    gameObject1.transform.SetParent(parent, false);
    RectTransform component1 = gameObject1.GetComponent<RectTransform>();
    component1.anchorMin = new Vector2(0.0f, 1f);
    component1.anchorMax = new Vector2(0.0f, 1f);
    component1.pivot = new Vector2(0.0f, 1f);
    component1.sizeDelta = new Vector2(80f, 80f);
    component1.anchoredPosition = new Vector2(x, y);
    gameObject1.GetComponent<Image>().color = FarmZoneCropPicker.ButtonColor;
    Button component2 = gameObject1.GetComponent<Button>();
    component2.colors = component2.colors with
    {
      normalColor = FarmZoneCropPicker.ButtonColor,
      highlightedColor = FarmZoneCropPicker.ButtonHover,
      pressedColor = FarmZoneCropPicker.ButtonHover,
      selectedColor = FarmZoneCropPicker.ButtonColor
    };
    string capturedSeed = seedName;
    component2.onClick.AddListener((UnityAction) (() => this.SelectCrop(capturedSeed)));
    if ((UnityEngine.Object) icon != (UnityEngine.Object) null)
    {
      GameObject gameObject2 = new GameObject("Icon", new System.Type[2]
      {
        typeof (RectTransform),
        typeof (Image)
      });
      gameObject2.transform.SetParent(gameObject1.transform, false);
      RectTransform component3 = gameObject2.GetComponent<RectTransform>();
      component3.anchorMin = new Vector2(0.5f, 0.5f);
      component3.anchorMax = new Vector2(0.5f, 0.5f);
      component3.pivot = new Vector2(0.5f, 0.5f);
      component3.sizeDelta = new Vector2(48f, 48f);
      component3.anchoredPosition = new Vector2(0.0f, 6f);
      Image component4 = gameObject2.GetComponent<Image>();
      component4.sprite = icon;
      component4.raycastTarget = false;
    }
    GameObject gameObject3 = new GameObject("Label", new System.Type[1]
    {
      typeof (RectTransform)
    });
    gameObject3.transform.SetParent(gameObject1.transform, false);
    TextMeshProUGUI textMeshProUgui = gameObject3.AddComponent<TextMeshProUGUI>();
    if ((UnityEngine.Object) this._font != (UnityEngine.Object) null)
      textMeshProUgui.font = this._font;
    textMeshProUgui.text = displayName;
    textMeshProUgui.fontSize = 10f;
    textMeshProUgui.color = FarmZoneCropPicker.TextColor;
    textMeshProUgui.alignment = TextAlignmentOptions.Bottom;
    textMeshProUgui.raycastTarget = false;
    textMeshProUgui.textWrappingMode = TextWrappingModes.Normal;
    textMeshProUgui.overflowMode = TextOverflowModes.Truncate;
    RectTransform component5 = gameObject3.GetComponent<RectTransform>();
    component5.anchorMin = new Vector2(0.0f, 0.0f);
    component5.anchorMax = new Vector2(1f, 0.35f);
    component5.offsetMin = new Vector2(2f, 2f);
    component5.offsetMax = new Vector2(-2f, 0.0f);
  }

  private void BuildCloseButton(Transform parent)
  {
    GameObject gameObject1 = new GameObject("CloseBtn", new System.Type[3]
    {
      typeof (RectTransform),
      typeof (Image),
      typeof (Button)
    });
    gameObject1.transform.SetParent(parent, false);
    RectTransform component1 = gameObject1.GetComponent<RectTransform>();
    component1.anchorMin = new Vector2(1f, 1f);
    component1.anchorMax = new Vector2(1f, 1f);
    component1.pivot = new Vector2(1f, 1f);
    component1.sizeDelta = new Vector2(28f, 28f);
    component1.anchoredPosition = new Vector2(-4f, -4f);
    gameObject1.GetComponent<Image>().color = new Color(0.3f, 0.15f, 0.1f, 0.7f);
    gameObject1.GetComponent<Button>().onClick.AddListener(new UnityAction(this.Cancel));
    GameObject gameObject2 = new GameObject("X", new System.Type[1]
    {
      typeof (RectTransform)
    });
    gameObject2.transform.SetParent(gameObject1.transform, false);
    TextMeshProUGUI textMeshProUgui = gameObject2.AddComponent<TextMeshProUGUI>();
    if ((UnityEngine.Object) this._font != (UnityEngine.Object) null)
      textMeshProUgui.font = this._font;
    textMeshProUgui.text = "X";
    textMeshProUgui.fontSize = 14f;
    textMeshProUgui.color = Color.white;
    textMeshProUgui.alignment = TextAlignmentOptions.Center;
    textMeshProUgui.raycastTarget = false;
    RectTransform component2 = gameObject2.GetComponent<RectTransform>();
    component2.anchorMin = Vector2.zero;
    component2.anchorMax = Vector2.one;
    component2.offsetMin = Vector2.zero;
    component2.offsetMax = Vector2.zero;
  }

  private void SelectCrop(string seedPrefabName)
  {
    Action<string> onSelect = this._onSelect;
    this.CleanupUI();
    if (onSelect == null)
      return;
    onSelect(seedPrefabName);
  }

  private void Cancel()
  {
    Action onCancel = this._onCancel;
    this.CleanupUI();
    if (onCancel == null)
      return;
    onCancel();
  }

  private void CleanupUI()
  {
    this._onSelect = (Action<string>) null;
    this._onCancel = (Action) null;
    if ((UnityEngine.Object) this._root != (UnityEngine.Object) null)
    {
      UnityEngine.Object.Destroy((UnityEngine.Object) this._root);
      this._root = (GameObject) null;
    }
    if (!((UnityEngine.Object) this._canvas != (UnityEngine.Object) null))
      return;
    UnityEngine.Object.Destroy((UnityEngine.Object) this._canvas.gameObject);
    this._canvas = (Canvas) null;
  }

  private static bool IsSeedMultiplicationEntry(string seedName, FarmController.SeedPlantInfo info)
  {
    if (FarmController.s_cropOutputNames == null || !FarmController.s_cropOutputNames.Contains(seedName))
      return false;
    Plant component1 = info.PlantPrefab?.GetComponent<Plant>();
    if (component1?.m_grownPrefabs != null)
    {
      foreach (GameObject grownPrefab in component1.m_grownPrefabs)
      {
        if (!((UnityEngine.Object) grownPrefab == (UnityEngine.Object) null))
        {
          Pickable component2 = grownPrefab.GetComponent<Pickable>();
          if ((UnityEngine.Object) component2?.m_itemPrefab != (UnityEngine.Object) null && component2.m_itemPrefab.name == seedName)
            return false;
        }
      }
    }
    if ((UnityEngine.Object) ZNetScene.instance != (UnityEngine.Object) null)
    {
      GameObject prefab = ZNetScene.instance.GetPrefab(seedName);
      if ((UnityEngine.Object) prefab != (UnityEngine.Object) null)
      {
        ItemDrop component3 = prefab.GetComponent<ItemDrop>();
        if (component3?.m_itemData?.m_shared != null)
        {
          ItemDrop.ItemData.SharedData shared = component3.m_itemData.m_shared;
          if ((double) shared.m_food > 0.0 || (double) shared.m_foodStamina > 0.0)
            return true;
        }
      }
    }
    return false;
  }

  private static string GetCropDisplayName(FarmController.SeedPlantInfo info)
  {
    if ((UnityEngine.Object) info.PlantPrefab == (UnityEngine.Object) null)
      return info.SeedPrefabName;
    Piece component = info.PlantPrefab.GetComponent<Piece>();
    return (UnityEngine.Object) component != (UnityEngine.Object) null && !string.IsNullOrEmpty(component.m_name) ? Localization.instance.Localize(component.m_name) : info.SeedPrefabName;
  }

  private static Sprite GetCropIcon(string seedPrefabName)
  {
    if ((UnityEngine.Object) ZNetScene.instance == (UnityEngine.Object) null)
      return (Sprite) null;
    GameObject prefab = ZNetScene.instance.GetPrefab(seedPrefabName);
    if ((UnityEngine.Object) prefab == (UnityEngine.Object) null)
      return (Sprite) null;
    ItemDrop component = prefab.GetComponent<ItemDrop>();
    return component?.m_itemData == null ? (Sprite) null : component.m_itemData.GetIcon();
  }

  private static TMP_FontAsset ResolveFont()
  {
    if ((UnityEngine.Object) InventoryGui.instance != (UnityEngine.Object) null)
    {
      TextMeshProUGUI[] componentsInChildren = InventoryGui.instance.GetComponentsInChildren<TextMeshProUGUI>(true);
      for (int index = 0; index < componentsInChildren.Length; ++index)
      {
        if ((UnityEngine.Object) componentsInChildren[index] != (UnityEngine.Object) null && (UnityEngine.Object) componentsInChildren[index].font != (UnityEngine.Object) null && componentsInChildren[index].font.name.IndexOf("LiberationSans", StringComparison.OrdinalIgnoreCase) < 0)
          return componentsInChildren[index].font;
      }
    }
    return TMP_Settings.defaultFontAsset;
  }

  private struct CropEntry
  {
    public string SeedPrefabName;
    public string DisplayName;
    public Sprite Icon;
  }

  [HarmonyPatch(typeof (Hud), "InRadial")]
  private static class HudInRadial_Patch
  {
    private static void Postfix(ref bool __result)
    {
      if (__result || !FarmZoneCropPicker.IsOpen)
        return;
      __result = true;
    }
  }
}


// Decompiled with JetBrains decompiler
// Type: Companions.FarmZonePlacer
// Assembly: Companions, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 0A9D6DFD-4997-4979-8571-F0AB8638B627
// Assembly location: C:\Users\travi\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\MYmodTest\BepInEx\plugins\ProfMags-Offline_Companions\Companions.dll

using HarmonyLib;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

#nullable disable
namespace Companions;

public class FarmZonePlacer : MonoBehaviour
{
  private bool _active;
  private CompanionSetup _companion;
  private ZNetView _companionNview;
  private FarmZoneVisual _zoneVisual;
  private List<FarmZone> _zones;
  private float _previewHalfSize = 4f;
  private float _previewRotation;
  private const float MinHalfSize = 2f;
  private const float MaxHalfSize = 15f;
  private const float SizeStep = 1f;
  private const float RotationStep = 15f;
  private LineRenderer _previewBorder;
  private GameObject _previewQuad;
  private Material _previewBorderMat;
  private Material _previewQuadMat;
  private bool _previewValid;
  private Vector3 _previewCenter;
  private bool _rmbScrolled;
  private float _savedZoomSens;
  private int _groundMask;
  private const float MaxRayDist = 50f;
  private GameObject _hudRoot;
  private TextMeshProUGUI _hintLabel;
  private TextMeshProUGUI _sizeLabel;
  private Canvas _hudCanvas;
  private FarmZone _pendingZone;
  private bool _pickingCrop;
  private static readonly Color PreviewColor = new Color(0.3f, 0.85f, 0.3f, 0.7f);
  private static readonly Color PreviewQuadColor = new Color(0.3f, 0.85f, 0.3f, 0.12f);
  private static readonly Color InvalidColor = new Color(0.85f, 0.3f, 0.3f, 0.7f);

  public static FarmZonePlacer Instance { get; private set; }

  public bool IsVisible => this._active;

  public static void EnsureInstance()
  {
    if ((UnityEngine.Object) FarmZonePlacer.Instance != (UnityEngine.Object) null)
      return;
    GameObject target = new GameObject("HC_FarmZonePlacer");
    UnityEngine.Object.DontDestroyOnLoad((UnityEngine.Object) target);
    target.AddComponent<FarmZonePlacer>();
  }

  public static void Enter(CompanionSetup companion)
  {
    if ((UnityEngine.Object) companion == (UnityEngine.Object) null)
      return;
    FarmZonePlacer.EnsureInstance();
    FarmZonePlacer.Instance.Show(companion);
  }

  private void Awake()
  {
    if ((UnityEngine.Object) FarmZonePlacer.Instance != (UnityEngine.Object) null && (UnityEngine.Object) FarmZonePlacer.Instance != (UnityEngine.Object) this)
    {
      UnityEngine.Object.Destroy((UnityEngine.Object) this.gameObject);
    }
    else
    {
      FarmZonePlacer.Instance = this;
      this._groundMask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "piece_nonsolid");
    }
  }

  private void OnDestroy()
  {
    if ((UnityEngine.Object) FarmZonePlacer.Instance == (UnityEngine.Object) this)
      FarmZonePlacer.Instance = (FarmZonePlacer) null;
    this.CleanupPreview();
    this.CleanupHud();
  }

  private void Show(CompanionSetup companion)
  {
    this._companion = companion;
    this._companionNview = companion.GetComponent<ZNetView>();
    this._zoneVisual = companion.GetComponent<FarmZoneVisual>();
    ZDO zdo = this._companionNview?.GetZDO();
    this._zones = zdo != null ? FarmZoneSerializer.Load(zdo) : new List<FarmZone>();
    this._zoneVisual?.ShowZones(this._zones);
    if ((UnityEngine.Object) this._previewBorder == (UnityEngine.Object) null)
      this.BuildPreview();
    if ((UnityEngine.Object) this._hudRoot == (UnityEngine.Object) null)
      this.BuildHud();
    this._previewHalfSize = 4f;
    this._previewRotation = 0.0f;
    this._rmbScrolled = false;
    this._pickingCrop = false;
    this._active = true;
    if ((UnityEngine.Object) GameCamera.instance != (UnityEngine.Object) null)
    {
      this._savedZoomSens = GameCamera.instance.m_zoomSens;
      GameCamera.instance.m_zoomSens = 0.0f;
    }
    this._hudRoot.SetActive(true);
    this.UpdateHintText();
  }

  public void Hide()
  {
    this._active = false;
    this._pickingCrop = false;
    if ((UnityEngine.Object) this._previewBorder != (UnityEngine.Object) null)
      this._previewBorder.enabled = false;
    if ((UnityEngine.Object) this._previewQuad != (UnityEngine.Object) null)
      this._previewQuad.SetActive(false);
    if ((UnityEngine.Object) this._hudRoot != (UnityEngine.Object) null)
      this._hudRoot.SetActive(false);
    this._zoneVisual?.HideAll();
    if ((UnityEngine.Object) GameCamera.instance != (UnityEngine.Object) null && (double) this._savedZoomSens > 0.0)
      GameCamera.instance.m_zoomSens = this._savedZoomSens;
    Cursor.lockState = CursorLockMode.Locked;
    Cursor.visible = false;
  }

  private void Update()
  {
    if (!this._active)
    {
      if (!((UnityEngine.Object) Player.m_localPlayer != (UnityEngine.Object) null) || InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen() || FarmZoneCropPicker.IsOpen || !Input.GetKeyDown(ModConfig.FarmZoneKey.Value) || !Input.GetKey(ModConfig.FarmZoneModifier.Value))
        return;
      CompanionSetup nearestCompanion = FarmZonePlacer.FindNearestCompanion();
      if (!((UnityEngine.Object) nearestCompanion != (UnityEngine.Object) null))
        return;
      FarmZonePlacer.Enter(nearestCompanion);
    }
    else if ((UnityEngine.Object) this._companion == (UnityEngine.Object) null || !(bool) (UnityEngine.Object) this._companion)
    {
      this.Hide();
    }
    else
    {
      if (this._pickingCrop)
        return;
      if (Input.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyMenu") || Input.GetKeyDown(ModConfig.FarmZoneKey.Value) && Input.GetKey(ModConfig.FarmZoneModifier.Value))
      {
        this.Hide();
      }
      else
      {
        this.UpdateRaycast();
        this.UpdatePreview();
        this.UpdateInput();
        this.UpdateHintText();
      }
    }
  }

  private static CompanionSetup FindNearestCompanion()
  {
    Player localPlayer = Player.m_localPlayer;
    if ((UnityEngine.Object) localPlayer == (UnityEngine.Object) null)
      return (CompanionSetup) null;
    Vector3 position = localPlayer.transform.position;
    CompanionSetup nearestCompanion = (CompanionSetup) null;
    float num1 = float.MaxValue;
    foreach (CompanionSetup allCompanion in (IEnumerable<CompanionSetup>) CompanionSetup.AllCompanions)
    {
      if (!((UnityEngine.Object) allCompanion == (UnityEngine.Object) null))
      {
        float num2 = Vector3.Distance(position, allCompanion.transform.position);
        if ((double) num2 < (double) num1)
        {
          num1 = num2;
          nearestCompanion = allCompanion;
        }
      }
    }
    return nearestCompanion;
  }

  private void UpdateRaycast()
  {
    this._previewValid = false;
    GameCamera instance = GameCamera.instance;
    if ((UnityEngine.Object) instance == (UnityEngine.Object) null)
      return;
    Transform transform = instance.transform;
    RaycastHit hitInfo;
    if (!Physics.Raycast(new Ray(transform.position, transform.forward), out hitInfo, 50f, this._groundMask) || (UnityEngine.Object) hitInfo.collider.attachedRigidbody != (UnityEngine.Object) null)
      return;
    this._previewCenter = hitInfo.point;
    this._previewValid = true;
  }

  private void UpdatePreview()
  {
    if (!this._previewValid)
    {
      this._previewBorder.enabled = false;
      this._previewQuad.SetActive(false);
    }
    else
    {
      Color previewColor = FarmZonePlacer.PreviewColor;
      float num = this._previewCenter.y + 0.12f;
      float previewHalfSize = this._previewHalfSize;
      Vector3 c0;
      Vector3 c1;
      Vector3 c2;
      Vector3 c3;
      new FarmZone()
      {
        Center = this._previewCenter,
        HalfSize = previewHalfSize,
        Rotation = this._previewRotation
      }.GetCorners(out c0, out c1, out c2, out c3);
      c0.y = num;
      c1.y = num;
      c2.y = num;
      c3.y = num;
      this._previewBorder.SetPosition(0, c0);
      this._previewBorder.SetPosition(1, c1);
      this._previewBorder.SetPosition(2, c2);
      this._previewBorder.SetPosition(3, c3);
      this._previewBorder.startColor = previewColor;
      this._previewBorder.endColor = previewColor;
      this._previewBorder.enabled = true;
      this._previewQuad.transform.position = new Vector3(this._previewCenter.x, num + 0.01f, this._previewCenter.z);
      this._previewQuad.transform.rotation = Quaternion.Euler(90f, this._previewRotation, 0.0f);
      this._previewQuad.transform.localScale = new Vector3(previewHalfSize * 2f, 1f, previewHalfSize * 2f);
      this._previewQuadMat.color = FarmZonePlacer.PreviewQuadColor;
      this._previewQuad.SetActive(true);
      if (!((UnityEngine.Object) this._sizeLabel != (UnityEngine.Object) null))
        return;
      string str = (double) Mathf.Abs(this._previewRotation) > 0.10000000149011612 ? $"  ({this._previewRotation:F0}°)" : "";
      this._sizeLabel.text = $"{(ValueType) (float) ((double) previewHalfSize * 2.0):F0}m x {(ValueType) (float) ((double) previewHalfSize * 2.0):F0}m{str}";
    }
  }

  private void UpdateInput()
  {
    float axis = Input.GetAxis("Mouse ScrollWheel");
    if (Input.GetMouseButton(1) && !ZInput.IsGamepadActive())
    {
      if ((double) axis > 0.0099999997764825821)
      {
        this._previewRotation = Mathf.Repeat(this._previewRotation + 15f, 360f);
        this._rmbScrolled = true;
      }
      else if ((double) axis < -0.0099999997764825821)
      {
        this._previewRotation = Mathf.Repeat(this._previewRotation - 15f, 360f);
        this._rmbScrolled = true;
      }
    }
    else if ((double) axis > 0.0099999997764825821)
      this._previewHalfSize = Mathf.Min(this._previewHalfSize + 1f, 15f);
    else if ((double) axis < -0.0099999997764825821)
      this._previewHalfSize = Mathf.Max(this._previewHalfSize - 1f, 2f);
    if (Input.GetMouseButtonDown(1) && !ZInput.IsGamepadActive())
      this._rmbScrolled = false;
    if (Input.GetMouseButtonUp(1) && !ZInput.IsGamepadActive() && !this._rmbScrolled)
      this.TryDelete();
    if (ZInput.GetButtonDown("JoyLTrigger"))
      this._previewHalfSize = Mathf.Max(this._previewHalfSize - 1f, 2f);
    if (ZInput.GetButtonDown("JoyRTrigger"))
      this._previewHalfSize = Mathf.Min(this._previewHalfSize + 1f, 15f);
    if (ZInput.GetButtonDown("JoyDPadUp"))
      this._previewRotation = Mathf.Repeat(this._previewRotation + 15f, 360f);
    if (ZInput.GetButtonDown("JoyDPadDown"))
      this._previewRotation = Mathf.Repeat(this._previewRotation - 15f, 360f);
    if (Input.GetMouseButtonDown(0) && !ZInput.IsGamepadActive() || ZInput.GetButtonDown("JoyButtonA"))
      this.TryPlace();
    if (!ZInput.GetButtonDown("JoyButtonX"))
      return;
    this.TryDelete();
  }

  private void TryPlace()
  {
    if (!this._previewValid)
      return;
    this._pendingZone = new FarmZone()
    {
      Center = this._previewCenter,
      HalfSize = this._previewHalfSize,
      Rotation = this._previewRotation,
      CropSeed = ""
    };
    this._previewBorder.enabled = false;
    this._previewQuad.SetActive(false);
    this._pickingCrop = true;
    FarmZoneCropPicker.Show(this._pendingZone, new Action<string>(this.OnCropSelected), new Action(this.OnCropCancelled));
  }

  private void OnCropSelected(string seedPrefabName)
  {
    this._pendingZone.CropSeed = seedPrefabName ?? "";
    this._zones.Add(this._pendingZone);
    this.SaveZones();
    this._zoneVisual?.ShowZones(this._zones);
    this._pickingCrop = false;
  }

  private void OnCropCancelled() => this._pickingCrop = false;

  private void TryDelete()
  {
    if (!this._previewValid)
      return;
    for (int index = this._zones.Count - 1; index >= 0; --index)
    {
      if (this._zones[index].Contains(this._previewCenter))
      {
        this._zones.RemoveAt(index);
        this.SaveZones();
        this._zoneVisual?.ShowZones(this._zones);
        break;
      }
    }
  }

  private void SaveZones()
  {
    ZDO zdo = this._companionNview?.GetZDO();
    if (zdo == null)
      return;
    FarmZoneSerializer.Save(zdo, this._zones);
  }

  private void BuildPreview()
  {
    GameObject gameObject = new GameObject("FarmZonePreviewBorder");
    gameObject.transform.SetParent(this.transform, false);
    this._previewBorderMat = new Material(Shader.Find("Sprites/Default"));
    this._previewBorder = gameObject.AddComponent<LineRenderer>();
    this._previewBorder.useWorldSpace = true;
    this._previewBorder.loop = true;
    this._previewBorder.positionCount = 4;
    this._previewBorder.startWidth = 0.08f;
    this._previewBorder.endWidth = 0.08f;
    this._previewBorder.material = this._previewBorderMat;
    this._previewBorder.startColor = FarmZonePlacer.PreviewColor;
    this._previewBorder.endColor = FarmZonePlacer.PreviewColor;
    this._previewBorder.shadowCastingMode = ShadowCastingMode.Off;
    this._previewBorder.receiveShadows = false;
    this._previewBorder.enabled = false;
    this._previewQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
    this._previewQuad.name = "FarmZonePreviewFill";
    this._previewQuad.transform.SetParent(this.transform, false);
    this._previewQuad.transform.rotation = Quaternion.Euler(90f, 0.0f, 0.0f);
    UnityEngine.Object.Destroy((UnityEngine.Object) this._previewQuad.GetComponent<Collider>());
    this._previewQuadMat = new Material(Shader.Find("Sprites/Default"));
    this._previewQuadMat.color = FarmZonePlacer.PreviewQuadColor;
    this._previewQuad.GetComponent<MeshRenderer>().material = this._previewQuadMat;
    this._previewQuad.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    this._previewQuad.GetComponent<MeshRenderer>().receiveShadows = false;
    this._previewQuad.SetActive(false);
  }

  private void CleanupPreview()
  {
    if ((UnityEngine.Object) this._previewBorderMat != (UnityEngine.Object) null)
      UnityEngine.Object.Destroy((UnityEngine.Object) this._previewBorderMat);
    if ((UnityEngine.Object) this._previewQuadMat != (UnityEngine.Object) null)
      UnityEngine.Object.Destroy((UnityEngine.Object) this._previewQuadMat);
    if ((UnityEngine.Object) this._previewBorder != (UnityEngine.Object) null)
      UnityEngine.Object.Destroy((UnityEngine.Object) this._previewBorder.gameObject);
    if (!((UnityEngine.Object) this._previewQuad != (UnityEngine.Object) null))
      return;
    UnityEngine.Object.Destroy((UnityEngine.Object) this._previewQuad);
  }

  private void BuildHud()
  {
    GameObject gameObject1 = new GameObject("HC_FarmZoneHud", new System.Type[3]
    {
      typeof (Canvas),
      typeof (CanvasScaler),
      typeof (GraphicRaycaster)
    });
    gameObject1.transform.SetParent(this.transform, false);
    this._hudCanvas = gameObject1.GetComponent<Canvas>();
    this._hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
    this._hudCanvas.sortingOrder = 28;
    CanvasScaler component1 = gameObject1.GetComponent<CanvasScaler>();
    component1.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    component1.referenceResolution = new Vector2(1920f, 1080f);
    this._hudRoot = new GameObject("HudRoot", new System.Type[2]
    {
      typeof (RectTransform),
      typeof (Image)
    });
    this._hudRoot.transform.SetParent(gameObject1.transform, false);
    RectTransform component2 = this._hudRoot.GetComponent<RectTransform>();
    component2.anchorMin = new Vector2(0.5f, 0.0f);
    component2.anchorMax = new Vector2(0.5f, 0.0f);
    component2.pivot = new Vector2(0.5f, 0.0f);
    component2.sizeDelta = new Vector2(500f, 60f);
    component2.anchoredPosition = new Vector2(0.0f, 20f);
    this._hudRoot.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.06f, 0.85f);
    TMP_FontAsset tmpFontAsset = FarmZonePlacer.ResolveFont();
    GameObject gameObject2 = new GameObject("Hint", new System.Type[1]
    {
      typeof (RectTransform)
    });
    gameObject2.transform.SetParent(this._hudRoot.transform, false);
    this._hintLabel = gameObject2.AddComponent<TextMeshProUGUI>();
    if ((UnityEngine.Object) tmpFontAsset != (UnityEngine.Object) null)
      this._hintLabel.font = tmpFontAsset;
    this._hintLabel.fontSize = 14f;
    this._hintLabel.color = new Color(0.85f, 0.8f, 0.65f, 1f);
    this._hintLabel.alignment = TextAlignmentOptions.Center;
    this._hintLabel.raycastTarget = false;
    RectTransform component3 = gameObject2.GetComponent<RectTransform>();
    component3.anchorMin = new Vector2(0.0f, 0.5f);
    component3.anchorMax = new Vector2(1f, 1f);
    component3.offsetMin = new Vector2(10f, 0.0f);
    component3.offsetMax = new Vector2(-10f, 0.0f);
    GameObject gameObject3 = new GameObject("Size", new System.Type[1]
    {
      typeof (RectTransform)
    });
    gameObject3.transform.SetParent(this._hudRoot.transform, false);
    this._sizeLabel = gameObject3.AddComponent<TextMeshProUGUI>();
    if ((UnityEngine.Object) tmpFontAsset != (UnityEngine.Object) null)
      this._sizeLabel.font = tmpFontAsset;
    this._sizeLabel.fontSize = 12f;
    this._sizeLabel.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
    this._sizeLabel.alignment = TextAlignmentOptions.Center;
    this._sizeLabel.raycastTarget = false;
    RectTransform component4 = gameObject3.GetComponent<RectTransform>();
    component4.anchorMin = new Vector2(0.0f, 0.0f);
    component4.anchorMax = new Vector2(1f, 0.5f);
    component4.offsetMin = new Vector2(10f, 0.0f);
    component4.offsetMax = new Vector2(-10f, 0.0f);
    this._hudRoot.SetActive(false);
  }

  private void UpdateHintText()
  {
    if ((UnityEngine.Object) this._hintLabel == (UnityEngine.Object) null)
      return;
    this._hintLabel.text = ZInput.IsGamepadActive() ? ModLocalization.Loc("hc_farmzone_hint_gamepad") : ModLocalization.Loc("hc_farmzone_hint");
  }

  private void CleanupHud()
  {
    if (!((UnityEngine.Object) this._hudCanvas != (UnityEngine.Object) null))
      return;
    UnityEngine.Object.Destroy((UnityEngine.Object) this._hudCanvas.gameObject);
  }

  private static TMP_FontAsset ResolveFont()
  {
    if ((UnityEngine.Object) InventoryGui.instance != (UnityEngine.Object) null)
    {
      TextMeshProUGUI[] componentsInChildren = InventoryGui.instance.GetComponentsInChildren<TextMeshProUGUI>(true);
      for (int index = 0; index < componentsInChildren.Length; ++index)
      {
        if ((UnityEngine.Object) componentsInChildren[index] != (UnityEngine.Object) null && (UnityEngine.Object) componentsInChildren[index].font != (UnityEngine.Object) null && componentsInChildren[index].font.name.IndexOf("LiberationSans", StringComparison.OrdinalIgnoreCase) < 0)
          return componentsInChildren[index].font;
      }
    }
    return TMP_Settings.defaultFontAsset;
  }

  [HarmonyPatch(typeof (Player), "UpdatePlacement")]
  private static class BlockPlacement
  {
    private static bool Prefix()
    {
      return (UnityEngine.Object) FarmZonePlacer.Instance == (UnityEngine.Object) null || !FarmZonePlacer.Instance._active;
    }
  }
}


// Decompiled with JetBrains decompiler
// Type: Companions.FarmZoneSerializer
// Assembly: Companions, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 0A9D6DFD-4997-4979-8571-F0AB8638B627
// Assembly location: C:\Users\travi\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\MYmodTest\BepInEx\plugins\ProfMags-Offline_Companions\Companions.dll

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

#nullable disable
namespace Companions;

internal static class FarmZoneSerializer
{
  internal static readonly int FarmZonesHash = "HC_FarmZones".GetStableHashCode();

  internal static void Save(ZDO zdo, List<FarmZone> zones)
  {
    if (zdo == null)
      return;
    StringBuilder stringBuilder = new StringBuilder();
    for (int index = 0; index < zones.Count; ++index)
    {
      if (zones[index].IsValid)
      {
        if (stringBuilder.Length > 0)
          stringBuilder.Append('|');
        FarmZone zone = zones[index];
        stringBuilder.Append(zone.Center.x.ToString((IFormatProvider) CultureInfo.InvariantCulture));
        stringBuilder.Append(',');
        stringBuilder.Append(zone.Center.y.ToString((IFormatProvider) CultureInfo.InvariantCulture));
        stringBuilder.Append(',');
        stringBuilder.Append(zone.Center.z.ToString((IFormatProvider) CultureInfo.InvariantCulture));
        stringBuilder.Append(',');
        stringBuilder.Append(zone.HalfSize.ToString((IFormatProvider) CultureInfo.InvariantCulture));
        stringBuilder.Append(',');
        stringBuilder.Append(zone.CropSeed ?? "");
        stringBuilder.Append(',');
        stringBuilder.Append(zone.Rotation.ToString((IFormatProvider) CultureInfo.InvariantCulture));
      }
    }
    zdo.Set(FarmZoneSerializer.FarmZonesHash, stringBuilder.ToString());
  }

  internal static List<FarmZone> Load(ZDO zdo)
  {
    List<FarmZone> farmZoneList = new List<FarmZone>(8);
    if (zdo == null)
      return farmZoneList;
    string str1 = zdo.GetString(FarmZoneSerializer.FarmZonesHash);
    if (string.IsNullOrEmpty(str1))
      return farmZoneList;
    string str2 = str1;
    char[] chArray1 = new char[1]{ '|' };
    foreach (string str3 in str2.Split(chArray1))
    {
      char[] chArray2 = new char[1]{ ',' };
      string[] strArray = str3.Split(chArray2);
      float result1;
      float result2;
      float result3;
      float result4;
      if (strArray.Length >= 4 && float.TryParse(strArray[0], NumberStyles.Float, (IFormatProvider) CultureInfo.InvariantCulture, out result1) && float.TryParse(strArray[1], NumberStyles.Float, (IFormatProvider) CultureInfo.InvariantCulture, out result2) && float.TryParse(strArray[2], NumberStyles.Float, (IFormatProvider) CultureInfo.InvariantCulture, out result3) && float.TryParse(strArray[3], NumberStyles.Float, (IFormatProvider) CultureInfo.InvariantCulture, out result4))
      {
        string str4 = strArray.Length > 4 ? strArray[4] : "";
        float result5 = 0.0f;
        if (strArray.Length > 5)
          float.TryParse(strArray[5], NumberStyles.Float, (IFormatProvider) CultureInfo.InvariantCulture, out result5);
        farmZoneList.Add(new FarmZone()
        {
          Center = new Vector3(result1, result2, result3),
          HalfSize = result4,
          CropSeed = str4,
          Rotation = result5
        });
      }
    }
    return farmZoneList;
  }
}


// Decompiled with JetBrains decompiler
// Type: Companions.FarmZoneVisual
// Assembly: Companions, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 0A9D6DFD-4997-4979-8571-F0AB8638B627
// Assembly location: C:\Users\travi\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\MYmodTest\BepInEx\plugins\ProfMags-Offline_Companions\Companions.dll

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#nullable disable
namespace Companions;

public class FarmZoneVisual : MonoBehaviour
{
  private readonly List<LineRenderer> _borders = new List<LineRenderer>();
  private Material _borderMaterial;
  private const float Width = 0.06f;
  private static readonly Color ZoneColor = new Color(0.3f, 0.85f, 0.3f, 0.7f);

  private void Awake() => this._borderMaterial = new Material(Shader.Find("Sprites/Default"));

  private void OnDestroy()
  {
    if (!((Object) this._borderMaterial != (Object) null))
      return;
    Object.Destroy((Object) this._borderMaterial);
  }

  private LineRenderer CreateBorder(int index)
  {
    GameObject gameObject = new GameObject($"FarmZoneBorder_{index}");
    gameObject.transform.SetParent(this.transform, false);
    LineRenderer border = gameObject.AddComponent<LineRenderer>();
    border.useWorldSpace = true;
    border.loop = true;
    border.positionCount = 4;
    border.startWidth = 0.06f;
    border.endWidth = 0.06f;
    border.material = this._borderMaterial;
    border.startColor = FarmZoneVisual.ZoneColor;
    border.endColor = FarmZoneVisual.ZoneColor;
    border.shadowCastingMode = ShadowCastingMode.Off;
    border.receiveShadows = false;
    border.enabled = false;
    return border;
  }

  private void EnsureCapacity(int count)
  {
    while (this._borders.Count < count)
      this._borders.Add(this.CreateBorder(this._borders.Count));
  }

  internal void ShowZones(List<FarmZone> zones)
  {
    // ISSUE: explicit non-virtual call
    this.EnsureCapacity(zones != null ? __nonvirtual (zones.Count) : 0);
    for (int index = 0; index < this._borders.Count; ++index)
    {
      if (zones != null && index < zones.Count && zones[index].IsValid)
      {
        FarmZone zone = zones[index];
        float num = zone.Center.y + 0.12f;
        Vector3 c0;
        Vector3 c1;
        Vector3 c2;
        Vector3 c3;
        zone.GetCorners(out c0, out c1, out c2, out c3);
        c0.y = num;
        c1.y = num;
        c2.y = num;
        c3.y = num;
        this._borders[index].SetPosition(0, c0);
        this._borders[index].SetPosition(1, c1);
        this._borders[index].SetPosition(2, c2);
        this._borders[index].SetPosition(3, c3);
        this._borders[index].enabled = true;
      }
      else
        this._borders[index].enabled = false;
    }
  }

  internal void HideAll()
  {
    for (int index = 0; index < this._borders.Count; ++index)
    {
      if ((Object) this._borders[index] != (Object) null)
        this._borders[index].enabled = false;
    }
  }
}
