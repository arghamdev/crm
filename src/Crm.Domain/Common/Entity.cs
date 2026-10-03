namespace Crm.Domain.Common;

public abstract class Entity(Guid id)
{
    public Guid Id { get; init; } = id;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; protected set; } = DateTimeOffset.UtcNow;
    public long Version { get; protected set; } = 1;

    protected void Touch()
    {
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        Version++;
    }
}
