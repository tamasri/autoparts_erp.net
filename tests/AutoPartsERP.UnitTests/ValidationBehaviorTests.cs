using AutoPartsERP.Application.Common.Behaviors;
using AutoPartsERP.Domain.Common;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_ShouldCallNext_WhenNoValidatorsAreRegistered()
    {
        var behavior = new ValidationBehavior<TestRequest, Result<string>>(Array.Empty<IValidator<TestRequest>>());
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestRequest("ok"),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result<string>.Success("done"));
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldCallNext_WhenValidationPasses()
    {
        var validators = new IValidator<TestRequest>[] { new TestRequestValidator() };
        var behavior = new ValidationBehavior<TestRequest, Result<string>>(validators);
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestRequest("valid"),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result<string>.Success("done"));
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnFailure_WhenValidationFails()
    {
        var validators = new IValidator<TestRequest>[] { new TestRequestValidator() };
        var behavior = new ValidationBehavior<TestRequest, Result<string>>(validators);
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestRequest(string.Empty),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result<string>.Success("done"));
            },
            CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation.Failed");
    }

    private sealed record TestRequest(string Name);

    private sealed class TestRequestValidator : AbstractValidator<TestRequest>
    {
        public TestRequestValidator()
        {
            RuleFor(request => request.Name).NotEmpty();
        }
    }
}
