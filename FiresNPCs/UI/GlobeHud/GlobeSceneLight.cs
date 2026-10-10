using UnityEngine;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Publishes the scene's sun or moon (EnvMan's directional light) in camera space for the globe shaders, so the
    /// glass highlight moves as the camera turns and follows the time of day. Overlay UI has no view matrix of its
    /// own, hence the conversion here.
    /// </summary>
    internal static class GlobeSceneLight
    {
        private static readonly int LightDirection = Shader.PropertyToID("_FiresGlobeLightDir");
        private static readonly int LightColor = Shader.PropertyToID("_FiresGlobeLightColor");
        private static readonly int Ambient = Shader.PropertyToID("_FiresGlobeAmbient");
        private static readonly int Sky = Shader.PropertyToID("_FiresGlobeSky");
        private static readonly int Ground = Shader.PropertyToID("_FiresGlobeGround");

        private const float MinLightStrength = 0.2f;
        private const float MaxLightStrength = 1.4f;
        private const float SkyLightShare = 0.25f;
        private const float GroundFromAmbient = 0.4f;

        public static void Publish(Camera camera)
        {
            var environment = EnvMan.instance;
            if (environment == null || environment.m_dirLight == null || camera == null) return;

            Light sun = environment.m_dirLight;
            Vector3 towardLight = camera.worldToCameraMatrix.MultiplyVector(-sun.transform.forward);
            float strength = Mathf.Clamp(sun.intensity, MinLightStrength, MaxLightStrength);
            Shader.SetGlobalVector(LightDirection, new Vector4(towardLight.x, towardLight.y, towardLight.z, strength));
            Shader.SetGlobalColor(LightColor, sun.color);
            Shader.SetGlobalColor(Ambient, RenderSettings.ambientLight);
            Shader.SetGlobalColor(Sky, Color.Lerp(RenderSettings.fogColor, sun.color * strength, SkyLightShare));
            Shader.SetGlobalColor(Ground, RenderSettings.ambientLight * GroundFromAmbient);
        }
    }
}
