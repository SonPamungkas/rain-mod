using UnityEngine;

namespace NuclearRain
{
    public class StreakSystem
    {
        private const float Rate        = 180f;
        private const float Range       = 80f;
        private const int   MaxPerFrame = 30;
        private const float ConeHalfAngleDeg = 70f;
        private const float CrawlSpeed  = 6f;
        private const float SizeMin = 0.1f, SizeMax = 0.3f;
        private const float LifeMin = 0.6f, LifeMax = 1.2f;
        private const float MinSurfaceAngle = 60f;

        private readonly ParticleSystem _ps;
        private readonly Material _mat;
        private readonly Transform _parent;
        private float _budget;

        public StreakSystem(Texture2D tex, Transform parent)
        {
            _parent = parent;
            tex.wrapMode = TextureWrapMode.Clamp;
            var go = new GameObject("SurfaceStreaks");
            go.transform.SetParent(parent, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.startSpeed      = 0f;
            main.maxParticles    = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake     = true;

            var emission = _ps.emission;
            emission.rateOverTime = 0f;
            var shape = _ps.shape;
            shape.enabled = false;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode        = ParticleSystemRenderMode.Stretch;
            r.lengthScale       = 2.5f;
            r.velocityScale     = 0f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;
            _mat = MakeStreakMaterial(tex);
            r.sharedMaterial = _mat;

            var tsa = _ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = 1;
            tsa.numTilesY = 4;
            tsa.cycleCount = 1;
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.99f);
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

            _ps.Play();
        }

        public void Update(float dt, Vector3 camPos, Vector3 rainWorldVel,
                           float intensity, float alpha, float dayNight, float cloudBaseY)
        {
            float a = Mathf.Clamp01(alpha * 1.5f);
            if (_mat != null)
                _mat.color = new Color(0.85f * dayNight, 0.9f * dayNight, 1f * dayNight, a);

            if (camPos.y >= cloudBaseY) { _budget = 0f; return; }
            if (intensity <= 0.01f) { _budget = 0f; return; }
            if (rainWorldVel.sqrMagnitude < 1e-4f) return;
            Vector3 rainDir = rainWorldVel.normalized;

            Vector3 fwd = _parent != null ? _parent.forward : Vector3.forward;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Quaternion coneRot = Quaternion.LookRotation(fwd);
            float halfAngleRad = ConeHalfAngleDeg * Mathf.Deg2Rad;
            float cosHalfAngle = Mathf.Cos(halfAngleRad);

            _budget += Rate * intensity * dt;
            int spawned = 0;
            while (_budget >= 1f && spawned < MaxPerFrame)
            {
                _budget -= 1f;
                spawned++;

                float cosAngle = UnityEngine.Random.Range(cosHalfAngle, 1f);
                float theta = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float sinAngle = Mathf.Sqrt(1f - cosAngle * cosAngle);
                Vector3 local = new Vector3(sinAngle * Mathf.Cos(theta), sinAngle * Mathf.Sin(theta), cosAngle);
                Vector3 rd = coneRot * local;
                if (!Physics.Raycast(camPos, rd, out RaycastHit hit, Range,
                                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                if (Vector3.Angle(hit.normal, Vector3.up) < MinSurfaceAngle) continue;

                Vector3 dir = Vector3.ProjectOnPlane(rainDir, hit.normal);
                if (dir.sqrMagnitude < 1e-4f) continue;
                dir.Normalize();

                _ps.Emit(new ParticleSystem.EmitParams
                {
                    position      = hit.point + hit.normal * 0.02f,
                    velocity      = dir * CrawlSpeed,
                    startSize     = UnityEngine.Random.Range(SizeMin, SizeMax),
                    startLifetime = UnityEngine.Random.Range(LifeMin, LifeMax)
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

        private static Material MakeStreakMaterial(Texture2D tex)
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
