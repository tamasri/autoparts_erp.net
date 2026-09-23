using System.Text.Json;
using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Behaviors;
using AutoPartsERP.Application.Common.Models;
using AutoPartsERP.Application.Features.Accounting;
using AutoPartsERP.Domain.Common;
using AutoPartsERP.Domain.Constants;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class MakerCheckerBehaviorTests
{
    private readonly IApprovalService _approvals = Substitute.For<IApprovalService>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IApprovalReplayContext _replay = Substitute.For<IApprovalReplayContext>();
    private readonly MakerCheckerBehavior<PostJournalEntryCommand, Result<Guid>> _behavior;
    private readonly PostJournalEntryCommand _command = new(Guid.NewGuid());

    public MakerCheckerBehaviorTests()
    {
        _user.UserId.Returns(Guid.NewGuid());
        _user.CorrelationId.Returns(Guid.NewGuid());
        _approvals.CreatePendingApprovalAsync(default!, default).ReturnsForAnyArgs(Result<Guid>.Success(Guid.NewGuid()));
        _behavior = new MakerCheckerBehavior<PostJournalEntryCommand, Result<Guid>>(_approvals, _user, Substitute.For<IManualAuditService>(), _replay, Substitute.For<IWarehouseAccess>());
    }

    private Task<Result<Guid>> Run(out Func<bool> nextRan)
    {
        var ran = false;
        nextRan = () => ran;
        return _behavior.Handle(_command, _ => { ran = true; return Task.FromResult(Result<Guid>.Success(_command.Id)); }, CancellationToken.None);
    }

    [Fact]
    public async Task OrdinaryUser_GetsAPendingApproval_InsteadOfPosting()
    {
        var result = await Run(out var nextRan);

        nextRan().Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Approval.Pending");
        await _approvals.Received(1).CreatePendingApprovalAsync(
            Arg.Is<PendingApprovalSubmission>(s => s.RequestType == nameof(PostJournalEntryCommand) && s.Module == "ACCOUNTING"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SystemAdministrator_PostsDirectly_WithoutAnApproval()
    {
        _user.HasRole(RoleCodes.SystemAdministrator).Returns(true);

        var result = await Run(out var nextRan);

        nextRan().Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        await _approvals.DidNotReceiveWithAnyArgs().CreatePendingApprovalAsync(default!, default);
    }

    [Fact]
    public async Task AnApprovedRequestBeingReplayed_ReachesTheHandler()
    {
        _replay.IsReplaying.Returns(true);

        var result = await Run(out var nextRan);

        nextRan().Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(nameof(PostJournalEntryCommand))]
    [InlineData(nameof(VoidJournalEntryCommand))]
    public void ApprovedJournalCommands_CanBeReplayed_BecauseTheirTypeIsResolvable(string typeName) =>
        ApprovalRequestTypeResolver.Resolve(typeName).Should().NotBeNull();

    [Fact]
    public void AStoredPostRequest_ComesBackAsTheSameCommand()
    {
        var json = JsonSerializer.Serialize(_command);

        var back = (PostJournalEntryCommand?)JsonSerializer.Deserialize(json, ApprovalRequestTypeResolver.Resolve(nameof(PostJournalEntryCommand))!);

        back.Should().Be(_command);
    }
}
