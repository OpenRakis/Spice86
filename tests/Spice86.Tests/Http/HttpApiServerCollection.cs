namespace Spice86.Tests.Http;

using Xunit;

[CollectionDefinition(Name)]
public sealed class HttpApiServerCollection : ICollectionFixture<HttpApiServerFixture> {
    public const string Name = "HttpApiServerCollection";
}
