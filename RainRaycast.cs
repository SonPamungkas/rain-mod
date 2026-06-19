using UnityEngine;

namespace NuclearRain
{
    public static class RainRaycast
    {
        public static void TryFindGround(Vector3 origin, float probeDown, out Vector3 land, out bool isWater)
        {
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeDown,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                isWater = IsWaterHit(hit);
                land = isWater
                    ? new Vector3(origin.x, Datum.LocalSeaY + 0.05f, origin.z)
                    : hit.point + Vector3.up * 0.05f;
            }
            else
            {
                land = new Vector3(origin.x, Datum.LocalSeaY + 0.05f, origin.z);
                isWater = true;
            }
        }

        public static bool IsWaterHit(RaycastHit hit)
        {
            if (Mathf.Abs(hit.point.y - Datum.LocalSeaY) < 1f) return true;
            Transform t = hit.collider.transform;
            for (int i = 0; i < 3 && t != null; i++, t = t.parent)
            {
                if (t.name.IndexOf("terrain2_tile", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public static bool IsShadowed(Vector3 land, bool isWater, float probeUp)
        {
            if (isWater) return false;
            return Physics.Raycast(land, Vector3.up, probeUp,
                                   Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
