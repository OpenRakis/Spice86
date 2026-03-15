namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Http;
using Spice86.Core.Emulator.Http.Contracts;

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

public partial class HttpApiViewModel : ViewModelBase, IDisposable {
    private readonly HttpClient _httpClient;
    private bool _disposed;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _baseUrl = HttpApiEndpoint.BaseUrl(HttpApiEndpoint.DefaultPort);

    [ObservableProperty]
    private string _memoryAddressInput = "0x40";

    [ObservableProperty]
    private string _memoryValueInput = "0x00";

    [ObservableProperty]
    private string _memoryRangeLengthInput = "16";

    [ObservableProperty]
    private string _status = "Unknown";

    [ObservableProperty]
    private string _cpuState = "Unknown";

    [ObservableProperty]
    private string _csIp = "----:----";

    [ObservableProperty]
    private string _ipPhysicalAddress = "0x00000000";

    [ObservableProperty]
    private string _memorySizeBytes = "0";

    [ObservableProperty]
    private long _cycles;

    [ObservableProperty]
    private string _lastHttpStatus = "n/a";

    [ObservableProperty]
    private string _lastRequestPath = "/api/status";

    [ObservableProperty]
    private string _lastResponseJson = "{}";

    [ObservableProperty]
    private string _lastErrorMessage = string.Empty;

    [ObservableProperty]
    private string _lastReadByte = "n/a";

    [ObservableProperty]
    private string _lastReadRange = "n/a";

    public HttpApiViewModel() {
        _httpClient = new HttpClient();
        Initialize(false, HttpApiEndpoint.DefaultPort);
    }

    public HttpApiViewModel(bool isEnabled, int port) {
        _httpClient = new HttpClient();
        Initialize(isEnabled, port);
    }

    private void Initialize(bool isEnabled, int port) {
        IsEnabled = isEnabled;
        BaseUrl = HttpApiEndpoint.BaseUrl(port);
        _httpClient.Timeout = TimeSpan.FromSeconds(2);
    }

    [RelayCommand]
    private async Task RefreshStatus() {
        await ExecuteGetStatus();
    }

    [RelayCommand]
    private async Task ReadByte() {
        if (!TryParseUInt32(MemoryAddressInput, out uint address)) {
            LastErrorMessage = "Invalid address";
            return;
        }

        await ExecuteGetByte(address);
    }

    [RelayCommand]
    private async Task WriteByte() {
        if (!TryParseUInt32(MemoryAddressInput, out uint address)) {
            LastErrorMessage = "Invalid address";
            return;
        }

        if (!TryParseByte(MemoryValueInput, out byte value)) {
            LastErrorMessage = "Invalid byte value";
            return;
        }

        await ExecutePutByte(address, value);
    }

    [RelayCommand]
    private async Task ReadRange() {
        if (!TryParseUInt32(MemoryAddressInput, out uint address)) {
            LastErrorMessage = "Invalid address";
            return;
        }

        if (!TryParsePositiveInt(MemoryRangeLengthInput, out int length)) {
            LastErrorMessage = "Invalid range length";
            return;
        }

        await ExecuteGetRange(address, length);
    }

    private async Task ExecuteGetStatus() {
        const string requestPath = "/api/status";
        HttpResponseMessage? response = await SendGet(requestPath);
        if (response is null) {
            return;
        }

        if (!response.IsSuccessStatusCode) {
            return;
        }

        HttpApiStatusResponse? payload = await ReadJson<HttpApiStatusResponse>(response);
        if (payload is null) {
            return;
        }

        Status = payload.IsPaused ? "Paused" : "Running";
        CpuState = payload.IsCpuRunning ? "CPU running" : "CPU stopped";
        CsIp = $"{payload.Cs:X4}:{payload.Ip:X4}";
        IpPhysicalAddress = $"0x{payload.IpPhysicalAddress:X8}";
        MemorySizeBytes = payload.MemorySizeBytes.ToString(CultureInfo.InvariantCulture);
        Cycles = payload.Cycles;
    }

    private async Task ExecuteGetByte(uint address) {
        string requestPath = $"/api/memory/{address}/byte";
        HttpResponseMessage? response = await SendGet(requestPath);
        if (response is null) {
            return;
        }

        if (!response.IsSuccessStatusCode) {
            return;
        }

        HttpApiMemoryByteResponse? payload = await ReadJson<HttpApiMemoryByteResponse>(response);
        if (payload is null) {
            return;
        }

        LastReadByte = $"0x{payload.Value:X2}";
    }

    private async Task ExecutePutByte(uint address, byte value) {
        string requestPath = $"/api/memory/{address}/byte";
        HttpApiWriteByteRequest request = new() {
            Value = value
        };
        HttpResponseMessage? response = await SendPut(requestPath, request);
        if (response is null) {
            return;
        }

        if (!response.IsSuccessStatusCode) {
            return;
        }

        HttpApiMemoryByteResponse? payload = await ReadJson<HttpApiMemoryByteResponse>(response);
        if (payload is null) {
            return;
        }

        LastReadByte = $"0x{payload.Value:X2}";
    }

    private async Task ExecuteGetRange(uint address, int length) {
        string requestPath = $"/api/memory/{address}/range/{length}";
        HttpResponseMessage? response = await SendGet(requestPath);
        if (response is null) {
            return;
        }

        if (!response.IsSuccessStatusCode) {
            return;
        }

        HttpApiMemoryRangeResponse? payload = await ReadJson<HttpApiMemoryRangeResponse>(response);
        if (payload is null) {
            return;
        }

        LastReadRange = ConvertToHexPreview(payload.Values);
    }

    private async Task<HttpResponseMessage?> SendGet(string requestPath) {
        if (!IsEnabled) {
            LastErrorMessage = "HTTP API unavailable";
            return null;
        }

        IsBusy = true;
        try {
            LastRequestPath = requestPath;
            LastErrorMessage = string.Empty;

            HttpResponseMessage response = await _httpClient.GetAsync($"{BaseUrl}{requestPath}");
            await UpdateResponseState(response);
            return response;
        } catch (HttpRequestException e) {
            LastErrorMessage = $"HTTP error: {e.Message}";
        } catch (TaskCanceledException) {
            LastErrorMessage = "HTTP timeout";
        } catch (InvalidOperationException e) {
            LastErrorMessage = $"Request error: {e.Message}";
        } finally {
            IsBusy = false;
        }

        return null;
    }

    private async Task<HttpResponseMessage?> SendPut(string requestPath, HttpApiWriteByteRequest request) {
        if (!IsEnabled) {
            LastErrorMessage = "HTTP API unavailable";
            return null;
        }

        IsBusy = true;
        try {
            LastRequestPath = requestPath;
            LastErrorMessage = string.Empty;

            HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"{BaseUrl}{requestPath}", request);
            await UpdateResponseState(response);
            return response;
        } catch (HttpRequestException e) {
            LastErrorMessage = $"HTTP error: {e.Message}";
        } catch (TaskCanceledException) {
            LastErrorMessage = "HTTP timeout";
        } catch (InvalidOperationException e) {
            LastErrorMessage = $"Request error: {e.Message}";
        } finally {
            IsBusy = false;
        }

        return null;
    }

    private async Task UpdateResponseState(HttpResponseMessage response) {
        LastHttpStatus = $"{(int)response.StatusCode} {response.ReasonPhrase}";

        string body = await response.Content.ReadAsStringAsync();
        LastResponseJson = PrettyPrintJson(body);

        if (response.IsSuccessStatusCode) {
            return;
        }

        HttpApiErrorResponse? error = TryDeserialize<HttpApiErrorResponse>(body);
        if (error is not null && !string.IsNullOrWhiteSpace(error.Message)) {
            LastErrorMessage = error.Message;
            return;
        }

        LastErrorMessage = "HTTP request failed";
    }

    private async Task<T?> ReadJson<T>(HttpResponseMessage response) where T : class {
        try {
            return await response.Content.ReadFromJsonAsync<T>();
        } catch (JsonException e) {
            LastErrorMessage = $"Invalid JSON: {e.Message}";
        } catch (NotSupportedException e) {
            LastErrorMessage = $"Unsupported content: {e.Message}";
        }

        return null;
    }

    private static T? TryDeserialize<T>(string body) where T : class {
        try {
            return JsonSerializer.Deserialize<T>(body);
        } catch (JsonException) {
            return null;
        }
    }

    private static string PrettyPrintJson(string body) {
        if (string.IsNullOrWhiteSpace(body)) {
            return "{}";
        }

        try {
            using JsonDocument document = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(document, new JsonSerializerOptions {
                WriteIndented = true
            });
        } catch (JsonException) {
            return body;
        }
    }

    private static bool TryParseUInt32(string input, out uint result) {
        result = 0;
        if (string.IsNullOrWhiteSpace(input)) {
            return false;
        }

        string trimmed = input.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            return uint.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        return uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseByte(string input, out byte result) {
        result = 0;
        if (string.IsNullOrWhiteSpace(input)) {
            return false;
        }

        string trimmed = input.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            return byte.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        return byte.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParsePositiveInt(string input, out int result) {
        result = 0;
        if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) {
            return false;
        }

        if (parsed <= 0) {
            return false;
        }

        result = parsed;
        return true;
    }

    private static string ConvertToHexPreview(IReadOnlyList<byte> values) {
        if (values.Count == 0) {
            return "(empty)";
        }

        int previewLength = Math.Min(values.Count, 32);
        string[] chunks = new string[previewLength];
        for (int i = 0; i < previewLength; i++) {
            chunks[i] = $"{values[i]:X2}";
        }

        string preview = string.Join(' ', chunks);
        if (values.Count > previewLength) {
            return $"{preview} ... ({values.Count} bytes)";
        }

        return preview;
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }
}
