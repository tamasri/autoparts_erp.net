using AutoPartsERP.Application.Features.Users.CreateUser;
using AutoPartsERP.Contracts.Users;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class CreateUserValidatorTests
{
    private readonly CreateUserCommandValidator _validator = new();

    [Fact]
    public void Validate_ShouldPass_ForValidCommand()
    {
        var result = _validator.Validate(CreateValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldFail_WhenIdempotencyKeyIsMissing()
    {
        var command = CreateValidCommand() with { IdempotencyKey = string.Empty };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "IdempotencyKey");
    }

    [Fact]
    public void Validate_ShouldFail_WhenEmailIsInvalid()
    {
        var request = CreateValidCommand().Request with { Email = "invalid-email" };
        var command = CreateValidCommand() with { Request = request };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "Request.Email");
    }

    [Fact]
    public void Validate_ShouldFail_WhenPasswordTooShort()
    {
        var request = CreateValidCommand().Request with { Password = "Ab1!" };
        var command = CreateValidCommand() with { Request = request };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "Request.Password");
    }

    [Fact]
    public void Validate_ShouldFail_WhenRoleIdsAreEmpty()
    {
        var request = CreateValidCommand().Request with { RoleIds = Array.Empty<Guid>() };
        var command = CreateValidCommand() with { Request = request };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "Request.RoleIds");
    }

    private static CreateUserCommand CreateValidCommand()
    {
        var request = new CreateUserRequest(
            "john.doe",
            "john.doe@example.com",
            "John",
            "Doe",
            "SecurePass123!",
            new[] { Guid.NewGuid() });

        return new CreateUserCommand(request, "idem-001");
    }
}
