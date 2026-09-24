using AutoPartsERP.Infrastructure.Maintenance;
using AutoPartsERP.Infrastructure.Services;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AutoPartsERP.Api.Maintenance;

/// <summary>
/// <c>--reset-business-data --confirm=DELETE-ALL-BUSINESS-DATA [--dry-run] [--skip-erpnext] [--keep-users]</c>
/// <para>Wipes the trial data before real work starts: first ERPNext (its transactions and the items, customers, suppliers and sales
/// persons we pushed there), then this database (<see cref="BusinessDataReset"/>), then the assistant's Redis keys. ERPNext goes first
/// so a failure there leaves both sides as they were — nothing half-deleted here that ERPNext still holds. Run by
/// scripts/reset-business-data.sh with the API stopped.</para>
/// </summary>
public static class ResetBusinessData
{
    public const string ConfirmPhrase = "DELETE-ALL-BUSINESS-DATA";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        static void Log(string message) => Console.WriteLine($"[reset] {message}");

        var dryRun = args.Contains("--dry-run");
        if (!dryRun && !args.Contains($"--confirm={ConfirmPhrase}"))
        {
            Log($"Refused: add --confirm={ConfirmPhrase} (or --dry-run to only see what would be deleted).");
            return 2;
        }

        using var scope = services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()
            ?? throw new InvalidOperationException("No database connection string.");
        var erpNext = scope.ServiceProvider.GetRequiredService<IOptions<ErpNextOptions>>().Value;
        using var cts = new CancellationTokenSource(TimeSpan.FromHours(1));

        Log(dryRun ? "DRY RUN — nothing will be deleted." : "Deleting all business data. This cannot be undone.");

        if (args.Contains("--skip-erpnext"))
        {
            Log("ERPNext: skipped (--skip-erpnext).");
        }
        else if (!erpNext.Enabled || string.IsNullOrWhiteSpace(erpNext.BaseUrl))
        {
            Log("ERPNext: integration not enabled — skipped.");
        }
        else
        {
            try
            {
                if (!await new ErpNextCompanyWipe(erpNext, Log).RunAsync(dryRun, TimeSpan.FromMinutes(30), cts.Token))
                {
                    Log("Stopped: ERPNext was not fully emptied, so this database was left untouched. Fix the cause and run again "
                        + "(it is safe to repeat), or use --skip-erpnext to wipe only this database.");
                    return 1;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                Log($"Stopped: ERPNext could not be reached or answered with an error ({ex.Message}). This database was left untouched.");
                return 1;
            }
        }

        if (!await BusinessDataReset.RunAsync(connectionString, dryRun, args.Contains("--keep-users"), Log, cts.Token))
        {
            return 1;
        }

        if (!dryRun)
        {
            // Pending WhatsApp choices and rate-limit counters point at deleted records; the gateway's own status stays.
            var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            var removed = 0;
            foreach (var endpoint in redis.GetEndPoints())
            {
                var server = redis.GetServer(endpoint);
                if (server.IsReplica)
                {
                    continue;
                }

                foreach (var key in server.Keys(pattern: "assistant:*"))
                {
                    if (key != "assistant:gateway" && await redis.GetDatabase().KeyDeleteAsync(key))
                    {
                        removed++;
                    }
                }
            }

            Log($"Redis: {removed} assistant key(s) removed.");
            await ReferenceDataSeeder.SeedAsync(services);
            Log("Done. Sign in with a SYSTEM_ADMIN account; warehouses, users' warehouse access, items and parties start empty.");
        }

        return 0;
    }
}
