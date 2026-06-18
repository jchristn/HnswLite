namespace HnswIndex.PostgresqlStorage
{
    using System;

    /// <summary>
    /// Server-facing PostgreSQL index metadata.
    /// </summary>
    public sealed record PostgresqlIndexMetadata(
        Guid Id,
        string Name,
        int Dimension,
        string DistanceFunction,
        int M,
        int MaxM,
        int EfConstruction,
        DateTime CreatedUtc,
        int VectorCount);
}
