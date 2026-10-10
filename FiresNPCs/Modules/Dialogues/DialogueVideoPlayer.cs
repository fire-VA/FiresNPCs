using System;
using System.Reflection;
using UnityEngine;

namespace FiresRPGmaker.Modules.Dialogues
{
    /// <summary>
    /// Fullscreen in-game video playback for the dialogue PlayVideo command (Marketplace parity:
    /// VideoPlayerUI.StartVideo). Renders on the game camera's near plane, plays direct audio at
    /// the requested volume, and closes on Escape or when playback ends.
    ///
    /// UnityEngine.VideoModule is accessed ENTIRELY via reflection — a direct reference breaks the
    /// net48 build (netstandard 2.1 metadata; the AudioModule/ImageConversion trap).
    /// </summary>
    public static class DialogueVideoPlayer
    {
        private static GameObject _host;

        public static void Play(string url, float volume)
        {
            if (string.IsNullOrEmpty(url)) return;
            Stop();
            try
            {
                var vpType = Type.GetType("UnityEngine.Video.VideoPlayer, UnityEngine.VideoModule");
                var sourceType = Type.GetType("UnityEngine.Video.VideoSource, UnityEngine.VideoModule");
                var renderType = Type.GetType("UnityEngine.Video.VideoRenderMode, UnityEngine.VideoModule");
                var audioType = Type.GetType("UnityEngine.Video.VideoAudioOutputMode, UnityEngine.VideoModule");
                if (vpType == null || sourceType == null || renderType == null || audioType == null)
                {
                    Debug.LogWarning("[DialogueVideo] VideoModule unavailable — cannot play " + url);
                    return;
                }

                Camera cam = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
                if (cam == null) { Debug.LogWarning("[DialogueVideo] no camera to render on"); return; }

                _host = new GameObject("FiresDialogueVideo");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                var player = _host.AddComponent(vpType);

                SetProp(player, "playOnAwake", false);
                SetProp(player, "isLooping", false);
                SetProp(player, "source", Enum.ToObject(sourceType, 1));        // VideoSource.Url
                SetProp(player, "url", url);
                SetProp(player, "renderMode", Enum.ToObject(renderType, 1));    // CameraNearPlane
                SetProp(player, "targetCamera", cam);
                SetProp(player, "audioOutputMode", Enum.ToObject(audioType, 2)); // Direct

                var setVolume = vpType.GetMethod("SetDirectAudioVolume", new[] { typeof(ushort), typeof(float) });
                setVolume?.Invoke(player, new object[] { (ushort)0, Mathf.Clamp01(volume) });

                vpType.GetMethod("Play", Type.EmptyTypes)?.Invoke(player, null);

                var closer = _host.AddComponent<VideoCloser>();
                closer.Init(player, vpType.GetProperty("isPlaying"));
                Debug.Log($"[DialogueVideo] playing {url} (volume {volume:0.00})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DialogueVideo] failed to start '{url}': {ex.Message}");
                Stop();
            }
        }

        public static void Stop()
        {
            if (_host != null) UnityEngine.Object.Destroy(_host);
            _host = null;
        }

        private static void SetProp(object target, string name, object value)
            => target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.SetValue(target, value, null);

        /// <summary>Escape stops playback; natural end (isPlaying false after a start grace) cleans up.</summary>
        private sealed class VideoCloser : MonoBehaviour
        {
            private object _player;
            private PropertyInfo _isPlaying;
            private float _startedAt;

            public void Init(object player, PropertyInfo isPlaying)
            {
                _player = player;
                _isPlaying = isPlaying;
                _startedAt = Time.unscaledTime;
            }

            private void Update()
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { Stop(); return; }
                // URL playback prepares async — give it a grace window before "not playing" means "done".
                if (Time.unscaledTime - _startedAt < 3f || _player == null || _isPlaying == null) return;
                try
                {
                    if (_isPlaying.GetValue(_player, null) is bool playing && !playing) Stop();
                }
                catch { Stop(); }
            }
        }
    }
}
