using DmOrder.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace DmOrder.Application.Features.Auth;

public sealed record RefreshRequest(string RefreshToken);

public sealed class RefreshHandler(ITokenService tokens, ILogger<RefreshHandler> logger)
{
    public async Task<AuthResponse> HandleAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await tokens.RefreshAsync(request.RefreshToken, cancellationToken);

        if (result is null)
        {
            logger.LogWarning("Refresh rejected: token unknown, expired or revoked");
            throw new UnauthorizedAccessException("The session has expired. Please sign in again.");
        }

        return result.ToResponse();
    }
}

public sealed class LogoutHandler(ITokenService tokens)
{
    /// <summary>
    /// Idempotent by design: logging out twice, or with a token the server has never seen, succeeds
    /// quietly. A logout endpoint that reports "unknown token" is another enumeration oracle.
    /// </summary>
    public Task HandleAsync(RefreshRequest request, CancellationToken cancellationToken) =>
        tokens.RevokeAsync(request.RefreshToken, cancellationToken);
}
