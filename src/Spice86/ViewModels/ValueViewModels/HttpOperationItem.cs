namespace Spice86.ViewModels.ValueViewModels;

using System.Diagnostics.CodeAnalysis;

public sealed class HttpOperationItem {
    public HttpOperationItem(string operationId, string tag, string method, string path, string summary, string description,
        string responseDescription, bool hasRequestBody,
        [StringSyntax(StringSyntaxAttribute.Json)] string requestBodyTemplate,
        IReadOnlyList<HttpOperationParameterDefinition> parameters) {
        OperationId = operationId;
        Tag = tag;
        Method = method;
        Path = path;
        Summary = summary;
        Description = description;
        ResponseDescription = responseDescription;
        HasRequestBody = hasRequestBody;
        RequestBodyTemplate = requestBodyTemplate;
        Parameters = parameters;
    }

    public string OperationId { get; }
    public string Tag { get; }
    public string Method { get; }
    public string Path { get; }
    public string Summary { get; }
    public string Description { get; }
    public string ResponseDescription { get; }
    public bool HasRequestBody { get; }
    [StringSyntax(StringSyntaxAttribute.Json)]
    public string RequestBodyTemplate { get; }
    public IReadOnlyList<HttpOperationParameterDefinition> Parameters { get; }
}
