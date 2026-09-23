using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Abstractions.Markers;
using AutoPartsERP.Application.Common.Behaviors;
using AutoPartsERP.Domain.Common;
using AutoPartsERP.Domain.Constants;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class WarehouseScopeBehaviorTests
{
    private sealed record ScopedCommand : IRequest<Result<string>>, IWarehouseScopedRequest;

    private sealed record PlainCommand : IRequest<Result<string>>;

    private static readonly WarehouseTouch Main = new([Guid.NewGuid()], false);

    private static (WarehouseScopeBehavior<TReq, Result<string>> Behavior, IWarehouseScope Scope) Build<TReq>(
        bool seesAll = false, WarehouseTouch? touch = null, bool canSee = false, bool replaying = false) where TReq : notnull
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(Guid.NewGuid());
        user.HasPermission(PermissionCodes.Inventory.AllWarehouses).Returns(seesAll);
        var scope = Substitute.For<IWarehouseScope>();
        scope.ResolveAsync(Arg.Any<IWarehouseScopedRequest>(), Arg.Any<CancellationToken>()).Returns(touch);
        scope.CanSeeAsync(Arg.Any<Guid>(), Arg.Any<WarehouseTouch>(), Arg.Any<CancellationToken>()).Returns(canSee);
        var replay = Substitute.For<IApprovalReplayContext>();
        replay.IsReplaying.Returns(replaying);
        return (new WarehouseScopeBehavior<TReq, Result<string>>(user, scope, replay), scope);
    }

    private static Task<Result<string>> Run<TReq>(WarehouseScopeBehavior<TReq, Result<string>> behavior, TReq request) where TReq : notnull =>
        behavior.Handle(request, _ => Task.FromResult(Result<string>.Success("ran")), CancellationToken.None);

    [Fact]
    public async Task A_Warehouse_Of_The_User_Passes()
    {
        var (behavior, _) = Build<ScopedCommand>(touch: Main, canSee: true);
        (await Run(behavior, new ScopedCommand())).Value.Should().Be("ran");
    }

    [Fact]
    public async Task Another_Warehouse_Is_Refused_With_403_Code()
    {
        var (behavior, _) = Build<ScopedCommand>(touch: Main, canSee: false);
        var result = await Run(behavior, new ScopedCommand());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Authorization.WarehouseNotAssigned");
    }

    [Fact]
    public async Task All_Warehouses_Permission_Skips_The_Lookup()
    {
        var (behavior, scope) = Build<ScopedCommand>(seesAll: true, touch: Main, canSee: false);

        (await Run(behavior, new ScopedCommand())).IsSuccess.Should().BeTrue();
        await scope.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task An_Approved_Request_Being_Replayed_Is_Not_Checked_Again()
    {
        var (behavior, scope) = Build<ScopedCommand>(touch: Main, canSee: false, replaying: true);

        (await Run(behavior, new ScopedCommand())).IsSuccess.Should().BeTrue();
        await scope.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task A_Missing_Document_Is_Left_To_The_Handler()
    {
        var (behavior, _) = Build<ScopedCommand>(touch: null, canSee: false);
        (await Run(behavior, new ScopedCommand())).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Other_Requests_Are_Untouched()
    {
        var (behavior, scope) = Build<PlainCommand>(canSee: false);

        (await Run(behavior, new PlainCommand())).IsSuccess.Should().BeTrue();
        await scope.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }
}
