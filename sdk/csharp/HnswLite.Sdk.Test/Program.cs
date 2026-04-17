namespace HnswLite.Sdk.Test
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using HnswLite.Sdk;
    using HnswLite.Sdk.Models;

    internal class Program
    {
        #region Private-Members

        private static string _BaseUrl = "http://localhost:8080";
        private static string _ApiKey = "default";
        private static int _PassCount = 0;
        private static int _FailCount = 0;

        #endregion

        #region Public-Methods

        static async Task<int> Main(string[] args)
        {
            if (args.Length >= 1) _BaseUrl = args[0];
            if (args.Length >= 2) _ApiKey = args[1];

            Console.WriteLine("HnswLite SDK Test Harness");
            Console.WriteLine("  Base URL : " + _BaseUrl);
            Console.WriteLine("  API Key  : " + _ApiKey);
            Console.WriteLine();

            using HnswLiteClient client = new HnswLiteClient(_BaseUrl, _ApiKey);

            string indexName = "sdk-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            int dimension = 4;
            Guid vectorGuid1 = Guid.Empty;
            Guid vectorGuid2 = Guid.Empty;
            Guid vectorGuid3 = Guid.Empty;

            // 1. Ping
            await RunTestAsync("GET / (Ping)", async () =>
            {
                bool ok = await client.PingAsync();
                if (!ok) throw new Exception("Ping returned false");
            });

            // 2. Head ping
            await RunTestAsync("HEAD / (HeadPing)", async () =>
            {
                bool ok = await client.HeadPingAsync();
                if (!ok) throw new Exception("HeadPing returned false");
            });

            // 3. Create index
            await RunTestAsync("POST /v1.0/indexes (CreateIndex)", async () =>
            {
                IndexResponse idx = await client.CreateIndexAsync(new CreateIndexRequest
                {
                    Name = indexName,
                    Dimension = dimension,
                    StorageType = "RAM",
                    DistanceFunction = "Cosine",
                    M = 16,
                    MaxM = 32,
                    EfConstruction = 200
                });

                if (idx.Name != indexName)
                    throw new Exception("Name mismatch: expected " + indexName + ", got " + idx.Name);
                if (idx.Dimension != dimension)
                    throw new Exception("Dimension mismatch");
            });

            // 4. Get index
            await RunTestAsync("GET /v1.0/indexes/{name} (GetIndex)", async () =>
            {
                IndexResponse idx = await client.GetIndexAsync(indexName);

                if (idx.Name != indexName)
                    throw new Exception("Name mismatch");
                if (idx.VectorCount != 0)
                    throw new Exception("Expected 0 vectors in fresh index");
            });

            // 5. Enumerate indexes
            await RunTestAsync("GET /v1.0/indexes (EnumerateIndexes)", async () =>
            {
                EnumerationResult<IndexResponse> result = await client.EnumerateIndexesAsync(new EnumerationQuery
                {
                    MaxResults = 100,
                    Skip = 0,
                    Ordering = EnumerationOrderEnum.CreatedDescending
                });

                if (result.Objects == null || result.Objects.Count == 0)
                    throw new Exception("Expected at least one index");

                bool found = false;
                foreach (IndexResponse idx in result.Objects)
                {
                    if (idx.Name == indexName)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    throw new Exception("Created index not found in enumeration");
            });

            // 6. Add single vector
            await RunTestAsync("POST /v1.0/indexes/{name}/vectors (AddVector)", async () =>
            {
                AddVectorRequest addResult = await client.AddVectorAsync(indexName, new AddVectorRequest
                {
                    Vector = new List<float> { 1.0f, 0.0f, 0.0f, 0.0f }
                });

                if (addResult.GUID == null || addResult.GUID == Guid.Empty)
                    throw new Exception("Expected a GUID in the response");

                vectorGuid1 = addResult.GUID.Value;
            });

            // 7. Add batch vectors
            await RunTestAsync("POST /v1.0/indexes/{name}/vectors/batch (AddVectors)", async () =>
            {
                AddVectorsRequest batchResult = await client.AddVectorsAsync(indexName, new AddVectorsRequest
                {
                    Vectors = new List<AddVectorRequest>
                    {
                        new AddVectorRequest { Vector = new List<float> { 0.0f, 1.0f, 0.0f, 0.0f } },
                        new AddVectorRequest { Vector = new List<float> { 0.0f, 0.0f, 1.0f, 0.0f } }
                    }
                });

                if (batchResult.Vectors == null || batchResult.Vectors.Count != 2)
                    throw new Exception("Expected 2 vectors in batch response");

                vectorGuid2 = batchResult.Vectors[0].GUID ?? Guid.Empty;
                vectorGuid3 = batchResult.Vectors[1].GUID ?? Guid.Empty;
            });

            // 8. Search
            await RunTestAsync("POST /v1.0/indexes/{name}/search (Search)", async () =>
            {
                SearchResponse searchResult = await client.SearchAsync(indexName, new SearchRequest
                {
                    Vector = new List<float> { 1.0f, 0.1f, 0.0f, 0.0f },
                    K = 3
                });

                if (searchResult.Results == null || searchResult.Results.Count == 0)
                    throw new Exception("Expected search results");
                if (searchResult.SearchTimeMs < 0)
                    throw new Exception("Invalid search time");

                Console.WriteLine("    Search returned " + searchResult.Results.Count + " results in " + searchResult.SearchTimeMs + " ms");
            });

            // 9. Enumerate vectors without vector values
            await RunTestAsync("GET /v1.0/indexes/{name}/vectors (EnumerateVectors, includeVectors=false)", async () =>
            {
                EnumerationResult<VectorEntryResponse> result = await client.EnumerateVectorsAsync(
                    indexName,
                    new EnumerationQuery { MaxResults = 10 },
                    includeVectors: false);

                if (result.TotalRecords < 3)
                    throw new Exception("Expected TotalRecords >= 3, got " + result.TotalRecords);
                if (result.Objects == null || result.Objects.Count == 0)
                    throw new Exception("Expected at least one vector object");

                foreach (VectorEntryResponse v in result.Objects)
                {
                    if (v.Vector != null)
                        throw new Exception("Expected Vector == null when includeVectors=false");
                }
            });

            // 10. Enumerate vectors with vector values, paged to 1
            await RunTestAsync("GET /v1.0/indexes/{name}/vectors (EnumerateVectors, includeVectors=true, MaxResults=1)", async () =>
            {
                EnumerationResult<VectorEntryResponse> result = await client.EnumerateVectorsAsync(
                    indexName,
                    new EnumerationQuery { MaxResults = 1 },
                    includeVectors: true);

                if (result.Objects == null || result.Objects.Count != 1)
                    throw new Exception("Expected exactly 1 object, got " + (result.Objects == null ? 0 : result.Objects.Count));

                VectorEntryResponse entry = result.Objects[0];
                if (entry.Vector == null)
                    throw new Exception("Expected Vector != null when includeVectors=true");
                if (entry.Vector.Count != dimension)
                    throw new Exception("Expected Vector length " + dimension + ", got " + entry.Vector.Count);
            });

            // 11. Get single vector
            await RunTestAsync("GET /v1.0/indexes/{name}/vectors/{guid} (GetVector)", async () =>
            {
                VectorEntryResponse entry = await client.GetVectorAsync(indexName, vectorGuid1);

                if (entry.GUID != vectorGuid1)
                    throw new Exception("Expected GUID " + vectorGuid1 + ", got " + entry.GUID);
                if (entry.Vector == null)
                    throw new Exception("Expected Vector != null on single-vector GET");
                if (entry.Vector.Count != dimension)
                    throw new Exception("Expected Vector length " + dimension + ", got " + entry.Vector.Count);
            });

            // 11a. Add vectors with Labels/Tags for filter tests
            Guid filterGuidA = Guid.Empty;
            Guid filterGuidB = Guid.Empty;
            Guid filterGuidC = Guid.Empty;
            await RunTestAsync("POST /v1.0/indexes/{name}/vectors (AddVector with Labels/Tags)", async () =>
            {
                AddVectorRequest a = await client.AddVectorAsync(indexName, new AddVectorRequest
                {
                    Vector = new List<float> { 0.5f, 0.5f, 0f, 0f },
                    Name = "filter-a",
                    Labels = new List<string> { "red", "small" },
                    Tags = new Dictionary<string, object> { { "env", "prod" }, { "owner", "alice" } }
                });
                filterGuidA = a.GUID ?? Guid.Empty;

                AddVectorRequest b = await client.AddVectorAsync(indexName, new AddVectorRequest
                {
                    Vector = new List<float> { 0.4f, 0.4f, 0.1f, 0f },
                    Name = "filter-b",
                    Labels = new List<string> { "red", "big" },
                    Tags = new Dictionary<string, object> { { "env", "prod" }, { "owner", "bob" } }
                });
                filterGuidB = b.GUID ?? Guid.Empty;

                AddVectorRequest c = await client.AddVectorAsync(indexName, new AddVectorRequest
                {
                    Vector = new List<float> { 0.3f, 0.3f, 0.2f, 0f },
                    Name = "filter-c",
                    Labels = new List<string> { "blue", "small" },
                    Tags = new Dictionary<string, object> { { "env", "dev" }, { "owner", "alice" } }
                });
                filterGuidC = c.GUID ?? Guid.Empty;

                if (filterGuidA == Guid.Empty || filterGuidB == Guid.Empty || filterGuidC == Guid.Empty)
                    throw new Exception("Expected non-empty GUIDs for metadata-tagged vectors");
            });

            // 11b. Search with Labels filter (AND)
            await RunTestAsync("POST /v1.0/indexes/{name}/search (Labels filter, AND)", async () =>
            {
                SearchResponse r = await client.SearchAsync(indexName, new SearchRequest
                {
                    Vector = new List<float> { 0.5f, 0.5f, 0f, 0f },
                    K = 10,
                    Labels = new List<string> { "red", "small" }
                });

                // Only vector A has BOTH 'red' AND 'small'.
                if (r.Results.Count != 1)
                    throw new Exception("Expected 1 result for Labels=[red,small], got " + r.Results.Count);
                if (r.Results[0].GUID != filterGuidA)
                    throw new Exception("Expected GUID A, got " + r.Results[0].GUID);
                if (r.FilteredCount <= 0)
                    throw new Exception("Expected FilteredCount > 0 (got " + r.FilteredCount + ")");
                if (r.Results[0].Labels == null || r.Results[0].Labels!.Count != 2)
                    throw new Exception("Expected Labels populated on the result");
            });

            // 11c. Search with Tags filter (AND)
            await RunTestAsync("POST /v1.0/indexes/{name}/search (Tags filter, AND)", async () =>
            {
                SearchResponse r = await client.SearchAsync(indexName, new SearchRequest
                {
                    Vector = new List<float> { 0.5f, 0.5f, 0f, 0f },
                    K = 10,
                    Tags = new Dictionary<string, string> { { "env", "prod" }, { "owner", "alice" } }
                });

                // Only vector A has env=prod AND owner=alice.
                if (r.Results.Count != 1)
                    throw new Exception("Expected 1 result, got " + r.Results.Count);
                if (r.Results[0].GUID != filterGuidA)
                    throw new Exception("Expected GUID A");
            });

            // 11d. Search with CaseInsensitive=true
            await RunTestAsync("POST /v1.0/indexes/{name}/search (CaseInsensitive=true)", async () =>
            {
                SearchResponse miss = await client.SearchAsync(indexName, new SearchRequest
                {
                    Vector = new List<float> { 0.5f, 0.5f, 0f, 0f },
                    K = 10,
                    Labels = new List<string> { "RED" },
                    CaseInsensitive = false
                });
                if (miss.Results.Count != 0)
                    throw new Exception("Case-sensitive 'RED' should match nothing, got " + miss.Results.Count);

                SearchResponse hit = await client.SearchAsync(indexName, new SearchRequest
                {
                    Vector = new List<float> { 0.5f, 0.5f, 0f, 0f },
                    K = 10,
                    Labels = new List<string> { "RED" },
                    CaseInsensitive = true
                });
                // A and B both have 'red'.
                if (hit.Results.Count != 2)
                    throw new Exception("Case-insensitive 'RED' should match 2 vectors, got " + hit.Results.Count);
            });

            // 11e. Enumerate with Labels + CaseInsensitive query-string
            await RunTestAsync("GET /v1.0/indexes/{name}/vectors (Labels filter + CaseInsensitive)", async () =>
            {
                EnumerationResult<VectorEntryResponse> r = await client.EnumerateVectorsAsync(
                    indexName,
                    new EnumerationQuery
                    {
                        MaxResults = 100,
                        Labels = new List<string> { "RED" },
                        CaseInsensitive = true
                    },
                    includeVectors: false);

                if (r.TotalRecords != 2)
                    throw new Exception("Expected TotalRecords=2, got " + r.TotalRecords);
                if (r.FilteredCount <= 0)
                    throw new Exception("Expected FilteredCount > 0, got " + r.FilteredCount);
            });

            // 12. Remove vector
            await RunTestAsync("DELETE /v1.0/indexes/{name}/vectors/{guid} (RemoveVector)", async () =>
            {
                await client.RemoveVectorAsync(indexName, vectorGuid1);

                // Verify the index now has fewer vectors (started with 3, added 3, removed 1 => 5)
                IndexResponse idx = await client.GetIndexAsync(indexName);
                if (idx.VectorCount != 5)
                    throw new Exception("Expected 5 vectors after removal, got " + idx.VectorCount);
            });

            // 13. Delete index
            await RunTestAsync("DELETE /v1.0/indexes/{name} (DeleteIndex)", async () =>
            {
                await client.DeleteIndexAsync(indexName);

                // Verify it is gone
                try
                {
                    await client.GetIndexAsync(indexName);
                    throw new Exception("Expected 404 but index still exists");
                }
                catch (HnswLiteApiException ex) when ((int)ex.StatusCode == 404)
                {
                    // Expected
                }
            });

            // Summary
            Console.WriteLine();
            Console.WriteLine("===========================================");
            Console.WriteLine("  Passed: " + _PassCount);
            Console.WriteLine("  Failed: " + _FailCount);
            Console.WriteLine("===========================================");

            return _FailCount == 0 ? 0 : 1;
        }

        #endregion

        #region Private-Methods

        private static async Task RunTestAsync(string testName, Func<Task> testAction)
        {
            try
            {
                await testAction();
                _PassCount++;
                Console.WriteLine("  [PASS] " + testName);
            }
            catch (Exception ex)
            {
                _FailCount++;
                Console.WriteLine("  [FAIL] " + testName + " - " + ex.Message);
            }
        }

        #endregion
    }
}
