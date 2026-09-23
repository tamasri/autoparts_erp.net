using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Behaviors;
using AutoPartsERP.Application.Common.Governance;
using AutoPartsERP.Application.Common.Models;
using AutoPartsERP.Application.Features.Transfers;
using AutoPartsERP.Domain.Common;
using AutoPartsERP.Domain.Constants;
using AutoPartsERP.Domain.Governance;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class TransferApprovalPolicyTests
{
    private static readonly Guid Main = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();
    private static readonly Guid Van = Guid.NewGuid();
    private static readonly Guid[] MainToBranch = [Main, Branch];
    private static readonly IReadOnlySet<Guid> None = new HashSet<Guid>();

    private static IReadOnlySet<Guid> Manages(params Guid[] warehouses) => warehouses.ToHashSet();

    [Fact]
    public void AManagerOfOneSide_Approves_ButTheOtherSideIsStillNeeded()
    {
        var outcome = TransferApprovalPolicy.EvaluateApproval(MainToBranch, None, [], Manages(Main), false);

        outcome.Allowed.Should().BeTrue();
        outcome.Completes.Should().BeFalse();
    }

    [Fact]
    public void TheOtherSidesManager_CompletesIt()
    {
        var outcome = TransferApprovalPolicy.EvaluateApproval(MainToBranch, None, [Manages(Main)], Manages(Branch), false);

        outcome.Allowed.Should().BeTrue();
        outcome.Completes.Should().BeTrue();
    }

    [Fact]
    public void AManagerOfBothWarehouses_CompletesItAlone()
    {
        TransferApprovalPolicy.EvaluateApproval(MainToBranch, None, [], Manages(Main, Branch, Van), false).Completes.Should().BeTrue();
    }

    [Fact]
    public void TheRequestersOwnWarehouse_CountsAsConsenting()
    {
        var outcome = TransferApprovalPolicy.EvaluateApproval(MainToBranch, Manages(Main), [], Manages(Branch), false);

        outcome.Completes.Should().BeTrue();
        TransferApprovalPolicy.Pending(MainToBranch, Manages(Main)).Should().Equal(Branch);
        TransferApprovalPolicy.Pending(MainToBranch, Manages(Main, Branch)).Should().BeEmpty();
    }

    [Fact]
    public void AManagerOfAnotherWarehouse_CannotApprove()
    {
        var outcome = TransferApprovalPolicy.EvaluateApproval(MainToBranch, None, [], Manages(Van), false);

        outcome.Allowed.Should().BeFalse();
        outcome.Error.Should().Be(TransferApprovalPolicy.NotAManager);
    }

    [Fact]
    public void AnApprovalThatAddsNoWarehouse_IsRefused()
    {
        var outcome = TransferApprovalPolicy.EvaluateApproval(MainToBranch, None, [Manages(Main)], Manages(Main), false);

        outcome.Allowed.Should().BeFalse();
        outcome.Error.Should().Be(TransferApprovalPolicy.NothingToAdd);
    }

    [Fact]
    public void ASystemAdministrator_CompletesAnyTransfer()
    {
        TransferApprovalPolicy.EvaluateApproval(MainToBranch, None, [], None, true).Should().Be(new TransferApprovalPolicy.Outcome(true, true, null));
    }

    [Fact]
    public void Rejection_IsOpenToEitherSidesManager_OrTheAdministrator()
    {
        TransferApprovalPolicy.CanReject(MainToBranch, Manages(Branch), false).Should().BeTrue();
        TransferApprovalPolicy.CanReject(MainToBranch, Manages(Van), false).Should().BeFalse();
        TransferApprovalPolicy.CanReject(MainToBranch, None, true).Should().BeTrue();
    }

    [Fact]
    public void ACallerDecidedCompletion_OverridesTheCount()
    {
        var approval = new ApprovalRequest(Guid.NewGuid(), "Transfer", "t", "ShipTransferOrderCommand", Guid.NewGuid(), "", requiredApprovals: 2);

        approval.Approve(Guid.NewGuid(), null, completes: false).IsSuccess.Should().BeTrue();
        approval.Status.Should().Be(ApprovalStatuses.InReview);
        approval.Approve(Guid.NewGuid(), null, completes: true);
        approval.Status.Should().Be(ApprovalStatuses.Approved);
    }
}

public sealed class WarehouseTransferRoutingTests
{
    private static readonly Guid Main = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();

    private readonly IApprovalService _approvals = Substitute.For<IApprovalService>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IWarehouseAccess _warehouses = Substitute.For<IWarehouseAccess>();
    private readonly MakerCheckerBehavior<ShipTransferOrderCommand, Result<Guid>> _behavior;
    private readonly ShipTransferOrderCommand _ship = new(Guid.NewGuid());
    private bool _ran;

    public WarehouseTransferRoutingTests()
    {
        _user.UserId.Returns(Guid.NewGuid());
        _approvals.CreatePendingApprovalAsync(default!, default).ReturnsForAnyArgs(Result<Guid>.Success(Guid.NewGuid()));
        _warehouses.ManagedWarehousesAsync(default, default).ReturnsForAnyArgs(new HashSet<Guid>());
        _behavior = new MakerCheckerBehavior<ShipTransferOrderCommand, Result<Guid>>(
            _approvals, _user, Substitute.For<IManualAuditService>(), Substitute.For<IApprovalReplayContext>(), _warehouses);
    }

    private Task<Result<Guid>> Run() => _behavior.Handle(_ship, _ => { _ran = true; return Task.FromResult(Result<Guid>.Success(_ship.TransferOrderId)); }, CancellationToken.None);

    private void Between(Guid from, Guid to) =>
        _warehouses.ResolveTransferAsync(default!, default).ReturnsForAnyArgs(Result<TransferScope?>.Success(from == to ? null : new TransferScope(from, to, _ship.TransferOrderId.ToString())));

    [Fact]
    public async Task AMoveInsideOneWarehouse_RunsWithoutApproval()
    {
        Between(Main, Main);

        (await Run()).IsSuccess.Should().BeTrue();
        _ran.Should().BeTrue();
    }

    [Fact]
    public async Task ATransferBetweenWarehouses_IsHeld_WithBothWarehousesAsItsScope()
    {
        Between(Main, Branch);

        var result = await Run();

        _ran.Should().BeFalse();
        result.Error.Code.Should().Be("Approval.Pending");
        await _approvals.Received(1).CreatePendingApprovalAsync(
            Arg.Is<PendingApprovalSubmission>(s => s.RequiredApprovals == 2 && s.ScopeWarehouseIds!.SequenceEqual(new[] { Main, Branch }) && s.EntityId == _ship.TransferOrderId.ToString()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARequesterManagingOneSide_OnlyNeedsTheOtherSide()
    {
        Between(Main, Branch);
        _warehouses.ManagedWarehousesAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(new HashSet<Guid> { Main });

        await Run();

        await _approvals.Received(1).CreatePendingApprovalAsync(Arg.Is<PendingApprovalSubmission>(s => s.RequiredApprovals == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARequesterManagingBothSides_ShipsDirectly()
    {
        Between(Main, Branch);
        _warehouses.ManagedWarehousesAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(new HashSet<Guid> { Main, Branch });

        (await Run()).IsSuccess.Should().BeTrue();
        _ran.Should().BeTrue();
    }

    [Fact]
    public async Task ASecondRequestForTheSameOrder_IsRefusedWhileTheFirstIsPending()
    {
        Between(Main, Branch);
        _approvals.HasPendingAsync(nameof(ShipTransferOrderCommand), _ship.TransferOrderId.ToString(), Arg.Any<CancellationToken>()).Returns(true);

        (await Run()).Error.Code.Should().Be("Approval.PendingConflict");
        await _approvals.DidNotReceiveWithAnyArgs().CreatePendingApprovalAsync(default!, default);
    }

    [Fact]
    public async Task TheSystemAdministrator_ShipsDirectly()
    {
        Between(Main, Branch);
        _user.HasRole(RoleCodes.SystemAdministrator).Returns(true);

        (await Run()).IsSuccess.Should().BeTrue();
        _ran.Should().BeTrue();
    }
}
