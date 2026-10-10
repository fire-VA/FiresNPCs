using System;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;

namespace FiresRPGmaker.Modules.Vault
{
 // Lightweight runtime timer manager for quest time limits. Runs as a MonoBehaviour.
 public class QuestTimerManager : MonoBehaviour
 {
 private static QuestTimerManager instance;
 private readonly Dictionary<string, DateTime> timers = new Dictionary<string, DateTime>(); // key = "{playerId}:{questId}" -> expiry

 public static void EnsureStarted()
 {
 if (instance != null) return;
 var go = new GameObject("FiresRPGmaker_QuestTimerManager");
 DontDestroyOnLoad(go);
 instance = go.AddComponent<QuestTimerManager>();
 }

 private void Awake()
 {
 if (instance != null && instance != this)
 {
 Destroy(this);
 return;
 }
 instance = this;
 StartCoroutine(Tick());
 }

 private IEnumerator Tick()
 {
 while (true)
 {
 try
 {
 var now = DateTime.UtcNow;
 var expired = new List<string>();
 lock (timers)
 {
 foreach (var kv in timers)
 {
 if (kv.Value <= now) expired.Add(kv.Key);
 }
 }

 foreach (var key in expired)
 {
 string[] parts = key.Split(new[] { ':' },2);
 if (parts.Length ==2 && long.TryParse(parts[0], out long playerId))
 {
 string questId = parts[1];
 // remove timer first
 lock (timers) { timers.Remove(key); }
 try
 {
 // call into vault to handle expiry (server should be authoritative)
 if (VaultOfKnowledge.Instance != null)
 {
 VaultOfKnowledge.Instance.HandleQuestTimerExpired(playerId, questId);
 }
 }
 catch (Exception ex)
 {
 Debug.LogWarning($"[FiresRPGmaker] QuestTimerManager expiry handler failed: {ex.Message}");
 }
 }
 else
 {
 lock (timers) { timers.Remove(key); }
 }
 }
 }
 catch { }

 yield return new WaitForSeconds(1f);
 }
 }

 public static void ScheduleTimer(long playerId, string questId, int minutes)
 {
 if (instance == null) EnsureStarted();
 if (minutes <=0) return;
 string key = playerId.ToString() + ":" + (questId ?? string.Empty);
 var expiry = DateTime.UtcNow.AddMinutes(minutes);
 lock (instance.timers)
 {
 instance.timers[key] = expiry;
 }
 Debug.Log($"[FiresRPGmaker] Scheduled quest timer for player {playerId} quest {questId} expiry {expiry:o}");
 }

 // Schedule a timer to a specific absolute expiry time
 public static void ScheduleTimerAt(long playerId, string questId, DateTime expiry)
 {
 if (instance == null) EnsureStarted();
 if (expiry <= DateTime.UtcNow) return;
 string key = playerId.ToString() + ":" + (questId ?? string.Empty);
 lock (instance.timers)
 {
 instance.timers[key] = expiry.ToUniversalTime();
 }
 Debug.Log($"[FiresRPGmaker] Scheduled quest timer (absolute) for player {playerId} quest {questId} expiry {expiry:o}");
 }

 // Load a set of persisted timers (expiry ticks). Called by server on startup.
 public static void LoadPersistedTimers(IDictionary<string, long> persisted)
 {
 if (persisted == null) return;
 if (instance == null) EnsureStarted();
 var now = DateTime.UtcNow;
 foreach (var kv in persisted)
 {
 try
 {
 var key = kv.Key;
 var ticks = kv.Value;
 if (ticks <=0) continue;
 var expiry = new DateTime(ticks, DateTimeKind.Utc);
 if (expiry <= now) continue;
 lock (instance.timers) { instance.timers[key] = expiry; }
 }
 catch { }
 }
 }

 public static void CancelTimer(long playerId, string questId)
 {
 if (instance == null) return;
 string key = playerId.ToString() + ":" + (questId ?? string.Empty);
 lock (instance.timers)
 {
 if (instance.timers.ContainsKey(key)) instance.timers.Remove(key);
 }
 Debug.Log($"[FiresRPGmaker] Cancelled quest timer for player {playerId} quest {questId}");
 }
 }
}
