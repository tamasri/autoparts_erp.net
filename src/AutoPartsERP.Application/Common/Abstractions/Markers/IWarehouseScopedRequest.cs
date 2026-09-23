namespace AutoPartsERP.Application.Common.Abstractions.Markers;

/// <summary>
/// A request that reads or changes a warehouse document or stock at a location. Users without <c>inventory:all_warehouses</c> may only act on
/// warehouses assigned to them; <see cref="IWarehouseScope"/> says which locations each request touches.
/// </summary>
public interface IWarehouseScopedRequest
{
}
