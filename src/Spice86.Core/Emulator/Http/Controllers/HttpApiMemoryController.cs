namespace Spice86.Core.Emulator.Http.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.Http.Contracts;
using Spice86.Core.Emulator.VM;

using System.Net;
using System.Threading;

/// <summary>
/// Memory read and write endpoints.
/// </summary>
[ApiController]
[Route("api/memory")]
public sealed class HttpApiMemoryController : HttpApiControllerBase {
    /// <summary>Operation ID for the <see cref="GetByte"/> endpoint.</summary>
    public const string GetByteOperationId = "HttpApiMemoryController.GetByte";

    /// <summary>Operation ID for the <see cref="PutByte"/> endpoint.</summary>
    public const string PutByteOperationId = "HttpApiMemoryController.PutByte";

    /// <summary>Operation ID for the <see cref="GetRange"/> endpoint.</summary>
    public const string GetRangeOperationId = "HttpApiMemoryController.GetRange";

    /// <summary>Initializes a new instance of <see cref="HttpApiMemoryController"/>.</summary>
    /// <param name="httpApiState">Shared emulator state injected by the DI container.</param>
    /// <param name="pauseHandler">Pause handler used by HTTP operations.</param>
    /// <param name="logger">Logger instance.</param>
    public HttpApiMemoryController(HttpApiState httpApiState, IPauseHandler pauseHandler,
        ILogger<HttpApiMemoryController> logger) : base(httpApiState, pauseHandler, logger) {
    }

    /// <summary>Reads a single byte from emulator memory at the given physical address.</summary>
    /// <param name="address">Physical memory address (0 – <see cref="uint.MaxValue"/>).</param>
    /// <param name="cancellationToken">Cancellation token for the current HTTP request.</param>
    /// <returns>200 OK with <see cref="HttpApiMemoryByteResponse"/>, 400 if the address is out of range, or 404 if it exceeds memory size.</returns>
    [HttpGet("{address:long}/byte")]
    public ActionResult<HttpApiMemoryByteResponse> GetByte(long address, CancellationToken cancellationToken) {
        uint validAddress = ValidateAddress(address);
        return WhilePaused($"GET /api/memory/0x{validAddress:X}/byte", cancellationToken,
            () => new HttpApiMemoryByteResponse(validAddress, HttpApiState.Memory[validAddress]));
    }

    /// <summary>Writes a single byte to emulator memory at the given physical address.</summary>
    /// <param name="address">Physical memory address (0 – <see cref="uint.MaxValue"/>).</param>
    /// <param name="request">Request body carrying the byte value to write.</param>
    /// <param name="cancellationToken">Cancellation token for the current HTTP request.</param>
    /// <returns>200 OK with the updated <see cref="HttpApiMemoryByteResponse"/>, 400 if the address is out of range or the request body is missing, or 404 if address exceeds memory size.</returns>
    [HttpPut("{address:long}/byte")]
    public ActionResult<HttpApiMemoryByteResponse> PutByte(long address, [FromBody] HttpApiWriteByteRequest? request, CancellationToken cancellationToken) {
        if (request is null) {
            throw new HttpApiException(HttpStatusCode.BadRequest, "request body is required");
        }

        uint validAddress = ValidateAddress(address);
        return WhilePaused($"PUT /api/memory/0x{validAddress:X}/byte", cancellationToken, () => {
            HttpApiState.Memory[validAddress] = request.Value;
            return new HttpApiMemoryByteResponse(validAddress, request.Value);
        });
    }

    /// <summary>Reads a contiguous range of bytes from emulator memory.</summary>
    /// <param name="address">Physical start address (0 – <see cref="uint.MaxValue"/>).</param>
    /// <param name="length">Number of bytes to read; must be between 1 and <see cref="HttpApiEndpoint.MaxRangeLength"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the current HTTP request.</param>
    /// <returns>200 OK with <see cref="HttpApiMemoryRangeResponse"/> (length may be clamped to memory boundary), 400 for invalid arguments, or 404 if the address exceeds memory size.</returns>
    [HttpGet("{address:long}/range/{length:int}")]
    public ActionResult<HttpApiMemoryRangeResponse> GetRange(long address, int length, CancellationToken cancellationToken) {
        if (length <= 0) {
            throw new HttpApiException(HttpStatusCode.BadRequest, "length must be greater than 0");
        }

        if (length > HttpApiEndpoint.MaxRangeLength) {
            throw new HttpApiException(HttpStatusCode.BadRequest, $"length must not exceed {HttpApiEndpoint.MaxRangeLength}");
        }

        uint validAddress = ValidateAddress(address);
        return WhilePaused($"GET /api/memory/0x{validAddress:X}/range/{length}", cancellationToken, () => {
            long readableLength = (long)HttpApiState.Memory.Length - validAddress;
            int boundedLength = (int)Math.Min(length, readableLength);
            byte[] values = new byte[boundedLength];
            for (int i = 0; i < boundedLength; i++) {
                if ((i & 0xFF) == 0) {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                values[i] = HttpApiState.Memory[validAddress + (uint)i];
            }
            return new HttpApiMemoryRangeResponse(validAddress, boundedLength, values);
        });
    }

    /// <summary>Validates that <paramref name="address"/> is within the valid memory range.</summary>
    /// <exception cref="HttpApiException">400 if the value is outside the uint range; 404 if it exceeds memory size.</exception>
    private uint ValidateAddress(long address) {
        if (address is < 0 or > uint.MaxValue) {
            throw new HttpApiException(HttpStatusCode.BadRequest, $"address must be between 0 and {uint.MaxValue}");
        }

        if (address >= HttpApiState.Memory.Length) {
            throw new HttpApiException(HttpStatusCode.NotFound, "address is outside of memory range");
        }

        return (uint)address;
    }
}
