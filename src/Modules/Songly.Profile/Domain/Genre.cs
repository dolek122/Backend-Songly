namespace Songly.Profile.Domain;

/// <summary>A music genre. The list is fixed — it comes with the schema (V2), not from users.</summary>
internal sealed class Genre
{
    // For EF Core.
    private Genre() => Name = string.Empty;

    public Guid Id { get; private set; }

    public string Name { get; private set; }
}
