namespace Spice86.Tests.UI;

using Xunit;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HttpApiUiCollection : ICollectionFixture<HttpApiUiFixture> {
    public const string Name = "HttpApiUiCollection";
}
