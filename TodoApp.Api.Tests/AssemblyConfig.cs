using Xunit;

// Disable parallelism across test classes — they share the same in-memory DB via the factory fixture
[assembly: CollectionBehavior(DisableTestParallelization = true)]
