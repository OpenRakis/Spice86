namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Http;

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

public partial class HttpApiViewModel : ViewModelBase, IDisposable {
    private const string AvailabilityOnlineText = "Online";
    private const string AvailabilityOfflineText = "Offline";
    private const string StatusRunningText = "Running";
    private const string StatusPausedText = "Paused";
    private const string StatusUnknownText = "Unknown";

    private readonly HttpClient _httpClient;
    private bool _disposed;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _baseUrl = HttpApiEndpoint.BaseUrl;

    [ObservableProperty]
    private string _status = StatusUnknownText;

    [ObservableProperty]
    private string _cpuPointer = "----:----";

    [ObservableProperty]
    private long _cycles;

    [ObservableProperty]
    private string _memoryAddressInput = "0x00000";

    [ObservableProperty]
    private string _memoryValueInput = "0x00";

    [ObservableProperty]
    private string _rangeLengthInput = "16";

    [ObservableProperty]
    private string _lastReadValue = "n/a";

    [ObservableProperty]
    private string _lastRangeValue = "n/a";

    [ObservableProperty]
    private string _lastMessage = "Ready";

    public MemoryViewModel? MemoryViewModel { get; private set; }

    public bool HasMemoryView => MemoryViewModel is not null;

    public bool IsMemoryViewUnavailable => !HasMemoryView;

    public bool IsDisabled => !IsEnabled;

    public string WindowTitleText => "Spice86 REST Console";

    public string WindowSubtitleText => "Postman-style controls for Spice86 local REST API";

    public string AvailabilityHeaderText => "Availability";

    public string AvailabilityText => IsEnabled ? AvailabilityOnlineText : AvailabilityOfflineText;

    public string EndpointHeaderText => "Endpoint";

    public string SessionPanelTitleText => "Session Snapshot";

    public string RequestPanelTitleText => "Request Builder";

    public string ResponsePanelTitleText => "Response";

    public string MemoryPanelTitleText => "Live Memory";

    public string MemoryUnavailableText => "Memory view is not available in this context.";

    public string AddressLabelText => "Address";

    public string ByteValueLabelText => "Byte Value";

    public string RangeLengthLabelText => "Range Length";

    public string AddressWatermarkText => "0x00000";

    public string ByteValueWatermarkText => "0x00";

    public string RangeLengthWatermarkText => "16";

    public string StateLabelText => "State";

    public string CpuPointerLabelText => "CS:IP";

    public string CyclesLabelText => "Cycles";

    public string LastEventLabelText => "Latest event";

    public string LastByteLabelText => "Last byte";

    public string LastRangeLabelText => "Last range";

    public string RefreshButtonText => "GET Status";

    public string ReadByteButtonText => "GET Byte";

    public string WriteByteButtonText => "PUT Byte";

    public string ReadRangeButtonText => "GET Range";

    public string RoutesTitleText => "Route Catalog";

    public string StateDisplayText => $"{StateLabelText}: {Status}";

    public string CpuPointerDisplayText => $"{CpuPointerLabelText}: {CpuPointer}";

    public string CyclesDisplayText => $"{CyclesLabelText}: {Cycles:N0}";

    public string LastByteDisplayText => $"{LastByteLabelText}: {LastReadValue}";

    public string LastRangeDisplayText => $"{LastRangeLabelText}: {LastRangeValue}";

    public string LastEventDisplayText => $"{LastEventLabelText}: {LastMessage}";

    public bool IsStatusRunning => string.Equals(Status, StatusRunningText, StringComparison.Ordinal);

    public bool IsStatusPaused => string.Equals(Status, StatusPausedText, StringComparison.Ordinal);

    public bool IsStatusUnknown => !IsStatusRunning && !IsStatusPaused;

    public string StatusGetRouteText => $"GET {BaseUrl}/api/status";

    public string ReadByteRouteText => $"GET {BaseUrl}/api/memory/{GetRouteAddressToken()}/byte";

    public string PutByteRouteText => $"PUT {BaseUrl}/api/memory/{GetRouteAddressToken()}/byte";

    public string ReadRangeRouteText =>
        $"GET {BaseUrl}/api/memory/{GetRouteAddressToken()}/range/{GetRouteRangeToken()}";

    public HttpApiViewModel() {
        _httpClient = new HttpClient();
        InitializeHttpApiState(false, null);
    }

    public HttpApiViewModel(bool isEnabled) {
        _httpClient = new HttpClient();
        InitializeHttpApiState(isEnabled, null);
    }

    public HttpApiViewModel(bool isEnabled, MemoryViewModel memoryViewModel) {
        _httpClient = new HttpClient();
        InitializeHttpApiState(isEnabled, memoryViewModel);
    }

    private void InitializeHttpApiState(bool isEnabled, MemoryViewModel? memoryViewModel) {
        IsEnabled = isEnabled;
        MemoryViewModel = memoryViewModel;
        OnPropertyChanged(nameof(HasMemoryView));
        OnPropertyChanged(nameof(IsMemoryViewUnavailable));
        _httpClient.Timeout = TimeSpan.FromSeconds(2);
        if (MemoryViewModel is not null && !string.IsNullOrWhiteSpace(MemoryViewModel.StartAddress)) {
            MemoryAddressInput = MemoryViewModel.StartAddress;
        }
    }

    [RelayCommand]
    private async Task RefreshStatus() {
        if (!IsEnabled) {
            Status = StatusUnknownText;
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

            Status = response.IsPaused ? StatusPausedText : StatusRunningText;
            CpuPointer = $"{response.Cs:X4}:{response.Ip:X4}";
            Cycles = response.Cycles;
            LastMessage = $"Status updated at {DateTime.Now:HH:mm:ss}";
        } catch (HttpRequestException e) {
            Status = StatusUnknownText;
            LastMessage = $"HTTP error: {e.Message}";
        } catch (TaskCanceledException) {
            Status = StatusUnknownText;
            LastMessage = "HTTP timeout";
        } catch (JsonException e) {
            Status = StatusUnknownText;
            LastMessage = $"Invalid JSON: {e.Message}";
        } catch (InvalidOperationException e) {
            Status = StatusUnknownText;
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
    private async Task ReadRange() {
        if (!TryParseUInt32(MemoryAddressInput, out uint address)) {
            LastMessage = "Invalid address";
            return;
        }

        if (!TryParsePositiveInt(RangeLengthInput, out int rangeLength)) {
            LastMessage = "Invalid range length";
            return;
        }

        if (!IsEnabled) {
            LastMessage = "HTTP API unavailable";
            return;
        }

        try {
            HttpApiMemoryRangeResponse? response = await _httpClient.GetFromJsonAsync<HttpApiMemoryRangeResponse>(
                $"{BaseUrl}/api/memory/{address}/range/{rangeLength}");
            if (response is null) {
                LastMessage = "No memory range response";
                return;
            }

            LastRangeValue = ConvertToHexPreview(response.Values);
            LastMessage = $"Memory range read succeeded ({response.Length} bytes)";
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

    private static string ConvertToHexPreview(byte[] values) {
        if (values.Length == 0) {
            return "(empty)";
        }

        int previewLength = Math.Min(values.Length, 32);
        string[] chunks = new string[previewLength];
        for (int i = 0; i < previewLength; i++) {
            chunks[i] = $"{values[i]:X2}";
        }

        string preview = string.Join(' ', chunks);
        if (values.Length > previewLength) {
            return $"{preview} ... ({values.Length} bytes)";
        }

        return preview;
    }

    private string GetRouteAddressToken() {
        if (string.IsNullOrWhiteSpace(MemoryAddressInput)) {
            return "{address}";
        }

        return MemoryAddressInput.Trim();
    }

    private string GetRouteRangeToken() {
        if (string.IsNullOrWhiteSpace(RangeLengthInput)) {
            return "{length}";
        }

        return RangeLengthInput.Trim();
    }

    partial void OnIsEnabledChanged(bool value) {
        OnPropertyChanged(nameof(AvailabilityText));
        OnPropertyChanged(nameof(IsDisabled));
    }

    partial void OnStatusChanged(string value) {
        OnPropertyChanged(nameof(IsStatusRunning));
        OnPropertyChanged(nameof(IsStatusPaused));
        OnPropertyChanged(nameof(IsStatusUnknown));
        OnPropertyChanged(nameof(StateDisplayText));
    }

    partial void OnBaseUrlChanged(string value) {
        OnPropertyChanged(nameof(StatusGetRouteText));
        OnPropertyChanged(nameof(ReadByteRouteText));
        OnPropertyChanged(nameof(PutByteRouteText));
        OnPropertyChanged(nameof(ReadRangeRouteText));
    }

    partial void OnMemoryAddressInputChanged(string value) {
        OnPropertyChanged(nameof(ReadByteRouteText));
        OnPropertyChanged(nameof(PutByteRouteText));
        OnPropertyChanged(nameof(ReadRangeRouteText));
    }

    partial void OnRangeLengthInputChanged(string value) {
        OnPropertyChanged(nameof(ReadRangeRouteText));
    }

    partial void OnCpuPointerChanged(string value) {
        OnPropertyChanged(nameof(CpuPointerDisplayText));
    }

    partial void OnCyclesChanged(long value) {
        OnPropertyChanged(nameof(CyclesDisplayText));
    }

    partial void OnLastReadValueChanged(string value) {
        OnPropertyChanged(nameof(LastByteDisplayText));
    }

    partial void OnLastRangeValueChanged(string value) {
        OnPropertyChanged(nameof(LastRangeDisplayText));
    }

    partial void OnLastMessageChanged(string value) {
        OnPropertyChanged(nameof(LastEventDisplayText));
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

    private sealed record HttpApiMemoryRangeResponse(uint Address, int Length, byte[] Values);
}
