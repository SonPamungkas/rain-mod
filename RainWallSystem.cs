using System.Collections.Generic;
using UnityEngine;

namespace NuclearRain
{
    public class RainWallSystem
    {
        private class WallInstance
        {
            public GameObject Go;
            public Mesh Mesh;
            public float[] Ridge;
            public Mesh SkirtMesh;
            public Vector2[] SkirtBaseUV;
            public Vector2[] SkirtScratch;
            public Vector2[] BaseUV;
            public Vector2[] Scratch;
            public Vector3 Center;
            public float Radius;
            public float SpawnRadius;
            public float CurRadius;
            public float Scroll;
            public bool Retired;
            public float RetireTarget;

            public Mesh DashMesh;
            public Vector2[] DashBaseUV;
            public Vector2[] DashScratch;
            public Vector3[] DashVerts;
            public float[] DashLayerNominal;
            public float[] DashAng0;
            public float[] DashLen;
            public float[] DashCur;
            public float[] DashTgt;
            public float[] DashTimer;
            public float[] DashReformTimer;
        }

        private readonly float _baseRadius;
        private readonly float _speedMult;
        private readonly bool _hasDashes;
        private readonly float _tileWorldW;
        private readonly int _maxWalls;

        private const float WallBelow    = 900f;
        private const float WallAbove    = 250f;
        private const float WallHeight   = WallBelow + WallAbove;
        private const int   WallSegments = 24;
        private const float SpeedRef     = 40f;
        private const float RadiusSlew   = 1.5f;

        private const int   DashLayerCount   = 3;
        private const float DashLayerSpacing = 5f;
        private const int   DashCount        = 12;
        private const int   TotalDashes      = DashLayerCount * DashCount;
        private const int   DashSub        = 3;
        private const float DashMinDeg     = 7f;
        private const float DashMaxDeg     = 24f;
        private const float RidgeAmp       = 32f;
        private const float DashOutset     = 6f;
        private const float DashJitter     = 10f;
        private const float DashSlew       = 3.5f;
        private const float DashIntervalMin = 0.8f;
        private const float DashIntervalMax = 3.0f;
        private const float DashReformIntervalMin = 4f;
        private const float DashReformIntervalMax = 9f;

        private static readonly Color FadeBottom = new Color(1f, 1f, 1f, 0f);
        private static readonly Color FadeFull   = Color.white;

        private readonly Material _mat;
        private readonly Material _skirtMat;
        private readonly List<WallInstance> _walls = new List<WallInstance>();
        private WallInstance _active;
        private float _cloudBaseY, _seaLevelY = float.NaN;
        private readonly System.Random _sharedRng = new System.Random(20260614);
        private Vector2 _snowSeed;
        private float _lastAlpha = -1f;

        public RainWallSystem(Material sharedRainMaterial, Texture2D fogTexture = null, float baseRadius = 75f,
                              float speedMult = 1f, bool hasDashes = true, float tileWorldW = 60f, int maxWalls = 5)
        {
            _mat = sharedRainMaterial;
            _baseRadius = baseRadius;
            _speedMult = speedMult;
            _hasDashes = hasDashes;
            _tileWorldW = tileWorldW;
            _maxWalls = maxWalls;
            if (fogTexture != null)
            {
                fogTexture.wrapMode = TextureWrapMode.Repeat;
                _skirtMat = MakeSkirtMaterial(fogTexture);
            }
        }

        public void Update(float dt, Vector3 camPos, Vector3 rainWorldVel, float camSpeed,
                           float wallAlpha, float dayNight, float cloudBaseY, float seaLevelY,
                           bool useSnow, Vector3? centerOverride = null, float? radiusOverride = null)
        {
            bool visible = wallAlpha > 0.001f;
            _cloudBaseY = cloudBaseY;
            _seaLevelY = seaLevelY;
            Vector3 effectiveCenter = centerOverride ?? camPos;

            float key = wallAlpha + dayNight * 1000f;
            if (!Mathf.Approximately(key, _lastAlpha))
            {
                _lastAlpha = key;
                _mat.color = new Color(0.75f * dayNight, 0.8f * dayNight, 0.9f * dayNight,
                                       wallAlpha * 0.6f);
            }

            if (visible)
            {
                bool needNew = _active == null;
                if (!needNew)
                {
                    Vector3 d = effectiveCenter - _active.Center;
                    float horiz = new Vector2(d.x, d.z).magnitude;
                    needNew = horiz >= _active.Radius * 0.5f
                           || Mathf.Abs(d.y) > WallHeight * 0.35f;
                }

                if (needNew)
                {
                    if (_active != null) Retire(_active);
                    _active = Spawn(effectiveCenter, rainWorldVel, camSpeed, radiusOverride);
                    _walls.Add(_active);

                    while (_walls.Count > _maxWalls)
                    {
                        DestroyWall(_walls[0]);
                        _walls.RemoveAt(0);
                    }
                }
            }

            float speedScale = useSnow ? 0.3f : 1f;
            float rate = RainModPlugin.ScrollSpeed.Value * speedScale * _speedMult
                       * (rainWorldVel.magnitude / Mathf.Max(0.1f, RainModPlugin.FallSpeed.Value));

            float xOffset = 0f;
            if (useSnow)
            {
                _snowSeed += new Vector2(dt, dt * 0.7f);
                xOffset = Mathf.Sin(_snowSeed.x) * 0.05f;
            }

            for (int i = _walls.Count - 1; i >= 0; i--)
            {
                WallInstance w = _walls[i];
                w.Scroll += rate * dt;
                ScrollUV(w.Mesh, w.BaseUV, w.Scratch, new Vector2(xOffset, w.Scroll));

                if (w.SkirtMesh != null)
                    ScrollUV(w.SkirtMesh, w.SkirtBaseUV, w.SkirtScratch, new Vector2(xOffset, w.Scroll));

                if (w == _active && !w.Retired)
                {
                    float targetR = radiusOverride ?? TargetRadius(camSpeed);
                    w.CurRadius = Mathf.Lerp(w.CurRadius, targetR, 1f - Mathf.Exp(-RadiusSlew * dt));
                    w.Radius = w.CurRadius;
                    float s = w.CurRadius / w.SpawnRadius;
                    w.Go.transform.localScale = new Vector3(s, 1f, s);

                    if (!float.IsNaN(_seaLevelY))
                    {
                        Vector3 p = w.Go.transform.position;
                        float lo = _seaLevelY + WallAbove;
                        float hi = _cloudBaseY - WallAbove;
                        if (hi < lo) hi = lo;
                        float desired = Mathf.Clamp(effectiveCenter.y, lo, hi);
                        if (Mathf.Abs(effectiveCenter.y - p.y) > WallHeight * 0.5f)
                            p.y = Mathf.Clamp(desired, lo, hi);
                        else
                            p.y = Mathf.Clamp(p.y, lo, hi);
                        w.Go.transform.position = new Vector3(p.x, p.y, p.z);
                    }
                }

                if (w.DashMesh != null)
                {
                    UpdateDashes(w, dt);
                    ScrollUV(w.DashMesh, w.DashBaseUV, w.DashScratch, new Vector2(xOffset, w.Scroll));
                }

                if (w.Retired && w.Scroll >= w.RetireTarget)
                {
                    DestroyWall(w);
                    _walls.RemoveAt(i);
                    continue;
                }

                if (w.Go.activeSelf != visible) w.Go.SetActive(visible);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _walls.Count; i++) DestroyWall(_walls[i]);
            _walls.Clear();
            _active = null;
            if (_skirtMat != null) Object.Destroy(_skirtMat);
        }

        private void Retire(WallInstance w)
        {
            w.Retired = true;
            w.RetireTarget = Mathf.Floor(w.Scroll) + 1f;
            if (w == _active) _active = null;
        }

        private float TargetRadius(float camSpeed)
            => _baseRadius * (1f + Mathf.Log(1f + camSpeed / SpeedRef));

        private WallInstance Spawn(Vector3 camPos, Vector3 rainWorldVel, float camSpeed, float? radiusOverride)
        {
            float radius = radiusOverride ?? TargetRadius(camSpeed);

            float tiles = Mathf.Max(2f, Mathf.Round(2f * Mathf.PI * radius / _tileWorldW));

            var w = new WallInstance { Center = camPos, Radius = radius, SpawnRadius = radius, CurRadius = radius };
            w.Ridge = GenerateRidge(WallSegments, RidgeAmp, _sharedRng);
            w.Go = BuildCylinder("RainWallStatic", radius, WallHeight, WallSegments, tiles,
                                 _mat, w.Ridge, _tileWorldW, out w.Mesh, out w.BaseUV);
            w.Scratch = new Vector2[w.BaseUV.Length];

            w.Go.transform.position = camPos;
            if (rainWorldVel.sqrMagnitude > 0.01f)
            {
                Vector3 dir = rainWorldVel.normalized;
                float angle = Vector3.Angle(Vector3.down, dir);
                float maxTilt = RainModPlugin.MaxTiltDeg.Value;
                if (angle > maxTilt)
                    dir = Vector3.Slerp(Vector3.down, dir, maxTilt / angle);
                w.Go.transform.rotation = Quaternion.FromToRotation(Vector3.down, dir);
            }

            if (_hasDashes) BuildDashRing(w);
            if (_skirtMat != null) BuildFogSkirt(w, radius, tiles);
            return w;
        }

        private static float[] GenerateRidge(int segments, float ridgeAmp, System.Random rng)
        {
            var ridge = new float[segments + 1];
            int idx = 0;
            float sign = 1f;
            while (idx < segments)
            {
                int runLen = 1 + rng.Next(3);
                float mag = Mathf.Lerp(ridgeAmp * 0.6f, ridgeAmp, (float)rng.NextDouble());
                for (int k = 0; k < runLen && idx < segments; k++, idx++)
                    ridge[idx] = sign * mag;
                sign = -sign;
            }
            ridge[segments] = ridge[0];
            return ridge;
        }

        private void BuildFogSkirt(WallInstance w, float radius, float tiles)
        {
            int segments = WallSegments;
            int ringVerts = segments + 1;
            const float Flare = 14f;
            const float Drop  = 10f;

            var verts = new Vector3[ringVerts * 2];
            var uvs   = new Vector2[ringVerts * 2];
            var cols  = new Color[ringVerts * 2];
            var tris  = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float ang = (float)i / segments * Mathf.PI * 2f;
                float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                float rTop = radius + w.Ridge[i];
                float rOut = rTop + Flare;
                float u = (float)i / segments * tiles;

                verts[i * 2]     = new Vector3(cx * rTop, -WallBelow, cz * rTop);
                verts[i * 2 + 1] = new Vector3(cx * rOut, -WallBelow - Drop, cz * rOut);
                uvs[i * 2]     = new Vector2(u, 1f);
                uvs[i * 2 + 1] = new Vector2(u, 0f);
                cols[i * 2]     = FadeFull;
                cols[i * 2 + 1] = FadeBottom;
            }

            for (int i = 0; i < segments; i++)
            {
                int v = i * 2;
                tris[i * 6]     = v;     tris[i * 6 + 1] = v + 1; tris[i * 6 + 2] = v + 2;
                tris[i * 6 + 3] = v + 2; tris[i * 6 + 4] = v + 1; tris[i * 6 + 5] = v + 3;
            }

            var go = new GameObject("RainWallSkirt");
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _skirtMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.transform.SetParent(w.Go.transform, false);

            w.SkirtBaseUV = uvs;
            w.SkirtScratch = new Vector2[uvs.Length];
            w.SkirtMesh = new Mesh { vertices = verts, uv = uvs, colors = cols, triangles = tris };
            w.SkirtMesh.RecalculateBounds();
            mf.sharedMesh = w.SkirtMesh;
        }

        private void BuildDashRing(WallInstance w)
        {
            w.DashLayerNominal = new float[DashLayerCount];
            for (int L = 0; L < DashLayerCount; L++)
                w.DashLayerNominal[L] = w.Radius + DashOutset + L * DashLayerSpacing;

            int vertsPerDash = (DashSub + 1) * 2;
            int totalVerts = TotalDashes * vertsPerDash;
            int totalTris = TotalDashes * DashSub * 6;

            w.DashVerts       = new Vector3[totalVerts];
            w.DashBaseUV      = new Vector2[totalVerts];
            w.DashScratch     = new Vector2[totalVerts];
            w.DashAng0        = new float[TotalDashes];
            w.DashLen         = new float[TotalDashes];
            w.DashCur         = new float[TotalDashes];
            w.DashTgt         = new float[TotalDashes];
            w.DashTimer       = new float[TotalDashes];
            w.DashReformTimer = new float[TotalDashes];
            var tris = new int[totalTris];

            for (int gi = 0; gi < TotalDashes; gi++)
            {
                PickDashShape(w, gi);
                WriteDashUV(w, gi, _tileWorldW);

                w.DashCur[gi] = Mathf.Lerp(-DashJitter, DashJitter, (float)_sharedRng.NextDouble());
                w.DashTgt[gi] = Mathf.Lerp(-DashJitter, DashJitter, (float)_sharedRng.NextDouble());
                w.DashTimer[gi] = Mathf.Lerp(DashIntervalMin, DashIntervalMax, (float)_sharedRng.NextDouble());
                w.DashReformTimer[gi] = Mathf.Lerp(DashReformIntervalMin, DashReformIntervalMax, (float)_sharedRng.NextDouble());

                int vBase = gi * vertsPerDash;
                int tBase = gi * DashSub * 6;
                for (int j = 0; j < DashSub; j++)
                {
                    int v = vBase + j * 2;
                    int t = tBase + j * 6;
                    tris[t]     = v;     tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                    tris[t + 3] = v + 2; tris[t + 4] = v + 1; tris[t + 5] = v + 3;
                }
            }

            var go = new GameObject("RainWallDashes");
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.transform.SetParent(w.Go.transform, false);

            WriteDashVerts(w);
            float maxNominal = w.DashLayerNominal[DashLayerCount - 1] + DashJitter;
            w.DashMesh = new Mesh
            {
                vertices = w.DashVerts,
                uv = w.DashBaseUV,
                triangles = tris,
                bounds = new Bounds(
                    new Vector3(0f, (WallAbove - WallBelow) * 0.5f, 0f),
                    new Vector3(maxNominal * 2.2f, WallHeight * 1.2f, maxNominal * 2.2f))
            };
            mf.sharedMesh = w.DashMesh;
        }

        private void PickDashShape(WallInstance w, int gi)
        {
            int layer = gi / DashCount;
            int d = gi % DashCount;
            float slot = Mathf.PI * 2f / DashCount;
            float phase = layer * (slot / DashLayerCount);
            float minLen = DashMinDeg * Mathf.Deg2Rad;
            float maxLen = DashMaxDeg * Mathf.Deg2Rad;

            float len = Mathf.Lerp(minLen, Mathf.Min(maxLen, slot * 0.9f),
                                   (float)_sharedRng.NextDouble());
            float freeplay = slot - len;
            float start = phase + d * slot + (float)_sharedRng.NextDouble() * freeplay;
            w.DashAng0[gi] = start;
            w.DashLen[gi] = len;
        }

        private static void WriteDashUV(WallInstance w, int gi, float tileWorldW)
        {
            int layer = gi / DashCount;
            int vertsPerDash = (DashSub + 1) * 2;
            float nominal = w.DashLayerNominal[layer];
            float tilesPerRad = nominal / tileWorldW;
            float vTop = WallHeight / tileWorldW;
            int vBase = gi * vertsPerDash;
            float start = w.DashAng0[gi];
            float len = w.DashLen[gi];
            for (int j = 0; j <= DashSub; j++)
            {
                float ang = start + len * ((float)j / DashSub);
                float u = ang * tilesPerRad;
                w.DashBaseUV[vBase + j * 2]     = new Vector2(u, 0f);
                w.DashBaseUV[vBase + j * 2 + 1] = new Vector2(u, vTop);
            }
        }

        private void UpdateDashes(WallInstance w, float dt)
        {
            for (int gi = 0; gi < TotalDashes; gi++)
            {
                w.DashTimer[gi] -= dt;
                if (w.DashTimer[gi] <= 0f)
                {
                    w.DashTgt[gi] = Mathf.Lerp(-DashJitter, DashJitter, (float)_sharedRng.NextDouble());
                    w.DashTimer[gi] = Mathf.Lerp(DashIntervalMin, DashIntervalMax, (float)_sharedRng.NextDouble());
                }
                w.DashCur[gi] = Mathf.Lerp(w.DashCur[gi], w.DashTgt[gi], 1f - Mathf.Exp(-DashSlew * dt));

                w.DashReformTimer[gi] -= dt;
                if (w.DashReformTimer[gi] <= 0f)
                {
                    PickDashShape(w, gi);
                    WriteDashUV(w, gi, _tileWorldW);
                    w.DashReformTimer[gi] = Mathf.Lerp(DashReformIntervalMin, DashReformIntervalMax, (float)_sharedRng.NextDouble());
                }
            }
            WriteDashVerts(w);
            w.DashMesh.vertices = w.DashVerts;
        }

        private static void WriteDashVerts(WallInstance w)
        {
            int vertsPerDash = (DashSub + 1) * 2;
            for (int gi = 0; gi < TotalDashes; gi++)
            {
                int layer = gi / DashCount;
                float r = w.DashLayerNominal[layer] + w.DashCur[gi];
                float start = w.DashAng0[gi];
                float len = w.DashLen[gi];
                int vBase = gi * vertsPerDash;
                for (int j = 0; j <= DashSub; j++)
                {
                    float ang = start + len * ((float)j / DashSub);
                    float x = Mathf.Cos(ang) * r;
                    float z = Mathf.Sin(ang) * r;
                    w.DashVerts[vBase + j * 2]     = new Vector3(x, -WallBelow, z);
                    w.DashVerts[vBase + j * 2 + 1] = new Vector3(x,  WallAbove, z);
                }
            }
        }

        private static void DestroyWall(WallInstance w)
        {
            if (w.Mesh != null) Object.Destroy(w.Mesh);
            if (w.DashMesh != null) Object.Destroy(w.DashMesh);
            if (w.SkirtMesh != null) Object.Destroy(w.SkirtMesh);
            if (w.Go != null) Object.Destroy(w.Go);
        }

        private static Material MakeSkirtMaterial(Texture2D tex)
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

        private static void ScrollUV(Mesh mesh, Vector2[] baseUV, Vector2[] scratch, Vector2 offset)
        {
            for (int i = 0; i < baseUV.Length; i++)
                scratch[i] = baseUV[i] + offset;
            mesh.uv = scratch;
        }

        private static GameObject BuildCylinder(string name, float radius, float height,
                                                int segments, float uvTilesX, Material mat,
                                                float[] ridge, float tileWorldW,
                                                out Mesh mesh, out Vector2[] baseUV)
        {
            var go = new GameObject(name);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            float coneH = radius * 0.8f;
            float slopeV = Mathf.Sqrt(radius * radius + coneH * coneH) / radius;

            int ringVerts = segments + 1;
            int sideCount = ringVerts * 3;
            int topRim  = sideCount;
            int topApex = topRim + ringVerts;
            int vertCount = topApex + ringVerts;

            var verts = new Vector3[vertCount];
            var uvs   = new Vector2[vertCount];
            var cols  = new Color[vertCount];
            var tris  = new int[segments * 12 + segments * 3];

            float vTop = height / tileWorldW;
            float vMid = vTop * (WallBelow / WallHeight);

            for (int i = 0; i <= segments; i++)
            {
                float ang = (float)i / segments * Mathf.PI * 2f;
                float rr = radius + ridge[i];
                float x = Mathf.Cos(ang) * rr;
                float z = Mathf.Sin(ang) * rr;
                float u = (float)i / segments * uvTilesX;

                int b = i * 3;
                verts[b]     = new Vector3(x, -WallBelow, z);
                verts[b + 1] = new Vector3(x, 0f, z);
                verts[b + 2] = new Vector3(x,  WallAbove, z);
                uvs[b]     = new Vector2(u, 0f);
                uvs[b + 1] = new Vector2(u, vMid);
                uvs[b + 2] = new Vector2(u, vTop);
                cols[b]     = FadeBottom;
                cols[b + 1] = FadeFull;
                cols[b + 2] = FadeFull;

                verts[topRim + i] = new Vector3(x, WallAbove, z);
                uvs[topRim + i]   = new Vector2(u, vTop);
                cols[topRim + i]  = FadeFull;

                float uApex = ((float)i + 0.5f) / segments * uvTilesX;
                verts[topApex + i] = new Vector3(0f, WallAbove + coneH, 0f);
                uvs[topApex + i]   = new Vector2(uApex, vTop + slopeV);
                cols[topApex + i]  = FadeFull;
            }

            int tt = 0;
            for (int i = 0; i < segments; i++)
            {
                int b = i * 3;
                int bn = (i + 1) * 3;
                tris[tt]     = b;      tris[tt + 1] = b + 1;  tris[tt + 2] = bn;
                tris[tt + 3] = bn;     tris[tt + 4] = b + 1;  tris[tt + 5] = bn + 1;
                tt += 6;
                tris[tt]     = b + 1;  tris[tt + 1] = b + 2;  tris[tt + 2] = bn + 1;
                tris[tt + 3] = bn + 1; tris[tt + 4] = b + 2;  tris[tt + 5] = bn + 2;
                tt += 6;

                tris[tt]     = topRim + i;
                tris[tt + 1] = topApex + i;
                tris[tt + 2] = topRim + i + 1;
                tt += 3;
            }

            mesh = new Mesh { vertices = verts, uv = uvs, colors = cols, triangles = tris };
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
            baseUV = uvs;
            return go;
        }
    }
}
