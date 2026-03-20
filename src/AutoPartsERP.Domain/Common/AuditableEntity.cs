namespace AutoPartsERP.Domain.Common;

public abstract class AuditableEntity
{
    protected AuditableEntity(Guid id)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    protected void Touch()
    {
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
