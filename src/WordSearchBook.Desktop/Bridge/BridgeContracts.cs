using WordSearchBook.Core.Application;

namespace WordSearchBook.Desktop.Bridge;

internal sealed record BridgeRequest(string? Id, string? Type);

internal sealed record BridgeError(string Code, string Message);

internal sealed record BridgeResponse(
    string? Id,
    string Type,
    bool Ok,
    ApplicationInfo? Data = null,
    BridgeError? Error = null);
