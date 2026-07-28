namespace SolarSim.Domain.Geo;

/// <summary>
/// Local east-north projection in millimeters around an origin lat/lon (equirectangular).
/// Design aid — accurate enough for small residential footprints, not surveying.
/// </summary>
public sealed class LocalTangentProjection
{
    private const double EarthRadiusM = 6378137.0;
    private const double DegToRad = Math.PI / 180.0;

    public double OriginLatitudeDegrees { get; }
    public double OriginLongitudeDegrees { get; }

    public LocalTangentProjection(double originLatitudeDegrees, double originLongitudeDegrees)
    {
        OriginLatitudeDegrees = originLatitudeDegrees;
        OriginLongitudeDegrees = originLongitudeDegrees;
    }

    public (double EastMm, double NorthMm) ToLocalMm(double latitudeDegrees, double longitudeDegrees)
    {
        var lat0 = OriginLatitudeDegrees * DegToRad;
        var dLat = (latitudeDegrees - OriginLatitudeDegrees) * DegToRad;
        var dLon = (longitudeDegrees - OriginLongitudeDegrees) * DegToRad;
        var northM = dLat * EarthRadiusM;
        var eastM = dLon * EarthRadiusM * Math.Cos(lat0);
        return (eastM * 1000.0, northM * 1000.0);
    }
}
