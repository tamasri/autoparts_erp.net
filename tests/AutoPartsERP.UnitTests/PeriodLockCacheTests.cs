using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Domain.Governance;
using AutoPartsERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using NSubstitute;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class PeriodLockCacheTests
{
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();
    private readonly PeriodLockService _service;

    public PeriodLockCacheTests()
    {
        _service = new PeriodLockService(_cache, Substitute.For<IDbConnectionFactory>());
    }

    [Fact]
    public async Task ALockOnOneModule_ForgetsThatModulesCachedAnswer_AndTheCatchAllOne()
    {
        await _service.InvalidateCacheAsync(2026, 3, "ACCOUNTING");

        await _cache.Received(1).RemoveAsync("period:ACCOUNTING:2026-03", Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveAsync("period:ALL:2026-03", Arg.Any<CancellationToken>());
        await _cache.DidNotReceive().RemoveAsync("period:SALES:2026-03", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("SALES")]
    [InlineData("PURCHASES")]
    [InlineData("PAYMENTS")]
    [InlineData("ACCOUNTING")]
    public async Task ALockOnEveryModule_ForgetsEachModulesCachedAnswer(string module)
    {
        await _service.InvalidateCacheAsync(2026, 3, "ALL");

        await _cache.Received(1).RemoveAsync($"period:{module}:2026-03", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ARelockedPeriod_IsLockedAgain_WithTheNewReason_AndNoUnlockTrace()
    {
        var locker = Guid.NewGuid();
        var period = new PeriodLock(Guid.NewGuid(), "2026-03", "ACCOUNTING", locker, "first lock");
        period.Unlock(Guid.NewGuid(), "reopened");

        period.Relock(locker, "second lock");

        period.IsLocked.Should().BeTrue();
        period.Reason.Should().Be("second lock");
        period.UnlockedAtUtc.Should().BeNull();
        period.UnlockedByUserId.Should().BeNull();
    }
}
