namespace RiftReview.Core.Configuration;

public sealed class RiotOptions
{
    public string ApiKey { get; set; } = "";
    public string RiotId { get; set; } = "";   // GameName#TAG
    public string Platform { get; set; } = "na1";
}
