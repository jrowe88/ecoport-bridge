namespace EcoPort.Cloud.Contracts;

/// <summary>200 response to <c>POST /device/v1/records</c>.</summary>
public sealed record UploadResponse(uint AckSeq, DateTimeOffset ServerTime, bool CommandsPending);

/// <summary>400 response body.</summary>
public sealed record ErrorResponse(string Error);
