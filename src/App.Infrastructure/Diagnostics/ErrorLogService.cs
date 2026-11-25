using App.Application.Abstractions.Security;
using App.Application.Diagnostics;
using App.Application.Querying;
using App.Contracts.Diagnostics;
using App.Contracts.Envelope;
using App.Domain.Diagnostics;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Diagnostics;

public sealed class ErrorLogService(AppDbContext db, CurrentUser currentUser) : IErrorLogService
{
    public async Task<PagedData<ErrorLogSummaryDto>> QueryAsync(
        ListQueryParameters parameters,
        int? statusCode = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ErrorLog> query = db.ErrorLogs.AsNoTracking();

        // Tenant scope check:
        // Platform SuperAdmin can view all error logs.
        // OrganizationAdmin sees error logs belonging to their organization communities.
        // CommunityAdmin sees error logs for their specific community.
        if (!currentUser.IsSuperAdmin)
        {
            if (currentUser.IsOrganizationAdmin && currentUser.OrganizationId.HasValue)
            {
                var orgId = currentUser.OrganizationId.Value;
                query = query.Where(e => e.OrganizationId == orgId || (e.CommunityId.HasValue && currentUser.AccessibleCommunityIds.Contains(e.CommunityId.Value)));
            }
            else if (currentUser.CommunityId.HasValue)
            {
                var commId = currentUser.CommunityId.Value;
                query = query.Where(e => e.CommunityId == commId);
            }
            else if (currentUser.UserId.HasValue)
            {
                var uid = currentUser.UserId.Value;
                query = query.Where(e => e.UserId == uid);
            }
        }

        if (statusCode.HasValue)
        {
            query = query.Where(e => e.StatusCode == statusCode.Value);
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(e => e.CreatedAtUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(e => e.CreatedAtUtc <= toUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(parameters.Search))
        {
            var search = parameters.Search.Trim();
            query = query.Where(e =>
                e.ApiPath.Contains(search) ||
                e.ErrorMessage.Contains(search) ||
                e.ExceptionType.Contains(search) ||
                (e.FileName != null && e.FileName.Contains(search)) ||
                (e.ControllerOrFunction != null && e.ControllerOrFunction.Contains(search)) ||
                (e.UserName != null && e.UserName.Contains(search)));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        var page = parameters.Page is > 0 ? parameters.Page.Value : 1;
        var pageSize = parameters.PageSize is > 0 and <= 200 ? parameters.PageSize.Value : 20;

        var items = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new ErrorLogSummaryDto
            {
                Id = e.Id,
                CreatedAtUtc = e.CreatedAtUtc,
                CorrelationId = e.CorrelationId,
                ApiPath = e.ApiPath,
                HttpMethod = e.HttpMethod,
                StatusCode = e.StatusCode,
                ExceptionType = e.ExceptionType,
                ErrorMessage = e.ErrorMessage,
                FileName = e.FileName,
                ControllerOrFunction = e.ControllerOrFunction,
                LineNumber = e.LineNumber,
                UserId = e.UserId,
                UserName = e.UserName,
                UserRole = e.UserRole,
                OrganizationId = e.OrganizationId,
                CommunityId = e.CommunityId,
            })
            .ToListAsync(cancellationToken);

        return new PagedData<ErrorLogSummaryDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    public async Task<ErrorLogDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var e = await db.ErrorLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (e is null) return null;

        return new ErrorLogDetailDto
        {
            Id = e.Id,
            CreatedAtUtc = e.CreatedAtUtc,
            CorrelationId = e.CorrelationId,
            ApiPath = e.ApiPath,
            HttpMethod = e.HttpMethod,
            StatusCode = e.StatusCode,
            ExceptionType = e.ExceptionType,
            ErrorMessage = e.ErrorMessage,
            StackTrace = e.StackTrace,
            FileName = e.FileName,
            ControllerOrFunction = e.ControllerOrFunction,
            LineNumber = e.LineNumber,
            UserId = e.UserId,
            UserName = e.UserName,
            UserRole = e.UserRole,
            OrganizationId = e.OrganizationId,
            CommunityId = e.CommunityId,
            RequestBody = e.RequestBody,
            QueryParams = e.QueryParams,
            ClientIp = e.ClientIp,
            UserAgent = e.UserAgent,
        };
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var log = await db.ErrorLogs.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (log is null) return false;

        db.ErrorLogs.Remove(log);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> ClearAsync(int? olderThanDays = null, CancellationToken cancellationToken = default)
    {
        IQueryable<ErrorLog> query = db.ErrorLogs;
        if (olderThanDays.HasValue && olderThanDays.Value > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-olderThanDays.Value);
            query = query.Where(e => e.CreatedAtUtc < cutoff);
        }

        return await query.ExecuteDeleteAsync(cancellationToken);
    }
}
