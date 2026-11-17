using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Dashboard;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Enums;
using InviteCraft.UnitTests.Payments;
using NSubstitute;
using Shouldly;

namespace InviteCraft.UnitTests.Dashboard;

public class DashboardServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly IDashboardRepository _repository = Substitute.For<IDashboardRepository>();
    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly DashboardService _sut;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _templateId = Guid.NewGuid();

    public DashboardServiceTests()
    {
        _sut = new DashboardService(_repository, _invitations, _unitOfWork, new TemplateMapper(_storage), new FixedClock(Now));
    }

    [Fact]
    public async Task Save_adds_a_bookmark_for_a_published_template_and_commits()
    {
        _repository.IsPublishedAsync(_templateId, Ct).Returns(true);
        _repository.AddSavedAsync(_userId, _templateId, Now, Ct).Returns(true);

        var result = await _sut.SaveAsync(_userId, _templateId, Ct);

        result.IsSuccess.ShouldBeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Save_is_idempotent_and_does_not_commit_when_already_saved()
    {
        _repository.IsPublishedAsync(_templateId, Ct).Returns(true);
        _repository.AddSavedAsync(_userId, _templateId, Now, Ct).Returns(false);

        var result = await _sut.SaveAsync(_userId, _templateId, Ct);

        result.IsSuccess.ShouldBeTrue();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_returns_not_found_for_an_unpublished_or_unknown_template()
    {
        _repository.IsPublishedAsync(_templateId, Ct).Returns(false);

        var result = await _sut.SaveAsync(_userId, _templateId, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Type.ShouldBe(ErrorType.NotFound);
        await _repository.DidNotReceive().AddSavedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_refuses_past_the_limit()
    {
        _repository.IsPublishedAsync(_templateId, Ct).Returns(true);
        _repository.CountSavedAsync(_userId, Ct).Returns(DashboardService.MaxSaved);

        var result = await _sut.SaveAsync(_userId, _templateId, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("Saved.LimitReached");
        result.Errors[0].Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task Unsave_removes_and_commits_even_when_nothing_was_saved()
    {
        var result = await _sut.UnsaveAsync(_userId, _templateId, Ct);

        result.IsSuccess.ShouldBeTrue();
        await _repository.Received(1).RemoveSavedAsync(_userId, _templateId, Ct);
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }
}
