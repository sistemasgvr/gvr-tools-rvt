using System.Text.Json;

namespace GvrLicense.Domain.Audit;

public static class AuditActionDescriber
{
    public static string Describe(string action, string? detailsJson = null)
    {
        if (string.Equals(action, "license_status_changed", StringComparison.Ordinal))
        {
            var (from, to) = TryParseStatusChange(detailsJson);
            if (to is not null)
            {
                return to switch
                {
                    "Active" => from == "Suspended" ? "Licencia reactivada" : "Licencia activada",
                    "Suspended" => "Licencia suspendida",
                    "Expired" => "Licencia vencida",
                    _ => $"Estado → {StatusLabel(to)}"
                };
            }

            return "Cambio de estado de licencia";
        }

        return action switch
        {
            "license.create" => "Licencia creada",
            "license.suspend" => "Licencia suspendida",
            "license.activate" => "Licencia reactivada",
            "license.expire" => "Licencia vencida",
            "device.kick" => "PC liberado",
            "device.deactivate" => "Desactivación en cliente",
            _ => action.Replace(".", " ", StringComparison.Ordinal)
        };
    }

    private static (string? From, string? To) TryParseStatusChange(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(detailsJson);
            var root = doc.RootElement;
            var from = root.TryGetProperty("from", out var fromEl) ? fromEl.GetString() : null;
            var to = root.TryGetProperty("to", out var toEl) ? toEl.GetString() : null;
            return (from, to);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string StatusLabel(string status) => status switch
    {
        "Active" => "activa",
        "Suspended" => "suspendida",
        "Expired" => "vencida",
        _ => status.ToLowerInvariant()
    };
}
