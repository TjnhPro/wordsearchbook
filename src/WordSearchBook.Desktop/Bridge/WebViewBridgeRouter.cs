using System.Text.Json;
using System.Text.Json.Serialization;
using WordSearchBook.Core.Application;

namespace WordSearchBook.Desktop.Bridge;

public sealed class WebViewBridgeRouter(IApplicationInfoProvider applicationInfoProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal string Handle(string? rawMessage)
    {
        BridgeRequest? request;
        try
        {
            request = string.IsNullOrWhiteSpace(rawMessage)
                ? null
                : JsonSerializer.Deserialize<BridgeRequest>(rawMessage, JsonOptions);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Type))
        {
            return Serialize(Failure(request?.Id, "malformed_message", "The bridge message must contain non-empty id and type fields."));
        }

        if (!string.Equals(request.Type, "ping", StringComparison.Ordinal))
        {
            return Serialize(Failure(request.Id, "unsupported_message", $"Message type '{request.Type}' is not supported."));
        }

        return Serialize(new BridgeResponse(
            Id: request.Id,
            Type: "pong",
            Ok: true,
            Data: applicationInfoProvider.GetCurrent()));
    }

    private static BridgeResponse Failure(string? id, string code, string message) =>
        new(id, "error", false, Error: new BridgeError(code, message));

    private static string Serialize(BridgeResponse response) =>
        JsonSerializer.Serialize(response, JsonOptions);
}
