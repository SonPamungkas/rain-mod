using UnityEngine;

namespace NuclearRain
{
    public class SeaFogSystem
    {
        private const float Rate        = 20f;
        private const float MaxAlt      = 250f;
        private const float ProbeDown   = 400f;
        private const int   MaxPerFrame = 5;
        private const float SpinMax = 5f;
        private const float RiseSpeedMin = 0.3f;
        private const float RiseSpeedMax = 1.0f;
        private const float RadiusMargin = 50f;
        private const float HeightStretch = 1.6f;
        private const float WidthStretch  = 6.0f;

        private readonly ParticleSystem _ps;
        private readonly Material _mat;
        private float _budget;

        private readonly float _radius;
        private readonly float _sizeMin;
        private readonly float _sizeMax;
        private readonly float _lifeMin;
        private readonly float _lifeMax;

        public SeaFogSystem(Texture2D tex, Transform parent, float radius = 150f, float sizeMin = 30f, float sizeMax = 80f, float lifeMin = 10f, float lifeMax = 20f)
        {
            _radius = radius;
            _sizeMin = sizeMin;
            _sizeMax = sizeMax;
            _lifeMin = lifeMin;
            _lifeMax = lifeMax;

            tex.wrapMode = TextureWrapMode.Clamp;
            var go = new GameObject("SeaFog");
            go.transform.SetParent(parent, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.startSpeed      = 0f;
            main.maxParticles    = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake     = true;
            main.startSize3D     = true;

            var emission = _ps.emission;
            emission.rateOverTime = 0f;
            var shape = _ps.shape;
            shape.enabled = false;

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.4f),
                        new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var rot = _ps.rotationOverLifetime;
            rot.enabled = true;
            float s = SpinMax * Mathf.Deg2Rad;
            rot.z = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, -s), new Keyframe(1f, s)));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode        = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;
            _mat = MakeFogMaterial(tex);
            r.sharedMaterial = _mat;

            _ps.Play();
        }

        public void Update(float dt, Vector3 camPos, float intensity, float alpha, float dayNight)
        {
            float a = Mathf.Clamp01(alpha * 1.2f);
            if (_mat != null)
                _mat.color = new Color(0.85f * dayNight, 0.9f * dayNight, 1f * dayNight, a);

            if (intensity <= 0.01f) { _budget = 0f; return; }

            float groundDist;
            if (Physics.Raycast(camPos, Vector3.down, out RaycastHit ground,
                                ProbeDown, Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Collide))
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

            _budget += Rate * intensity * altFade * dt;
            int spawned = 0;
            while (_budget >= 1f && spawned < MaxPerFrame)
            {
                _budget -= 1f;
                spawned++;

                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float dist  = UnityEngine.Random.Range(_radius - RadiusMargin, _radius + RadiusMargin);
                Vector2 rnd = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
                Vector3 origin = new Vector3(camPos.x + rnd.x, camPos.y, camPos.z + rnd.y);

                Vector3 land;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                    ProbeDown, Physics.DefaultRaycastLayers,
                                    QueryTriggerInteraction.Collide))
                {
                    if (!RainRaycast.IsWaterHit(hit)) continue;
                    land = new Vector3(origin.x, Datum.LocalSeaY + 0.05f, origin.z);
                }
                else
                {
                    land = new Vector3(origin.x, Datum.LocalSeaY + 0.05f, origin.z);
                }

                float baseSize = UnityEngine.Random.Range(_sizeMin, _sizeMax);
                _ps.Emit(new ParticleSystem.EmitParams
                {
                    position      = land,
                    velocity      = Vector3.up * UnityEngine.Random.Range(RiseSpeedMin, RiseSpeedMax),
                    rotation3D    = new Vector3(45f, 0f, UnityEngine.Random.Range(0f, 360f)),
                    startSize3D   = new Vector3(baseSize * WidthStretch, baseSize * HeightStretch, baseSize * WidthStretch),
                    startLifetime = UnityEngine.Random.Range(_lifeMin, _lifeMax)
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

        private static Material MakeFogMaterial(Texture2D tex)
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
