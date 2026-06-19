namespace RiftReview.Core.Sync;

public sealed record SyncProgress(int Fetched, int Total, string? Message);
public sealed record SyncResult(int NewMatches, int Skipped, string? Error);
