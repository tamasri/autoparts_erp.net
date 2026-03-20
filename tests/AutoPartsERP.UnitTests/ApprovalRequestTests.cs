using AutoPartsERP.Domain.Constants;
using AutoPartsERP.Domain.Governance;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class ApprovalRequestTests
{
    [Fact]
    public void Constructor_ShouldInitializePendingState()
    {
        var sut = CreateApproval(requiredApprovals: 2);

        sut.Status.Should().Be(ApprovalStatuses.Pending);
        sut.RequiredApprovals.Should().Be(2);
        sut.CurrentApprovals.Should().Be(0);
    }

    [Fact]
    public void Constructor_ShouldNormalizeRequiredApprovalsToOne_WhenInvalid()
    {
        var sut = CreateApproval(requiredApprovals: 0);

        sut.RequiredApprovals.Should().Be(1);
    }

    [Fact]
    public void Approve_ShouldMoveToInReview_UntilThresholdReached()
    {
        var sut = CreateApproval(requiredApprovals: 2);

        var result = sut.Approve(Guid.NewGuid(), "first");

        result.IsSuccess.Should().BeTrue();
        sut.Status.Should().Be(ApprovalStatuses.InReview);
        sut.CompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Approve_ShouldMoveToApproved_WhenThresholdReached()
    {
        var sut = CreateApproval(requiredApprovals: 2);
        sut.Approve(Guid.NewGuid(), "first");

        var result = sut.Approve(Guid.NewGuid(), "second");

        result.IsSuccess.Should().BeTrue();
        sut.Status.Should().Be(ApprovalStatuses.Approved);
        sut.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Reject_ShouldMoveToRejected()
    {
        var sut = CreateApproval(requiredApprovals: 2);

        var result = sut.Reject(Guid.NewGuid(), "policy violation");

        result.IsSuccess.Should().BeTrue();
        sut.Status.Should().Be(ApprovalStatuses.Rejected);
        sut.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Review_ShouldFail_WhenReviewerDuplicatesDecision()
    {
        var reviewerId = Guid.NewGuid();
        var sut = CreateApproval(requiredApprovals: 2);
        sut.Approve(reviewerId, "approved");

        var duplicateResult = sut.Approve(reviewerId, "again");

        duplicateResult.IsFailure.Should().BeTrue();
        duplicateResult.Error.Code.Should().Be("approval.duplicate-review");
    }

    [Fact]
    public void Cancel_ShouldFail_WhenRequestAlreadyTerminal()
    {
        var sut = CreateApproval();
        sut.Reject(Guid.NewGuid(), "reject");

        var result = sut.Cancel("cancel");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("approval.already-completed");
    }

    private static ApprovalRequest CreateApproval(int requiredApprovals = 1)
    {
        return new ApprovalRequest(
            Guid.NewGuid(),
            "USERS",
            Guid.NewGuid().ToString(),
            "DEACTIVATE",
            Guid.NewGuid(),
            "Needs review",
            requiredApprovals);
    }
}
