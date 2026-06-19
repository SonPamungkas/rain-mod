using UnityEngine;

namespace NuclearRain
{
    public class LandMistSystem
    {
        private const float Rate        = 1.5f;
        private const float Radius      = 200f;
        private const float MaxAlt      = 250f;
        private const float ProbeDown   = 400f;
        private const int   MaxPerFrame = 3;
        private const float RainShadowProbe = 500f;
        private const float SizeMin = 40f, SizeMax = 80f;
        private const float LifeMin = 15f, LifeMax = 30f;
        private const float SpinMax = 12f;
        private const float SpeedRef = 60f;

        private readonly ParticleSystem _ps;
        private readonly Material _mat;
        private float _budget;

        public LandMistSystem(Texture2D tex, Transform parent)
        {
            tex.wrapMode = TextureWrapMode.Clamp;
            var go = new GameObject("LandMist");
            go.transform.SetParent(parent, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.startSpeed      = 0f;
            main.maxParticles    = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake     = true;

            var emission = _ps.emission;
            emission.rateOverTime = 0f;
            var shape = _ps.shape;
            shape.enabled = false;

            var col = _ps.colorOverLifetime;
            col.enabled = true;

            var rot = _ps.rotationOverLifetime;
            rot.enabled = true;
            float s = SpinMax * Mathf.Deg2Rad;
            rot.z = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, -s), new Keyframe(1f, s)));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode        = ParticleSystemRenderMode.HorizontalBillboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;
            r.minParticleSize   = 0f;
            r.maxParticleSize   = 2f;
            _mat = MakeMistMaterial(tex);
            r.sharedMaterial = _mat;

            var tsa = _ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = 2;
            tsa.numTilesY = 1;
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 1.99f);
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

            _ps.Play();
        }

        public void Update(float dt, Vector3 camPos, float camSpeed, float intensity, float alpha, float dayNight)
        {
            float a = Mathf.Clamp01(alpha * 1.2f);
            if (_mat != null)
                _mat.color = new Color(0.85f * dayNight, 0.9f * dayNight, 1f * dayNight, a);

            if (intensity <= 0.01f) { _budget = 0f; return; }

            float fadeInT = Mathf.Lerp(0.4f, 0.05f, intensity);
            var col = _ps.colorOverLifetime;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeInT),
                        new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            float groundDist;
            if (Physics.Raycast(camPos, Vector3.down, out RaycastHit ground,
                                ProbeDown, Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Ignore))
            {
                groundDist = ground.distance;
            }
            else
            {
                groundDist = camPos.y - Datum.LocalSeaY;
                if (groundDist < 0f) { _budget = 0f; return; }
            }
            float altFade = Mathf.Clamp01(1f - groundDist / MaxAlt);
            if (altFade <= 0f) { _budget = 0f; return; }

            float speedFactor = Mathf.Clamp01(camSpeed / SpeedRef);
            float lifeMul = Mathf.Lerp(1f, 0.25f, speedFactor);
            float sizeMul = Mathf.Lerp(1f, 0.6f, speedFactor);

            _budget += Rate * intensity * altFade * dt;
            int spawned = 0;
            while (_budget >= 1f && spawned < MaxPerFrame)
            {
                _budget -= 1f;
                spawned++;

                Vector2 rnd = UnityEngine.Random.insideUnitCircle * Radius;
                Vector3 origin = new Vector3(camPos.x + rnd.x, camPos.y, camPos.z + rnd.y);

                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                    ProbeDown, Physics.DefaultRaycastLayers,
                                    QueryTriggerInteraction.Ignore))
                    continue;

                Vector3 land = hit.point + Vector3.up * 0.5f;

                if (Physics.Raycast(land, Vector3.up, RainShadowProbe,
                                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;

                _ps.Emit(new ParticleSystem.EmitParams
                {
                    position      = land,
                    velocity      = Vector3.zero,
                    rotation      = UnityEngine.Random.Range(0f, 360f),
                    startSize     = UnityEngine.Random.Range(SizeMin, SizeMax) * sizeMul,
                    startLifetime = UnityEngine.Random.Range(LifeMin, LifeMax) * lifeMul
                }, 1);
            }
        }

        public void SetActive(bool on)
        {
            if (_ps != null) _ps.gameObject.SetActive(on);
        }

        public void Dispose()
        {
            if (_ps != null) Object.Destroy(_ps.gameObject);
            if (_mat != null) Object.Destroy(_mat);
        }

        private static Material MakeMistMaterial(Texture2D tex)
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
