using Lragrz.Enova.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Lragrz.Enova.Server.Services;

/// <summary>
/// Periodically processes the upload queue in batches and delivers to the configured DMS.
/// </summary>
public sealed class UploadQueueProcessor : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<UploadQueueProcessor> _logger;

    public UploadQueueProcessor(IServiceProvider services, ILogger<UploadQueueProcessor> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Queue processor error");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnovaDbContext>();
        var dms = scope.ServiceProvider.GetRequiredService<DocuWareService>();

        var batch = await db.UploadQueue
            .Where(q => q.Status == "pending" && q.Attempts < 5)
            .OrderBy(q => q.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        foreach (var item in batch)
        {
            item.Attempts++;
            item.UpdatedAt = DateTime.UtcNow;

            AgencyEntity? agency = null;
            if (item.OrganisationId.HasValue)
            {
                agency = await db.Agencies
                    .FirstOrDefaultAsync(a => a.OrganisationId == item.OrganisationId, ct);
            }

            if (agency?.DmsType == "docuware")
            {
                var ok = await dms.UploadAsync(item.LocalPath, agency, ct);
                item.Status = ok ? "uploaded" : "failed";
                if (!ok) item.LastError = "DocuWare delivery failed";
            }
            else
            {
                // Local-only: mark as uploaded (file already on server)
                item.Status = "uploaded";
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Processed {Count} queue items", batch.Count);
    }
}
