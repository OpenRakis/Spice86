namespace Spice86.Tests;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.Mcp;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

internal sealed class McpIntegrationContext : IAsyncDisposable {
    private readonly HttpClient _httpClient;
    private readonly Uri _baseEndpoint;
    private readonly Uri _endpoint;
    private long _requestId = 1;
    private string? _sessionId;

    private McpIntegrationContext(
        Spice86DependencyInjection spice86,
        EmulatorMcpServices services,
        McpHttpHost host,
        HttpClient httpClient,
        Uri baseEndpoint,
        Uri endpoint) {
        Spice86 = spice86;
        Services = services;
        Host = host;
        _httpClient = httpClient;
        _baseEndpoint = baseEndpoint;
        _endpoint = endpoint;
    }

    public Spice86DependencyInjection Spice86 { get; }
    public EmulatorMcpServices Services { get; }
    public McpHttpHost Host { get; }
    public string? SessionId => _sessionId;

    public static async Task<McpIntegrationContext> CreateAsync(
        string testProgramName,
        bool enableXms = false,
        bool enableEms = false,
        bool initializeDos = false) {
        return await CreateAsync(testProgramName, enableXms, enableEms, initializeDos, SbType.None, OplMode.None);
    }

    public static async Task<McpIntegrationContext> CreateAsync(
        string testProgramName,
        bool enableXms,
        bool enableEms,
        bool initializeDos,
        SbType sbType,
        OplMode oplMode) {
        Spice86Creator creator = new(
            testProgramName,
            enablePit: false,
            installInterruptVectors: initializeDos,
            enableXms: enableXms,
            enableEms: enableEms,
            sbType: sbType,
            oplMode: oplMode);
        Spice86DependencyInjection spice86 = creator.Create();
        EmulatorMcpServices services = spice86.McpServices;

        ExpandedMemoryManager? emsManager = services.EmsManager;
        bool mustProvideEms = enableEms || services.XmsManager != null;
        if (mustProvideEms && emsManager == null) {
            emsManager = new ExpandedMemoryManager(
                services.Memory,
                services.CfgCpu,
                spice86.Machine.Stack,
                services.State,
                services.LoggerService);
        }


        services = new EmulatorMcpServices(
            services.Memory,
            services.State,
            services.FunctionCatalogue,
            services.CfgCpu,
            services.IoPortDispatcher,
            services.VgaRenderer,
            services.PauseHandler,
            emsManager,
            services.XmsManager,
            services.BreakpointsManager,
            services.LoggerService);

        // Always assign optional devices, regardless of EMS/XMS enablement
        services.Intel8042Controller = spice86.McpServices.Intel8042Controller;
        services.SoundBlaster = spice86.McpServices.SoundBlaster;
        services.Opl3Fm = spice86.McpServices.Opl3Fm;
        services.PcSpeaker = spice86.McpServices.PcSpeaker;
        services.Midi = spice86.McpServices.Midi;
        services.VgaFunctionality = spice86.McpServices.VgaFunctionality;
        services.BiosDataArea = spice86.McpServices.BiosDataArea;
        services.InterruptVectorTable = spice86.McpServices.InterruptVectorTable;
        services.Dos = spice86.McpServices.Dos;

        int port = GetFreeTcpPort();
        McpHttpHost host = new(services.LoggerService);
        host.Start(services, port);

        HttpClient httpClient = new();
        Uri baseEndpoint = new($"http://localhost:{port}/");
        Uri endpoint = new($"http://localhost:{port}/mcp");

        McpIntegrationContext context = new(spice86, services, host, httpClient, baseEndpoint, endpoint);

        await WaitForPortAsync(port);
        return context;
    }

    public async Task<JsonDocument> InitializeAsync() {
        JsonDocument initializeResponse = await SendJsonRpcAsync(new Dictionary<string, object?> {
            ["jsonrpc"] = "2.0",
            ["id"] = GetNextRequestId(),
            ["method"] = "initialize",
            ["params"] = new Dictionary<string, object?> {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new Dictionary<string, object?>(),
                ["clientInfo"] = new Dictionary<string, object?> {
                    ["name"] = "Spice86.Tests",
                    ["version"] = "1.0.0"
                }
            }
        });

        JsonDocument initializedNotificationResponse = await SendJsonRpcAsync(new Dictionary<string, object?> {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/initialized",
            ["params"] = new Dictionary<string, object?>()
        });
        initializedNotificationResponse.Dispose();

        return initializeResponse;
    }

    public Task<JsonDocument> ToolsListAsync() {
        return SendJsonRpcAsync(new Dictionary<string, object?> {
            ["jsonrpc"] = "2.0",
            ["id"] = GetNextRequestId(),
            ["method"] = "tools/list",
            ["params"] = new Dictionary<string, object?>()
        });
    }

    public Task<JsonDocument> ToolsListWithExplicitSessionIdAsync(string sessionId) {
        return SendJsonRpcWithExplicitSessionIdAsync(new Dictionary<string, object?> {
            ["jsonrpc"] = "2.0",
            ["id"] = GetNextRequestId(),
            ["method"] = "tools/list",
            ["params"] = new Dictionary<string, object?>()
        }, sessionId);
    }

    public async Task<JsonDocument> ToolsListWithFreshConnectionAsync(string? sessionId) {
        Dictionary<string, object?> payload = new() {
            ["jsonrpc"] = "2.0",
            ["id"] = GetNextRequestId(),
            ["method"] = "tools/list",
            ["params"] = new Dictionary<string, object?>()
        };
        using HttpClient freshClient = new();
        using HttpRequestMessage request = CreateJsonRpcRequest(payload, sessionId);
        using HttpResponseMessage response = await freshClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        return ParseResponseBody(body);
    }

    public Task<JsonDocument> CallToolAsync(string toolName, Dictionary<string, object?> arguments) {
        return SendJsonRpcAsync(new Dictionary<string, object?> {
            ["jsonrpc"] = "2.0",
            ["id"] = GetNextRequestId(),
            ["method"] = "tools/call",
            ["params"] = new Dictionary<string, object?> {
                ["name"] = toolName,
                ["arguments"] = arguments
            }
        });
    }

    public async Task<JsonDocument> GetHealthAsync() {
        using HttpResponseMessage response = await _httpClient.GetAsync(new Uri(_baseEndpoint, "health"));
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        return ParseJsonDocument(body);
    }

    public async ValueTask DisposeAsync() {
        _httpClient.Dispose();
        Host.Dispose();
        Spice86.Dispose();
        await Task.CompletedTask;
    }

    private long GetNextRequestId() {
        long id = _requestId;
        _requestId++;
        return id;
    }

    private async Task<JsonDocument> SendJsonRpcAsync(Dictionary<string, object?> payload) {
        using HttpRequestMessage request = CreateJsonRpcRequest(payload, _sessionId);
        using HttpResponseMessage response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        UpdateSessionId(response);
        string body = await response.Content.ReadAsStringAsync();
        return ParseResponseBody(body);
    }

    private async Task<JsonDocument> SendJsonRpcWithExplicitSessionIdAsync(Dictionary<string, object?> payload, string sessionId) {
        using HttpRequestMessage request = CreateJsonRpcRequest(payload, sessionId);
        using HttpResponseMessage response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        UpdateSessionId(response);
        string body = await response.Content.ReadAsStringAsync();
        return ParseResponseBody(body);
    }

    private HttpRequestMessage CreateJsonRpcRequest(Dictionary<string, object?> payload, string? sessionId) {
        string jsonPayload = JsonSerializer.Serialize(payload);
        StringContent content = new(jsonPayload, Encoding.UTF8, "application/json");
        HttpRequestMessage request = new(HttpMethod.Post, _endpoint) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrWhiteSpace(sessionId)) {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        }
        return request;
    }

    private void UpdateSessionId(HttpResponseMessage response) {
        if (!response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? sessionValues)) {
            return;
        }

        string? sessionId = sessionValues.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(sessionId)) {
            _sessionId = sessionId;
        }
    }

    private static JsonDocument ParseResponseBody(string body) {
        if (string.IsNullOrWhiteSpace(body)) {
            return ParseJsonDocument("{}");
        }

        if (body.TrimStart().StartsWith("event:", StringComparison.Ordinal)) {
            string sseJsonPayload = ExtractLastSseJsonPayload(body);
            return ParseJsonDocument(sseJsonPayload);
        }

        return ParseJsonDocument(body);
    }

    private static JsonDocument ParseJsonDocument([StringSyntax(StringSyntaxAttribute.Json)] string json) {
        return JsonDocument.Parse(json);
    }

    private static string ExtractLastSseJsonPayload(string body) {
        string[] lines = body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        List<string> jsonCandidates = new();
        foreach (string line in lines) {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) {
                continue;
            }

            string dataValue = line[5..].Trim();
            if (dataValue.StartsWith("{", StringComparison.Ordinal)) {
                jsonCandidates.Add(dataValue);
            }
        }

        if (jsonCandidates.Count == 0) {
            throw new InvalidOperationException($"Could not find JSON data in SSE response: {body}");
        }

        return jsonCandidates[jsonCandidates.Count - 1];
    }

    private static int GetFreeTcpPort() {
        using TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try {
            System.Net.IPEndPoint localEndpoint = (System.Net.IPEndPoint)listener.LocalEndpoint;
            return localEndpoint.Port;
        } finally {
            listener.Stop();
        }
    }

    private static async Task WaitForPortAsync(int port) {
        const int maxAttempts = 40;

        for (int attempt = 0; attempt < maxAttempts; attempt++) {
            using TcpClient client = new();
            try {
                await client.ConnectAsync("127.0.0.1", port);
                return;
            } catch (SocketException) {
                await Task.Delay(10);
            }
        }

        throw new InvalidOperationException($"MCP host did not open port {port} in time.");
    }
}
