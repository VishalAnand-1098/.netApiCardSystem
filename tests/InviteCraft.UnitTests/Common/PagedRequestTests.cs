using InviteCraft.Application.Common.Models;

namespace InviteCraft.UnitTests.Common;

public class PagedRequestTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public void Page_is_clamped_to_at_least_one(int requested, int expected)
    {
        new PagedRequest { Page = requested }.Page.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, PagedRequest.DefaultPageSize)]
    [InlineData(500, PagedRequest.MaxPageSize)]
    [InlineData(24, 24)]
    public void PageSize_is_clamped_to_allowed_range(int requested, int expected)
    {
        new PagedRequest { PageSize = requested }.PageSize.ShouldBe(expected);
    }

    [Fact]
    public void Skip_is_derived_from_page_and_size()
    {
        new PagedRequest { Page = 3, PageSize = 24 }.Skip.ShouldBe(48);
    }

    [Fact]
    public void PagedResult_computes_total_pages()
    {
        var result = new PagedResult<string>(["a"], Page: 1, PageSize: 24, TotalCount: 49);

        result.TotalPages.ShouldBe(3);
        result.HasNextPage.ShouldBeTrue();
    }
}
