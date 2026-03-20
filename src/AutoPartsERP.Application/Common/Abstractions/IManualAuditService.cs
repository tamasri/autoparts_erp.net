using AutoPartsERP.Application.Common.Models;

namespace AutoPartsERP.Application.Common.Abstractions;

public interface IManualAuditService
{
    Task LogAsync(ManualAuditEntry entry, CancellationToken cancellationToken = default);

    Task LogRejectionAsync(RejectionEntry entry, CancellationToken cancellationToken = default);
}
