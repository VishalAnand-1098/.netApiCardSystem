using InviteCraft.Application.Common.Models;

namespace InviteCraft.UnitTests.Common;

public class ResultTests
{
    [Fact]
    public void Success_result_exposes_value_and_no_errors()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Failure_result_carries_error_and_blocks_value_access()
    {
        Result<int> result = Error.NotFound("Template.NotFound", "Template was not found.");

        result.IsFailure.ShouldBeTrue();
        result.FirstError!.Type.ShouldBe(ErrorType.NotFound);
        result.FirstError.Code.ShouldBe("Template.NotFound");
        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Failure_without_errors_is_rejected()
    {
        Should.Throw<ArgumentException>(() => Result.Failure(Array.Empty<Error>()));
    }
}
