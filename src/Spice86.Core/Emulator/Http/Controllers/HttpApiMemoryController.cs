namespace Spice86.Core.Emulator.Http.Controllers;

using Microsoft.AspNetCore.Mvc;

using Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// Memory read and write endpoints.
/// </summary>
[ApiController]
[Route("api/memory")]
public sealed class HttpApiMemoryController : ControllerBase {
    private readonly HttpApiState _httpApiState;

    public HttpApiMemoryController(HttpApiState httpApiState) {
        _httpApiState = httpApiState;
    }

    [HttpGet("{address:uint}/byte")]
    public ActionResult<HttpApiMemoryByteResponse> GetByte(uint address) {
        if (address >= _httpApiState.Memory.Length) {
            return NotFound(new HttpApiErrorResponse("address is outside of memory range"));
        }

        byte value = _httpApiState.Memory[address];
        HttpApiMemoryByteResponse response = new(address, value);
        return Ok(response);
    }

    [HttpPut("{address:uint}/byte")]
    public ActionResult<HttpApiMemoryByteResponse> PutByte(uint address, [FromBody] HttpApiWriteByteRequest request) {
        if (address >= _httpApiState.Memory.Length) {
            return NotFound(new HttpApiErrorResponse("address is outside of memory range"));
        }

        _httpApiState.Memory[address] = request.Value;
        HttpApiMemoryByteResponse response = new(address, request.Value);
        return Ok(response);
    }

    [HttpGet("{address:uint}/range/{length:int}")]
    public ActionResult<HttpApiMemoryRangeResponse> GetRange(uint address, int length) {
        if (length <= 0) {
            return BadRequest(new HttpApiErrorResponse("length must be greater than 0"));
        }

        if (address >= _httpApiState.Memory.Length) {
            return NotFound(new HttpApiErrorResponse("address is outside of memory range"));
        }

        long readableLength = _httpApiState.Memory.Length - address;
        if (readableLength <= 0) {
            return NotFound(new HttpApiErrorResponse("address is outside of memory range"));
        }

        int boundedLength = (int)Math.Min(length, readableLength);
        byte[] values = new byte[boundedLength];
        for (int i = 0; i < boundedLength; i++) {
            values[i] = _httpApiState.Memory[address + (uint)i];
        }

        HttpApiMemoryRangeResponse response = new(address, boundedLength, values);
        return Ok(response);
    }
}
