namespace NauAssist.Backend.Features.AutonomousAgent.Sources.Matrix;

public sealed record MatrixRoomInfo(string RoomId, string? DisplayName);

public sealed record MatrixMessage(
    string RoomId,
    string EventId,
    string Sender,
    string Body,
    DateTimeOffset Timestamp);

public sealed record MatrixSyncResult(
    string NextBatch,
    IReadOnlyList<MatrixMessage> Messages);
