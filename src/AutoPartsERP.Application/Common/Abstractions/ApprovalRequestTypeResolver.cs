namespace AutoPartsERP.Application.Common.Abstractions;

/// <summary>
/// Resolves the short type name persisted by <c>MakerCheckerBehavior</c> (<c>typeof(TRequest).Name</c>,
/// e.g. "AdjustInventoryCommand") back to its CLR <see cref="Type"/>, so an approved request's
/// serialized payload can be deserialized and re-dispatched through MediatR.
/// </summary>
public static class ApprovalRequestTypeResolver
{
    private static readonly Lazy<IReadOnlyDictionary<string, Type>> TypesByName = new(() =>
        typeof(IMakerCheckerRequest).Assembly
            .GetTypes()
            .Where(t => typeof(IMakerCheckerRequest).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .ToDictionary(t => t.Name, t => t, StringComparer.Ordinal));

    public static Type? Resolve(string requestTypeName) =>
        TypesByName.Value.TryGetValue(requestTypeName, out var type) ? type : null;
}
