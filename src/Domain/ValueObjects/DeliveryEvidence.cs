namespace delivery_schedule_management.Domain.ValueObjects;

public sealed record DeliveryEvidence
{
    public DateTimeOffset RecordedAt { get; }

    public string? PhotoUrl { get; }

    public string? SignatureUrl { get; }

    public DeliveryEvidence(DateTimeOffset recordedAt, string? photoUrl = null, string? signatureUrl = null)
    {
        RecordedAt = recordedAt;
        PhotoUrl = photoUrl;
        SignatureUrl = signatureUrl;
    }
}
