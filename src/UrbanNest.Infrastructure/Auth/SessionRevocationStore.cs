using UrbanNest.Application.Auth;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace UrbanNest.Infrastructure.Auth;

/// <summary>
/// EF-backed <see cref="ISessionRevocationStore"/> (Req 1.3). One indexed, <c>AsNoTracking</c> read
/// on <c>LoginSession.TokenId</c> per authenticated request: a token authenticates only while its
/// session row exists and is not revoked. Logout stamps <c>RevokedAtUtc</c>, so the very next
/// request with that token fails here (steering: efficient session-revocation check).
/// </summary>
public sealed class SessionRevocationStore(AppDbContext db) : ISessionRevocationStore
{
    public async Task<bool> IsSessionActiveAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenId))
        {
            return false;
        }

        return await db.LoginSessions
            .AsNoTracking()
            .AnyAsync(s => s.TokenId == tokenId && s.RevokedAtUtc == null, cancellationToken);
    }
}
