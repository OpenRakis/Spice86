namespace Spice86.Tests.Http;

using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Spice86.Core.Emulator.Http.Contracts;
using Spice86.Core.Emulator.Http.Controllers;

using Xunit;

[Collection(HttpApiServerCollection.Name)]
public sealed class HttpApiServerTests {
    private readonly HttpApiServerFixture _fixture;

    public HttpApiServerTests(HttpApiServerFixture fixture) {
        _fixture = fixture;
    }

    private void SeedMemory() {
        _fixture.Memory[0x40] = 0x12;
        _fixture.Memory[0x41] = 0x34;
        _fixture.Memory[0x42] = 0x56;
        _fixture.Memory[0x43] = 0x78;
    }

    [Fact]
    public async Task GetInfo_ReturnsApiMetadata() {
        SeedMemory();
        HttpResponseMessage response = await _fixture.HttpClient.GetAsync("/api");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpApiInfoResponse payload = await response.Content.ReadFromJsonAsync<HttpApiInfoResponse>()
            ?? throw new InvalidOperationException("Expected non-null payload");
        payload.Name.Should().Be("Spice86 HTTP API");
        payload.Endpoints.Should().Contain("/api/status");
        payload.Endpoints.Should().Contain("/api/memory/{address}/byte");
        payload.Endpoints.Should().Contain("/api/memory/{address}/range/{length}");
    }

    [Fact]
    public async Task GetStatus_ReturnsMachineState() {
        SeedMemory();
        HttpResponseMessage response = await _fixture.HttpClient.GetAsync("/api/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpApiStatusResponse payload = await response.Content.ReadFromJsonAsync<HttpApiStatusResponse>()
            ?? throw new InvalidOperationException("Expected non-null payload");
        payload.IsPaused.Should().BeFalse();
        payload.IsCpuRunning.Should().BeTrue();
        payload.Cs.Should().Be(0x1234);
        payload.Ip.Should().Be(0x5678);
        payload.Cycles.Should().Be(128);
        payload.MemorySizeBytes.Should().Be(_fixture.Memory.Length);
    }

    [Fact]
    public async Task GetByte_ReturnsByteAtAddress() {
        SeedMemory();
        HttpResponseMessage response = await _fixture.HttpClient.GetAsync("/api/memory/64/byte");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpApiMemoryByteResponse payload = await response.Content.ReadFromJsonAsync<HttpApiMemoryByteResponse>()
            ?? throw new InvalidOperationException("Expected non-null payload");
        payload.Address.Should().Be(64);
        payload.Value.Should().Be(0x12);
    }

    [Fact]
    public async Task PutByte_WritesAndReadsBackValue() {
        SeedMemory();
        HttpApiWriteByteRequest request = new() {
            Value = 0xAB
        };

        HttpResponseMessage putResponse = await _fixture.HttpClient.PutAsJsonAsync("/api/memory/64/byte", request);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpApiMemoryByteResponse putPayload = await putResponse.Content.ReadFromJsonAsync<HttpApiMemoryByteResponse>()
            ?? throw new InvalidOperationException("Expected non-null putPayload");
        putPayload.Value.Should().Be(0xAB);

        HttpResponseMessage getResponse = await _fixture.HttpClient.GetAsync("/api/memory/64/byte");
        HttpApiMemoryByteResponse getPayload = await getResponse.Content.ReadFromJsonAsync<HttpApiMemoryByteResponse>()
            ?? throw new InvalidOperationException("Expected non-null getPayload");
        getPayload.Value.Should().Be(0xAB);
        _fixture.Memory[0x40].Should().Be(0xAB);
    }

    [Fact]
    public async Task GetRange_ReturnsRequestedRange() {
        SeedMemory();
        HttpResponseMessage response = await _fixture.HttpClient.GetAsync("/api/memory/64/range/4");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpApiMemoryRangeResponse payload = await response.Content.ReadFromJsonAsync<HttpApiMemoryRangeResponse>()
            ?? throw new InvalidOperationException("Expected non-null payload");
        payload.Address.Should().Be(64);
        payload.Length.Should().Be(4);
        payload.Values.Should().Equal([0x12, 0x34, 0x56, 0x78]);
    }

    [Fact]
    public async Task GetRange_TruncatesAtMemoryEnd() {
        SeedMemory();
        int lastAddress = _fixture.Memory.Length - 2;
        _fixture.Memory[(uint)lastAddress] = 0x9A;
        _fixture.Memory[(uint)(lastAddress + 1)] = 0xBC;

        HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"/api/memory/{lastAddress}/range/16");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpApiMemoryRangeResponse payload = await response.Content.ReadFromJsonAsync<HttpApiMemoryRangeResponse>()
            ?? throw new InvalidOperationException("Expected non-null payload");
        payload.Length.Should().Be(2);
        payload.Values.Should().Equal([0x9A, 0xBC]);
    }

    [Fact]
    public async Task GetByte_WithNegativeAddress_ReturnsBadRequest() {
        SeedMemory();
        HttpResponseMessage response = await _fixture.HttpClient.GetAsync("/api/memory/-1/byte");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        HttpApiErrorResponse payload = await response.Content.ReadFromJsonAsync<HttpApiErrorResponse>()
            ?? throw new InvalidOperationException("Expected non-null payload");
        payload.Message.Should().Contain("between 0 and 4294967295");
    }

    [Fact]
    public async Task GetByte_WithAddressTooLarge_ReturnsBadRequest() {
        SeedMemory();
        HttpResponseMessage response = await _fixture.HttpClient.GetAsync("/api/memory/4294967296/byte");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        HttpApiErrorResponse payload = await response.Content.ReadFromJsonAsync<HttpApiErrorResponse>()
            ?? throw new InvalidOperationException("Expected non-null payload");
        payload.Message.Should().Contain("between 0 and 4294967295");
    }

    [Fact]
    public async Task GetByte_WithOutOfRangeAddress_ReturnsNotFound() {
        SeedMemory();
        HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"/api/memory/{_fixture.Memory.Length}/byte");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        HttpApiErrorResponse payload = await response.Content.ReadFromJsonAsync<HttpApiErrorResponse>()
            ?? throw new InvalidOperationException("Expected non-null payload");
        payload.Message.Should().Be("address is outside of memory range");
    }

    [Fact]
    public async Task GetRange_WithExcessiveLength_ReturnsBadRequest() {
        SeedMemory();
        int excessiveLength = HttpApiMemoryController.MaxRangeLength + 1;
        HttpResponseMessage response = await _fixture.HttpClient.GetAsync($"/api/memory/64/range/{excessiveLength}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        HttpApiErrorResponse payload = await response.Content.ReadFromJsonAsync<HttpApiErrorResponse>()
            ?? throw new InvalidOperationException("Expected non-null payload");
        payload.Message.Should().Contain($"{HttpApiMemoryController.MaxRangeLength}");
    }

    [Fact]
    public async Task GetRange_WithInvalidLength_ReturnsBadRequest() {
        SeedMemory();
        HttpResponseMessage response = await _fixture.HttpClient.GetAsync("/api/memory/64/range/0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        HttpApiErrorResponse payload = await response.Content.ReadFromJsonAsync<HttpApiErrorResponse>()
            ?? throw new InvalidOperationException("Expected non-null payload");
        payload.Message.Should().Be("length must be greater than 0");
    }

    [Fact]
    public async Task PutByte_WithOutOfRangeAddress_ReturnsNotFound() {
        SeedMemory();
        HttpApiWriteByteRequest request = new() {
            Value = 0xEF
        };

        HttpResponseMessage response = await _fixture.HttpClient.PutAsJsonAsync($"/api/memory/{_fixture.Memory.Length}/byte", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        HttpApiErrorResponse payload = await response.Content.ReadFromJsonAsync<HttpApiErrorResponse>()
            ?? throw new InvalidOperationException("Expected non-null payload");
        payload.Message.Should().Be("address is outside of memory range");
    }
}
