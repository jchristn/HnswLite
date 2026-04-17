namespace HnswLite.Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Hnsw;
    using HnswIndex.Server.Classes;
    using HnswIndex.Server.Services;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the Labels/Tags metadata filter used by
    /// <see cref="IndexManager.SearchAsync"/> and <see cref="IndexManager.EnumerateVectorsAsync"/>.
    /// Exercises the <see cref="MetadataFilter"/> helper directly and end-to-end via
    /// <see cref="IndexManager"/> on the RAM backend. Also covers query-string parsing
    /// on <see cref="EnumerationQuery"/>.
    /// </summary>
    public static class MetadataFilterSuites
    {
        #region Public-Members

        /// <summary>
        /// All metadata-filter suites surfaced through the runner.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor>
                {
                    MetadataFilterHelperSuite(),
                    SearchFilterSuite(),
                    EnumerateFilterSuite(),
                    QueryStringParsingSuite(),
                };
            }
        }

        #endregion

        #region Private-Members

        private const int _Dimension = 2;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Direct unit tests for <see cref="MetadataFilter.Matches"/>. No storage involved.
        /// </summary>
        /// <returns>MetadataFilter helper test suite.</returns>
        public static TestSuiteDescriptor MetadataFilterHelperSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "MetadataFilter.Helper",
                displayName: "Metadata Filter - Helper",
                cases: new List<TestCaseDescriptor>
                {
                    Case("MetadataFilter.Helper", "NullFilterMatchesEverything",
                        "A null/empty filter always matches, even a null node",
                        ct =>
                        {
                            TestAssert.True(MetadataFilter.Matches(null, null, null, false),
                                "null node with null filter matches");
                            TestAssert.True(MetadataFilter.Matches(null, new List<string>(), new Dictionary<string, string>(), false),
                                "null node with empty filter matches");
                            TestAssert.True(MetadataFilter.Matches(
                                new FakeNode(labels: new List<string> { "a" }), null, null, false),
                                "real node with null filter matches");
                            return Task.CompletedTask;
                        }),

                    Case("MetadataFilter.Helper", "LabelsAndAllMatch",
                        "Labels use AND semantics — node must have every required label",
                        ct =>
                        {
                            FakeNode node = new FakeNode(labels: new List<string> { "red", "small", "square" });
                            TestAssert.True(MetadataFilter.Matches(node, new List<string> { "red" }, null, false),
                                "single required label present");
                            TestAssert.True(MetadataFilter.Matches(node, new List<string> { "red", "small" }, null, false),
                                "two required labels, both present");
                            TestAssert.False(MetadataFilter.Matches(node, new List<string> { "red", "large" }, null, false),
                                "one required label missing rejects the node");
                            TestAssert.False(MetadataFilter.Matches(new FakeNode(labels: null), new List<string> { "red" }, null, false),
                                "node with null Labels never matches a non-empty label filter");
                            return Task.CompletedTask;
                        }),

                    Case("MetadataFilter.Helper", "TagsAndAllMatch",
                        "Tags use AND semantics — every required key must match",
                        ct =>
                        {
                            FakeNode node = new FakeNode(tags: new Dictionary<string, object>
                            {
                                { "owner", "alice" },
                                { "env", "prod" },
                                { "count", 42L },
                            });
                            TestAssert.True(MetadataFilter.Matches(node, null, new Dictionary<string, string> { { "owner", "alice" } }, false),
                                "single tag equality");
                            TestAssert.True(MetadataFilter.Matches(node, null, new Dictionary<string, string>
                            {
                                { "owner", "alice" }, { "env", "prod" }
                            }, false), "two tags, both equal");
                            TestAssert.False(MetadataFilter.Matches(node, null, new Dictionary<string, string> { { "owner", "bob" } }, false),
                                "value mismatch rejects");
                            TestAssert.False(MetadataFilter.Matches(node, null, new Dictionary<string, string> { { "missing", "x" } }, false),
                                "missing key rejects");
                            return Task.CompletedTask;
                        }),

                    Case("MetadataFilter.Helper", "TagsNumericStringified",
                        "Non-string tag values are compared via Convert.ToString(InvariantCulture)",
                        ct =>
                        {
                            FakeNode node = new FakeNode(tags: new Dictionary<string, object>
                            {
                                { "count", 42L },
                                { "ratio", 1.5 },
                                { "active", true },
                            });
                            TestAssert.True(MetadataFilter.Matches(node, null, new Dictionary<string, string> { { "count", "42" } }, false),
                                "long 42 matches string \"42\"");
                            TestAssert.True(MetadataFilter.Matches(node, null, new Dictionary<string, string> { { "ratio", "1.5" } }, false),
                                "double 1.5 matches string \"1.5\" under invariant culture");
                            TestAssert.True(MetadataFilter.Matches(node, null, new Dictionary<string, string> { { "active", "True" } }, false),
                                "bool true stringifies to \"True\"");
                            return Task.CompletedTask;
                        }),

                    Case("MetadataFilter.Helper", "CaseInsensitiveLabelsAndTags",
                        "CaseInsensitive=true compares labels, tag keys, and tag values without regard to case",
                        ct =>
                        {
                            FakeNode node = new FakeNode(
                                labels: new List<string> { "Alpha", "Beta" },
                                tags: new Dictionary<string, object> { { "Owner", "Alice" } });

                            // Case sensitive: fails
                            TestAssert.False(MetadataFilter.Matches(node, new List<string> { "alpha" }, null, false),
                                "label case mismatch rejects when case-sensitive");
                            TestAssert.False(MetadataFilter.Matches(node, null, new Dictionary<string, string> { { "owner", "Alice" } }, false),
                                "tag key case mismatch rejects when case-sensitive");

                            // Case insensitive: matches
                            TestAssert.True(MetadataFilter.Matches(node, new List<string> { "alpha", "BETA" }, null, true),
                                "labels match when case-insensitive");
                            TestAssert.True(MetadataFilter.Matches(node, null, new Dictionary<string, string> { { "owner", "alice" } }, true),
                                "tag key+value match when case-insensitive");
                            return Task.CompletedTask;
                        }),

                    Case("MetadataFilter.Helper", "CombinedLabelsAndTagsAllMustPass",
                        "When both filters are set, both must pass (AND across filter kinds)",
                        ct =>
                        {
                            FakeNode node = new FakeNode(
                                labels: new List<string> { "red" },
                                tags: new Dictionary<string, object> { { "env", "prod" } });
                            TestAssert.True(MetadataFilter.Matches(node,
                                new List<string> { "red" },
                                new Dictionary<string, string> { { "env", "prod" } }, false),
                                "labels and tags both match");
                            TestAssert.False(MetadataFilter.Matches(node,
                                new List<string> { "red" },
                                new Dictionary<string, string> { { "env", "dev" } }, false),
                                "labels match but tags fail");
                            TestAssert.False(MetadataFilter.Matches(node,
                                new List<string> { "blue" },
                                new Dictionary<string, string> { { "env", "prod" } }, false),
                                "tags match but labels fail");
                            return Task.CompletedTask;
                        }),
                });
        }

        /// <summary>
        /// End-to-end search filtering via <see cref="IndexManager.SearchAsync"/> on RAM storage.
        /// </summary>
        /// <returns>Search-filter test suite.</returns>
        public static TestSuiteDescriptor SearchFilterSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "MetadataFilter.Search",
                displayName: "Metadata Filter - Search",
                cases: new List<TestCaseDescriptor>
                {
                    Case("MetadataFilter.Search", "LabelsAllOf",
                        "Search: Labels filter requires ALL labels to be present",
                        async ct =>
                        {
                            using ServerFixture f = await ServerFixture.CreateAsync("lbl-all", ct).ConfigureAwait(false);
                            await f.PopulateAsync(10, ct).ConfigureAwait(false);

                            // Only indices 0,2,4 have both "even" and "small".
                            SearchResponse r = await f.Manager.SearchAsync(f.IndexName, new SearchRequest
                            {
                                Vector = new List<float> { 0f, 0f },
                                K = 10,
                                Labels = new List<string> { "even", "small" }
                            }, ct).ConfigureAwait(false);

                            TestAssert.Equal(3, r.Results.Count, "Only 3 vectors have BOTH 'even' AND 'small'");
                            TestAssert.Equal(7, r.FilteredCount, "Remaining 7 of 10 candidates were filtered out");
                        }),

                    Case("MetadataFilter.Search", "TagsAllOf",
                        "Search: Tags filter uses AND across all key/value pairs",
                        async ct =>
                        {
                            using ServerFixture f = await ServerFixture.CreateAsync("tag-all", ct).ConfigureAwait(false);
                            await f.PopulateAsync(10, ct).ConfigureAwait(false);

                            SearchResponse r = await f.Manager.SearchAsync(f.IndexName, new SearchRequest
                            {
                                Vector = new List<float> { 0f, 0f },
                                K = 10,
                                Tags = new Dictionary<string, string> { { "parity", "even" }, { "size", "small" } }
                            }, ct).ConfigureAwait(false);

                            TestAssert.Equal(3, r.Results.Count, "AND on tags gives 3 matching");
                            TestAssert.Equal(7, r.FilteredCount, "7 candidates filtered out");
                        }),

                    Case("MetadataFilter.Search", "CombinedLabelsAndTags",
                        "Search: Labels AND Tags must both pass",
                        async ct =>
                        {
                            using ServerFixture f = await ServerFixture.CreateAsync("combo", ct).ConfigureAwait(false);
                            await f.PopulateAsync(10, ct).ConfigureAwait(false);

                            SearchResponse r = await f.Manager.SearchAsync(f.IndexName, new SearchRequest
                            {
                                Vector = new List<float> { 0f, 0f },
                                K = 10,
                                Labels = new List<string> { "even" },
                                Tags = new Dictionary<string, string> { { "size", "small" } }
                            }, ct).ConfigureAwait(false);

                            TestAssert.Equal(3, r.Results.Count, "even AND size=small gives 3");
                            TestAssert.Equal(7, r.FilteredCount, "7 filtered");
                        }),

                    Case("MetadataFilter.Search", "CaseInsensitive",
                        "Search: CaseInsensitive=true matches mixed-case labels and tag values",
                        async ct =>
                        {
                            using ServerFixture f = await ServerFixture.CreateAsync("ci", ct).ConfigureAwait(false);
                            await f.PopulateAsync(10, ct).ConfigureAwait(false);

                            // Case-sensitive miss.
                            SearchResponse miss = await f.Manager.SearchAsync(f.IndexName, new SearchRequest
                            {
                                Vector = new List<float> { 0f, 0f },
                                K = 10,
                                Labels = new List<string> { "EVEN" },
                                CaseInsensitive = false
                            }, ct).ConfigureAwait(false);
                            TestAssert.Equal(0, miss.Results.Count, "case-sensitive 'EVEN' does not match 'even'");
                            TestAssert.Equal(10, miss.FilteredCount, "all 10 candidates filtered");

                            // Case-insensitive hit.
                            SearchResponse hit = await f.Manager.SearchAsync(f.IndexName, new SearchRequest
                            {
                                Vector = new List<float> { 0f, 0f },
                                K = 10,
                                Labels = new List<string> { "EVEN" },
                                CaseInsensitive = true
                            }, ct).ConfigureAwait(false);
                            TestAssert.Equal(5, hit.Results.Count, "case-insensitive 'EVEN' matches 5 'even'");
                            TestAssert.Equal(5, hit.FilteredCount, "5 filtered");
                        }),

                    Case("MetadataFilter.Search", "ReturnsFewerThanKWhenRestrictive",
                        "Search: restrictive filter returns fewer than K results and FilteredCount sums to K",
                        async ct =>
                        {
                            using ServerFixture f = await ServerFixture.CreateAsync("restrictive", ct).ConfigureAwait(false);
                            await f.PopulateAsync(10, ct).ConfigureAwait(false);

                            SearchResponse r = await f.Manager.SearchAsync(f.IndexName, new SearchRequest
                            {
                                Vector = new List<float> { 0f, 0f },
                                K = 10,
                                Labels = new List<string> { "small" }
                            }, ct).ConfigureAwait(false);

                            TestAssert.Equal(5, r.Results.Count, "5 vectors have 'small'");
                            TestAssert.Equal(5, r.FilteredCount, "and 5 were filtered out");
                            TestAssert.Equal(10, r.Results.Count + r.FilteredCount,
                                "Results + FilteredCount equals the number of HNSW candidates");
                        }),

                    Case("MetadataFilter.Search", "NoMatchesReturnsEmpty",
                        "Search: no matches returns empty results with FilteredCount equal to candidate count",
                        async ct =>
                        {
                            using ServerFixture f = await ServerFixture.CreateAsync("nomatch", ct).ConfigureAwait(false);
                            await f.PopulateAsync(10, ct).ConfigureAwait(false);

                            SearchResponse r = await f.Manager.SearchAsync(f.IndexName, new SearchRequest
                            {
                                Vector = new List<float> { 0f, 0f },
                                K = 10,
                                Labels = new List<string> { "nonexistent-label" }
                            }, ct).ConfigureAwait(false);

                            TestAssert.Equal(0, r.Results.Count, "no results");
                            TestAssert.Equal(10, r.FilteredCount, "all 10 candidates filtered");
                        }),

                    Case("MetadataFilter.Search", "NoFilterLeavesFilteredCountZero",
                        "Search: with no filter, FilteredCount is always 0",
                        async ct =>
                        {
                            using ServerFixture f = await ServerFixture.CreateAsync("nofilter", ct).ConfigureAwait(false);
                            await f.PopulateAsync(10, ct).ConfigureAwait(false);

                            SearchResponse r = await f.Manager.SearchAsync(f.IndexName, new SearchRequest
                            {
                                Vector = new List<float> { 0f, 0f },
                                K = 10
                            }, ct).ConfigureAwait(false);

                            TestAssert.True(r.Results.Count > 0, "unfiltered search returns results");
                            TestAssert.Equal(0, r.FilteredCount, "FilteredCount is 0 when no filter is set");
                        }),
                });
        }

        /// <summary>
        /// End-to-end enumeration filtering via <see cref="IndexManager.EnumerateVectorsAsync"/>.
        /// </summary>
        /// <returns>Enumeration-filter test suite.</returns>
        public static TestSuiteDescriptor EnumerateFilterSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "MetadataFilter.Enumerate",
                displayName: "Metadata Filter - Enumerate",
                cases: new List<TestCaseDescriptor>
                {
                    Case("MetadataFilter.Enumerate", "LabelsAndPaginationCounts",
                        "Enumerate: label filter applied before pagination; TotalRecords/FilteredCount accurate",
                        async ct =>
                        {
                            using ServerFixture f = await ServerFixture.CreateAsync("enum-lbl", ct).ConfigureAwait(false);
                            await f.PopulateAsync(10, ct).ConfigureAwait(false);

                            EnumerationResult<VectorEntryResponse> r = await f.Manager.EnumerateVectorsAsync(
                                f.IndexName,
                                new EnumerationQuery
                                {
                                    MaxResults = 3,
                                    Labels = new List<string> { "even" }
                                },
                                includeVectors: false,
                                ct).ConfigureAwait(false);

                            TestAssert.Equal(5, r.TotalRecords, "5 vectors have 'even' label");
                            TestAssert.Equal(5, r.FilteredCount, "other 5 were filtered out");
                            TestAssert.Equal(3, r.Objects.Count, "page size respected");
                            TestAssert.Equal(2, r.RecordsRemaining, "two remain after first page");
                            TestAssert.False(r.EndOfResults, "not end of results");
                        }),

                    Case("MetadataFilter.Enumerate", "TagsFilter",
                        "Enumerate: tag filter reduces TotalRecords and populates FilteredCount",
                        async ct =>
                        {
                            using ServerFixture f = await ServerFixture.CreateAsync("enum-tag", ct).ConfigureAwait(false);
                            await f.PopulateAsync(10, ct).ConfigureAwait(false);

                            EnumerationResult<VectorEntryResponse> r = await f.Manager.EnumerateVectorsAsync(
                                f.IndexName,
                                new EnumerationQuery
                                {
                                    MaxResults = 100,
                                    Tags = new Dictionary<string, string> { { "parity", "odd" }, { "size", "big" } }
                                },
                                includeVectors: false,
                                ct).ConfigureAwait(false);

                            TestAssert.Equal(3, r.TotalRecords, "3 vectors are odd AND big (indices 5,7,9)");
                            TestAssert.Equal(7, r.FilteredCount, "7 filtered out");
                            TestAssert.Equal(3, r.Objects.Count, "all 3 fit on the page");
                            TestAssert.True(r.EndOfResults, "no more pages");
                        }),

                    Case("MetadataFilter.Enumerate", "CaseInsensitive",
                        "Enumerate: CaseInsensitive=true matches regardless of case",
                        async ct =>
                        {
                            using ServerFixture f = await ServerFixture.CreateAsync("enum-ci", ct).ConfigureAwait(false);
                            await f.PopulateAsync(10, ct).ConfigureAwait(false);

                            EnumerationResult<VectorEntryResponse> r = await f.Manager.EnumerateVectorsAsync(
                                f.IndexName,
                                new EnumerationQuery
                                {
                                    MaxResults = 100,
                                    Labels = new List<string> { "EVEN" },
                                    CaseInsensitive = true
                                },
                                includeVectors: false,
                                ct).ConfigureAwait(false);

                            TestAssert.Equal(5, r.TotalRecords, "case-insensitive match returns 5");
                            TestAssert.Equal(5, r.FilteredCount, "5 filtered out");
                        }),

                    Case("MetadataFilter.Enumerate", "NoFilterLeavesFilteredCountZero",
                        "Enumerate: with no metadata filter, FilteredCount is 0",
                        async ct =>
                        {
                            using ServerFixture f = await ServerFixture.CreateAsync("enum-nofilter", ct).ConfigureAwait(false);
                            await f.PopulateAsync(10, ct).ConfigureAwait(false);

                            EnumerationResult<VectorEntryResponse> r = await f.Manager.EnumerateVectorsAsync(
                                f.IndexName,
                                new EnumerationQuery { MaxResults = 100 },
                                includeVectors: false,
                                ct).ConfigureAwait(false);

                            TestAssert.Equal(10, r.TotalRecords, "all 10 returned");
                            TestAssert.Equal(0, r.FilteredCount, "no metadata filter ⇒ FilteredCount=0");
                        }),
                });
        }

        /// <summary>
        /// Query-string parsing tests for the new <see cref="EnumerationQuery"/> filter fields.
        /// </summary>
        /// <returns>Query-string parsing test suite.</returns>
        public static TestSuiteDescriptor QueryStringParsingSuite()
        {
            return new TestSuiteDescriptor(
                suiteId: "MetadataFilter.QueryString",
                displayName: "Metadata Filter - Query String Parsing",
                cases: new List<TestCaseDescriptor>
                {
                    Case("MetadataFilter.QueryString", "LabelsSplitOnComma",
                        "labels=a,b,c parses to three entries",
                        ct =>
                        {
                            NameValueCollection q = new NameValueCollection();
                            q["labels"] = "a,b,c";
                            EnumerationQuery eq = EnumerationQuery.FromQueryString(q);
                            TestAssert.Equal(3, eq.Labels!.Count, "three labels");
                            TestAssert.Equal("a", eq.Labels![0], "first");
                            TestAssert.Equal("c", eq.Labels![2], "last");
                            return Task.CompletedTask;
                        }),

                    Case("MetadataFilter.QueryString", "LabelsEmptyYieldsNull",
                        "labels= with no value leaves Labels null",
                        ct =>
                        {
                            NameValueCollection q = new NameValueCollection();
                            q["labels"] = "";
                            EnumerationQuery eq = EnumerationQuery.FromQueryString(q);
                            TestAssert.True(eq.Labels == null, "empty labels parses to null");
                            return Task.CompletedTask;
                        }),

                    Case("MetadataFilter.QueryString", "LabelsDropsEmptySegments",
                        "labels=a,,b drops empty segments",
                        ct =>
                        {
                            NameValueCollection q = new NameValueCollection();
                            q["labels"] = "a,,b";
                            EnumerationQuery eq = EnumerationQuery.FromQueryString(q);
                            TestAssert.Equal(2, eq.Labels!.Count, "two non-empty labels");
                            return Task.CompletedTask;
                        }),

                    Case("MetadataFilter.QueryString", "TagsParsed",
                        "tags=k1:v1,k2:v2 parses to a dictionary",
                        ct =>
                        {
                            NameValueCollection q = new NameValueCollection();
                            q["tags"] = "owner:alice,env:prod";
                            EnumerationQuery eq = EnumerationQuery.FromQueryString(q);
                            TestAssert.Equal(2, eq.Tags!.Count, "two tags");
                            TestAssert.Equal("alice", eq.Tags!["owner"], "owner value");
                            TestAssert.Equal("prod", eq.Tags!["env"], "env value");
                            return Task.CompletedTask;
                        }),

                    Case("MetadataFilter.QueryString", "TagsMissingColonThrows",
                        "tags=bad (no colon) throws ArgumentException",
                        ct =>
                        {
                            NameValueCollection q = new NameValueCollection();
                            q["tags"] = "bad";
                            bool threw = false;
                            try { EnumerationQuery.FromQueryString(q); }
                            catch (ArgumentException) { threw = true; }
                            TestAssert.True(threw, "malformed tag segment rejected");
                            return Task.CompletedTask;
                        }),

                    Case("MetadataFilter.QueryString", "CaseInsensitiveAccepted",
                        "caseInsensitive accepts true/false/1/0 and rejects garbage",
                        ct =>
                        {
                            foreach (string trueVal in new[] { "true", "True", "1" })
                            {
                                NameValueCollection q = new NameValueCollection();
                                q["caseInsensitive"] = trueVal;
                                EnumerationQuery eq = EnumerationQuery.FromQueryString(q);
                                TestAssert.True(eq.CaseInsensitive, $"'{trueVal}' parses as true");
                            }
                            foreach (string falseVal in new[] { "false", "FALSE", "0" })
                            {
                                NameValueCollection q = new NameValueCollection();
                                q["caseInsensitive"] = falseVal;
                                EnumerationQuery eq = EnumerationQuery.FromQueryString(q);
                                TestAssert.False(eq.CaseInsensitive, $"'{falseVal}' parses as false");
                            }

                            NameValueCollection bad = new NameValueCollection();
                            bad["caseInsensitive"] = "maybe";
                            bool threw = false;
                            try { EnumerationQuery.FromQueryString(bad); }
                            catch (ArgumentException) { threw = true; }
                            TestAssert.True(threw, "unrecognised value rejected");
                            return Task.CompletedTask;
                        }),
                });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string suiteId, string caseId, string display, Func<CancellationToken, Task> exec)
        {
            return new TestCaseDescriptor(suiteId: suiteId, caseId: caseId, displayName: display, executeAsync: exec);
        }

        private sealed class FakeNode : IHnswNode
        {
            private readonly Dictionary<int, HashSet<Guid>> _Neighbors = new Dictionary<int, HashSet<Guid>>();

            public FakeNode(List<string>? labels = null, Dictionary<string, object>? tags = null, string? name = null)
            {
                Id = Guid.NewGuid();
                Vector = new List<float>();
                Name = name;
                Labels = labels;
                Tags = tags;
            }

            public Guid Id { get; }
            public List<float> Vector { get; }
            public string? Name { get; set; }
            public List<string>? Labels { get; set; }
            public Dictionary<string, object>? Tags { get; set; }

            public Dictionary<int, HashSet<Guid>> GetNeighbors() => _Neighbors;
            public void AddNeighbor(int layer, Guid neighborGuid) { }
            public void RemoveNeighbor(int layer, Guid neighborGuid) { }
        }

        private sealed class ServerFixture : IDisposable
        {
            public IndexManager Manager { get; }
            public string IndexName { get; }
            private readonly string _TempDir;

            private ServerFixture(IndexManager manager, string indexName, string tempDir)
            {
                Manager = manager;
                IndexName = indexName;
                _TempDir = tempDir;
            }

            public static async Task<ServerFixture> CreateAsync(string suffix, CancellationToken ct)
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "hnswlite-filter-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                IndexManager manager = new IndexManager(tempDir);
                string name = "idx-" + suffix + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                await manager.CreateIndexAsync(new CreateIndexRequest
                {
                    Name = name,
                    Dimension = _Dimension,
                    StorageType = "RAM",
                    DistanceFunction = "Euclidean",
                }, ct).ConfigureAwait(false);
                return new ServerFixture(manager, name, tempDir);
            }

            /// <summary>
            /// Populate the index with <paramref name="count"/> vectors at positions (i, i) for i in 0..count-1.
            /// Vectors get Labels and Tags based on their parity (even/odd) and size (small if i &lt; 5 else big).
            /// </summary>
            public async Task PopulateAsync(int count, CancellationToken ct)
            {
                for (int i = 0; i < count; i++)
                {
                    string parity = (i % 2 == 0) ? "even" : "odd";
                    string size = (i < count / 2) ? "small" : "big";
                    await Manager.AddVectorAsync(IndexName, new AddVectorRequest
                    {
                        GUID = Guid.NewGuid(),
                        Vector = new List<float> { i, i },
                        Labels = new List<string> { parity, size },
                        Tags = new Dictionary<string, object>
                        {
                            { "parity", parity },
                            { "size", size },
                            { "ordinal", (long)i },
                        }
                    }, ct).ConfigureAwait(false);
                }
            }

            public void Dispose()
            {
                try { Manager.Dispose(); } catch { }
                try { if (Directory.Exists(_TempDir)) Directory.Delete(_TempDir, recursive: true); } catch { }
            }
        }

        #endregion
    }
}
