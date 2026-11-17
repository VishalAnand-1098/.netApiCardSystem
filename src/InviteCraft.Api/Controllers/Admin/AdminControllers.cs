using System.Text;
using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Admin;
using InviteCraft.Application.Features.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers.Admin;

/// <summary>Headline numbers and sales reports. Reports are cut on Indian (IST) calendar days.</summary>
[Route(ApiRoutes.AdminV1)]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class AdminReportsController(IAdminReportService reports) : ApiControllerBase
{
    /// <summary>KPIs, last-30-days revenue series, top templates, sales by package and the latest orders.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType<ApiResponse<AdminDashboardDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminDashboardDto>>> Dashboard(CancellationToken cancellationToken) =>
        OkResponse(await reports.GetDashboardAsync(cancellationToken));

    /// <summary>?from=&amp;to=&amp;groupBy=day|month — paid orders grouped by period, with refunds shown separately.</summary>
    [HttpGet("reports/sales")]
    [ProducesResponseType<ApiResponse<SalesReportDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SalesReportDto>>> Sales([FromQuery] SalesReportQuery query, CancellationToken cancellationToken) =>
        FromResult(await reports.GetSalesAsync(query, cancellationToken));

    /// <summary>The same report as a CSV download (UTF-8 with BOM so Excel opens it correctly).</summary>
    [HttpGet("reports/sales.csv")]
    [Produces("text/csv")]
    public async Task<ActionResult> SalesCsv([FromQuery] SalesReportQuery query, CancellationToken cancellationToken)
    {
        var result = await reports.GetSalesCsvAsync(query, cancellationToken);
        if (result.IsFailure)
            return ErrorResponse(result.Errors);

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(result.Value)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"sales-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    /// <summary>Best-selling templates in the range (top 20 by revenue).</summary>
    [HttpGet("reports/templates")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TemplateSalesRow>>>> Templates([FromQuery] SalesReportQuery query, CancellationToken cancellationToken) =>
        FromResult(await reports.GetTopTemplatesAsync(query, cancellationToken));

    /// <summary>Redemptions and discount given per coupon in the range.</summary>
    [HttpGet("reports/coupons")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CouponUsageRow>>>> Coupons([FromQuery] SalesReportQuery query, CancellationToken cancellationToken) =>
        FromResult(await reports.GetCouponUsageAsync(query, cancellationToken));
}

[Route(ApiRoutes.AdminV1 + "/customers")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class AdminCustomersController(IAdminDirectoryService directory) : ApiControllerBase
{
    /// <summary>?search=(name or email)&amp;isActive=&amp;sortBy=name|email|lastLogin&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminCustomerListItem>>>> List([FromQuery] AdminCustomerQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await directory.ListCustomersAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminCustomerDetail>>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await directory.GetCustomerAsync(id, cancellationToken));

    /// <summary>Deactivating signs the customer out everywhere and blocks sign-in; administrators cannot be deactivated here.</summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<AdminCustomerListItem>>> SetStatus(Guid id, SetActiveStatusRequest request, CancellationToken cancellationToken) =>
        FromResult(await directory.SetCustomerActiveAsync(id, request.IsActive, cancellationToken),
            request.IsActive ? "Customer activated." : "Customer deactivated.");
}

[Route(ApiRoutes.AdminV1)]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class AdminOrdersController(IAdminDirectoryService directory) : ApiControllerBase
{
    /// <summary>?status=&amp;from=&amp;to=&amp;userId=&amp;search=(order number, name, email)&amp;page=&amp;pageSize=</summary>
    [HttpGet("orders")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminOrderListItem>>>> Orders([FromQuery] AdminOrderQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await directory.ListOrdersAsync(query, cancellationToken));

    [HttpGet("orders/{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminOrderDetailDto>>> Order(Guid id, CancellationToken cancellationToken) =>
        FromResult(await directory.GetOrderAsync(id, cancellationToken));

    /// <summary>
    /// Full refund of a paid order. The provider is asked first (502-style failures leave the order untouched, 422), then the order becomes
    /// Refunded and the invitations lock again. 409 when the order is not paid.
    /// </summary>
    [HttpPost("orders/{id:guid}/refund")]
    public async Task<ActionResult<ApiResponse<AdminOrderDetailDto>>> Refund(Guid id, RefundOrderRequest request, CancellationToken cancellationToken) =>
        FromResult(await directory.RefundOrderAsync(id, request, cancellationToken), "The order was refunded.");

    /// <summary>?status=&amp;search=(order number, email, gateway payment id)&amp;page=&amp;pageSize=</summary>
    [HttpGet("payments")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminPaymentListItem>>>> Payments([FromQuery] AdminPaymentQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await directory.ListPaymentsAsync(query, cancellationToken));
}

[Route(ApiRoutes.AdminV1 + "/coupons")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class AdminCouponsController(IAdminCouponService coupons) : ApiControllerBase
{
    /// <summary>?search=&amp;isActive=&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminCouponDto>>>> List([FromQuery] AdminCouponQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await coupons.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminCouponDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await coupons.GetAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType<ApiResponse<AdminCouponDto>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<AdminCouponDto>>> Create(SaveCouponRequest request, CancellationToken cancellationToken) =>
        CreatedResult(await coupons.CreateAsync(request, cancellationToken), c => $"/{ApiRoutes.AdminV1}/coupons/{c.Id}", "Coupon created.");

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminCouponDto>>> Update(Guid id, SaveCouponRequest request, CancellationToken cancellationToken) =>
        FromResult(await coupons.UpdateAsync(id, request, cancellationToken), "Coupon updated.");

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<AdminCouponDto>>> SetStatus(Guid id, SetActiveStatusRequest request, CancellationToken cancellationToken) =>
        FromResult(await coupons.SetActiveAsync(id, request.IsActive, cancellationToken), request.IsActive ? "Coupon activated." : "Coupon deactivated.");

    /// <summary>Refused (409) once the coupon has been used — deactivate it instead.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        FromResult(await coupons.DeleteAsync(id, cancellationToken), "Coupon deleted.");
}

[Route(ApiRoutes.AdminV1)]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class AdminModerationController(IAdminDirectoryService directory) : ApiControllerBase
{
    /// <summary>?status=Pending|Approved|Rejected&amp;search=&amp;page=&amp;pageSize=</summary>
    [HttpGet("reviews")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminReviewItem>>>> Reviews([FromQuery] AdminReviewQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await directory.ListReviewsAsync(query, cancellationToken));

    /// <summary>Approve/reject, feature on the storefront (approved only) and/or reply. Fields left out stay as they are.</summary>
    [HttpPatch("reviews/{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminReviewItem>>> UpdateReview(Guid id, UpdateReviewRequest request, CancellationToken cancellationToken) =>
        FromResult(await directory.UpdateReviewAsync(id, request, cancellationToken), "Review updated.");

    /// <summary>?action=&amp;entityType=&amp;from=&amp;to=&amp;search=&amp;page=&amp;pageSize= — newest first.</summary>
    [HttpGet("audit-logs")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AuditLogItem>>>> AuditLogs([FromQuery] AuditLogQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await directory.ListAuditLogsAsync(query, cancellationToken));
}
