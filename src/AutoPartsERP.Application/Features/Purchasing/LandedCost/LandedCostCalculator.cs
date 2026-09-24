namespace AutoPartsERP.Application.Features.Purchasing.LandedCost;

/// <summary>A bill line that receives landed costs: the quantity still kept (bought minus returned) and its net unit cost.</summary>
public sealed record LandedLine(Guid LineId, Guid SkuId, decimal Quantity, decimal NetUnitCost);

/// <summary>One charge (transport, customs, …) and how it is split over the lines.</summary>
public sealed record LandedCharge(Guid ChargeId, decimal Amount, string SplitMethod);

public sealed record ChargeShare(Guid ChargeId, Guid LineId, decimal Amount);

/// <summary>
/// What a voucher does to one SKU: the landed cost it received, and how much of it goes into the stock still on hand (raising the
/// average cost) versus into cost of goods sold for the part already sold.
/// </summary>
public sealed record SkuEffect(Guid SkuId, decimal Quantity, decimal OnHand, decimal Allocated, decimal Capitalized, decimal Expensed);

public sealed record LandedCostResult(IReadOnlyList<ChargeShare> Shares, IReadOnlyList<SkuEffect> Effects);

/// <summary>
/// Landed cost distribution, as ERPNext's Landed Cost Voucher ("distribute charges based on") and Odoo's split methods:
/// <list type="bullet">
/// <item><c>VALUE</c>: in proportion to each line's value (quantity × net cost) — the usual basis for customs and insurance.</item>
/// <item><c>QTY</c>: in proportion to quantity — the usual basis for transport of similar goods.</item>
/// <item><c>EQUAL</c>: the same amount per line.</item>
/// </list>
/// Each charge is shared out to 4 decimals and the last line takes the rounding rest, so the shares add up to the charge exactly.
/// <para>Stock coverage (the perpetual moving-average rule, as SAP's price difference): the part of a SKU's landed cost that belongs to
/// goods still on hand is capitalized into their average cost; the part belonging to goods already sold goes to cost of goods sold,
/// because those goods can no longer carry it.</para>
/// </summary>
public static class LandedCostCalculator
{
    public const string ByValue = "VALUE";
    public const string ByQuantity = "QTY";
    public const string Equally = "EQUAL";
    public static readonly IReadOnlyList<string> SplitMethods = [ByValue, ByQuantity, Equally];

    public static Result<LandedCostResult> Calculate(
        IReadOnlyList<LandedLine> lines, IReadOnlyList<LandedCharge> charges, IReadOnlyDictionary<Guid, decimal> onHandBySku)
    {
        var kept = lines.Where(l => l.Quantity > 0).ToList();
        if (kept.Count == 0)
        {
            return Result<LandedCostResult>.Failure(new Error("LandedCost.NoGoods", "The chosen bills keep no goods to carry the costs (everything was returned)."));
        }

        var shares = new List<ChargeShare>();
        foreach (var charge in charges)
        {
            var basis = kept.Select(l => charge.SplitMethod switch
            {
                ByValue => l.Quantity * l.NetUnitCost,
                ByQuantity => l.Quantity,
                _ => 1m
            }).ToList();
            var total = basis.Sum();
            if (total <= 0)
            {
                return Result<LandedCostResult>.Failure(new Error("LandedCost.NoBasis", "The goods have no value to split this charge by; split it by quantity or equally."));
            }

            var given = 0m;
            for (var i = 0; i < kept.Count; i++)
            {
                var amount = i == kept.Count - 1 ? charge.Amount - given : Math.Round(charge.Amount * basis[i] / total, 4);
                given += amount;
                shares.Add(new ChargeShare(charge.ChargeId, kept[i].LineId, amount));
            }
        }

        var lineSku = kept.ToDictionary(l => l.LineId, l => l.SkuId);
        var effects = kept.GroupBy(l => l.SkuId).Select(g =>
        {
            var quantity = g.Sum(l => l.Quantity);
            var allocated = shares.Where(s => lineSku[s.LineId] == g.Key).Sum(s => s.Amount);
            var onHand = Math.Max(onHandBySku.GetValueOrDefault(g.Key), 0m);
            var coverage = Math.Min(onHand / quantity, 1m);
            var capitalized = Math.Round(allocated * coverage, 4);
            return new SkuEffect(g.Key, quantity, onHand, allocated, capitalized, allocated - capitalized);
        }).ToList();

        return Result<LandedCostResult>.Success(new LandedCostResult(shares, effects));
    }
}
