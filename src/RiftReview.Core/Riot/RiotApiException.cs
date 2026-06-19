namespace RiftReview.Core.Riot;

public sealed class RiotApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public bool IsKeyProblem => StatusCode is 401 or 403;
    public bool IsRateLimited => StatusCode == 429;
    public bool IsNotFound => StatusCode == 404;
}
