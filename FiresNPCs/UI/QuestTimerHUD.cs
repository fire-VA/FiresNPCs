using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System.Linq;

namespace FiresRPGmaker.Modules.Vault
{
 public class QuestTimerHUD : MonoBehaviour
 {
 private static QuestTimerHUD instance;
 private float updateInterval =1f;
 private float nextUpdate =0f;

 public static void EnsureCreated()
 {
 if (instance != null) return;
 var go = new GameObject("FiresRPGmaker_QuestTimerHUD");
 DontDestroyOnLoad(go);
 instance = go.AddComponent<QuestTimerHUD>();
 }

 private void Awake()
 {
 if (instance != null && instance != this)
 {
 Destroy(this);
 return;
 }
 instance = this;
 }

 private void Update()
 {
 if (Time.time < nextUpdate) return;
 nextUpdate = Time.time + updateInterval;

 try
 {
 var local = Player.m_localPlayer;
 if (local == null) return;

 Dictionary<string, string> map = null;
 try
 {
 if (local.m_customData.TryGetValue("FiresRPGmaker_QuestTimers", out string raw) && !string.IsNullOrEmpty(raw))
 map = JsonConvert.DeserializeObject<Dictionary<string, string>>(raw) ?? new Dictionary<string, string>();
 }
 catch { map = null; }

 if (map == null || map.Count ==0)
 {
 // clear top-left by sending empty (no-op)
 return;
 }

 var items = new List<(string id, DateTime expiry)>();
 foreach (var kv in map)
 {
 try
 {
 if (DateTime.TryParseExact(kv.Value, "o", null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime exp))
 {
 if (exp > DateTime.UtcNow) items.Add((kv.Key, exp));
 }
 }
 catch { }
 }

 if (items.Count ==0) return;
 items = items.OrderBy(i => i.expiry).ToList();

 // Build a compact multi-line message
 var lines = new List<string>();
 foreach (var it in items.Take(5))
 {
 var remaining = it.expiry - DateTime.UtcNow;
 string timestr;
 if (remaining.TotalHours >=1)
 timestr = string.Format("{0}h {1}m {2}s", (int)remaining.TotalHours, remaining.Minutes, remaining.Seconds);
 else if (remaining.TotalMinutes >=1)
 timestr = string.Format("{0}m {1}s", remaining.Minutes, remaining.Seconds);
 else
 timestr = string.Format("{0}s", remaining.Seconds);

 lines.Add($"{it.id}: {timestr}");
 }

 string msg = string.Join("\n", lines);
 try
 {
 MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, msg);
 }
 catch { }
 }
 catch { }
 }
 }
}
