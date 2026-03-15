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

    [HttpGet("{address:long}/byte")]
    public ActionResult<HttpApiMemoryByteResponse> GetByte(long address) {
        if (!TryValidateAddress(address, out uint validatedAddress, out ActionResult? error)) {
            if (error is not null) {
                return error;
            }

            return BadRequest(new HttpApiErrorResponse("invalid address"));
        }

        byte value = _httpApiState.Memory[validatedAddress];
        HttpApiMemoryByteResponse response = new(validatedAddress, value);
        return Ok(response);
    }

    [HttpPut("{address:long}/byte")]
    public ActionResult<HttpApiMemoryByteResponse> PutByte(long address, [FromBody] HttpApiWriteByteRequest request) {
        if (!TryValidateAddress(address, out uint validatedAddress, out ActionResult? error)) {
            if (error is not null) {
                return error;
            }

            return BadRequest(new HttpApiErrorResponse("invalid address"));
        }

        _httpApiState.Memory[validatedAddress] = request.Value;
        HttpApiMemoryByteResponse response = new(validatedAddress, request.Value);
        return Ok(response);
    }

    [HttpGet("{address:long}/range/{length:int}")]
    public ActionResult<HttpApiMemoryRangeResponse> GetRange(long address, int length) {
        if (length <= 0) {
            return BadRequest(new HttpApiErrorResponse("length must be greater than 0"));
        }

        if (!TryValidateAddress(address, out uint validatedAddress, out ActionResult? error)) {
            if (error is not null) {
                return error;
            }

            return BadRequest(new HttpApiErrorResponse("invalid address"));
        }

        long readableLength = _httpApiState.Memory.Length - validatedAddress;
        if (readableLength <= 0) {
            return NotFound(new HttpApiErrorResponse("address is outside of memory range"));
        }

        int boundedLength = (int)Math.Min(length, readableLength);
        byte[] values = new byte[boundedLength];
        for (int i = 0; i < boundedLength; i++) {
            values[i] = _httpApiState.Memory[validatedAddress + (uint)i];
        }

        HttpApiMemoryRangeResponse response = new(validatedAddress, boundedLength, values);
        return Ok(response);
    }

    private bool TryValidateAddress(long address, out uint validatedAddress, out ActionResult? error) {
        validatedAddress = 0;
        error = null;

        if (address < 0 || address > uint.MaxValue) {
            error = BadRequest(new HttpApiErrorResponse("address must be between 0 and 4294967295"));
            return false;
        }

        validatedAddress = (uint)address;
        if (validatedAddress >= _httpApiState.Memory.Length) {
            error = NotFound(new HttpApiErrorResponse("address is outside of memory range"));
            return false;
        }

        return true;
    }
}
