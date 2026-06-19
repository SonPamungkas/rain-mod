
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NuclearRain
{
    [BepInPlugin("neutral.rain", "Nuclear Rain", "0.2.0")]
    public class RainModPlugin : BaseUnityPlugin
    {
        public static ConfigEntry<float> ScrollSpeed;
        public static ConfigEntry<float> RainAlpha;
        public static ConfigEntry<float> FadeBand;
        public static ConfigEntry<float> FallSpeed;
        public static ConfigEntry<float> MaxTiltDeg;
        public static ConfigEntry<bool> UseSnow;
        public static ConfigEntry<bool> DebugLogging;
        public static ConfigEntry<bool> LogUnderCamera;

        public static ConfigEntry<bool> EnableRainWall;
        public static ConfigEntry<bool> EnableSplash;
        public static ConfigEntry<bool> EnableRainDrop;
        public static ConfigEntry<bool> EnableMist;
        public static ConfigEntry<bool> EnableFog;
        public static ConfigEntry<bool> EnableStreaks;
        public static ConfigEntry<float> RainWallQuality;

        public static BepInEx.Logging.ManualLogSource LogInstance;

        private void Awake()
        {
            LogInstance = Logger;
            ScrollSpeed = Config.Bind("Rain", "ScrollSpeed", 3.5f, "UV scrolls per second (rain fall speed)");
            RainAlpha   = Config.Bind("Rain", "MaxAlpha", 0.35f, "Peak opacity of the rain layer");
            FadeBand    = Config.Bind("Rain", "FadeBand", 300f, "Fade distance (m) below cloud base");
            FallSpeed   = Config.Bind("Rain", "FallSpeed", 9f, "Rain terminal velocity (m/s) for tilt math");
            MaxTiltDeg  = Config.Bind("Rain", "MaxTilt", 65f, "Max tilt of the rain rig from vertical");
            UseSnow     = Config.Bind("Rain", "UseSnow", false, "Use the snow texture/behaviour instead of rain");
            DebugLogging = Config.Bind("Debug", "DebugLogging", true, "Log diagnostic info about rain state to the BepInEx console/log");
            LogUnderCamera = Config.Bind("Debug", "LogUnderCamera", false,
                "Periodically log what RainRaycast finds directly under the camera (ground/water/shadowed)");

            EnableRainWall = Config.Bind("Elements", "EnableRainWall", true, "Show the rainwall onion (concentric dynamic wall layers)");
            EnableSplash   = Config.Bind("Elements", "EnableSplash", true, "Show ground/sea splash particles");
            EnableRainDrop = Config.Bind("Elements", "EnableRainDrop", true, "Show falling rain drop particles");
            EnableMist     = Config.Bind("Elements", "EnableMist", true, "Show ground/sea mist puffs");
            EnableFog      = Config.Bind("Elements", "EnableFog", true, "Show ground/sea fog layers");
            EnableStreaks  = Config.Bind("Elements", "EnableStreaks", true, "Show surface water streaks on steep terrain");
            RainWallQuality = Config.Bind("Elements", "RainWallQuality", 1f,
                new ConfigDescription("1 = full quality (dash rings on every onion layer), 0 = optimized (dash ring only on the outer layer, fewer concurrent wall instances)",
                                      new AcceptableValueRange<float>(0f, 1f)));

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StartCoroutine(AttachWhenCameraReady());
        }

        private System.Collections.IEnumerator AttachWhenCameraReady()
        {
            if (GameManager.IsHeadless) yield break;

            float timeout = 15f;
            while (timeout > 0f)
            {
                var camMgr = CameraStateManager.i;
                if ((object)camMgr != null && (UnityEngine.Object)camMgr != null
                    && (UnityEngine.Object)camMgr.mainCamera != null)
                {
                    if (((Component)camMgr).GetComponentInChildren<RainController>() == null)
                    {
                        var go = new GameObject("NuclearRain_Controller");
                        var ctrl = go.AddComponent<RainController>();
                        ctrl.Init(camMgr.mainCamera);
                        Logger.LogInfo("Rain controller attached to flight camera.");
                    }
                    yield break;
                }
                timeout -= Time.deltaTime;
                yield return null;
            }
        }
    }
}
