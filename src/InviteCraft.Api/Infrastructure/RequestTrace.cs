using System.Diagnostics;

namespace InviteCraft.Api.Infrastructure;

internal static class RequestTrace
{
    /// <summary>W3C trace id when tracing is active, otherwise ASP.NET Core's request id. Matches Serilog's RequestId/TraceId.</summary>
    public static string For(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;
}

internal static class FieldName
{
    /// <summary>Converts a .NET property path ("Venue.AddressLine1") to the JSON path the client sent ("venue.addressLine1").</summary>
    public static string? ToCamelCase(string? propertyPath)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
            return null;

        var segments = propertyPath.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var s = segments[i];
            if (s.Length > 0 && char.IsUpper(s[0]))
                segments[i] = char.ToLowerInvariant(s[0]) + s[1..];
        }

        return string.Join('.', segments);
    }
}
