namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Http;

using System.Diagnostics.CodeAnalysis;

/// <summary>ViewModel for the HTTP API launcher window.</summary>
public partial class HttpApiViewModel : ViewModelBase {
    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    private string _baseUrl = HttpApiEndpoint.BaseUrl(HttpApiEndpoint.DefaultPort);

    [ObservableProperty]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    private string _swaggerUiUrl = string.Empty;

    [ObservableProperty]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    private string _openApiJsonUrl = string.Empty;

    [ObservableProperty]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    private string _openApiYamlUrl = string.Empty;

    private HttpApiViewModel(bool isEnabled, int port) {
        IsEnabled = isEnabled;
        BaseUrl = isEnabled && port > 0 ? HttpApiEndpoint.BaseUrl(port) : string.Empty;
        SwaggerUiUrl = string.IsNullOrWhiteSpace(BaseUrl) ? string.Empty : $"{BaseUrl}/swagger/index.html";
        OpenApiJsonUrl = string.IsNullOrWhiteSpace(BaseUrl) ? string.Empty : $"{BaseUrl}/openapi/v1.json";
        OpenApiYamlUrl = string.IsNullOrWhiteSpace(BaseUrl) ? string.Empty : $"{BaseUrl}/openapi/v1.yaml";
    }

    /// <summary>Creates a new instance. The port comes from the running HTTP API server.</summary>
    public static Task<HttpApiViewModel> CreateAsync(bool isEnabled, int port) {
        return Task.FromResult(new HttpApiViewModel(isEnabled, port));
    }
}
