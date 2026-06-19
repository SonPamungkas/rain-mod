using UnityEngine;

namespace NuclearRain
{
    public class LandSplashSystem
    {
        private const float CrownAFrameStart = 0f  / 24f;
        private const float CrownAFrameEnd   = 7f  / 24f;
        private const float CrownBFrameStart = 8f  / 24f;
        private const float CrownBFrameEnd   = 15f / 24f;
        private const float PlateFrameStart  = 16f / 24f;
        private const float PlateFrameEnd    = 23f / 24f;

        private const float Rate        = 2400f;
        private const float Radius      = 200f;
        private const float MaxAlt      = 250f;
        private const float ProbeDown   = 400f;
        private const float RainShadowProbe = 500f;
        private const int   MaxPerFrame = 50;
        private const float SizeMin = 0.7f, SizeMax = 1.6f;
        private const float CrownScale = 0.6f;
        private const float LifeMin = 0.45f, LifeMax = 0.7f;

        private readonly ParticleSystem _plate;
        private readonly ParticleSystem _crownA;
        private readonly ParticleSystem _crownB;
        private readonly Material _matPlate;
        private readonly Material _matCrownA;
        private readonly Material _matCrownB;
        private float _budget;

        public LandSplashSystem(Texture2D sheet, Transform parent)
        {
            sheet.wrapMode = TextureWrapMode.Clamp;
            _plate = Build(sheet, parent, "SplashPlate",
                           ParticleSystemRenderMode.HorizontalBillboard,
                           PlateFrameStart, PlateFrameEnd, out _matPlate);
            _crownA = Build(sheet, parent, "SplashCrownA",
                           ParticleSystemRenderMode.VerticalBillboard,
                           CrownAFrameStart, CrownAFrameEnd, out _matCrownA);
            _crownB = Build(sheet, parent, "SplashCrownB",
                           ParticleSystemRenderMode.VerticalBillboard,
                           CrownBFrameStart, CrownBFrameEnd, out _matCrownB);
        }

        public void Update(float dt, Vector3 camPos, float intensity, float alpha, float dayNight)
        {
            float a = Mathf.Clamp01(alpha * 2.5f);
            Color tintP = new Color(0.85f * dayNight, 0.9f * dayNight, 1f * dayNight, a);
            if (_matPlate != null) _matPlate.color = tintP;
            if (_matCrownA != null) _matCrownA.color = tintP;
            if (_matCrownB != null) _matCrownB.color = tintP;

            if (intensity <= 0.01f) { _budget = 0f; return; }

            if (!Physics.Raycast(camPos, Vector3.down, out RaycastHit ground,
                                 ProbeDown, Physics.DefaultRaycastLayers,
                                 QueryTriggerInteraction.Ignore))
            {
                _budget = 0f;
                return;
            }
            float altFade = Mathf.Clamp01(1f - ground.distance / MaxAlt);
            if (altFade <= 0f) { _budget = 0f; return; }

            _budget += Rate * intensity * altFade * dt;
            int spawned = 0;
            while (_budget >= 1f && spawned < MaxPerFrame)
            {
                _budget -= 1f;
                spawned++;

                Vector2 r = UnityEngine.Random.insideUnitCircle * Radius;
                Vector3 origin = new Vector3(camPos.x + r.x, camPos.y, camPos.z + r.y);

                Vector3 land;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                    ProbeDown, Physics.DefaultRaycastLayers,
                                    QueryTriggerInteraction.Ignore))
                    land = hit.point + Vector3.up * 0.05f;
                else
                    land = new Vector3(origin.x, Datum.LocalSeaY + 0.05f, origin.z);

                if (Physics.Raycast(land, Vector3.up, RainShadowProbe,
                                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;

                float size = UnityEngine.Random.Range(SizeMin, SizeMax);
                float life = UnityEngine.Random.Range(LifeMin, LifeMax);

                var plateEp = new ParticleSystem.EmitParams
                {
                    position      = land,
                    velocity      = Vector3.zero,
                    rotation      = UnityEngine.Random.Range(0f, 360f),
                    startSize     = size,
                    startLifetime = life
                };
                _plate.Emit(plateEp, 1);

                var crownEp = plateEp;
                crownEp.rotation = 0f;
                crownEp.startSize = size * CrownScale;
                if (UnityEngine.Random.value < 0.5f) _crownA.Emit(crownEp, 1);
                else _crownB.Emit(crownEp, 1);
            }
        }

        public void SetActive(bool on)
        {
            if (_plate != null) _plate.gameObject.SetActive(on);
            if (_crownA != null) _crownA.gameObject.SetActive(on);
            if (_crownB != null) _crownB.gameObject.SetActive(on);
        }

        public void Dispose()
        {
            if (_plate != null) Object.Destroy(_plate.gameObject);
            if (_crownA != null) Object.Destroy(_crownA.gameObject);
            if (_crownB != null) Object.Destroy(_crownB.gameObject);
            if (_matPlate != null) Object.Destroy(_matPlate);
            if (_matCrownA != null) Object.Destroy(_matCrownA);
            if (_matCrownB != null) Object.Destroy(_matCrownB);
        }

        private static ParticleSystem Build(Texture2D sheet, Transform parent, string name,
                                            ParticleSystemRenderMode mode,
                                            float frameStart, float frameEnd, out Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.startSpeed      = 0f;
            main.maxParticles    = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake     = true;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = false;

            var tsa = ps.textureSheetAnimation;
            tsa.enabled    = true;
            tsa.numTilesX  = 8;
            tsa.numTilesY  = 3;
            tsa.cycleCount = 1;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.Linear(0f, frameStart, 1f, frameEnd));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode        = mode;
            if (mode == ParticleSystemRenderMode.VerticalBillboard)
                r.pivot = new Vector3(0f, 0.4f, 0f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;
            mat = MakeFlipbookMaterial(sheet);
            r.sharedMaterial = mat;

            ps.Play();
            return ps;
        }

        private static Material MakeFlipbookMaterial(Texture2D tex)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Diffuse");

            var m = new Material(sh) { renderQueue = 3100 };
            string texProp = m.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            m.SetTexture(texProp, tex);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend"))   m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHABLEND_ON");
            return m;
        }
    }
}
