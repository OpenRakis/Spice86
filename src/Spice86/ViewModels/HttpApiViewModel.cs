namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Http;

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

public partial class HttpApiViewModel : ViewModelBase, IDisposable {
    private readonly HttpClient _httpClient;
    private bool _disposed;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _baseUrl;

    [ObservableProperty]
    private string _status = "Not checked";

    [ObservableProperty]
    private string _cpuPointer = "----:----";

    [ObservableProperty]
    private long _cycles;

    [ObservableProperty]
    private string _memoryAddressInput = "0x00000";

    [ObservableProperty]
    private string _memoryValueInput = "0x00";

    [ObservableProperty]
    private string _lastReadValue = "n/a";

    [ObservableProperty]
    private string _lastMessage = "Ready";

    public HttpApiViewModel() {
        _httpClient = new HttpClient();
        BaseUrl = HttpApiEndpoint.BaseUrl;
        IsEnabled = false;
        _httpClient.Timeout = TimeSpan.FromSeconds(2);
    }

    public HttpApiViewModel(bool isEnabled) {
        _httpClient = new HttpClient();
        BaseUrl = HttpApiEndpoint.BaseUrl;
        IsEnabled = isEnabled;
        _httpClient.Timeout = TimeSpan.FromSeconds(2);
    }

    [RelayCommand]
    private async Task RefreshStatus() {
        if (!IsEnabled) {
            LastMessage = "HTTP API unavailable";
            return;
        }

        try {
            HttpApiStatusResponse? response = await _httpClient.GetFromJsonAsync<HttpApiStatusResponse>(
                $"{BaseUrl}/api/status");
            if (response is null) {
                LastMessage = "No status response";
                return;
            }

            Status = response.IsPaused ? "Paused" : "Running";
            CpuPointer = $"{response.Cs:X4}:{response.Ip:X4}";
            Cycles = response.Cycles;
            LastMessage = $"Status updated at {DateTime.Now:HH:mm:ss}";
        } catch (HttpRequestException e) {
            LastMessage = $"HTTP error: {e.Message}";
        } catch (TaskCanceledException) {
            LastMessage = "HTTP timeout";
        } catch (JsonException e) {
            LastMessage = $"Invalid JSON: {e.Message}";
        } catch (InvalidOperationException e) {
            LastMessage = $"Request error: {e.Message}";
        }
    }

    [RelayCommand]
    private async Task ReadByte() {
        if (!TryParseUInt32(MemoryAddressInput, out uint address)) {
            LastMessage = "Invalid address";
            return;
        }

        if (!IsEnabled) {
            LastMessage = "HTTP API unavailable";
            return;
        }

        try {
            HttpApiMemoryByteResponse? response = await _httpClient.GetFromJsonAsync<HttpApiMemoryByteResponse>(
                $"{BaseUrl}/api/memory/{address}/byte");
            if (response is null) {
                LastMessage = "No memory response";
                return;
            }

            LastReadValue = $"0x{response.Value:X2}";
            LastMessage = "Memory read succeeded";
        } catch (HttpRequestException e) {
            LastMessage = $"HTTP error: {e.Message}";
        } catch (TaskCanceledException) {
            LastMessage = "HTTP timeout";
        } catch (JsonException e) {
            LastMessage = $"Invalid JSON: {e.Message}";
        } catch (InvalidOperationException e) {
            LastMessage = $"Request error: {e.Message}";
        }
    }

    [RelayCommand]
    private async Task WriteByte() {
        if (!TryParseUInt32(MemoryAddressInput, out uint address)) {
            LastMessage = "Invalid address";
            return;
        }

        if (!TryParseByte(MemoryValueInput, out byte value)) {
            LastMessage = "Invalid byte value";
            return;
        }

        if (!IsEnabled) {
            LastMessage = "HTTP API unavailable";
            return;
        }

        try {
            HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
                $"{BaseUrl}/api/memory/{address}/byte",
                new HttpApiWriteByteRequest(value));
            if (!response.IsSuccessStatusCode) {
                LastMessage = $"Write failed: {(int)response.StatusCode} {response.ReasonPhrase}";
                return;
            }

            LastMessage = "Memory write succeeded";
        } catch (HttpRequestException e) {
            LastMessage = $"HTTP error: {e.Message}";
        } catch (TaskCanceledException) {
            LastMessage = "HTTP timeout";
        } catch (InvalidOperationException e) {
            LastMessage = $"Request error: {e.Message}";
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

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    private sealed record HttpApiWriteByteRequest(byte Value);

    private sealed record HttpApiStatusResponse(bool IsPaused, bool IsCpuRunning, long Cycles, ushort Cs, ushort Ip);

    private sealed record HttpApiMemoryByteResponse(uint Address, byte Value);
}
