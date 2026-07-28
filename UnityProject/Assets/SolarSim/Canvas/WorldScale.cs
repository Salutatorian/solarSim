using SolarSim.Domain.Equipment;
using UnityEngine;

namespace SolarSim.Unity.Canvas
{
    /// <summary>
    /// World scale helpers. Internal equipment dimensions are millimeters.
    /// Unity world unit is meters (1 unit = 1000 mm).
    /// </summary>
    public static class WorldScale
    {
        public const float MmPerMeter = 1000f;

        public static Vector3 MmToWorld(double xMm, double yMm, float z = 0f) =>
            new((float)(xMm / MmPerMeter), (float)(yMm / MmPerMeter), z);

        public static Vector2 WorldToMm(Vector3 world) =>
            new(world.x * MmPerMeter, world.y * MmPerMeter);

        public static Vector2 PanelSizeMeters(SolarPanelDefinition definition, int rotationDegrees)
        {
            var widthM = (float)(definition.WidthMm / MmPerMeter);
            var heightM = (float)(definition.HeightMm / MmPerMeter);
            var rotated = ((rotationDegrees % 180) + 180) % 180;
            return rotated is 90 ? new Vector2(heightM, widthM) : new Vector2(widthM, heightM);
        }
    }
}
