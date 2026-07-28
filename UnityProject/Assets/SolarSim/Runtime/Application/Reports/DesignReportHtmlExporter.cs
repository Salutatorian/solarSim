using System.Globalization;
using System.Net;
using System.Text;
using SolarSim.Domain.Electrical;

namespace SolarSim.Application.Reports;

/// <summary>
/// Writes a printable HTML design report (one-line + array layout SVG + BOM).
/// Open in a browser → Print → Save as PDF.
/// </summary>
public static class DesignReportHtmlExporter
{
    public static string ToHtml(DesignReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"/>");
        sb.AppendLine($"<title>{Escape(report.ProjectName)} — solarSim Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("""
            :root { color-scheme: light; }
            body { font-family: "Segoe UI", system-ui, sans-serif; margin: 24px; color: #1f1f1f; }
            h1 { font-size: 22px; margin: 0 0 4px; }
            h2 { font-size: 15px; margin: 28px 0 8px; border-bottom: 1px solid #ddd; padding-bottom: 4px; }
            .meta { color: #5c5c5c; font-size: 12px; margin-bottom: 20px; }
            .badge { display: inline-block; background: #eef3ff; color: #2f6fed; padding: 2px 8px; border-radius: 4px; font-size: 12px; margin-right: 6px; }
            pre { background: #f7f7f5; border: 1px solid #e2e2de; padding: 12px; font-size: 12px; overflow: auto; white-space: pre-wrap; }
            table { border-collapse: collapse; width: 100%; font-size: 12px; }
            th, td { border: 1px solid #e2e2de; padding: 6px 8px; text-align: left; }
            th { background: #fafaf8; }
            .layout { border: 1px solid #e2e2de; background: #fff; padding: 8px; overflow: auto; }
            .disclaimer { color: #b54708; font-size: 12px; margin-top: 28px; }
            @media print {
              body { margin: 12mm; }
              .no-print { display: none; }
              h2 { break-after: avoid; }
              .layout, pre, table { break-inside: avoid; }
            }
            """);
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h1>{Escape(report.ProjectName)}</h1>");
        sb.AppendLine("<div class=\"meta\">");
        sb.AppendLine($"solarSim design report · generated {report.GeneratedUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine("</div>");
        sb.AppendLine("<p class=\"no-print\"><em>Tip: Ctrl+P → Save as PDF</em></p>");

        sb.AppendLine("<p>");
        sb.AppendLine($"<span class=\"badge\">{report.PanelCount} modules</span>");
        sb.AppendLine($"<span class=\"badge\">{report.TotalDcWatts:0.#} W DC</span>");
        sb.AppendLine($"<span class=\"badge\">{report.StringCount} strings</span>");
        sb.AppendLine($"<span class=\"badge\">{Escape(report.LocationName)}</span>");
        sb.AppendLine($"<span class=\"badge\">Cold Voc {report.MinAmbientCelsius:0.#} °C</span>");
        sb.AppendLine($"<span class=\"badge\">Hot cell {report.HotCellCelsius:0.#} °C</span>");
        sb.AppendLine($"<span class=\"badge\">~{report.EstimatedAnnualKwh:0} kWh/yr</span>");
        sb.AppendLine("</p>");

        sb.AppendLine("<h2>0. Site assumptions</h2>");
        sb.AppendLine("<pre>");
        sb.AppendLine($"Location: {Escape(report.LocationName)}");
        if (report.LatitudeDegrees is double lat && report.LongitudeDegrees is double lon)
            sb.AppendLine($"Lat/Lon: {lat:0.###}, {lon:0.###}");
        sb.AppendLine($"Cold Voc ambient: {report.MinAmbientCelsius:0.#} °C");
        sb.AppendLine($"Hot cell: {report.HotCellCelsius:0.#} °C");
        sb.AppendLine($"Peak sun hours: {report.PeakSunHoursPerDay:0.#} h/day");
        sb.AppendLine($"System derate: {report.SystemDerateFactor:0.##}");
        sb.AppendLine($"Array tilt / az: {report.ArrayTiltDegrees:0.#}° / {report.ArrayAzimuthDegrees:0.#}°");
        sb.AppendLine($"Est. energy: ~{report.EstimatedDailyKwh:0.##} kWh/day · ~{report.EstimatedAnnualKwh:0} kWh/year");
        sb.AppendLine(Escape(report.ProductionMethodNote));
        sb.AppendLine("</pre>");

        if (report.MonthlyProduction.Count > 0)
        {
            sb.AppendLine("<h2>0b. Monthly production (est.)</h2>");
            sb.AppendLine("<table><thead><tr>");
            foreach (var m in report.MonthlyProduction)
                sb.AppendLine($"<th>{Escape(m.MonthName)}</th>");
            sb.AppendLine("</tr></thead><tbody><tr>");
            foreach (var m in report.MonthlyProduction)
                sb.AppendLine($"<td>{m.EstimatedKwh:0}</td>");
            sb.AppendLine("</tr></tbody></table>");
        }

        sb.AppendLine("<h2>1. Single-line summary</h2>");
        sb.AppendLine($"<pre>{Escape(report.SingleLineText)}</pre>");

        sb.AppendLine("<h2>2. Array layout</h2>");
        sb.AppendLine("<div class=\"layout\">");
        sb.AppendLine(BuildSvg(report));
        sb.AppendLine("</div>");

        sb.AppendLine("<h2>3. Module schedule</h2>");
        sb.AppendLine("<table><thead><tr>");
        sb.AppendLine("<th>#</th><th>Module</th><th>String</th><th>X (mm)</th><th>Y (mm)</th><th>W×H (mm)</th><th>Rot</th>");
        sb.AppendLine("</tr></thead><tbody>");
        foreach (var m in report.Modules)
        {
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{m.Index}</td>");
            sb.AppendLine($"<td>{Escape(m.Name)}</td>");
            sb.AppendLine($"<td>{Escape(m.StringName)}</td>");
            sb.AppendLine($"<td>{F(m.XMm)}</td><td>{F(m.YMm)}</td>");
            sb.AppendLine($"<td>{F(m.WidthMm)} × {F(m.HeightMm)}</td>");
            sb.AppendLine($"<td>{m.RotationDegrees}°</td>");
            sb.AppendLine("</tr>");
        }
        if (report.Modules.Count == 0)
            sb.AppendLine("<tr><td colspan=\"7\">No modules placed.</td></tr>");
        sb.AppendLine("</tbody></table>");

        if (report.Racking is { RailCount: > 0 } rack)
        {
            sb.AppendLine("<h2>4. Racking estimate</h2>");
            sb.AppendLine("<pre>");
            sb.AppendLine($"Rows: {rack.RowCount}");
            sb.AppendLine($"Rails: {rack.RailCount}  ·  Total rail {rack.TotalRailLengthMm / 1000.0:0.###} m");
            sb.AppendLine($"Attachments: {rack.AttachmentCount}");
            sb.AppendLine($"End clamps: {rack.EndClampCount}  ·  Mid clamps: {rack.MidClampCount}");
            sb.AppendLine("Design aid only — not structural engineering.");
            sb.AppendLine("</pre>");
        }

        sb.AppendLine("<h2>5. BOM / wire schedule</h2>");
        sb.AppendLine($"<pre>{Escape(report.BomText)}</pre>");

        if (report.Warnings.Count > 0)
        {
            sb.AppendLine("<h2>6. Warnings / issues</h2>");
            sb.AppendLine("<pre>");
            foreach (var w in report.Warnings)
                sb.AppendLine(Escape(w));
            sb.AppendLine("</pre>");
        }

        sb.AppendLine("<p class=\"disclaimer\">Design aid only — not for permit approval. Verify with a licensed electrician / structural engineer.</p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    public static string WriteToFile(DesignReport report, string path)
    {
        var html = ToHtml(report);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, html, Encoding.UTF8);
        return path;
    }

    private static string BuildSvg(DesignReport report)
    {
        if (report.Modules.Count == 0)
            return "<p style=\"color:#5c5c5c\">Place modules to generate an array layout sheet.</p>";

        var minX = report.Modules.Min(m => m.XMm);
        var minY = report.Modules.Min(m => m.YMm);
        var maxX = report.Modules.Max(m => m.XMm + m.WidthMm);
        var maxY = report.Modules.Max(m => m.YMm + m.HeightMm);
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);
        const double pad = 200;
        var vbW = width + pad * 2;
        var vbH = height + pad * 2;
        var svgW = 720;
        var svgH = Math.Clamp(svgW * vbH / vbW, 220, 900);

        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(svgW)}\" height=\"{F(svgH)}\" viewBox=\"0 0 {F(vbW)} {F(vbH)}\">");
        sb.AppendLine($"<rect x=\"0\" y=\"0\" width=\"{F(vbW)}\" height=\"{F(vbH)}\" fill=\"#fafaf8\"/>");

        foreach (var m in report.Modules)
        {
            var x = m.XMm - minX + pad;
            var y = m.YMm - minY + pad;
            sb.AppendLine(
                $"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(m.WidthMm)}\" height=\"{F(m.HeightMm)}\" " +
                "fill=\"#2a3a52\" stroke=\"#1a2433\" stroke-width=\"20\" rx=\"30\"/>");
            var lx = x + m.WidthMm / 2;
            var ly = y + m.HeightMm / 2;
            var font = Math.Clamp(Math.Min(m.WidthMm, m.HeightMm) * 0.18, 80, 220);
            sb.AppendLine(
                $"<text x=\"{F(lx)}\" y=\"{F(ly)}\" text-anchor=\"middle\" dominant-baseline=\"middle\" " +
                $"fill=\"#ffffff\" font-size=\"{F(font)}\" font-family=\"Segoe UI, sans-serif\">{m.Index}</text>");
        }

        sb.AppendLine("</svg>");
        sb.AppendLine("<p style=\"font-size:11px;color:#5c5c5c;margin:6px 0 0\">Plan view · numbers match module schedule · not to survey grade</p>");
        return sb.ToString();
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);

    private static string F(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
