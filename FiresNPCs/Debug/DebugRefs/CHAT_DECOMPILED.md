# Valheim Chat System — Decompiled Reference
# Decompiled from assembly_valheim.dll (MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800)
# Saved for reference only — do NOT compile these files directly.

## KEY ARCHITECTURE NOTES

### Chat Flow
```
Player types text ? Enter key ?
  Terminal.SendInput() ?
    Chat.InputText() ?
      text[0] == '/' ? TryRunCommand(text.Substring(1))
      text[0] != '/' ? TryRunCommand("say " + text)
    ? Terminal commands "say", "s", "w" call Chat.instance.SendText(type, text)
      ? Shout: broadcasts via ZRoutedRpc "ChatMessage" to each player individually (permission-checked)
      ? Normal/Whisper: Player.GetComponent<Talker>().Say(type, text) ? distance-limited RPC
```

### Talker.Type Enum
- Whisper = 0 (range: m_visperDistance = 4m)
- Normal = 1  (range: m_normalDistance = 15m)
- Shout = 2   (range: m_shoutDistance = 70m, but actually server-wide via ZRoutedRpc)
- Ping = 3    (map ping, not text)

### Key Fields (Terminal base)
- m_input : GuiInputField (extends TMP_InputField) — the chat text input
- m_output : TextMeshProUGUI — the chat log display
- m_chatWindow : RectTransform — the chat panel root
- m_chatBuffer : List<string> — chat history lines
- m_focused : bool — whether input is active

### Key Fields (Chat)
- m_hideTimer / m_hideDelay : float — auto-hide timing
- m_wasFocused : bool — tracks focus state between frames
- m_worldTexts : List<WorldTextInstance> — floating world text bubbles
- m_npcTexts : List<NpcText> — NPC speech bubbles

### Critical Methods
- Chat.InputText() — parses "/" prefix, routes to TryRunCommand
- Chat.SendText(Talker.Type, string) — sends chat via appropriate channel
- Chat.OnNewChatMessage(...) — receives incoming messages, adds to buffer + world text
- Chat.SendInput() — calls base.SendInput() then hides input field
- Terminal.SendInput() — reads m_input.text, calls InputText(), adds to history, clears input
- Terminal.AddString(PlatformUserID, string, Talker.Type) — formats and adds colored chat line

---

## Chat.cs (Decompiled)

```csharp
// Type: Chat : Terminal
// Key: Singleton via Chat.m_instance / Chat.instance

public class Chat : Terminal
{
  private static Chat m_instance;
  public float m_hideDelay = 10f;
  public float m_worldTextTTL = 5f;
  public GameObject m_worldTextBase;
  public GameObject m_npcTextBase;
  public GameObject m_npcTextBaseLarge;
  protected bool m_doubleOpenForVirtualKeyboard = true;
  private List<Chat.WorldTextInstance> m_worldTexts = new List<Chat.WorldTextInstance>();
  private List<Chat.NpcText> m_npcTexts = new List<Chat.NpcText>();
  private float m_hideTimer = 9999f;
  public bool m_wasFocused;
  private bool m_socialRestrictionNotificationShown;

  public static Chat instance => Chat.m_instance;

  // Awake: registers RPCs, sets up input hints, binds
  public override void Awake() { ... }

  public bool HasFocus() =>
    m_chatWindow != null && m_chatWindow.gameObject.activeInHierarchy && m_input.isFocused;

  // Update: handles show/hide, Enter key open, Escape close, binds
  public override void Update() { ... }

  // CRITICAL: overrides Terminal.SendInput — hides input after send
  public new void SendInput()
  {
    base.SendInput();
    m_input.gameObject.SetActive(false);
  }

  // CRITICAL: overrides Terminal.InputText — the "/" prefix routing
  protected override void InputText()
  {
    string text = m_input.text;
    if (text.Length == 0) return;
    // No "/" prefix ? "say <text>" ? Terminal command "say" ? SendText(Normal, text)
    // "/" prefix ? strip "/" and run as command (e.g. "/s hello" ? command "s hello" ? SendText(Shout, "hello"))
    TryRunCommand(text[0] != '/' ? "say " + text : text.Substring(1), this);
  }

  // CRITICAL: sends message via appropriate network path
  public void SendText(Talker.Type type, string text)
  {
    Player localPlayer = Player.m_localPlayer;
    if (!localPlayer) return;
    if (type == Talker.Type.Shout)
      // Shout: sends individually to each player via ZRoutedRpc (permission-checked)
      CheckPermissionsAndSendChatMessageRPCsAsync(...);
    else
      // Normal/Whisper: distance-limited via Talker component
      localPlayer.GetComponent<Talker>().Say(type, text);
  }

  // Receives incoming chat — formats and displays
  public void OnNewChatMessage(GameObject go, long senderID, Vector3 pos, Talker.Type type, UserInfo sender, string text)
  { ... }

  // World text colors:
  // Whisper ? white 75% alpha, lowercase
  // Normal ? white
  // Shout  ? yellow, uppercase
  // Ping   ? blue-ish, "PING"

  // Chat buffer format: AddString(user, text, type) ?
  //   "<color=orange>{playerName}</color>: <color=#{typeColor}>{text}</color>"
}
```

## Terminal.cs (Decompiled)

```csharp
// Type: Terminal : MonoBehaviour (abstract)
// Base class for Chat and Console

public abstract class Terminal : MonoBehaviour
{
  // UI References
  public RectTransform m_chatWindow;
  public TextMeshProUGUI m_output;
  public GuiInputField m_input;      // extends TMP_InputField
  public TMP_Text m_search;

  // State
  protected bool m_focused;
  protected List<string> m_chatBuffer = new List<string>();
  public int m_maxVisibleBufferLength = 30;
  private int m_scrollHeight;
  private List<string> m_history = new List<string>();
  private int m_historyPosition;

  // Commands
  protected static Dictionary<string, ConsoleCommand> commands;

  // CRITICAL: reads input, calls InputText(), manages history
  protected void SendInput()
  {
    if (string.IsNullOrEmpty(m_input.text)) return;
    InputText();
    if (m_history.Count == 0 || m_history[m_history.Count - 1] != m_input.text)
      m_history.Add(m_input.text);
    m_historyPosition = m_history.Count;
    m_input.text = "";
    m_scrollHeight = 0;
    UpdateChat();
    m_input.ActivateInputField();
  }

  // Default implementation (overridden by Chat)
  protected virtual void InputText()
  {
    string text = m_input.text;
    AddString(text);
    TryRunCommand(text);
  }

  // Built-in commands relevant to chat:
  //   "say" ? Chat.instance.SendText(Talker.Type.Normal, text)
  //   "s"   ? Chat.instance.SendText(Talker.Type.Shout, text)
  //   "w"   ? Chat.instance.SendText(Talker.Type.Whisper, text)
}
```

## Talker.cs (Decompiled)

```csharp
// Type: Talker : MonoBehaviour
// Attached to Player GameObjects — handles distance-limited chat

public class Talker : MonoBehaviour
{
  public float m_visperDistance = 4f;   // Whisper range
  public float m_normalDistance = 15f;  // Local chat range
  public float m_shoutDistance = 70f;   // Shout range (but Shout bypasses this via Chat.SendText)
  private ZNetView m_nview;
  private Character m_character;

  // Sends chat RPC to other players
  public void Say(Talker.Type type, string text)
  {
    // Permission-checked, sends "Say" RPC to each valid player
    Chat.CheckPermissionsAndSendChatMessageRPCsAsync((user, filterText) =>
    {
      m_nview.InvokeRPC(user, "Say", (int)type, userInfo, textToSend);
    });
  }

  // Receives "Say" RPC — checks distance, forwards to Chat.instance.OnNewChatMessage
  private void RPC_Say(long sender, int ctype, UserInfo user, string text)
  {
    float range = type switch {
      Whisper => m_visperDistance,  // 4m
      Normal  => m_normalDistance,  // 15m
      Shout   => m_shoutDistance,   // 70m
    };
    if (Vector3.Distance(transform.position, Player.m_localPlayer.transform.position) < range)
      Chat.instance.OnNewChatMessage(gameObject, sender, headPoint, type, user, text);
  }

  public enum Type { Whisper, Normal, Shout, Ping }
}
```
