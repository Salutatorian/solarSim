using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SolarSim.Domain.Electrical;

namespace SolarSim.Application.Integrations.Pvlib;

public sealed class PvlibAvailability
{
    public bool PythonFound { get; init; }
    public string? PythonPath { get; init; }
    public bool ScriptFound { get; init; }
    public string? ScriptPath { get; init; }
    public bool PvlibImportOk { get; init; }
    public string Summary { get; init; } = "";
}

public sealed class PvlibBridgeResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public DetailedProductionEstimate? Estimate { get; init; }
    public string Engine { get; init; } = "";
}

/// <summary>
/// Optional Python/pvlib production bridge. Falls back gracefully when Python or pvlib is missing.
/// </summary>
public static class PvlibProductionBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static PvlibAvailability Probe(string? repoRootHint = null)
    {
        var python = FindPython();
        var script = FindScript(repoRootHint);
        var pvlibOk = false;
        if (python is not null)
        {
            try
            {
                var (exit, stdout, _) = RunProcess(python, "-c \"import pvlib; print(pvlib.__version__)\"", null, 15_000);
                pvlibOk = exit == 0 && !string.IsNullOrWhiteSpace(stdout);
            }
            catch
            {
                pvlibOk = false;
            }
        }

        string summary;
        if (python is null)
            summary = "Python not found. Install Python 3.10+ and ensure it is on PATH.";
        else if (script is null)
            summary = "pvlib_estimate.py not found next to the app or under Tools/.";
        else if (!pvlibOk)
            summary = "Python found, but pvlib is not installed. Run: pip install pvlib pandas numpy";
        else
            summary = $"Ready · {python} · pvlib OK";

        return new PvlibAvailability
        {
            PythonFound = python is not null,
            PythonPath = python,
            ScriptFound = script is not null,
            ScriptPath = script,
            PvlibImportOk = pvlibOk,
            Summary = summary,
        };
    }

    public static async Task<PvlibBridgeResult> EstimateAsync(
        double arrayKwDc,
        SiteDesignConditions site,
        string? repoRootHint = null,
        CancellationToken ct = default)
    {
        var availability = Probe(repoRootHint);
        if (!availability.PythonFound || availability.PythonPath is null)
            return new PvlibBridgeResult { Ok = false, Error = availability.Summary };
        if (!availability.ScriptFound || availability.ScriptPath is null)
            return new PvlibBridgeResult { Ok = false, Error = availability.Summary };
        if (site.LatitudeDegrees is not double lat || site.LongitudeDegrees is not double lon)
            return new PvlibBridgeResult
            {
                Ok = false,
                Error = "Set site latitude/longitude (or import Google Solar / climate preset) before running pvlib.",
            };
        if (arrayKwDc <= 0)
            return new PvlibBridgeResult { Ok = false, Error = "Place modules first (array kW is 0)." };

        var request = new Dictionary<string, object?>
        {
            ["latitude"] = lat,
            ["longitude"] = lon,
            ["arrayKwDc"] = arrayKwDc,
            ["tiltDegrees"] = site.ArrayTiltDegrees,
            ["azimuthDegrees"] = site.ArrayAzimuthDegrees,
            ["derate"] = site.SystemDerateFactor,
            ["pmaxTempCoeffPercentPerC"] = SiteDesignConditions.DefaultPmaxTempCoeffPercentPerC,
            ["clearskyScale"] = 0.55,
        };
        var json = JsonSerializer.Serialize(request);

        try
        {
            var args = $"\"{availability.ScriptPath}\"";
            var (exit, stdout, stderr) = await Task.Run(
                () => RunProcess(availability.PythonPath, args, json, 120_000),
                ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(stdout))
                return new PvlibBridgeResult
                {
                    Ok = false,
                    Error = string.IsNullOrWhiteSpace(stderr)
                        ? $"pvlib script exited {exit} with no output."
                        : stderr.Trim(),
                };

            var dto = JsonSerializer.Deserialize<PvlibResponseDto>(stdout, JsonOptions);
            if (dto is null)
                return new PvlibBridgeResult { Ok = false, Error = "Could not parse pvlib JSON response." };
            if (!dto.Ok)
                return new PvlibBridgeResult { Ok = false, Error = dto.Error ?? "pvlib estimate failed." };

            var months = (dto.Months ?? new List<PvlibMonthDto>())
                .OrderBy(m => m.Month)
                .Select(m => new MonthlyProductionRow
                {
                    Month = m.Month,
                    MonthName = string.IsNullOrWhiteSpace(m.MonthName)
                        ? CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m.Month)
                        : m.MonthName!,
                    EstimatedKwh = m.EstimatedKwh,
                })
                .ToList();

            var estimate = new DetailedProductionEstimate
            {
                ArrayKwDc = dto.ArrayKwDc,
                ArrayTiltDegrees = dto.TiltDegrees,
                ArrayAzimuthDegrees = dto.AzimuthDegrees,
                SystemDerateFactor = dto.Derate,
                LatitudeDegrees = dto.Latitude,
                EstimatedAnnualKwh = dto.EstimatedAnnualKwh,
                EstimatedDailyKwh = dto.EstimatedDailyKwh,
                Months = months,
                MethodNote = dto.MethodNote ?? "pvlib estimate",
            };

            return new PvlibBridgeResult
            {
                Ok = true,
                Estimate = estimate,
                Engine = dto.Engine ?? "pvlib",
            };
        }
        catch (Exception ex)
        {
            return new PvlibBridgeResult { Ok = false, Error = ex.Message };
        }
    }

    /// <summary>Parse a successful pvlib JSON payload (for unit tests without Python).</summary>
    public static DetailedProductionEstimate ParseSuccessPayload(string json)
    {
        var dto = JsonSerializer.Deserialize<PvlibResponseDto>(json, JsonOptions)
                  ?? throw new InvalidOperationException("Invalid JSON.");
        if (!dto.Ok)
            throw new InvalidOperationException(dto.Error ?? "not ok");
        return new DetailedProductionEstimate
        {
            ArrayKwDc = dto.ArrayKwDc,
            ArrayTiltDegrees = dto.TiltDegrees,
            ArrayAzimuthDegrees = dto.AzimuthDegrees,
            SystemDerateFactor = dto.Derate,
            LatitudeDegrees = dto.Latitude,
            EstimatedAnnualKwh = dto.EstimatedAnnualKwh,
            EstimatedDailyKwh = dto.EstimatedDailyKwh,
            Months = (dto.Months ?? new List<PvlibMonthDto>()).Select(m => new MonthlyProductionRow
            {
                Month = m.Month,
                MonthName = m.MonthName ?? m.Month.ToString(CultureInfo.InvariantCulture),
                EstimatedKwh = m.EstimatedKwh,
            }).ToList(),
            MethodNote = dto.MethodNote ?? "pvlib",
        };
    }

    public static string? FindScript(string? repoRootHint = null)
    {
        var candidates = new List<string>();
        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                candidates.Add(path);
        }

        Add(Path.Combine(AppContext.BaseDirectory, "Tools", "pvlib_estimate.py"));
        Add(Path.Combine(AppContext.BaseDirectory, "pvlib_estimate.py"));
        if (!string.IsNullOrWhiteSpace(repoRootHint))
            Add(Path.Combine(repoRootHint, "Tools", "pvlib_estimate.py"));

        // Walk up from base directory looking for Tools/pvlib_estimate.py (dev launches).
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
                Add(Path.Combine(dir.FullName, "Tools", "pvlib_estimate.py"));
        }
        catch
        {
            // ignore
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    public static string? FindPython()
    {
        foreach (var name in new[] { "python", "python3", "py" })
        {
            try
            {
                var args = name == "py" ? "-3 -c \"print(1)\"" : "-c \"print(1)\"";
                var (exit, stdout, _) = RunProcess(name, args, null, 8_000);
                if (exit == 0 && stdout.Contains('1'))
                    return name == "py" ? "py" : name;
            }
            catch
            {
                // try next
            }
        }
        return null;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName,
        string arguments,
        string? stdin,
        int timeoutMs)
    {
        // `py` launcher needs -3 prefix for the script path.
        if (fileName == "py" && !arguments.TrimStart().StartsWith("-3", StringComparison.Ordinal))
            arguments = "-3 " + arguments;

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardInput = stdin is not null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };
        proc.Start();
        if (stdin is not null)
        {
            proc.StandardInput.Write(stdin);
            proc.StandardInput.Close();
        }

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"Timed out running {fileName} {arguments}");
        }

        return (proc.ExitCode, stdout, stderr);
    }

    private sealed class PvlibResponseDto
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Engine { get; set; }
        public double ArrayKwDc { get; set; }
        public double TiltDegrees { get; set; }
        public double AzimuthDegrees { get; set; }
        public double Derate { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double EstimatedAnnualKwh { get; set; }
        public double EstimatedDailyKwh { get; set; }
        public string? MethodNote { get; set; }
        public List<PvlibMonthDto>? Months { get; set; }
    }

    private sealed class PvlibMonthDto
    {
        public int Month { get; set; }
        public string? MonthName { get; set; }
        public double EstimatedKwh { get; set; }
    }
}
