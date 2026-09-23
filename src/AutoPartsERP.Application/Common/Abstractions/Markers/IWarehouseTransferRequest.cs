namespace AutoPartsERP.Application.Common.Abstractions.Markers;

/// <summary>
/// A governed request that moves stock from one warehouse to another. Its approvers are the managers of those warehouses (not the general
/// APPROVER role); SYSTEM_ADMIN needs no approval. A move inside one warehouse needs no approval at all. See <see cref="IWarehouseAccess"/>.
/// </summary>
public interface IWarehouseTransferRequest : IMakerCheckerRequest
{
}
