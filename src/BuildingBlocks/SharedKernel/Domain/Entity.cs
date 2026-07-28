namespace SharedKernel.Domain;

public abstract class Entity<TId>
{
    public TId Id { get; protected set; }

    protected Entity(TId id)
    {
        Id = id;
    }

#pragma warning disable CS8618 
    protected Entity() { }
#pragma warning restore CS8618
}
