using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace NuclearRain
{
    public class RainController : MonoBehaviour
    {
        private Camera _cam;
        private Transform _camT;

        private Material _matWall;
        private readonly List<RainWallSystem> _wallLayers = new List<RainWallSystem>();
        private LandFogSystem _fogTranslucentLand;
        private SeaFogSystem _fogTranslucentSea;
        private LandFogSystem _fogDitheredLand;
        private SeaFogSystem _fogDitheredSea;

        private ParticleSystem _dropPS;
        private Material _matDropParticle;
        private float _dropBudget;
        private Vector3 _dropVelocity;

        private float _debugTimer;

        private LandSplashSystem _landSplashes;
        private SeaSplashSystem _seaSplashes;
        private readonly ShipHangarDetector _shipHangarDetector = new ShipHangarDetector();
        private LandMistSystem _mistLand;
        private SeaMistSystem _mistSea;
        private StreakSystem _streaks;

        private const float DropEmitRate      = 10000f;
        private const float DropScatterRadius = 75f;
        private const float DropSpawnAbove    = 10f;
        private const float DropProbeDown     = 450f;
        private const float DropMaxLifetime   = 1.5f;
        private const int   DropMaxPerFrame   = 200;
        private const float DropSize          = 0.06f;
        private const float DropVelocityScale = 0.025f;
        private const float DropFallBoost     = 25f;
        private const float RainShadowProbe   = 500f;

        private static readonly float[] OnionRadii = { 300f, 225f, 165f, 105f, 60f };

        public void Init(Camera cam)
        {
            _cam = cam;
            _camT = cam.transform;

            if (RainModPlugin.DebugLogging.Value)
            {
                var names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
                RainModPlugin.LogInstance.LogInfo("[NuclearRain] Embedded resources: " + string.Join(", ", names));
            }

            bool useSnow = RainModPlugin.UseSnow.Value;

            Texture2D wallTex = useSnow
                ? (LoadEmbeddedPng("NuclearRain.snow.png") ?? GenerateStreakTexture())
                : (LoadEmbeddedPng("NuclearRain.rainwall.png")
                   ?? LoadEmbeddedPng("NuclearRain.rain.png")
                   ?? GenerateStreakTexture());
            _matWall = MakeRainMaterial(wallTex);

            const float TileRatio = 60f / 300f;
            bool highQuality = RainModPlugin.RainWallQuality.Value >= 0.5f;
            for (int i = 0; i < OnionRadii.Length; i++)
            {
                bool hasDashes = highQuality || i == 0;
                int maxWalls = highQuality ? 5 : 3;
                _wallLayers.Add(new RainWallSystem(_matWall, null, OnionRadii[i], 1f, hasDashes,
                                                   OnionRadii[i] * TileRatio, maxWalls));
            }

            Texture2D fogTranslucentTex = LoadEmbeddedPng("NuclearRain.fog_translucent.png");
            if (fogTranslucentTex != null)
            {
                _fogTranslucentLand = new LandFogSystem(fogTranslucentTex, transform, 200f, 5f, 10f, 1.5f, 3f);
                _fogTranslucentSea = new SeaFogSystem(fogTranslucentTex, transform, 200f, 10f, 20f, 5f, 10f);
            }

            Texture2D fogDitheredTex = LoadEmbeddedPng("NuclearRain.fog_dithered.png");
            if (fogDitheredTex != null)
            {
                _fogDitheredLand = new LandFogSystem(fogDitheredTex, transform, 200f, 4f, 8f, 1.5f, 3f);
                _fogDitheredSea = new SeaFogSystem(fogDitheredTex, transform, 200f, 8f, 16f, 5f, 10f);
            }

            if (!useSnow)
            {
                Texture2D dropTex = LoadEmbeddedPng("NuclearRain.rain.png") ?? GenerateStreakTexture();
                _dropPS = BuildDropParticleSystem(dropTex, transform, out _matDropParticle);
            }

            Texture2D splashTex = LoadEmbeddedPng("NuclearRain.rain_anim.png");
            if (splashTex != null)
            {
                _landSplashes = new LandSplashSystem(splashTex, transform);
                _seaSplashes = new SeaSplashSystem(splashTex, transform);
            }
            else if (RainModPlugin.DebugLogging.Value)
            {
                RainModPlugin.LogInstance.LogWarning("[NuclearRain] rain_anim.png not embedded; ground splashes disabled.");
            }

            if (!useSnow)
            {
                Texture2D mistTex = LoadEmbeddedPng("NuclearRain.mist.png");
                if (mistTex != null)
                {
                    _mistLand = new LandMistSystem(mistTex, transform);
                    _mistSea = new SeaMistSystem(mistTex, transform);
                }

                Texture2D streakTex = LoadEmbeddedPng("NuclearRain.rain.png");
                if (streakTex != null) _streaks = new StreakSystem(streakTex, transform);
            }

            SetAlpha(0f, 1f);
        }

        private void LateUpdate()
        {
            if (_cam == null) { Destroy(gameObject); return; }
            if ((object)CameraStateManager.i == null || (object)LevelInfo.i == null)
            {
                SetAlpha(0f, 1f);
                return;
            }

            transform.position = _camT.position;

            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            bool useSnow = RainModPlugin.UseSnow.Value;

            Vector3 rainWorldVel = LevelInfo.i.windVelocity + Vector3.down * RainModPlugin.FallSpeed.Value;
            Vector3 apparent = rainWorldVel - CameraStateManager.i.cameraVelocity;
            float apparentSpeed = apparent.magnitude;

            _dropVelocity = apparent;

            if (apparentSpeed > 0.1f)
            {
                Vector3 dir = apparent / apparentSpeed;
                float angle = Vector3.Angle(Vector3.down, dir);
                float maxTilt = RainModPlugin.MaxTiltDeg.Value;
                if (angle > maxTilt)
                    dir = Vector3.Slerp(Vector3.down, dir, maxTilt / angle);
                Quaternion target = Quaternion.FromToRotation(Vector3.down, dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, target,
                                                      1f - Mathf.Exp(-8f * dt));
            }

            float cloudBaseY = Datum.LocalSeaY + LevelInfo.i.cloudHeight;
            float depthBelow = cloudBaseY - _camT.position.y;
            float altitudeFade = Mathf.Clamp01(depthBelow / Mathf.Max(1f, RainModPlugin.FadeBand.Value));

            float weather = Mathf.Clamp01(LevelInfo.i.conditions);
            float rainW = Mathf.Clamp01((weather - 0.6f) / 0.4f);
            float wallW = Mathf.Clamp01((weather - 0.8f) / 0.2f);

            float intensity     = altitudeFade * rainW;
            float wallIntensity = altitudeFade * wallW;

            float rainWBoost = weather < 0.6f ? 0f : 1f + (weather - 0.6f) / 0.4f;
            float splashDropIntensity = altitudeFade * rainWBoost;

            float dayNight = DayNightBrightness();

            SetAlpha(intensity * RainModPlugin.RainAlpha.Value, dayNight);

            _shipHangarDetector.Update(dt, _camT.position);
            Transform shipAnchor = _shipHangarDetector.ShipAnchor;
            float shipLen = shipAnchor != null ? _shipHangarDetector.ShipAnchorLength : 0f;
            float camSpeed = CameraStateManager.i.cameraVelocity.magnitude;

            for (int i = 0; i < _wallLayers.Count; i++)
            {
                bool isOuter = i == 0;
                Vector3? centerOverride = (isOuter && shipAnchor != null) ? shipAnchor.position : (Vector3?)null;
                float? radiusOverride = (isOuter && shipAnchor != null && shipLen > 0f) ? shipLen : (float?)null;
                float layerAlpha = RainModPlugin.EnableRainWall.Value
                    ? ((!isOuter && shipAnchor != null) ? 0f : wallIntensity * RainModPlugin.RainAlpha.Value)
                    : 0f;
                _wallLayers[i].Update(dt, _camT.position, rainWorldVel, camSpeed,
                                     layerAlpha, dayNight, cloudBaseY, Datum.LocalSeaY, useSnow,
                                     centerOverride, radiusOverride);
            }

            UpdateDropEmission(dt, RainModPlugin.EnableRainDrop.Value ? splashDropIntensity : 0f);
            float rainAlpha = intensity * RainModPlugin.RainAlpha.Value;

            float splashIntensity = RainModPlugin.EnableSplash.Value ? splashDropIntensity : 0f;
            _landSplashes?.Update(dt, _camT.position, splashIntensity, rainAlpha, dayNight);
            _seaSplashes?.Update(dt, _camT.position, splashIntensity, rainAlpha, dayNight);

            float mistIntensity = RainModPlugin.EnableMist.Value ? intensity : 0f;
            _mistLand?.Update(dt, _camT.position, camSpeed, mistIntensity, rainAlpha, dayNight);
            _mistSea?.Update(dt, _camT.position, camSpeed, mistIntensity, rainAlpha, dayNight);

            _streaks?.Update(dt, _camT.position, rainWorldVel, RainModPlugin.EnableStreaks.Value ? intensity : 0f, rainAlpha, dayNight, cloudBaseY);

            float fogIntensity = RainModPlugin.EnableFog.Value ? intensity : 0f;
            _fogTranslucentLand?.Update(dt, _camT.position, fogIntensity, rainAlpha, dayNight);
            _fogTranslucentSea?.Update(dt, _camT.position, fogIntensity, rainAlpha, dayNight);
            _fogDitheredLand?.Update(dt, _camT.position, fogIntensity, rainAlpha, dayNight);
            _fogDitheredSea?.Update(dt, _camT.position, fogIntensity, rainAlpha, dayNight);

            if (RainModPlugin.DebugLogging.Value || RainModPlugin.LogUnderCamera.Value)
            {
                _debugTimer -= dt;
                if (_debugTimer <= 0f)
                {
                    _debugTimer = 2f;
                    if (RainModPlugin.DebugLogging.Value)
                    {
                        RainModPlugin.LogInstance.LogInfo(string.Format(
                            "[NuclearRain] camY={0:F1} cloudBaseY={1:F1} depthBelow={2:F1} conditions={3:F2} wind={4} alpha={5:F3}",
                            _camT.position.y, cloudBaseY, depthBelow, LevelInfo.i.conditions,
                            LevelInfo.i.windVelocity, intensity * RainModPlugin.RainAlpha.Value));
                    }

                    if (RainModPlugin.LogUnderCamera.Value)
                    {
                        if (Physics.Raycast(_camT.position, Vector3.down, out RaycastHit hit, 500f,
                                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        {
                            int layer = hit.collider.gameObject.layer;
                            RainModPlugin.LogInstance.LogInfo(string.Format(
                                "[NuclearRain] under camera: HIT obj='{0}' collider={1} layer={2}({3}) isTrigger={4} dist={5:F1} hitY={6:F2} seaY={7:F2} deltaToSea={8:F2}",
                                hit.collider.gameObject.name, hit.collider.GetType().Name,
                                layer, LayerMask.LayerToName(layer), hit.collider.isTrigger,
                                hit.distance, hit.point.y, Datum.LocalSeaY, hit.point.y - Datum.LocalSeaY));
                        }
                        else
                        {
                            RainModPlugin.LogInstance.LogInfo(string.Format(
                                "[NuclearRain] under camera: MISS camPos={0} altAboveSea={1:F1}",
                                _camT.position, _camT.position.y - Datum.LocalSeaY));
                        }
                    }
                }
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _wallLayers.Count; i++) _wallLayers[i].Dispose();
            _fogTranslucentLand?.Dispose();
            _fogTranslucentSea?.Dispose();
            _fogDitheredLand?.Dispose();
            _fogDitheredSea?.Dispose();
            _landSplashes?.Dispose();
            _seaSplashes?.Dispose();
            _mistLand?.Dispose();
            _mistSea?.Dispose();
            _streaks?.Dispose();
        }

        private void SetAlpha(float a, float dayNight)
        {
            bool on = a > 0.001f;
            if (_dropPS != null) _dropPS.gameObject.SetActive(on);
            _landSplashes?.SetActive(on);
            _seaSplashes?.SetActive(on);
            _mistLand?.SetActive(on);
            _mistSea?.SetActive(on);
            _streaks?.SetActive(on);
            _fogTranslucentLand?.SetActive(on);
            _fogTranslucentSea?.SetActive(on);
            _fogDitheredLand?.SetActive(on);
            _fogDitheredSea?.SetActive(on);

            if (!on) return;
            if (_matDropParticle != null)
                _matDropParticle.color = new Color(0.85f * dayNight, 0.9f * dayNight, 1f * dayNight,
                                                   Mathf.Clamp01(a * 1.8f));
        }

        private static float DayNightBrightness()
        {
            float b = RenderSettings.ambientLight.grayscale;
            Light sun = RenderSettings.sun;
            if (sun != null) b = Mathf.Max(b, sun.intensity * sun.color.grayscale);
            return Mathf.Clamp(b, 0.12f, 1f);
        }

        private void UpdateDropEmission(float dt, float intensity)
        {
            if (_dropPS == null) return;
            if (intensity <= 0.01f) { _dropBudget = 0f; return; }

            Vector3 dropVel = _dropVelocity + Vector3.down * DropFallBoost;
            float dropSpeed = dropVel.magnitude;
            if (dropSpeed < 0.1f) return;

            Vector3 camPos = _camT.position;
            _dropBudget += DropEmitRate * intensity * dt;
            int spawned = 0;

            while (_dropBudget >= 1f && spawned < DropMaxPerFrame)
            {
                _dropBudget -= 1f;
                spawned++;

                Vector2 rand2 = UnityEngine.Random.insideUnitCircle * DropScatterRadius;
                float spawnX  = camPos.x + rand2.x;
                float spawnZ  = camPos.z + rand2.y;
                float spawnY  = camPos.y + DropSpawnAbove;

                float groundY;
                bool isWater;
                if (Physics.Raycast(new Vector3(spawnX, spawnY, spawnZ), Vector3.down,
                                    out RaycastHit hit, DropProbeDown,
                                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    isWater = RainRaycast.IsWaterHit(hit);
                    groundY = isWater ? Datum.LocalSeaY : hit.point.y;
                }
                else
                {
                    groundY = Datum.LocalSeaY;
                    isWater = true;
                }

                float travelDist = spawnY - groundY;
                if (travelDist <= 0.5f) continue;

                if (!isWater && Physics.Raycast(new Vector3(spawnX, groundY + 0.1f, spawnZ), Vector3.up,
                                    RainShadowProbe, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;

                float lifetime = Mathf.Min(travelDist / dropSpeed, DropMaxLifetime);

                var ep = new ParticleSystem.EmitParams
                {
                    position      = new Vector3(spawnX, spawnY, spawnZ),
                    velocity      = dropVel,
                    startLifetime = lifetime,
                    startSize     = DropSize,
                };
                _dropPS.Emit(ep, 1);
            }
        }

        private static ParticleSystem BuildDropParticleSystem(Texture2D tex, Transform parent,
                                                              out Material mat)
        {
            var go = new GameObject("RainDropParticles");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.startSpeed      = 0f;
            main.startSize       = DropSize;
            main.maxParticles    = 20000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake     = true;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = false;

            var tsa = ps.textureSheetAnimation;
            tsa.enabled       = true;
            tsa.numTilesX     = 1;
            tsa.numTilesY     = 4;
            tsa.cycleCount    = 1;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            tsa.startFrame    = new ParticleSystem.MinMaxCurve(0f, 0.99f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode        = ParticleSystemRenderMode.Stretch;
            r.velocityScale     = DropVelocityScale;
            r.lengthScale       = 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;

            mat = MakeRainMaterial(tex);
            r.sharedMaterial = mat;

            ps.Play();
            return ps;
        }

        private static Material MakeRainMaterial(Texture2D tex)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default");
            if (sh == null)
            {
                Debug.LogWarning("[NuclearRain] No suitable unlit/transparent shader found; rain will not render correctly.");
                sh = Shader.Find("Diffuse");
            }
            var m = new Material(sh)
            {
                renderQueue = 3100
            };
            string texProp = m.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            m.SetTexture(texProp, tex);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return m;
        }

        private static Texture2D LoadEmbeddedPng(string resourceName, bool mipChain = true)
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (s == null) return null;
                    var bytes = new byte[s.Length];
                    s.Read(bytes, 0, bytes.Length);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain);
                    tex.LoadImage(bytes);
                    tex.wrapMode = TextureWrapMode.Repeat;
                    return tex;
                }
            }
            catch { return null; }
        }

        private static Texture2D GenerateStreakTexture()
        {
            const int W = 256, H = 256;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
            var px  = new Color[W * H];
            var rng = new System.Random(1234);

            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            for (int n = 0; n < 90; n++)
            {
                int x      = rng.Next(W);
                int y0     = rng.Next(H);
                int len    = 20 + rng.Next(50);
                float bright = 0.4f + (float)rng.NextDouble() * 0.6f;
                for (int j = 0; j < len; j++)
                {
                    int y    = (y0 + j) % H;
                    float fall = Mathf.Sin((float)j / len * Mathf.PI);
                    px[y * W + x] = new Color(1f, 1f, 1f, bright * fall * 0.8f);
                }
            }

            tex.SetPixels(px);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }
    }
}
