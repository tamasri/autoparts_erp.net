using AutoPartsERP.Domain.Messaging;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class OutboxMessageTests
{
    [Fact]
    public void Create_SetsOccurredAtUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var message = OutboxMessage.Create(
            "InvoicePosted",
            "Invoice",
            Guid.NewGuid(),
            new { Value = 10 },
            Guid.NewGuid());

        message.OccurredAt.Should().BeOnOrAfter(before);
        message.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public void MarkProcessed_SetsProcessedAt()
    {
        var message = OutboxMessage.Create(
            "InvoicePosted",
            "Invoice",
            Guid.NewGuid(),
            new { Value = 10 },
            Guid.NewGuid());

        message.MarkProcessed();

        message.ProcessedAt.Should().NotBeNull();
        message.ProcessingError.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_IncrementsRetryCount()
    {
        var message = OutboxMessage.Create(
            "InvoicePosted",
            "Invoice",
            Guid.NewGuid(),
            new { Value = 10 },
            Guid.NewGuid());

        message.MarkFailed("failed once");
        message.MarkFailed("failed twice");

        message.RetryCount.Should().Be(2);
        message.ProcessingError.Should().Be("failed twice");
    }
}
