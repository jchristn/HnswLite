namespace HnswLite.Test.Shared
{
    /// <summary>
    /// Storage backends selectable by shared test runner configuration.
    /// </summary>
    public enum TestStorageKind
    {
        Ram,
        Sqlite,
        Postgresql
    }
}
