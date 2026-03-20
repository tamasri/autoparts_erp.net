using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Abstractions.Markers;
using AutoPartsERP.Application.Common.Behaviors;
using AutoPartsERP.Application.Common.Models;
using AutoPartsERP.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class PeriodLockBehaviorTests
{
    [Fact]
    public async Task Handle_ShouldCallNext_WhenPeriodIsNotLocked()
    {
        var periodLockService = Substitute.For<IPeriodLockService>();
        var currentUser = BuildCurrentUser();
        var audit = Substitute.For<IManualAuditService>();
        periodLockService.IsLockedAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var behavior = new PeriodLockBehavior<PeriodSensitiveCommand, Result<string>>(periodLockService, currentUser, audit);
        var nextCalled = false;

        var result = await behavior.Handle(
            new PeriodSensitiveCommand(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "USERS"),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result<string>.Success("ok"));
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnFailureAndAudit_WhenPeriodIsLocked()
    {
        var periodLockService = Substitute.For<IPeriodLockService>();
        var currentUser = BuildCurrentUser();
        var audit = Substitute.For<IManualAuditService>();
        periodLockService.IsLockedAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var behavior = new PeriodLockBehavior<PeriodSensitiveCommand, Result<string>>(periodLockService, currentUser, audit);
        var nextCalled = false;

        var result = await behavior.Handle(
            new PeriodSensitiveCommand(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "USERS"),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result<string>.Success("ok"));
            },
            CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PeriodLock.Locked");
        await audit.Received(1).LogAsync(Arg.Any<ManualAuditEntry>(), Arg.Any<CancellationToken>());
    }

    private static ICurrentUser BuildCurrentUser()
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(Guid.NewGuid());
        user.Username.Returns("tester");
        user.CorrelationId.Returns(Guid.NewGuid());
        user.IpAddress.Returns("127.0.0.1");
        user.UserAgent.Returns("xunit");
        return user;
    }

    private sealed record PeriodSensitiveCommand(DateTimeOffset OperationDate, string Module) : IPeriodSensitiveRequest;
}
