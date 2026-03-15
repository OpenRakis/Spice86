namespace Spice86.Core.Emulator.Http;

using System.Net;

/// <summary>
/// Exception thrown by HTTP API controllers when a request cannot be fulfilled.
/// Caught by the exception-handling middleware and translated
/// into the appropriate HTTP status code with a JSON error body.
/// </summary>
public sealed class HttpApiException : Exception {
    /// <summary>HTTP status code to return to the client.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Initializes a new instance of <see cref="HttpApiException"/>.</summary>
    /// <param name="statusCode">HTTP status code to return.</param>
    /// <param name="message">Human-readable error message included in the response body.</param>
    public HttpApiException(HttpStatusCode statusCode, string message) : base(message) {
        StatusCode = statusCode;
    }
}
