using AutoPartsERP.Domain.Constants;
using AutoPartsERP.Domain.Party;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class PartyTests
{
    [Fact]
    public void Create_WithEmptyArabicName_ReturnsFailure()
    {
        var result = Party.Create("PTY-0001", "Main Party", string.Empty, null, Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Party.NameArRequired");
    }

    [Fact]
    public void RequestTypeAssignment_AlreadyActive_ReturnsFailure()
    {
        var party = CreateParty();
        party.RequestTypeAssignment(PartyTypeCodes.Customer, Guid.NewGuid(), null);
        party.ActivateTypeAssignment(PartyTypeCodes.Customer, Guid.NewGuid());

        var duplicateResult = party.RequestTypeAssignment(PartyTypeCodes.Customer, Guid.NewGuid(), null);

        duplicateResult.IsFailure.Should().BeTrue();
        duplicateResult.Error.Code.Should().Be("Party.TypeAlreadyAssigned");
    }

    [Fact]
    public void ActivateTypeAssignment_NotPending_ReturnsFailure()
    {
        var party = CreateParty();

        var result = party.ActivateTypeAssignment(PartyTypeCodes.Vendor, Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Party.AssignmentNotFound");
    }

    [Fact]
    public void HasCombinedStatement_WhenCustomerAndVendor_ReturnsTrue()
    {
        var party = CreateParty();
        party.RequestTypeAssignment(PartyTypeCodes.Customer, Guid.NewGuid(), null);
        party.RequestTypeAssignment(PartyTypeCodes.Vendor, Guid.NewGuid(), null);
        party.ActivateTypeAssignment(PartyTypeCodes.Customer, Guid.NewGuid());
        party.ActivateTypeAssignment(PartyTypeCodes.Vendor, Guid.NewGuid());

        party.HasCombinedStatement.Should().BeTrue();
    }

    [Fact]
    public void HasCombinedStatement_WhenOnlyCustomer_ReturnsFalse()
    {
        var party = CreateParty();
        party.RequestTypeAssignment(PartyTypeCodes.Customer, Guid.NewGuid(), null);
        party.ActivateTypeAssignment(PartyTypeCodes.Customer, Guid.NewGuid());

        party.HasCombinedStatement.Should().BeFalse();
    }

    private static Party CreateParty()
    {
        var result = Party.Create("PTY-0001", "Main Party", "الطرف الرئيسي", null, Guid.NewGuid());
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }
}
