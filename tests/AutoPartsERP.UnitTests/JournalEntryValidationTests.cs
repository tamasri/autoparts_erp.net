using AutoPartsERP.Application.Features.Accounting;
using AutoPartsERP.Contracts.Accounting;
using AutoPartsERP.Domain.Constants;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class JournalEntryValidationTests
{
    private static readonly SaveJournalEntryCommandValidator Validator = new();

    private static SaveJournalEntryCommand Entry(params JournalLineInput[] lines) =>
        new(null, new SaveJournalEntryRequest(Guid.NewGuid(), new DateOnly(2026, 9, 20), "test", null, lines));

    private static JournalLineInput Dr(string account, decimal amount) => new(account, null, amount, 0, null);

    private static JournalLineInput Cr(string account, decimal amount) => new(account, null, 0, amount, null);

    [Fact]
    public void BalancedTwoLineEntry_IsValid() =>
        Validator.Validate(Entry(Dr("Cash", 100), Cr("Sales", 100))).IsValid.Should().BeTrue();

    [Fact]
    public void EntryWhoseSidesDiffer_IsRejected() =>
        Validator.Validate(Entry(Dr("Cash", 100), Cr("Sales", 90))).Errors.Should().Contain(e => e.ErrorMessage.Contains("debits must equal"));

    [Fact]
    public void SingleLine_IsRejected() =>
        Validator.Validate(Entry(Dr("Cash", 100))).IsValid.Should().BeFalse();

    [Fact]
    public void LineWithBothSides_IsRejected() =>
        Validator.Validate(Entry(new JournalLineInput("Cash", null, 50, 50, null), Cr("Sales", 0))).IsValid.Should().BeFalse();

    [Fact]
    public void LineWithNoAmount_IsRejected() =>
        Validator.Validate(Entry(Dr("Cash", 100), Cr("Sales", 100), new JournalLineInput("Rent", null, 0, 0, null))).IsValid.Should().BeFalse();

    [Fact]
    public void ManyLinesThatBalance_AreValid() =>
        Validator.Validate(Entry(Dr("Rent", 40), Dr("Fuel", 60), Cr("Cash", 30), Cr("Bank", 70))).IsValid.Should().BeTrue();

    [Fact]
    public void TinyRoundingDifference_IsNotTolerated() =>
        Validator.Validate(Entry(Dr("Cash", 100.0002m), Cr("Sales", 100))).IsValid.Should().BeFalse();
}

public sealed class EntryKindTests
{
    [Theory]
    [InlineData(EntryKinds.Journal, "Journal Entry")]
    [InlineData(EntryKinds.Receipt, "Bank Entry")]
    [InlineData(EntryKinds.Payment, "Bank Entry")]
    [InlineData(EntryKinds.Contra, "Contra Entry")]
    [InlineData(EntryKinds.Opening, "Opening Entry")]
    [InlineData(EntryKinds.DebitNote, "Debit Note")]
    [InlineData(EntryKinds.CreditNote, "Credit Note")]
    public void EveryKind_MapsToAnErpNextVoucherType(string kind, string expected) =>
        EntryKinds.ErpNextVoucherType(kind).Should().Be(expected);

    [Fact]
    public void EveryKind_IsListed() => EntryKinds.All.Should().HaveCount(7);
}
