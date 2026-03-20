using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Abstractions.Markers;
using AutoPartsERP.Application.Common.Behaviors;
using AutoPartsERP.Application.Common.Models;
using AutoPartsERP.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class IdempotencyBehaviorTests
{
    [Fact]
    public async Task Handle_ShouldExecuteAndComplete_WhenRequestIsNew()
    {
        var idempotency = Substitute.For<IIdempotencyService>();
        var currentUser = BuildCurrentUser();
        var audit = Substitute.For<IManualAuditService>();
        idempotency.CheckAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(IdempotencyCheckResult.New());

        var behavior = new IdempotencyBehavior<IdempotentCommand, Result<string>>(idempotency, currentUser, audit);
        var nextCalled = false;

        var result = await behavior.Handle(
            new IdempotentCommand("idem-001"),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result<string>.Success("created"));
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        await idempotency.Received(1).CompleteAsync("idem-001", currentUser.UserId, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnConflictFailure_WhenKeyIsInProgress()
    {
        var idempotency = Substitute.For<IIdempotencyService>();
        var currentUser = BuildCurrentUser();
        var audit = Substitute.For<IManualAuditService>();
        idempotency.CheckAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(IdempotencyCheckResult.Conflict());

        var behavior = new IdempotencyBehavior<IdempotentCommand, Result<string>>(idempotency, currentUser, audit);
        var nextCalled = false;

        var result = await behavior.Handle(
            new IdempotentCommand("idem-002"),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result<string>.Success("created"));
            },
            CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Idempotency.Conflict");
    }

    [Fact]
    public async Task Handle_ShouldReturnDeserializeFailure_WhenReplayPayloadCannotBeRestored()
    {
        var idempotency = Substitute.For<IIdempotencyService>();
        var currentUser = BuildCurrentUser();
        var audit = Substitute.For<IManualAuditService>();
        idempotency.CheckAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(IdempotencyCheckResult.Replay("{\"broken\":true}"));

        var behavior = new IdempotencyBehavior<IdempotentCommand, Result<string>>(idempotency, currentUser, audit);
        var nextCalled = false;

        var action = async () =>
            await behavior.Handle(
                new IdempotentCommand("idem-003"),
                _ =>
                {
                    nextCalled = true;
                    return Task.FromResult(Result<string>.Success("created"));
                },
                CancellationToken.None);

        await action.Should().ThrowAsync<NotSupportedException>();
        nextCalled.Should().BeFalse();
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

    private sealed record IdempotentCommand(string IdempotencyKey) : IIdempotentRequest;
}
