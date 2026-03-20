namespace AutoPartsERP.Application.Common.Abstractions.Markers;

public interface IIdempotentRequest
{
    string IdempotencyKey { get; }
}
