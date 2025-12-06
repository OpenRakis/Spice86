namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

using Spice86.Core.Emulator.Mcp;
using Spice86.ViewModels.Services;

public sealed partial class McpStatusViewModel : ViewModelBase {
    private const string OfflineColor = "#D14D4D";
    private const string OnlineColor = "#2AA876";
    private const string BusyColor = "#D3A11D";

    private readonly HttpClient _httpClient = new();
    private readonly EmulatorMcpServices? _services;
    private DispatcherTimer? _networkProbeTimer;
    private long _requestId = 1;
    private string? _sessionId;

    [ObservableProperty]
    private bool _isServerRunning;

    [ObservableProperty]
    private int _port;

    [ObservableProperty]
    private int _enabledToolsCount;

    [ObservableProperty]
    private int _totalToolsCount;

    [ObservableProperty]
    private string _toolFilter = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InvokeSelectedToolCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetArgumentsToTemplateCommand))]
    private McpToolViewModel? _selectedTool;

    [ObservableProperty]
    private string _manualArgumentsJson = "{}";

    [ObservableProperty]
    private string _manualResponseJson = "Select a tool, edit the JSON arguments, then click Invoke.";

    [ObservableProperty]
    private string _manualStatus = "Ready for manual MCP testing.";

    [ObservableProperty]
    private string _networkStatusText = "Offline";

    [ObservableProperty]
    private string _networkStatusTooltip = "MCP HTTP transport is offline.";

    [ObservableProperty]
    private string _networkStatusColor = OfflineColor;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PingServerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ListAdvertisedToolsCommand))]
    [NotifyCanExecuteChangedFor(nameof(InvokeSelectedToolCommand))]
    private bool _isManualRequestRunning;

    public ObservableCollection<McpToolViewModel> Tools { get; } = new();
    public ObservableCollection<McpToolViewModel> FilteredTools { get; } = new();

    public string ServerEndpoint => IsServerRunning ? $"http://localhost:{Port}/mcp" : "MCP disabled";

    public string SelectedToolDescription => SelectedTool?.Description ?? "Select a tool to inspect its MCP description.";

    public bool HasSelectedTool => SelectedTool is not null;

    public string BuiltInGroupStatus => $"{EnabledToolsCount}/{TotalToolsCount}";

    public McpStatusViewModel() {
        _port = 0;
        _isServerRunning = false;
        UpdateNetworkIndicator(false, "MCP HTTP transport is disabled.");
        RefreshToolState();
    }

    public McpStatusViewModel(EmulatorMcpServices services, int port) {
        _services = services;
        _port = port;
        _isServerRunning = true;

        AddTools(services);

        RefreshToolState();
    }

    public void StartNetworkMonitoring() {
        if (_networkProbeTimer != null) {
            return;
        }

        _networkProbeTimer = StartNetworkProbeTimer();
    }

    partial void OnToolFilterChanged(string value) {
        RefreshFilteredTools();
    }

    partial void OnIsServerRunningChanged(bool value) {
        OnPropertyChanged(nameof(ServerEndpoint));
    }

    partial void OnEnabledToolsCountChanged(int value) {
        OnPropertyChanged(nameof(BuiltInGroupStatus));
    }

    partial void OnTotalToolsCountChanged(int value) {
        OnPropertyChanged(nameof(BuiltInGroupStatus));
    }

    partial void OnSelectedToolChanged(McpToolViewModel? value) {
        ManualArgumentsJson = value?.ArgumentsTemplateJson ?? "{}";
        OnPropertyChanged(nameof(SelectedToolDescription));
    }

    [RelayCommand]
    private void EnableAllTools() {
        foreach (McpToolViewModel tool in Tools) {
            if (tool.CanToggle && !tool.IsEnabled) {
                tool.IsEnabled = true;
            }
        }

        RefreshToolState();
    }

    [RelayCommand]
    private void DisableAllTools() {
        foreach (McpToolViewModel tool in Tools) {
            if (tool.CanToggle && tool.IsEnabled) {
                tool.IsEnabled = false;
            }
        }

        RefreshToolState();
    }

    private bool PingServerCanExecute() => IsServerRunning && !IsManualRequestRunning;

    [RelayCommand(CanExecute = nameof(PingServerCanExecute))]
    private async Task PingServerAsync() {
        IsManualRequestRunning = true;
        SetBusyIndicator();
        McpManualResult result = await GetHealthResultAsync();
        ApplyManualResult(result, "Health probe completed.");
        IsManualRequestRunning = false;
    }

    private bool ListAdvertisedToolsCanExecute() => IsServerRunning && !IsManualRequestRunning;

    [RelayCommand(CanExecute = nameof(ListAdvertisedToolsCanExecute))]
    private async Task ListAdvertisedToolsAsync() {
        IsManualRequestRunning = true;
        SetBusyIndicator();
        McpManualResult result = await GetToolsListResultAsync();
        ApplyManualResult(result, "Fetched tools/list from the MCP server.");
        IsManualRequestRunning = false;
    }

    private bool InvokeSelectedToolCanExecute() => IsServerRunning && !IsManualRequestRunning && SelectedTool is not null;

    [RelayCommand(CanExecute = nameof(InvokeSelectedToolCanExecute))]
    private async Task InvokeSelectedToolAsync() {
        if (SelectedTool == null) {
            return;
        }

        IsManualRequestRunning = true;
        SetBusyIndicator();
        McpManualResult result = await InvokeToolAsync(SelectedTool);
        ApplyManualResult(result, $"Invoked '{SelectedTool.Name}'.");
        IsManualRequestRunning = false;
    }

    private bool ResetArgumentsToTemplateCanExecute() => SelectedTool is not null;

    [RelayCommand(CanExecute = nameof(ResetArgumentsToTemplateCanExecute))]
    private void ResetArgumentsToTemplate() {
        ManualArgumentsJson = SelectedTool?.ArgumentsTemplateJson ?? "{}";
        ManualStatus = "Arguments reset to the generated template.";
    }

    private void RefreshToolState() {
        TotalToolsCount = Tools.Count;
        EnabledToolsCount = Tools.Count(t => t.IsEnabled);
        RefreshFilteredTools();
    }

    private void RefreshFilteredTools() {
        FilteredTools.Clear();

        IEnumerable<McpToolViewModel> filtered = string.IsNullOrWhiteSpace(ToolFilter)
            ? Tools
            : Tools.Where(t => MatchesFilter(t, ToolFilter));

        foreach (McpToolViewModel tool in filtered) {
            FilteredTools.Add(tool);
        }

        if (SelectedTool == null && FilteredTools.Count > 0) {
            SelectedTool = FilteredTools[0];
        }
    }

    private DispatcherTimer StartNetworkProbeTimer() {
        DispatcherTimer timer = DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromSeconds(2),
            DispatcherPriority.Background, async (_, _) => await ProbeHealthAsync());
        return timer;
    }

    private async Task ProbeHealthAsync() {
        if (!IsServerRunning || IsManualRequestRunning) {
            return;
        }

        ApplyIndicatorResult(await GetHealthResultAsync());
    }

    private void UpdateNetworkIndicator(bool isOnline, string tooltip) {
        NetworkStatusText = isOnline ? "Online" : "Offline";
        NetworkStatusTooltip = tooltip;
        NetworkStatusColor = isOnline ? OnlineColor : OfflineColor;
    }

    private async Task<McpManualResult> GetHealthResultAsync() {
        return await SendGetAsync(GetHealthEndpoint());
    }

    private async Task<McpManualResult> GetToolsListResultAsync() {
        McpManualResult initializationResult = await EnsureSessionAsync();
        if (!initializationResult.Success) {
            return initializationResult;
        }

        return await SendJsonRpcAsync(BuildToolsListPayload());
    }

    private async Task<McpManualResult> InvokeToolAsync(McpToolViewModel selectedTool) {
        McpManualResult initializationResult = await EnsureSessionAsync();
        if (!initializationResult.Success) {
            return initializationResult;
        }

        string payload = BuildToolCallPayload(selectedTool.Name, ManualArgumentsJson);
        return await SendJsonRpcAsync(payload);
    }

    private async Task<McpManualResult> EnsureSessionAsync() {
        if (!string.IsNullOrWhiteSpace(_sessionId)) {
            return McpManualResult.SuccessResult(string.Empty, $"Session active on port {Port}.");
        }

        McpManualResult initializeResult = await SendJsonRpcAsync(BuildInitializePayload());
        if (!initializeResult.Success) {
            return initializeResult;
        }

        return await SendJsonRpcAsync(BuildInitializedNotificationPayload());
    }

    private async Task<McpManualResult> SendJsonRpcAsync(string payload) {
        return await SendPostAsync(ServerEndpoint, payload, true);
    }

    private async Task<McpManualResult> SendGetAsync(string endpoint) {
        using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        return await SendAsync(request, false);
    }

    private async Task<McpManualResult> SendPostAsync(string endpoint, string payload, bool acceptEventStream) {
        using StringContent content = new(payload, Encoding.UTF8, "application/json");
        using HttpRequestMessage request = new(HttpMethod.Post, endpoint) { Content = content };
        AddAcceptHeaders(request, acceptEventStream);
        AddSessionHeader(request);
        return await SendAsync(request, acceptEventStream);
    }

    private async Task<McpManualResult> SendAsync(HttpRequestMessage request, bool eventStreamExpected) {
        try {
            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            UpdateSessionId(response);
            return CreateResult(response.IsSuccessStatusCode, body, eventStreamExpected);
        } catch (HttpRequestException ex) {
            return McpManualResult.Failure(ex.Message, ex.Message);
        } catch (TaskCanceledException ex) {
            return McpManualResult.Failure(ex.Message, ex.Message);
        }
    }

    private void AddSessionHeader(HttpRequestMessage request) {
        if (!string.IsNullOrWhiteSpace(_sessionId)) {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }
    }

    private static void AddAcceptHeaders(HttpRequestMessage request, bool acceptEventStream) {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (acceptEventStream) {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }
    }

    private McpManualResult CreateResult(bool success, string body, bool eventStreamExpected) {
        string normalizedBody = eventStreamExpected ? NormalizeSseBody(body) : body;
        string message = success ? $"Request succeeded on port {Port}." : $"Request failed on port {Port}.";
        return success
            ? McpManualResult.SuccessResult(normalizedBody, message)
            : McpManualResult.Failure(normalizedBody, message);
    }

    private void AddTools(EmulatorMcpServices services) {
        foreach (string toolName in services.GetAllToolNames()) {
            bool enabled = services.IsToolEnabled(toolName);
            McpToolViewModel viewModel = new(toolName,
                EmulatorMcpServices.GetToolDescription(toolName),
                EmulatorMcpServices.GetToolArgumentsTemplateJson(toolName),
                enabled,
                true);
            viewModel.PropertyChanged += OnToolPropertyChanged;
            Tools.Add(viewModel);
        }
    }

    private void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs args) {
        if (args.PropertyName != nameof(McpToolViewModel.IsEnabled)) {
            return;
        }

        if (sender is not McpToolViewModel tool || _services == null) {
            return;
        }

        _services.SetToolEnabled(tool.Name, tool.IsEnabled);
        RefreshToolState();
    }

    private void ApplyManualResult(McpManualResult result, string successStatus) {
        ManualResponseJson = string.IsNullOrWhiteSpace(result.Body) ? "{}" : result.Body;
        ManualStatus = result.Success ? successStatus : result.StatusMessage;
        ApplyIndicatorResult(result);
    }

    private void ApplyIndicatorResult(McpManualResult result) {
        UpdateNetworkIndicator(result.Success, result.StatusMessage);
    }

    private void SetBusyIndicator() {
        NetworkStatusText = "Busy";
        NetworkStatusTooltip = $"MCP request in flight on port {Port}.";
        NetworkStatusColor = BusyColor;
    }

    private void UpdateSessionId(HttpResponseMessage response) {
        if (!response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values)) {
            return;
        }

        string? sessionId = values.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(sessionId)) {
            _sessionId = sessionId;
        }
    }

    private string BuildInitializePayload() {
        long requestId = GetNextRequestId();
        return $"{{\"jsonrpc\":\"2.0\",\"id\":{requestId},\"method\":\"initialize\",\"params\":{{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{{}},\"clientInfo\":{{\"name\":\"Spice86.UI\",\"version\":\"1.0.0\"}}}}}}";
    }

    private static string BuildInitializedNotificationPayload() {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{}}";
    }

    private string BuildToolsListPayload() {
        long requestId = GetNextRequestId();
        return $"{{\"jsonrpc\":\"2.0\",\"id\":{requestId},\"method\":\"tools/list\",\"params\":{{}}}}";
    }

    private string BuildToolCallPayload(string toolName, string argumentsJson) {
        long requestId = GetNextRequestId();
        string escapedToolName = System.Text.Json.JsonSerializer.Serialize(toolName);
        string normalizedArgumentsJson = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
        return $"{{\"jsonrpc\":\"2.0\",\"id\":{requestId},\"method\":\"tools/call\",\"params\":{{\"name\":{escapedToolName},\"arguments\":{normalizedArgumentsJson}}}}}";
    }

    private string GetHealthEndpoint() {
        return $"http://localhost:{Port}/health";
    }

    private long GetNextRequestId() {
        long current = _requestId;
        _requestId++;
        return current;
    }

    private static bool MatchesFilter(McpToolViewModel tool, string filter) {
        return tool.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || tool.Description.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSseBody(string body) {
        string[] lines = body.Split(["\r\n", "\n"], StringSplitOptions.None);
        string latestPayload = string.Empty;
        foreach (string line in lines) {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) {
                continue;
            }

            latestPayload = line[5..].Trim();
        }

        return string.IsNullOrWhiteSpace(latestPayload) ? body : latestPayload;
    }

    private sealed record McpManualResult(bool Success, string Body, string StatusMessage) {
        public static McpManualResult SuccessResult(string body, string statusMessage) {
            return new(true, body, statusMessage);
        }

        public static McpManualResult Failure(string body, string statusMessage) {
            return new(false, body, statusMessage);
        }
    }
}
