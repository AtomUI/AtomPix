using Xunit;

// Avalonia controls and AtomUI icon geometries are Dispatcher-affine. Running
// independent HeadlessUnitTestSession instances concurrently can re-use static
// themed resources from different UI threads and makes the release gate flaky.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
