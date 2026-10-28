using System.Text.Json;

namespace Chubb.Contracts.Events;

public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
