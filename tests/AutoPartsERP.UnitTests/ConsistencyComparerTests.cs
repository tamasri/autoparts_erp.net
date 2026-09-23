using AutoPartsERP.Application.Features.Accounting.Consistency;
using AutoPartsERP.Contracts.Accounting;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class ConsistencyComparerTests
{
    private static LocalRecord L(string name, decimal? amount = 100, bool active = true, string? status = "SYNCED", string? error = null) =>
        new("Invoice", Guid.NewGuid(), "INV-" + name, amount, active, status, status is null ? null : name, error);

    private static RemoteRecord R(string name, decimal? amount = 100, bool active = true, bool cancelled = false) => new(name, amount, active, cancelled);

    private static Dictionary<string, int> Kinds(params (string Kind, int Count)[] kinds) => kinds.ToDictionary(k => k.Kind, k => k.Count);

    [Fact]
    public void Everything_Matched_HasNoIssues()
    {
        var s = ConsistencyComparer.Compare("x", "Sales Invoice", [L("A"), L("B", 50)], [R("A"), R("B", 50)], true);

        s.MatchedCount.Should().Be(2);
        s.Issues.Should().BeEmpty();
        s.LocalTotal.Should().Be(150);
        s.ErpNextTotal.Should().Be(150);
    }

    [Fact]
    public void Every_Kind_Of_Difference_Is_Reported()
    {
        var local = new[]
        {
            L("never", status: null),                         // never sent
            L("failed", status: "FAILED", error: "boom"),     // failed
            L("gone"),                                        // sent, deleted in ERPNext
            L("cancelledThere"),                              // cancelled only in ERPNext
            L("voidedHere", active: false),                   // voided here, still live there
            L("amount", 100),                                 // amount differs
            L("ok"),
        };
        var remote = new[]
        {
            R("cancelledThere", active: false, cancelled: true),
            R("voidedHere"),
            R("amount", 90),
            R("ok"),
            R("stranger", 5),                                 // only in ERPNext
            R("draft", active: false),                        // not active there: ignored
        };

        var s = ConsistencyComparer.Compare("x", "Sales Invoice", local, remote, true);

        s.IssueCounts.Should().BeEquivalentTo(Kinds(
            ("NOT_SENT", 2), ("MISSING_IN_ERPNEXT", 1), ("CANCELLED_IN_ERPNEXT_ONLY", 1), ("NOT_CANCELLED_IN_ERPNEXT", 1), ("AMOUNT_DIFFERS", 1), ("ONLY_IN_ERPNEXT", 1)));
        s.MatchedCount.Should().Be(1);
        s.Issues.Single(i => i.Kind == "NOT_SENT" && i.LocalRef == "INV-failed").Detail.Should().Contain("boom");
        s.Issues.Single(i => i.Kind == "AMOUNT_DIFFERS").Should().Match<ErpNextConsistencyIssueDto>(i => i.LocalAmount == 100 && i.ErpNextAmount == 90);
        s.Issues.Single(i => i.Kind == "ONLY_IN_ERPNEXT").ErpNextName.Should().Be("stranger");
        s.LocalCount.Should().Be(6);
        s.ErpNextCount.Should().Be(4);
    }

    [Fact]
    public void Voided_Before_Sending_And_Properly_Cancelled_Are_Fine()
    {
        var s = ConsistencyComparer.Compare("x", "Sales Invoice",
            [L("neverSent", active: false, status: null), L("cancelled", active: false, status: "CANCELLED")],
            [R("cancelled", active: false, cancelled: true)], true);

        s.Issues.Should().BeEmpty();
    }

    [Fact]
    public void A_Record_Without_A_Local_Amount_Is_Not_Compared_On_Amount()
    {
        var s = ConsistencyComparer.Compare("x", "Journal Entry", [L("adj", amount: null)], [R("adj", 123)], false);

        s.Issues.Should().BeEmpty();
        s.MatchedCount.Should().Be(1);
        s.LocalTotal.Should().BeNull();
    }

    [Fact]
    public void Differences_Within_A_Cent_Are_Ignored()
    {
        ConsistencyComparer.Compare("x", "Payment Entry", [L("p", 10.004m)], [R("p", 10m)], true).Issues.Should().BeEmpty();
    }

    [Fact]
    public void Issues_Are_Capped_But_Counted()
    {
        var local = Enumerable.Range(0, 250).Select(i => L("n" + i, status: null)).ToList();

        var s = ConsistencyComparer.Compare("x", "Item", local, [], false);

        s.Issues.Should().HaveCount(ConsistencyComparer.MaxIssues);
        s.IssueCounts["NOT_SENT"].Should().Be(250);
    }
}
