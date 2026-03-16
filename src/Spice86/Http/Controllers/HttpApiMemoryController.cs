namespace Spice86.Http.Controllers;

using Microsoft.AspNetCore.Mvc;

using Spice86.Core.Emulator.Http;
using Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// Memory read and write endpoints.
/// </summary>
[ApiController]
[Route("api/memory")]
public sealed class HttpApiMemoryController : ControllerBase {
    private readonly HttpApiState _httpApiState;

    /// <summary>Initializes a new instance of <see cref="HttpApiMemoryController"/>.</summary>
    /// <param name="httpApiState">Shared emulator state injected by the DI container.</param>
    public HttpApiMemoryController(HttpApiState httpApiState) {
        _httpApiState = httpApiState;
    }

    /// <summary>Reads a single byte from emulator memory at the given physical address.</summary>
    /// <param name="address">Post-A20 physical memory address (0 – <see cref="uint.MaxValue"/>). No A20 wrapping is applied; callers must supply the already-transformed address.</param>
    /// <returns>200 OK with <see cref="HttpApiMemoryByteResponse"/>, 400 if the address is out of range, or 404 if it exceeds memory size.</returns>
    [HttpGet("{address:long}/byte")]
    public ActionResult<HttpApiMemoryByteResponse> GetByte(long address) {
        ActionResult? error = ValidateAddress(address, out uint validatedAddress);
        if (error is not null) {
            return error;
        }

        byte value = _httpApiState.Memory.SneakilyRead(validatedAddress);
        HttpApiMemoryByteResponse response = new(validatedAddress, value);
        return Ok(response);
    }

    /// <summary>Writes a single byte to emulator memory at the given physical address.</summary>
    /// <param name="address">Post-A20 physical memory address (0 – <see cref="uint.MaxValue"/>). No A20 wrapping is applied; callers must supply the already-transformed address.</param>
    /// <param name="request">Request body carrying the byte value to write.</param>
    /// <returns>200 OK with the updated <see cref="HttpApiMemoryByteResponse"/>, 400 if the address is out of range or the request body is missing, 404 if address exceeds memory size, or 409 if the emulator is not paused.</returns>
    [HttpPut("{address:long}/byte")]
    public ActionResult<HttpApiMemoryByteResponse> PutByte(long address, [FromBody] HttpApiWriteByteRequest? request) {
        if (request is null) {
            return BadRequest(new HttpApiErrorResponse("request body is required"));
        }

        ActionResult? error = ValidateAddress(address, out uint validatedAddress);
        if (error is not null) {
            return error;
        }

        if (!_httpApiState.PauseHandler.IsPaused) {
            return Conflict(new HttpApiErrorResponse("emulator must be paused to write memory"));
        }

        _httpApiState.Memory.SneakilyWrite(validatedAddress, request.Value);
        HttpApiMemoryByteResponse response = new(validatedAddress, request.Value);
        return Ok(response);
    }

    /// <summary>Reads a contiguous range of bytes from emulator memory.</summary>
    /// <param name="address">Post-A20 physical start address (0 – <see cref="uint.MaxValue"/>). No A20 wrapping is applied; callers must supply the already-transformed address.</param>
    /// <param name="length">Number of bytes to read; must be between 1 and <see cref="HttpApiEndpoint.MaxRangeLength"/>.</param>
    /// <returns>200 OK with <see cref="HttpApiMemoryRangeResponse"/> (length may be clamped to memory boundary), 400 for invalid arguments, or 404 if the address exceeds memory size.</returns>
    [HttpGet("{address:long}/range/{length:int}")]
    public ActionResult<HttpApiMemoryRangeResponse> GetRange(long address, int length) {
        if (length <= 0) {
            return BadRequest(new HttpApiErrorResponse("length must be greater than 0"));
        }

        if (length > HttpApiEndpoint.MaxRangeLength) {
            return BadRequest(new HttpApiErrorResponse($"length must not exceed {HttpApiEndpoint.MaxRangeLength}"));
        }

        ActionResult? error = ValidateAddress(address, out uint validatedAddress);
        if (error is not null) {
            return error;
        }

        long readableLength = (long)_httpApiState.Memory.Length - validatedAddress;
        int boundedLength = (int)Math.Min(length, readableLength);
        byte[] values = _httpApiState.Memory.ReadRam((uint)boundedLength, validatedAddress);

        HttpApiMemoryRangeResponse response = new(validatedAddress, boundedLength, values);
        return Ok(response);
    }

    private ActionResult? ValidateAddress(long address, out uint validatedAddress) {
        validatedAddress = 0;

        if (address < 0 || address > uint.MaxValue) {
            return BadRequest(new HttpApiErrorResponse($"address must be between 0 and {uint.MaxValue}"));
        }

        validatedAddress = (uint)address;
        if ((long)validatedAddress >= _httpApiState.Memory.Length) {
            return NotFound(new HttpApiErrorResponse("address is outside of memory range"));
        }

        return null;
    }
}
