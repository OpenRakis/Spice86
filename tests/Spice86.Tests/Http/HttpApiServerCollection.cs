namespace Spice86.Tests.Http;

using Xunit;

// The collection shares one HTTP server and its mutable CPU/memory state across all its tests.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HttpApiServerCollection : ICollectionFixture<HttpApiServerFixture> {
    public const string Name = "HttpApiServerCollection";
}
