namespace Spice86.ViewModels.ValueViewModels;

using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

using Spice86.Core.Emulator.Http.Contracts;
using Spice86.ViewModels;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

internal static class HttpApiViewModelSupport {
    private static readonly HttpClient HttpClient = new HttpClient();

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    internal static async Task<string> FetchTextAsync([StringSyntax(StringSyntaxAttribute.Uri)] string url, CancellationToken cancellationToken) {
        return await HttpClient.GetStringAsync(url, cancellationToken);
    }

    internal static async Task<IReadOnlyList<HttpOperationItem>> FetchOperationsAsync(
        [StringSyntax(StringSyntaxAttribute.Uri)] string baseUrl, CancellationToken cancellationToken) {
        using Stream stream = await HttpClient.GetStreamAsync($"{baseUrl}/openapi/v1.json", cancellationToken);
        ReadResult result = await OpenApiModelFactory.LoadAsync(stream, "json", cancellationToken: cancellationToken);
        if (result.Document is not { Paths: { } paths }) return [];

        List<HttpOperationItem> items = [];
        foreach ((string path, IOpenApiPathItem pathItem) in paths) {
            if (pathItem.Operations is not { } operations) continue;
            foreach ((HttpMethod httpMethod, OpenApiOperation operation) in operations) {
                items.Add(MapOperation(path, httpMethod, operation));
            }
        }
        return items;
    }

    private static HttpOperationItem MapOperation(string path, HttpMethod httpMethod, OpenApiOperation operation) {
        string tag = operation.Tags?.FirstOrDefault()?.Name ?? "";
        string summary = operation.Summary ?? "";
        bool hasBody = operation.RequestBody is not null;
        return new HttpOperationItem(
            operation.OperationId ?? "", tag, httpMethod.Method, path, summary, summary, "200 OK",
            hasBody, hasBody ? BuildBodyTemplate(operation.RequestBody!) : "",
            MapParameters(operation.Parameters));
    }

    private static List<HttpOperationParameterDefinition> MapParameters(IList<IOpenApiParameter>? openApiParameters) {
        if (openApiParameters is null) return [];
        List<HttpOperationParameterDefinition> parameters = [];
        foreach (IOpenApiParameter p in openApiParameters) {
            string? name = p.Name;
            if (p.In != ParameterLocation.Path || name is null) continue;
            bool isInteger = p.Schema?.Type?.HasFlag(JsonSchemaType.Integer) == true;
            parameters.Add(new HttpOperationParameterDefinition(
                name, char.ToUpperInvariant(name[0]) + name[1..], "", p.Required,
                isInteger ? HttpOperationParameterKind.Integer : HttpOperationParameterKind.Text));
        }
        return parameters;
    }

    internal static HttpApiPreparedRequest BuildRequest(HttpOperationItem operation,
        IEnumerable<HttpOperationParameter> parameters,
        [StringSyntax(StringSyntaxAttribute.Json)] string requestBody,
        [StringSyntax(StringSyntaxAttribute.Uri)] string baseUrl) {
        string path = operation.Path;
        foreach (HttpOperationParameter p in parameters) {
            string trimmed = p.Value.Trim();
            if (p.Kind == HttpOperationParameterKind.Integer &&
                !long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) {
                return HttpApiPreparedRequest.ForError($"Invalid value for '{p.Name}'");
            }
            path = path.Replace($"{{{p.Name}}}", trimmed);
        }

        HttpContent? content = null;
        if (operation.HasRequestBody && !string.IsNullOrWhiteSpace(requestBody)) {
            content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        }

        string url = $"{baseUrl}{path}";
        string preview = operation.HasRequestBody && !string.IsNullOrWhiteSpace(requestBody)
            ? $"curl -X {operation.Method} \"{url}\"\n  -H \"Content-Type: application/json\"\n  -d '{requestBody}'"
            : $"curl -X {operation.Method} \"{url}\"";

        return new HttpApiPreparedRequest(path, url, content, preview);
    }

    internal static async Task<HttpApiSendResult> SendAsync(string method,
        HttpApiPreparedRequest request, CancellationToken cancellationToken) {
        try {
            using HttpRequestMessage message = new(new HttpMethod(method), request.Url) { Content = request.Content };
            return HttpApiSendResult.Ok(await HttpClient.SendAsync(message, cancellationToken));
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return HttpApiSendResult.Fail("Request cancelled");
        } catch (TaskCanceledException) {
            return HttpApiSendResult.Fail("HTTP timeout");
        } catch (HttpRequestException e) {
            return HttpApiSendResult.Fail(e.Message);
        }
    }

    internal static async Task<string> FormatResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return await Task.Run(() => PrettyPrint(body), cancellationToken);
    }

    internal static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response) where T : class {
        try {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        } catch (JsonException) {
            return null;
        }
    }

    internal static string? ParseErrorMessage([StringSyntax(StringSyntaxAttribute.Json)] string responseBody) {
        try {
            return JsonSerializer.Deserialize<HttpApiErrorResponse>(responseBody, JsonOptions)?.Message;
        } catch (JsonException) {
            return null;
        }
    }

    private static string BuildBodyTemplate(IOpenApiRequestBody requestBody) {
        if (requestBody.Content is not { } content ||
            !content.TryGetValue("application/json", out OpenApiMediaType? mediaType) ||
            mediaType.Schema is null) {
            return "{}";
        }
        return SchemaToDefaultJson(mediaType.Schema);
    }

    private static string SchemaToDefaultJson(IOpenApiSchema schema) {
        if (schema.Type?.HasFlag(JsonSchemaType.Object) != true ||
            schema.Properties is null || schema.Properties.Count == 0) {
            return "{}";
        }
        string entries = string.Join(",\n",
            schema.Properties.Select(p => $"  \"{p.Key}\": {DefaultSchemaValue(p.Value)}"));
        return $"{{\n{entries}\n}}";
    }

    private static string DefaultSchemaValue(IOpenApiSchema schema) {
        JsonSchemaType? type = schema.Type;
        if (type?.HasFlag(JsonSchemaType.Integer) == true || type?.HasFlag(JsonSchemaType.Number) == true) return "0";
        if (type?.HasFlag(JsonSchemaType.String) == true) return "\"\"";
        if (type?.HasFlag(JsonSchemaType.Boolean) == true) return "false";
        if (type?.HasFlag(JsonSchemaType.Array) == true) return "[]";
        return "null";
    }

    private static string PrettyPrint([StringSyntax(StringSyntaxAttribute.Json)] string body) {
        if (string.IsNullOrWhiteSpace(body)) return "{}";
        try {
            using JsonDocument doc = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(doc, JsonOptions);
        } catch (JsonException) {
            return body;
        }
    }
}

internal sealed record HttpApiPreparedRequest(string Path, [property: StringSyntax(StringSyntaxAttribute.Uri)] string Url, HttpContent? Content, string Preview) {
    internal static HttpApiPreparedRequest ForError(string error) => new("", "", null, "") { Error = error };
    internal string Error { get; init; } = "";
    internal bool IsValid => Error.Length == 0;
}

internal sealed record HttpApiSendResult(HttpResponseMessage? Response, string Error) {
    internal static HttpApiSendResult Ok(HttpResponseMessage response) => new(response, "");
    internal static HttpApiSendResult Fail(string error) => new(null, error);
}
