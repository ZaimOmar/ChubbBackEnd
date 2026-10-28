using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chubb.Claims.Api;

// One configuration for HTTP binding, responses and the stored idempotent bytes, so a replay equals the first answer.
public static class ClaimsJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
