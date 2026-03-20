using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Abstractions.Markers;
using AutoPartsERP.Application.Common.Behaviors;
using AutoPartsERP.Application.Common.Models;
using AutoPartsERP.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class AuthorizationBehaviorTests
{
    [Fact]
    public async Task Handle_ShouldCallNext_WhenCurrentUserHasPermission()
    {
        var currentUser = BuildCurrentUser(hasPermission: true);
        var audit = Substitute.For<IManualAuditService>();
        var behavior = new AuthorizationBehavior<AuthorizedCommand, Result<string>>(currentUser, audit);
        var nextCalled = false;

        var result = await behavior.Handle(
            new AuthorizedCommand("users.read"),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result<string>.Success("ok"));
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        await audit.DidNotReceiveWithAnyArgs().LogRejectionAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldReturnFailureAndAuditRejection_WhenPermissionIsMissing()
    {
        var currentUser = BuildCurrentUser(hasPermission: false);
        var audit = Substitute.For<IManualAuditService>();
        var behavior = new AuthorizationBehavior<AuthorizedCommand, Result<string>>(currentUser, audit);
        var nextCalled = false;

        var result = await behavior.Handle(
            new AuthorizedCommand("users.read"),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result<string>.Success("ok"));
            },
            CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Authorization.Forbidden");
        await audit.Received(1).LogRejectionAsync(Arg.Any<RejectionEntry>(), Arg.Any<CancellationToken>());
    }

    private static ICurrentUser BuildCurrentUser(bool hasPermission)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(Guid.NewGuid());
        user.Username.Returns("tester");
        user.CorrelationId.Returns(Guid.NewGuid());
        user.HasPermission(Arg.Any<string>()).Returns(hasPermission);
        return user;
    }

    private sealed record AuthorizedCommand(string RequiredPermission) : IAuthorizedRequest;
}
