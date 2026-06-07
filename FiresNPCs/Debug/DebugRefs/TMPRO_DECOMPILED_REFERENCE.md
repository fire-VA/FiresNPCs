# TMPro Decompiled Reference — TMP_Text, TMP_InputField, TMP_Dropdown

Decompiled from `Unity.TextMeshPro.dll` (Version=0.0.0.0) shipped with Valheim.
Source: `C:\Users\travi\OneDrive\Desktop\ValheimDll\Unity.TextMeshPro.dll`

This file covers TextMeshPro components critical to our UI Builder — text rendering,
input fields, and TMP dropdowns. Understanding these internals lets us make smarter
decisions about property application in `ApplyNodeDeep()`, when to use `ForceMeshUpdate()`,
and how TMP_InputField/TMP_Dropdown differ from legacy Unity UI equivalents.

**See also:**
- `UNITY_UI_DECOMPILED_REFERENCE.md` — Part 1 (LayoutRebuilder, LayoutGroup, ScrollRect)
- `UNITY_UI_DECOMPILED_REFERENCE_2.md` — Part 2 (Selectable, Button, Slider, CanvasScaler, GraphicRaycaster, Mask, RectMask2D)
- `UNITY_UI_DECOMPILED_REFERENCE_3.md` — Part 3 (HorizontalOrVerticalLayoutGroup, GridLayoutGroup, ContentSizeFitter, Image, Toggle, Dropdown)

### Reference File Index

| File | Contents | Status |
|------|----------|--------|
| `UNITY_UI_DECOMPILED_REFERENCE.md` | LayoutRebuilder, LayoutGroup, ScrollRect | ? Complete |
| `UNITY_UI_DECOMPILED_REFERENCE_2.md` | Selectable, Button, Slider, CanvasScaler, GraphicRaycaster, Mask, RectMask2D | ? Complete |
| `UNITY_UI_DECOMPILED_REFERENCE_3.md` | HorizontalOrVerticalLayoutGroup, HLG/VLG, GridLayoutGroup, ContentSizeFitter, Image, Toggle, Dropdown (Legacy) | ? Complete |
| `TMPRO_DECOMPILED_REFERENCE.md` | TMP_Text, TMP_InputField, TMP_Dropdown — text rendering, input handling, TMP dropdown vs legacy | ? Complete |
| `VALHEIM_UI_REFERENCE.md` | InventoryGui, InventoryGrid, UITooltip, UIGroupHandler — Valheim-specific UI components our parser captures | ?? Pending |
| `VALHEIM_COMPONENT_REFERENCE.md` | ZRoutedRpc (for UILayoutSyncRPC), ZNet/ZDO patterns, Player/Game UI hooks our integration uses | ?? Pending |

---

## Table of Contents

1. [TMP_Text (abstract)](#tmp_text-abstract) — Base class for all TMP text rendering
2. [TMP_InputField](#tmp_inputfield) — TMP-based input field (replaces legacy InputField)
3. [TMP_Dropdown](#tmp_dropdown) — TMP-based dropdown (replaces legacy Dropdown)

---

## TMP_Text (abstract)

**Key insights for our UI Builder:**

- **`text` property setter** compares length AND string equality before marking dirty. If the text is unchanged, no work is done — safe to call repeatedly from `ApplyNodeDeep()`. It calls both `SetVerticesDirty()` and `SetLayoutDirty()`.
- **`fontSize` setter** checks `m_fontSize == value` — no-op if unchanged. Calls both `SetVerticesDirty()` and `SetLayoutDirty()`. If auto-sizing is disabled, also updates `m_fontSizeBase`.
- **`color` property** (inherited from `MaskableGraphic`) only calls `SetVerticesDirty()` — color changes don't affect layout. Our `ApplyTextDeep()` setting color is cheap.
- **`font` setter** triggers `LoadFontAsset()` which reloads the entire font. This is expensive — our builder should avoid setting font unless it actually changed.
- **`alignment` setter** decomposes into `m_HorizontalAlignment` + `m_VerticalAlignment`. Only calls `SetVerticesDirty()` (not `SetLayoutDirty()`). Alignment changes don't require layout recalculation.
- **`SetVerticesDirty()`** marks for graphical rebuild (mesh regeneration). `SetLayoutDirty()` marks for layout rebuild. Properties that affect size (fontSize, text, fontStyle) call both; properties that only affect appearance (color, alignment) call only vertices.
- **`ForceMeshUpdate()`** synchronously regenerates the text mesh. Use this when you need `textInfo` (character positions, bounds, etc.) to be current immediately — e.g., for our selection overlay positioning.
- **`preferredWidth`/`preferredHeight`** are computed on-demand via `GetPreferredWidth()`/`GetPreferredHeight()`. They run through `CalculatePreferredValues()` which does a full text layout pass without actually generating vertices. These are used by `ContentSizeFitter` and `LayoutGroup` to determine element sizes.
- **`textWrappingMode`** replaces the deprecated `enableWordWrapping`. It's a `TextWrappingModes` enum. The property setter calls both dirty methods.
- **`richText` property** controls whether `<b>`, `<color>`, etc. tags are parsed. Our builder stores this on UIElementNode and applies it — the setter calls both dirty methods.
- **`overflowMode`** (Overflow, Ellipsis, Truncate, Page, Linked, ScrollRect) controls what happens when text exceeds bounds. Property setter calls both dirty methods.
- **`margin` property** — Vector4 (left, top, right, bottom). Setter calls `ComputeMarginSize()` + `SetVerticesDirty()`. This affects the text rendering area within the RectTransform.
- **Text processing pipeline:** `text` setter ? `m_inputSource = TextString` ? next layout pass calls `ParseInputText()` ? `PopulateTextBackingArray()` ? `PopulateTextProcessingArray()` ? `SetArraySizes()` ? `GenerateTextMesh()` (in subclass). Rich text tags, style tags, escape sequences are all resolved during `PopulateTextProcessingArray()`.
- **`m_havePropertiesChanged`** is the master dirty flag. Most property setters set this to `true`. The rendering pass checks it to decide if regeneration is needed.

```csharp
// Decompiled with JetBrains decompiler
// Type: TMPro.TMP_Text
// Assembly: Unity.TextMeshPro, Version=0.0.0.0

// TMP_Text extends MaskableGraphic which extends Graphic (UnityEngine.UI)
// This means all TMP text components participate in the Canvas rendering pipeline.

public abstract class TMP_Text : MaskableGraphic
{
  [SerializeField] protected string m_text;
  private bool m_IsTextBackingStringDirty;
  [SerializeField] protected TMP_FontAsset m_fontAsset;
  [SerializeField] protected Material m_sharedMaterial;
  [SerializeField] protected Color32 m_fontColor32 = (Color32) Color.white;
  [SerializeField] protected Color m_fontColor = Color.white;
  [SerializeField] protected bool m_enableVertexGradient;
  [SerializeField] protected VertexGradient m_fontColorGradient = new VertexGradient(Color.white);
  [SerializeField] protected float m_fontSize = -99f;
  protected float m_currentFontSize;
  [SerializeField] protected float m_fontSizeBase = 36f;
  [SerializeField] protected bool m_enableAutoSizing;
  [SerializeField] protected float m_fontSizeMin;
  [SerializeField] protected float m_fontSizeMax;
  [SerializeField] protected FontStyles m_fontStyle;
  [SerializeField] protected HorizontalAlignmentOptions m_HorizontalAlignment = HorizontalAlignmentOptions.Left;
  [SerializeField] protected VerticalAlignmentOptions m_VerticalAlignment = VerticalAlignmentOptions.Top;
  [SerializeField] protected TextAlignmentOptions m_textAlignment = TextAlignmentOptions.Converted;
  [SerializeField] protected float m_characterSpacing;
  [SerializeField] protected float m_lineSpacing;
  [SerializeField] protected float m_paragraphSpacing;
  [SerializeField] protected TextWrappingModes m_TextWrappingMode;
  [SerializeField] protected TextOverflowModes m_overflowMode;
  [SerializeField] protected bool m_isRichText = true;
  [SerializeField] protected Vector4 m_margin = new Vector4(0, 0, 0, 0);
  protected TMP_TextInfo m_textInfo;
  protected bool m_havePropertiesChanged;
  internal TMP_Text.TextInputSources m_inputSource;

  // --- Text property: The core text content ---

  /// <summary>
  /// Compares length AND equality before marking dirty. Safe to call repeatedly.
  /// Sets both vertices dirty AND layout dirty — text changes can affect size.
  /// </summary>
  public virtual string text
  {
    get => m_IsTextBackingStringDirty ? InternalTextBackingArrayToString() : m_text;
    set
    {
      if (!m_IsTextBackingStringDirty && m_text != null && value != null &&
          m_text.Length == value.Length && m_text == value) return;
      m_IsTextBackingStringDirty = false;
      m_text = value;
      m_inputSource = TextInputSources.TextString;
      m_havePropertiesChanged = true;
      SetVerticesDirty();
      SetLayoutDirty();
    }
  }

  // --- Font property: Triggers full font reload ---

  /// <summary>
  /// EXPENSIVE: Calls LoadFontAsset() which reloads the entire font asset.
  /// Only set this when the font actually changes. Our ApplyTextDeep should compare first.
  /// </summary>
  public TMP_FontAsset font
  {
    get => m_fontAsset;
    set
    {
      if (m_fontAsset == value) return;
      m_fontAsset = value;
      LoadFontAsset();
      m_havePropertiesChanged = true;
      SetVerticesDirty();
      SetLayoutDirty();
    }
  }

  // --- Color: Only vertices dirty (no layout impact) ---

  public override Color color
  {
    get => m_fontColor;
    set
    {
      if (m_fontColor == value) return;
      m_havePropertiesChanged = true;
      m_fontColor = value;
      SetVerticesDirty(); // No SetLayoutDirty — color doesn't affect size
    }
  }

  public float alpha
  {
    get => m_fontColor.a;
    set
    {
      if (m_fontColor.a == value) return;
      m_fontColor.a = value;
      m_havePropertiesChanged = true;
      SetVerticesDirty();
    }
  }

  // --- Font size: Affects both vertices and layout ---

  public float fontSize
  {
    get => m_fontSize;
    set
    {
      if (m_fontSize == value) return;
      m_havePropertiesChanged = true;
      m_fontSize = value;
      if (!m_enableAutoSizing) m_fontSizeBase = m_fontSize;
      SetVerticesDirty();
      SetLayoutDirty();
    }
  }

  // --- Font style: Bold, italic, etc. ---

  public FontStyles fontStyle
  {
    get => m_fontStyle;
    set
    {
      if (m_fontStyle == value) return;
      m_fontStyle = value;
      m_havePropertiesChanged = true;
      SetVerticesDirty();
      SetLayoutDirty();
    }
  }

  // --- Alignment: Only vertices dirty ---

  /// <summary>
  /// Alignment only affects vertex positioning, NOT layout size.
  /// So only SetVerticesDirty() is called — cheaper than a full layout pass.
  /// </summary>
  public HorizontalAlignmentOptions horizontalAlignment
  {
    get => m_HorizontalAlignment;
    set
    {
      if (m_HorizontalAlignment == value) return;
      m_HorizontalAlignment = value;
      m_havePropertiesChanged = true;
      SetVerticesDirty();
    }
  }

  public VerticalAlignmentOptions verticalAlignment
  {
    get => m_VerticalAlignment;
    set
    {
      if (m_VerticalAlignment == value) return;
      m_VerticalAlignment = value;
      m_havePropertiesChanged = true;
      SetVerticesDirty();
    }
  }

  /// <summary>
  /// Combined alignment. Decomposes into HorizontalAlignment + VerticalAlignment.
  /// </summary>
  public TextAlignmentOptions alignment
  {
    get => (TextAlignmentOptions)(m_HorizontalAlignment | (HorizontalAlignmentOptions)m_VerticalAlignment);
    set
    {
      var h = (HorizontalAlignmentOptions)(value & (TextAlignmentOptions)0xFF);
      var v = (VerticalAlignmentOptions)(value & (TextAlignmentOptions)0xFF00);
      if (m_HorizontalAlignment == h && m_VerticalAlignment == v) return;
      m_HorizontalAlignment = h;
      m_VerticalAlignment = v;
      m_havePropertiesChanged = true;
      SetVerticesDirty();
    }
  }

  // --- Spacing properties: All affect both vertices and layout ---

  public float characterSpacing
  {
    get => m_characterSpacing;
    set
    {
      if (m_characterSpacing == value) return;
      m_havePropertiesChanged = true;
      m_characterSpacing = value;
      SetVerticesDirty();
      SetLayoutDirty();
    }
  }

  public float lineSpacing
  {
    get => m_lineSpacing;
    set
    {
      if (m_lineSpacing == value) return;
      m_havePropertiesChanged = true;
      m_lineSpacing = value;
      SetVerticesDirty();
      SetLayoutDirty();
    }
  }

  // --- Wrapping and overflow ---

  public TextWrappingModes textWrappingMode
  {
    get => m_TextWrappingMode;
    set
    {
      if (m_TextWrappingMode == value) return;
      m_havePropertiesChanged = true;
      m_TextWrappingMode = value;
      SetVerticesDirty();
      SetLayoutDirty();
    }
  }

  public TextOverflowModes overflowMode
  {
    get => m_overflowMode;
    set
    {
      if (m_overflowMode == value) return;
      m_overflowMode = value;
      m_havePropertiesChanged = true;
      SetVerticesDirty();
      SetLayoutDirty();
    }
  }

  // --- Rich text ---

  public bool richText
  {
    get => m_isRichText;
    set
    {
      if (m_isRichText == value) return;
      m_isRichText = value;
      m_havePropertiesChanged = true;
      SetVerticesDirty();
      SetLayoutDirty();
    }
  }

  // --- Margin ---

  public virtual Vector4 margin
  {
    get => m_margin;
    set
    {
      if (m_margin == value) return;
      m_margin = value;
      ComputeMarginSize();
      m_havePropertiesChanged = true;
      SetVerticesDirty();
    }
  }

  // --- Auto-sizing ---

  public bool enableAutoSizing
  {
    get => m_enableAutoSizing;
    set
    {
      if (m_enableAutoSizing == value) return;
      m_enableAutoSizing = value;
      SetVerticesDirty();
      SetLayoutDirty();
    }
  }

  // --- Vertex gradient ---

  public bool enableVertexGradient
  {
    get => m_enableVertexGradient;
    set
    {
      if (m_enableVertexGradient == value) return;
      m_havePropertiesChanged = true;
      m_enableVertexGradient = value;
      SetVerticesDirty();
    }
  }

  public VertexGradient colorGradient
  {
    get => m_fontColorGradient;
    set { m_havePropertiesChanged = true; m_fontColorGradient = value; SetVerticesDirty(); }
  }

  // --- TextInfo: Character-level data access ---

  public TMP_TextInfo textInfo
  {
    get
    {
      if (m_textInfo == null) m_textInfo = new TMP_TextInfo(this);
      return m_textInfo;
    }
  }

  // --- Bounds: For selection overlay positioning ---

  /// <summary>
  /// Returns mesh bounds. Use after ForceMeshUpdate() for accurate positioning.
  /// </summary>
  public Bounds bounds => m_mesh == null ? new Bounds() : GetCompoundBounds();

  /// <summary>
  /// Returns bounds computed from character positions (tighter than mesh bounds).
  /// Iterates all visible characters to find min/max extents.
  /// </summary>
  public Bounds textBounds => m_textInfo == null ? new Bounds() : GetTextBounds();

  // --- Layout interface (used by ContentSizeFitter, LayoutGroup) ---

  /// <summary>
  /// Computed on-demand. Runs CalculatePreferredValues() which does a full text
  /// layout pass WITHOUT generating vertices. Used by LayoutGroup/ContentSizeFitter.
  /// </summary>
  public virtual float preferredWidth
  {
    get { m_preferredWidth = GetPreferredWidth(); return m_preferredWidth; }
  }

  public virtual float preferredHeight
  {
    get { m_preferredHeight = GetPreferredHeight(); return m_preferredHeight; }
  }

  // --- Force mesh update ---

  /// <summary>
  /// SYNCHRONOUS mesh regeneration. Call this when you need textInfo/bounds
  /// to be correct immediately (e.g., before reading character positions for
  /// our selection overlay or inline text editor cursor positioning).
  /// </summary>
  public virtual void ForceMeshUpdate(bool ignoreActiveState = false, bool forceTextReparsing = false) { }

  // --- SetText overloads (alternative to text property) ---

  /// <summary>
  /// Alternative to the text property. Calls PopulateTextBackingArray + PopulateTextProcessingArray
  /// directly. Slightly more efficient for programmatic text changes since it skips
  /// the string comparison in the text property setter.
  /// </summary>
  public void SetText(string sourceText)
  {
    int length = sourceText == null ? 0 : sourceText.Length;
    PopulateTextBackingArray(sourceText, 0, length);
    m_text = sourceText;
    m_inputSource = TextInputSources.TextString;
    PopulateTextProcessingArray();
    m_havePropertiesChanged = true;
    SetVerticesDirty();
    SetLayoutDirty();
  }

  // --- Text processing pipeline ---

  /// <summary>
  /// Entry point for text parsing. Called at the beginning of the layout/render pass.
  /// Converts the source text string ? TextBackingArray ? TextProcessingArray.
  /// Rich text tags, style tags, and escape sequences are resolved during
  /// PopulateTextProcessingArray().
  /// </summary>
  protected void ParseInputText()
  {
    switch (m_inputSource)
    {
      case TextInputSources.TextInputBox:
      case TextInputSources.TextString:
        PopulateTextBackingArray(m_TextPreprocessor == null ? m_text : m_TextPreprocessor.PreprocessText(m_text));
        PopulateTextProcessingArray();
        break;
    }
    SetArraySizes(m_TextProcessingArray);
  }

  internal enum TextInputSources { TextInputBox, SetText, SetTextArray, TextString }
}
```

---

## TMP_InputField

**Key insights for our UI Builder:**

- **Extends `Selectable`** (same as Button, Slider, Toggle) — inherits all color transition, interactable, and navigation behavior. Also implements `ILayoutElement` for preferred size calculation.
- **`text` property setter** calls `SetText(value)` which: replaces null chars, updates soft keyboard text, clamps caret positions, calls `UpdateLabel()`, and fires `SendOnValueChanged()`. Use `SetTextWithoutNotify()` to avoid firing the callback.
- **`UpdateLabel()`** is the core visual refresh. It: updates the TMP_Text component's text, handles composition (IME), manages placeholder visibility, forces mesh update if needed, and calls `MarkGeometryAsDirty()`. This is called by the `text` setter, by focus changes, and by keyboard input.
- **`contentType`** (Standard, Autocorrected, IntegerNumber, DecimalNumber, etc.) calls `EnforceContentType()` which auto-sets `m_InputType`, `m_KeyboardType`, `m_CharacterValidation`, and `m_LineType`. Changing contentType changes multiple internal settings at once.
- **`lineType`** (SingleLine, MultiLineSubmit, MultiLineNewline) controls wrap mode on the TMP text component via `SetTextComponentWrapMode()`. SingleLine = `PreserveWhitespaceNoWrap`, MultiLine = `Normal`.
- **`characterLimit`** setter clamps string positions and updates the soft keyboard limit.
- **`readOnly`** is a simple bool field — no side effects. Checked throughout input processing.
- **Layout interface:** `preferredWidth` and `preferredHeight` account for the text viewport offset and layout group padding. The TMP_InputField implements `ILayoutElement` with `layoutPriority = 1` (higher than default 0, so it wins over default layout element sizing).
- **`m_TextViewport`** (RectTransform) is the viewport rect that clips the text. The text component is a child that scrolls within this viewport. `RectMask2D` on the viewport handles visual clipping.
- **Caret rendering:** Creates a separate "Caret" GameObject with `TMP_SelectionCaret` + `CanvasRenderer` + `LayoutElement(ignoreLayout=true)`. The caret mesh is generated in `GenerateCaret()` and rendered by `m_CachedInputRenderer`.
- **`ActivateInputField()`** ? `ActivateInputFieldInternal()` opens the soft keyboard (on mobile), sets `m_AllowInput = true`, calls `OnFocus()` (which may SelectAll), starts caret blinking.
- **`DeactivateInputField()`** handles the reverse: restores original text if cancelled, closes soft keyboard, sets `m_SelectionStillActive`. The `m_ResetOnDeActivation` flag controls whether selection is released immediately.
- **`scrollSensitivity`** — `MarkGeometryAsDirty()` on change (not layout dirty). Only affects visual rendering of the scroll position.
- **`caretColor`, `selectionColor`** — both call `MarkGeometryAsDirty()` only. Safe for in-place updates.
- **`onValueChanged`, `onEndEdit`, `onSubmit`, `onSelect`, `onDeselect`** — all event properties use `SetPropertyUtility.SetClass` which only marks dirty if the reference actually changed.
- **`LateUpdate()`** handles soft keyboard synchronization, text validation, IME composition, and deactivation logic. This is where the main input processing loop runs when the field is focused.

```csharp
// Decompiled with JetBrains decompiler
// Type: TMPro.TMP_InputField
// Assembly: Unity.TextMeshPro, Version=0.0.0.0

[AddComponentMenu("UI/TextMeshPro - Input Field", 11)]
public class TMP_InputField :
  Selectable,
  IUpdateSelectedHandler, IEventSystemHandler,
  IBeginDragHandler, IDragHandler, IEndDragHandler,
  IPointerClickHandler, ISubmitHandler, ICancelHandler,
  ICanvasElement, ILayoutElement, IScrollHandler
{
  protected TouchScreenKeyboard m_SoftKeyboard;
  protected RectTransform m_RectTransform;
  [SerializeField] protected RectTransform m_TextViewport;
  [SerializeField] protected TMP_Text m_TextComponent;
  protected RectTransform m_TextComponentRectTransform;
  [SerializeField] protected Graphic m_Placeholder;
  [SerializeField] protected Scrollbar m_VerticalScrollbar;
  private bool m_IsDrivenByLayoutComponents;
  [SerializeField] private LayoutGroup m_LayoutGroup;
  private IScrollHandler m_IScrollHandlerParent;
  [SerializeField] protected float m_ScrollSensitivity = 1f;
  [SerializeField] private ContentType m_ContentType;
  [SerializeField] private InputType m_InputType;
  [SerializeField] private char m_AsteriskChar = '*';
  [SerializeField] private TouchScreenKeyboardType m_KeyboardType;
  [SerializeField] private LineType m_LineType;
  [SerializeField] private bool m_HideMobileInput;
  [SerializeField] private bool m_HideSoftKeyboard;
  [SerializeField] private CharacterValidation m_CharacterValidation;
  [SerializeField] private int m_CharacterLimit;
  [SerializeField] protected string m_Text = string.Empty;
  [SerializeField] private float m_CaretBlinkRate = 0.85f;
  [SerializeField] private int m_CaretWidth = 1;
  [SerializeField] private bool m_ReadOnly;
  [SerializeField] private bool m_RichText = true;
  [SerializeField] private Color m_CaretColor = new Color(0.196f, 0.196f, 0.196f, 1f);
  [SerializeField] private bool m_CustomCaretColor;
  [SerializeField] private Color m_SelectionColor = new Color(0.659f, 0.808f, 1f, 0.753f);
  protected int m_StringPosition;
  protected int m_StringSelectPosition;
  protected int m_CaretPosition;
  protected int m_CaretSelectPosition;
  private bool m_AllowInput;
  [SerializeField] protected bool m_OnFocusSelectAll = true;
  [SerializeField] protected bool m_ResetOnDeActivation = true;
  [SerializeField] private bool m_RestoreOriginalTextOnEscape = true;

  // --- Events ---
  [SerializeField] private SubmitEvent m_OnEndEdit = new SubmitEvent();
  [SerializeField] private SubmitEvent m_OnSubmit = new SubmitEvent();
  [SerializeField] private SelectionEvent m_OnSelect = new SelectionEvent();
  [SerializeField] private SelectionEvent m_OnDeselect = new SelectionEvent();
  [SerializeField] private OnChangeEvent m_OnValueChanged = new OnChangeEvent();
  [SerializeField] private OnValidateInput m_OnValidateInput;

  // --- Text property: Core content ---

  /// <summary>
  /// Setting text calls SetText() which: replaces null chars, updates soft keyboard,
  /// clamps caret positions, calls UpdateLabel(), fires SendOnValueChanged().
  /// Use SetTextWithoutNotify() to skip the callback.
  /// </summary>
  public string text
  {
    get => m_Text;
    set => SetText(value);
  }

  public void SetTextWithoutNotify(string input) => SetText(input, false);

  private void SetText(string value, bool sendCallback = true)
  {
    if (text == value) return;
    if (value == null) value = "";
    value = value.Replace("\0", string.Empty);
    m_Text = value;
    if (m_SoftKeyboard != null) m_SoftKeyboard.text = m_Text;
    if (m_StringPosition > m_Text.Length)
      m_StringPosition = m_StringSelectPosition = m_Text.Length;
    else if (m_StringSelectPosition > m_Text.Length)
      m_StringSelectPosition = m_Text.Length;
    m_forceRectTransformAdjustment = true;
    m_IsTextComponentUpdateRequired = true;
    UpdateLabel();
    if (sendCallback) SendOnValueChanged();
  }

  public bool isFocused => m_AllowInput;

  // --- Visual properties (MarkGeometryAsDirty only — no layout impact) ---

  public Color caretColor
  {
    get => !customCaretColor ? textComponent.color : m_CaretColor;
    set { if (SetPropertyUtility.SetColor(ref m_CaretColor, value)) MarkGeometryAsDirty(); }
  }

  public Color selectionColor
  {
    get => m_SelectionColor;
    set { if (SetPropertyUtility.SetColor(ref m_SelectionColor, value)) MarkGeometryAsDirty(); }
  }

  public float scrollSensitivity
  {
    get => m_ScrollSensitivity;
    set { if (SetPropertyUtility.SetStruct(ref m_ScrollSensitivity, value)) MarkGeometryAsDirty(); }
  }

  public int caretWidth
  {
    get => m_CaretWidth;
    set { if (SetPropertyUtility.SetStruct(ref m_CaretWidth, value)) MarkGeometryAsDirty(); }
  }

  // --- Content type: Changes multiple internal settings at once ---

  /// <summary>
  /// Calls EnforceContentType() which auto-sets InputType, KeyboardType,
  /// CharacterValidation, and LineType based on the content type.
  /// This is a compound setter — one change affects several fields.
  /// </summary>
  public ContentType contentType
  {
    get => m_ContentType;
    set
    {
      if (!SetPropertyUtility.SetStruct(ref m_ContentType, value)) return;
      EnforceContentType();
    }
  }

  /// <summary>
  /// Calls SetTextComponentWrapMode(): SingleLine ? PreserveWhitespaceNoWrap,
  /// MultiLine ? Normal wrap mode on the TMP text component.
  /// </summary>
  public LineType lineType
  {
    get => m_LineType;
    set
    {
      if (!SetPropertyUtility.SetStruct(ref m_LineType, value)) return;
      SetToCustomIfContentTypeIsNot(ContentType.Standard, ContentType.Autocorrected);
      SetTextComponentWrapMode();
    }
  }

  public int characterLimit
  {
    get => m_CharacterLimit;
    set
    {
      if (!SetPropertyUtility.SetStruct(ref m_CharacterLimit, Math.Max(0, value))) return;
      UpdateLabel();
      if (m_SoftKeyboard != null) m_SoftKeyboard.characterLimit = value;
    }
  }

  public bool readOnly { get => m_ReadOnly; set => m_ReadOnly = value; }

  // --- Component references ---

  public RectTransform textViewport
  {
    get => m_TextViewport;
    set => SetPropertyUtility.SetClass(ref m_TextViewport, value);
  }

  public TMP_Text textComponent
  {
    get => m_TextComponent;
    set
    {
      if (!SetPropertyUtility.SetClass(ref m_TextComponent, value)) return;
      SetTextComponentWrapMode();
    }
  }

  public Graphic placeholder
  {
    get => m_Placeholder;
    set => SetPropertyUtility.SetClass(ref m_Placeholder, value);
  }

  // --- UpdateLabel: Core visual refresh ---

  /// <summary>
  /// The main visual update method. Called by text setter, focus changes, keyboard input.
  /// Updates TMP text content, handles IME composition, manages placeholder visibility,
  /// forces mesh update if needed, marks geometry dirty.
  /// Calls LayoutRebuilder.MarkLayoutForRebuild if driven by layout components.
  /// </summary>
  protected void UpdateLabel()
  {
    if (m_TextComponent == null || m_TextComponent.font == null || m_PreventCallback) return;
    m_PreventCallback = true;

    string displayText;
    if (compositionLength > 0 && !m_ReadOnly)
    {
      Delete();
      displayText = m_RichText
        ? $"{text.Substring(0, m_StringPosition)}<u>{compositionString}</u>{text.Substring(m_StringPosition)}"
        : text.Substring(0, m_StringPosition) + compositionString + text.Substring(m_StringPosition);
      m_IsCompositionActive = true;
    }
    else
    {
      displayText = text;
      m_IsCompositionActive = false;
    }

    string rendered = m_InputType == InputType.Password
      ? new string(m_AsteriskChar, displayText.Length)
      : displayText;

    bool isEmpty = string.IsNullOrEmpty(displayText);
    if (m_Placeholder != null) m_Placeholder.enabled = isEmpty;
    if (!isEmpty && !m_ReadOnly) SetCaretVisible();

    m_TextComponent.text = rendered + "\u200B"; // Zero-width space for cursor positioning

    // If driven by layout, trigger layout rebuild
    if (m_IsDrivenByLayoutComponents)
      LayoutRebuilder.MarkLayoutForRebuild(m_RectTransform);

    // Line limit enforcement
    if (m_LineLimit > 0)
    {
      m_TextComponent.ForceMeshUpdate();
      TMP_TextInfo textInfo = m_TextComponent.textInfo;
      if (textInfo != null && textInfo.lineCount > m_LineLimit)
      {
        int lastCharIdx = textInfo.lineInfo[m_LineLimit - 1].lastCharacterIndex;
        int startIdx = textInfo.characterInfo[lastCharIdx].index +
                       textInfo.characterInfo[lastCharIdx].stringLength;
        text = rendered.Remove(startIdx, rendered.Length - startIdx);
        m_TextComponent.text = text + "\u200B";
      }
    }

    if (m_IsTextComponentUpdateRequired)
    {
      m_IsTextComponentUpdateRequired = false;
      m_TextComponent.ForceMeshUpdate();
    }

    MarkGeometryAsDirty();
    m_PreventCallback = false;
  }

  // --- Layout interface ---

  /// <summary>
  /// layoutPriority = 1 (higher than default 0). TMP_InputField's preferred size
  /// wins over a plain LayoutElement on the same GO.
  /// Accounts for LayoutGroup padding and TextViewport offset.
  /// </summary>
  public virtual int layoutPriority => 1;

  public virtual float preferredWidth
  {
    get
    {
      if (textComponent == null) return 0f;
      float extra = 0f;
      if (m_LayoutGroup != null) extra = m_LayoutGroup.padding.horizontal;
      if (m_TextViewport != null) extra += m_TextViewport.offsetMin.x - m_TextViewport.offsetMax.x;
      return m_TextComponent.preferredWidth + extra;
    }
  }

  public virtual float preferredHeight
  {
    get
    {
      if (textComponent == null) return 0f;
      float extra = 0f;
      if (m_LayoutGroup != null) extra = m_LayoutGroup.padding.vertical;
      if (m_TextViewport != null) extra += m_TextViewport.offsetMin.y - m_TextViewport.offsetMax.y;
      return m_TextComponent.preferredHeight + extra;
    }
  }

  public virtual float minWidth => 0f;
  public virtual float minHeight => 0f;
  public virtual float flexibleWidth => -1f;
  public virtual float flexibleHeight => -1f;

  // --- OnEnable: Caret creation, scrollbar hookup, label update ---

  protected override void OnEnable()
  {
    base.OnEnable();
    if (m_Text == null) m_Text = string.Empty;

    // Check if driven by layout components
    if (GetComponent<ILayoutController>() != null)
    {
      m_IsDrivenByLayoutComponents = true;
      m_LayoutGroup = GetComponent<LayoutGroup>();
    }

    // Create caret renderer if needed
    if (Application.isPlaying && m_CachedInputRenderer == null && m_TextComponent != null)
    {
      GameObject caretGO = new GameObject("Caret", typeof(TMP_SelectionCaret));
      caretGO.hideFlags = HideFlags.DontSave;
      caretGO.transform.SetParent(m_TextComponent.transform.parent);
      caretGO.transform.SetAsFirstSibling();
      caretGO.layer = gameObject.layer;
      caretRectTrans = caretGO.GetComponent<RectTransform>();
      m_CachedInputRenderer = caretGO.GetComponent<CanvasRenderer>();
      m_CachedInputRenderer.SetMaterial(Graphic.defaultGraphicMaterial, Texture2D.whiteTexture);
      caretGO.AddComponent<LayoutElement>().ignoreLayout = true; // Don't affect parent layout
      AssignPositioningIfNeeded();
    }

    // Find parent scroll handler for scroll event forwarding
    IScrollHandler[] scrollHandlers = GetComponentsInParent<IScrollHandler>();
    if (scrollHandlers.Length > 1)
      m_IScrollHandlerParent = (IScrollHandler)(scrollHandlers[1] as ScrollRect);

    // Setup viewport mask
    if (m_TextViewport != null)
      m_TextViewportRectMask = m_TextViewport.GetComponent<RectMask2D>();

    // Hook up text component callbacks
    if (m_TextComponent != null)
    {
      m_TextComponent.RegisterDirtyVerticesCallback(MarkGeometryAsDirty);
      m_TextComponent.RegisterDirtyVerticesCallback(UpdateLabel);
      if (m_VerticalScrollbar != null)
        m_VerticalScrollbar.onValueChanged.AddListener(OnScrollbarValueChange);
      UpdateLabel();
    }
  }

  // --- Scroll handling: Forwards to parent for single-line ---

  public virtual void OnScroll(PointerEventData eventData)
  {
    if (m_LineType == LineType.SingleLine)
    {
      // Forward scroll to parent ScrollRect
      if (m_IScrollHandlerParent != null)
        m_IScrollHandlerParent.OnScroll(eventData);
    }
    else
    {
      if (m_TextComponent.preferredHeight < m_TextViewport.rect.height) return;
      float scrollDelta = -eventData.scrollDelta.y;
      m_ScrollPosition = GetScrollPositionRelativeToViewport();
      m_ScrollPosition += 1f / m_TextComponent.textInfo.lineCount * scrollDelta * m_ScrollSensitivity;
      m_ScrollPosition = Mathf.Clamp01(m_ScrollPosition);
      AdjustTextPositionRelativeToViewport(m_ScrollPosition);
      if (m_VerticalScrollbar) m_VerticalScrollbar.value = m_ScrollPosition;
    }
  }

  // --- Enums ---

  public enum ContentType
  {
    Standard, Autocorrected, IntegerNumber, DecimalNumber,
    Alphanumeric, Name, EmailAddress, Password, Pin, Custom
  }
  public enum InputType { Standard, AutoCorrect, Password }
  public enum CharacterValidation
  {
    None, Digit, Integer, Decimal, Alphanumeric,
    Name, Regex, EmailAddress, CustomValidator
  }
  public enum LineType { SingleLine, MultiLineSubmit, MultiLineNewline }

  [Serializable] public class SubmitEvent : UnityEvent<string> { }
  [Serializable] public class OnChangeEvent : UnityEvent<string> { }
  [Serializable] public class SelectionEvent : UnityEvent<string> { }
  [Serializable] public class TextSelectionEvent : UnityEvent<string, int, int> { }
  public delegate char OnValidateInput(string text, int charIndex, char addedChar);
}
```

---

## TMP_Dropdown

**Key insights for our UI Builder:**

- **Extends `Selectable`** — same interactive base as Button/Slider. Also implements `IPointerClickHandler`, `ISubmitHandler`, `ICancelHandler`.
- **`value` property** calls `SetValue()` which: clamps the value, calls `RefreshShownValue()`, and fires `m_OnValueChanged`. Use `SetValueWithoutNotify()` to avoid the callback.
- **`RefreshShownValue()`** updates the caption text/image and placeholder visibility. For multi-select mode, shows "Nothing", "Everything", "Mixed...", or the single selected option.
- **`options` property setter** replaces the entire options list and calls `RefreshShownValue()`. Use `AddOptions()`/`ClearOptions()` for incremental changes.
- **`Show()`** is the most complex method:
  1. Clones the template to create the dropdown list
  2. Creates `DropdownItem` components for each option (toggle-based)
  3. Sets up a blocker (full-screen transparent button) to detect outside clicks
  4. Handles flip detection — if dropdown would go off-screen, flips via `RectTransformUtility.FlipLayoutOnAxis()`
  5. Alpha-fades in using `TweenRunner<FloatTween>`
  6. Template must have a child with `Toggle` component (validated in `SetupTemplate()`)
- **`Hide()`** alpha-fades out, starts coroutine for delayed destroy, destroys blocker, re-selects the dropdown.
- **`m_MultiSelect`** — supports multi-select via bitmask. `value` stores a bitmask where each bit represents an option. Shows special "Nothing"/"Everything"/"Mixed..." entries in the dropdown.
- **Template validation** in `SetupTemplate()` is strict: must have `Toggle` child, `Toggle` parent must have `RectTransform`, item text/image must be children of the toggle. Logs detailed errors on failure.
- **Blocker** is a full-screen `Canvas` with `GraphicRaycaster` + transparent `Image` + `Button` that calls `Hide()` on click. Sorted at `sortingOrder - 1` (one below the dropdown list). The blocker also has `CanvasGroup.ignoreParentGroups = true` to prevent parent CanvasGroup interference.
- **`DropdownItem`** is a protected internal MonoBehaviour that handles pointer enter (auto-select) and cancel (hide dropdown). It's added to each item toggle at runtime.
- **`template`** setter calls `RefreshShownValue()` — changing the template reference triggers a visual refresh.
- **`alphaFadeSpeed`** is a simple float — no side effects. Controls the Show/Hide tween duration.
- **`IsExpanded`** is a read-only check: returns `m_Dropdown != null`. Use this to detect if the dropdown list is currently visible.

```csharp
// Decompiled with JetBrains decompiler
// Type: TMPro.TMP_Dropdown
// Assembly: Unity.TextMeshPro, Version=0.0.0.0

[AddComponentMenu("UI/Dropdown - TextMeshPro", 35)]
[RequireComponent(typeof(RectTransform))]
public class TMP_Dropdown :
  Selectable,
  IPointerClickHandler, IEventSystemHandler,
  ISubmitHandler, ICancelHandler
{
  [SerializeField] private RectTransform m_Template;
  [SerializeField] private TMP_Text m_CaptionText;
  [SerializeField] private Image m_CaptionImage;
  [SerializeField] private Graphic m_Placeholder;
  [SerializeField] private TMP_Text m_ItemText;
  [SerializeField] private Image m_ItemImage;
  [SerializeField] private int m_Value;
  [SerializeField] private bool m_MultiSelect;
  [SerializeField] private OptionDataList m_Options = new OptionDataList();
  [SerializeField] private DropdownEvent m_OnValueChanged = new DropdownEvent();
  [SerializeField] private float m_AlphaFadeSpeed = 0.15f;
  private GameObject m_Dropdown;
  private GameObject m_Blocker;
  private List<DropdownItem> m_Items = new List<DropdownItem>();
  private bool validTemplate;

  // --- Properties that trigger RefreshShownValue ---

  public RectTransform template
  {
    get => m_Template;
    set { m_Template = value; RefreshShownValue(); }
  }

  public TMP_Text captionText
  {
    get => m_CaptionText;
    set { m_CaptionText = value; RefreshShownValue(); }
  }

  public Image captionImage
  {
    get => m_CaptionImage;
    set { m_CaptionImage = value; RefreshShownValue(); }
  }

  public TMP_Text itemText
  {
    get => m_ItemText;
    set { m_ItemText = value; RefreshShownValue(); }
  }

  public List<OptionData> options
  {
    get => m_Options.options;
    set { m_Options.options = value; RefreshShownValue(); }
  }

  // --- Value property: Fires callback ---

  /// <summary>
  /// Calls SetValue() which clamps, refreshes caption, fires onValueChanged.
  /// For multi-select, value is a bitmask. For single-select, it's the index.
  /// Use SetValueWithoutNotify() to skip the callback.
  /// </summary>
  public int value
  {
    get => m_Value;
    set => SetValue(value);
  }

  public void SetValueWithoutNotify(int input) => SetValue(input, false);

  private void SetValue(int value, bool sendCallback = true)
  {
    if (Application.isPlaying && (value == m_Value || options.Count == 0)) return;
    m_Value = !m_MultiSelect
      ? Mathf.Clamp(value, m_Placeholder ? -1 : 0, options.Count - 1)
      : value;
    RefreshShownValue();
    if (sendCallback)
    {
      UISystemProfilerApi.AddMarker("Dropdown.value", this);
      m_OnValueChanged.Invoke(m_Value);
    }
  }

  public bool IsExpanded => m_Dropdown != null;
  public bool MultiSelect { get => m_MultiSelect; set => m_MultiSelect = value; }

  // --- Simple properties (no side effects) ---

  public DropdownEvent onValueChanged { get => m_OnValueChanged; set => m_OnValueChanged = value; }
  public float alphaFadeSpeed { get => m_AlphaFadeSpeed; set => m_AlphaFadeSpeed = value; }

  // --- RefreshShownValue: Updates caption display ---

  /// <summary>
  /// Updates caption text/image based on current value and options.
  /// Multi-select mode shows special entries: "Nothing" (0), "Everything" (all bits),
  /// single option name (one bit), or "Mixed..." (multiple bits).
  /// Manages placeholder visibility.
  /// </summary>
  public void RefreshShownValue()
  {
    OptionData optionData = s_NoOptionData;
    if (options.Count > 0)
    {
      if (m_MultiSelect)
      {
        int index = FirstActiveFlagIndex(m_Value);
        optionData = m_Value == 0 || index >= options.Count
          ? k_NothingOption
          : IsEverythingValue(options.Count, m_Value)
            ? k_EverythingOption
            : Mathf.IsPowerOfTwo(m_Value) && m_Value > 0
              ? options[index]
              : k_MixedOption;
      }
      else if (m_Value >= 0)
        optionData = options[Mathf.Clamp(m_Value, 0, options.Count - 1)];
    }

    if (m_CaptionText) m_CaptionText.text = optionData?.text ?? "";
    if (m_CaptionImage)
    {
      m_CaptionImage.sprite = optionData.image;
      m_CaptionImage.color = optionData.color;
      m_CaptionImage.enabled = m_CaptionImage.sprite != null && m_CaptionImage.color.a > 0f;
    }
    if (m_Placeholder) m_Placeholder.enabled = options.Count == 0 || m_Value == -1;
  }

  // --- Show: Creates the dropdown list ---

  /// <summary>
  /// Complex method: clones template, creates DropdownItems with Toggles,
  /// handles flip detection for off-screen, alpha-fades in, creates blocker.
  /// Template must have a child Toggle (validated in SetupTemplate).
  /// For multi-select, adds "Nothing" and "Everything" entries at the top.
  /// </summary>
  public void Show()
  {
    if (!IsActive() || !IsInteractable() || m_Dropdown != null) return;

    // Find root canvas for blocker placement
    List<Canvas> canvasList = TMP_ListPool<Canvas>.Get();
    gameObject.GetComponentsInParent(false, canvasList);
    Canvas rootCanvas = canvasList[canvasList.Count - 1];
    for (int i = 0; i < canvasList.Count; i++)
      if (canvasList[i].isRootCanvas) { rootCanvas = canvasList[i]; break; }
    TMP_ListPool<Canvas>.Release(canvasList);

    if (!validTemplate) { SetupTemplate(); if (!validTemplate) return; }

    // Clone template and create items
    m_Template.gameObject.SetActive(true);
    m_Dropdown = CreateDropdownList(m_Template.gameObject);
    m_Dropdown.name = "Dropdown List";
    m_Dropdown.SetActive(true);
    // ... item creation, toggle hookup, navigation setup ...

    // Flip if off-screen
    RectTransform dropdownRect = m_Dropdown.transform as RectTransform;
    Vector3[] corners = new Vector3[4];
    dropdownRect.GetWorldCorners(corners);
    RectTransform rootRect = rootCanvas.transform as RectTransform;
    for (int axis = 0; axis < 2; axis++)
    {
      bool offScreen = false;
      for (int c = 0; c < 4; c++)
      {
        Vector3 local = rootRect.InverseTransformPoint(corners[c]);
        if (local[axis] < rootRect.rect.min[axis] || local[axis] > rootRect.rect.max[axis])
        { offScreen = true; break; }
      }
      if (offScreen)
        RectTransformUtility.FlipLayoutOnAxis(dropdownRect, axis, false, false);
    }

    AlphaFadeList(m_AlphaFadeSpeed, 0f, 1f);
    m_Template.gameObject.SetActive(false);
    m_Blocker = CreateBlocker(rootCanvas);
  }

  // --- Hide: Fade out and cleanup ---

  public void Hide()
  {
    if (m_Coroutine != null) return;
    if (m_Dropdown != null)
    {
      AlphaFadeList(m_AlphaFadeSpeed, 0f);
      if (IsActive())
        m_Coroutine = StartCoroutine(DelayedDestroyDropdownList(m_AlphaFadeSpeed));
    }
    if (m_Blocker != null) DestroyBlocker(m_Blocker);
    m_Blocker = null;
    Select();
  }

  // --- Blocker: Full-screen click catcher ---

  /// <summary>
  /// Creates a full-screen transparent button that calls Hide() on click.
  /// Sorted at sortingOrder - 1 (one below the dropdown list).
  /// CanvasGroup.ignoreParentGroups = true prevents parent CanvasGroup interference.
  /// Copies raycaster type from parent canvas for correct input handling.
  /// </summary>
  protected virtual GameObject CreateBlocker(Canvas rootCanvas)
  {
    GameObject go = new GameObject("Blocker");
    RectTransform rt = go.AddComponent<RectTransform>();
    rt.SetParent(rootCanvas.transform, false);
    rt.anchorMin = Vector3.zero;
    rt.anchorMax = Vector3.one;
    rt.sizeDelta = Vector2.zero;
    Canvas c = go.AddComponent<Canvas>();
    c.overrideSorting = true;
    c.sortingOrder = m_Dropdown.GetComponent<Canvas>().sortingOrder - 1;
    go.AddComponent<GraphicRaycaster>();
    go.AddComponent<Image>().color = Color.clear;
    go.AddComponent<Button>().onClick.AddListener(Hide);
    go.AddComponent<CanvasGroup>().ignoreParentGroups = true;
    return go;
  }

  // --- Option selection handling ---

  /// <summary>
  /// For single-select: sets value = index, hides dropdown.
  /// For multi-select: index 0 = "Nothing" (clear all), index 1 = "Everything" (set all),
  /// index 2+ = toggle individual bit in the bitmask.
  /// </summary>
  private void OnSelectItem(Toggle toggle)
  {
    int itemIndex = -1;
    Transform parent = toggle.transform.parent;
    for (int i = 1; i < parent.childCount; i++)
      if (parent.GetChild(i) == toggle.transform) { itemIndex = i - 1; break; }
    if (itemIndex < 0) return;

    if (m_MultiSelect)
    {
      switch (itemIndex)
      {
        case 0: // "Nothing"
          value = 0;
          // Uncheck all option toggles
          break;
        case 1: // "Everything"
          value = EverythingValue(options.Count);
          // Check all option toggles
          break;
        default: // Individual option
          int bit = 1 << (itemIndex - 2);
          bool wasSet = (value & bit) != 0;
          toggle.SetIsOnWithoutNotify(!wasSet);
          value = wasSet ? (value & ~bit) : (value | bit);
          break;
      }
    }
    else
    {
      if (!toggle.isOn) toggle.SetIsOnWithoutNotify(true);
      value = itemIndex;
    }
    Hide();
  }

  // --- Option data ---

  [Serializable]
  public class OptionData
  {
    [SerializeField] private string m_Text;
    [SerializeField] private Sprite m_Image;
    [SerializeField] private Color m_Color = Color.white;
    public string text { get => m_Text; set => m_Text = value; }
    public Sprite image { get => m_Image; set => m_Image = value; }
    public Color color { get => m_Color; set => m_Color = value; }
    public OptionData() { }
    public OptionData(string text) => this.text = text;
    public OptionData(Sprite image) => this.image = image;
    public OptionData(string text, Sprite image, Color color)
    { this.text = text; this.image = image; this.color = color; }
  }

  [Serializable]
  public class OptionDataList
  {
    [SerializeField] private List<OptionData> m_Options;
    public List<OptionData> options { get => m_Options; set => m_Options = value; }
    public OptionDataList() => options = new List<OptionData>();
  }

  [Serializable] public class DropdownEvent : UnityEvent<int> { }

  /// <summary>
  /// Runtime MonoBehaviour added to each dropdown item toggle.
  /// Handles pointer enter (auto-select in EventSystem) and cancel (hide dropdown).
  /// </summary>
  protected internal class DropdownItem : MonoBehaviour, IPointerEnterHandler, IEventSystemHandler, ICancelHandler
  {
    [SerializeField] private TMP_Text m_Text;
    [SerializeField] private Image m_Image;
    [SerializeField] private RectTransform m_RectTransform;
    [SerializeField] private Toggle m_Toggle;
    public TMP_Text text { get => m_Text; set => m_Text = value; }
    public Image image { get => m_Image; set => m_Image = value; }
    public RectTransform rectTransform { get => m_RectTransform; set => m_RectTransform = value; }
    public Toggle toggle { get => m_Toggle; set => m_Toggle = value; }

    public virtual void OnPointerEnter(PointerEventData eventData)
      => EventSystem.current.SetSelectedGameObject(gameObject);

    public virtual void OnCancel(BaseEventData eventData)
    {
      TMP_Dropdown dd = GetComponentInParent<TMP_Dropdown>();
      if (dd) dd.Hide();
    }
  }
}
```

---

## Actionable Takeaways for UI Builder

### 1. TMP_Text.text setter is cheap when unchanged
The setter does a length check AND string comparison before marking dirty. Our `ApplyNodeDeep()` calling `tmpText.text = node.Text` every frame is safe — it's a no-op when the text hasn't changed.

### 2. Color and alignment are vertices-only — no layout cost
`TMP_Text.color`, `horizontalAlignment`, `verticalAlignment` only call `SetVerticesDirty()`. These are pure visual updates with no layout recalculation. Contrast with `fontSize`, `text`, `fontStyle` which call both `SetVerticesDirty()` AND `SetLayoutDirty()`.

### 3. Font changes are expensive — compare before setting
`TMP_Text.font` setter triggers `LoadFontAsset()` which reloads the entire font. Our `ApplyTextDeep()` should use `if (tmpText.font != targetFont)` before setting.

### 4. Use ForceMeshUpdate() for immediate textInfo access
`TMP_Text.textInfo` contains character-level position data needed for cursor positioning in our inline text editor. Call `ForceMeshUpdate()` first to ensure data is current.

### 5. TMP_InputField.text setter fires callbacks
`SetText()` fires `onValueChanged` by default. Use `SetTextWithoutNotify()` from our inspector to avoid infinite callback loops when syncing inspector ? runtime.

### 6. TMP_InputField has higher layout priority
`layoutPriority = 1` means TMP_InputField's preferred size wins over default `LayoutElement` (priority 0). If our builder adds a `LayoutElement` to an input field GO, it needs `layoutPriority >= 1` to override.

### 7. TMP_Dropdown.Show() handles flip detection automatically
The dropdown list auto-flips if it would go off-screen. Our builder doesn't need to manually position dropdown lists — just ensure the template is configured correctly.

### 8. TMP_Dropdown blocker uses ignoreParentGroups
The blocker's `CanvasGroup.ignoreParentGroups = true` prevents our opacity CanvasGroups from interfering with dropdown click-outside detection. This is already handled correctly by Unity.

### 9. TMP_InputField caret has ignoreLayout
The caret GameObject gets `LayoutElement.ignoreLayout = true` — it doesn't interfere with parent LayoutGroups. No special handling needed in our layout capture/apply.

### 10. TMP_Dropdown multi-select uses bitmask
`value` stores `1 << optionIndex` for each selected option. `value = 0` means nothing selected, `value = (1 << count) - 1` means everything selected. Our inspector should display this as checkboxes per option, not a single dropdown index.
