using UnityEngine;

namespace NuclearRain
{
    public class ShipHangarDetector
    {
        private const float DetectRadius  = 150f;
        private const float ReleaseRadius = 300f;
        private const float PollInterval  = 1f;

        private Transform _shipAnchor;
        private float _shipAnchorLength;
        private float _pollTimer;

        public Transform ShipAnchor => _shipAnchor;
        public float ShipAnchorLength => _shipAnchorLength;

        public void Update(float dt, Vector3 camPos)
        {
            if (_shipAnchor != null && Vector3.Distance(camPos, _shipAnchor.position) > ReleaseRadius)
                _shipAnchor = null;

            if (_shipAnchor != null)
                return;

            _pollTimer -= dt;
            if (_pollTimer > 0f) return;
            _pollTimer = PollInterval;

            Collider[] hits = Physics.OverlapSphere(camPos, DetectRadius, Physics.DefaultRaycastLayers,
                                                     QueryTriggerInteraction.Ignore);

            float bestShipDist = float.MaxValue;
            Transform bestShipCenter = null;
            float bestShipLen = 0f;

            for (int i = 0; i < hits.Length; i++)
            {
                Ship ship = hits[i].GetComponentInParent<Ship>();
                if (ship == null) continue;

                Airbase ab = ship.GetAirbase();
                if (ab == null || ab.center == null) continue;

                float d = Vector3.Distance(camPos, ab.center.position);
                if (d < bestShipDist)
                {
                    bestShipDist = d;
                    bestShipCenter = ab.center;
                    bestShipLen = ship.definition != null ? ship.definition.length : 0f;
                }
            }

            if (bestShipCenter != null) { _shipAnchor = bestShipCenter; _shipAnchorLength = bestShipLen; }
        }
    }
}
