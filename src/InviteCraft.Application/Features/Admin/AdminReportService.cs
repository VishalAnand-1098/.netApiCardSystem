using System.Globalization;
using System.Text;
using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Admin;

internal sealed class AdminReportService(IAdminRepository repository, TimeProvider timeProvider) : IAdminReportService
{
    public const int MaxRangeDays = 366;

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var today = ReportTime.DayOf(now);
        var firstDay = today.AddDays(-29);
        var from = ReportTime.StartOf(firstDay);
        var to = ReportTime.StartOf(today.AddDays(1));

        var facts = await repository.GetOrderFactsAsync(from, to, cancellationToken);
        var paid = facts.Where(f => f.Status == OrderStatus.Paid).ToList();
        var byDay = paid.GroupBy(f => ReportTime.DayOf(f.PaidAt)).ToDictionary(g => g.Key, g => (Orders: g.Count(), Revenue: g.Sum(f => f.Total)));

        var series = Enumerable.Range(0, 30).Select(i => firstDay.AddDays(i))
            .Select(day => byDay.TryGetValue(day, out var v) ? new DayPoint(day, v.Orders, v.Revenue) : new DayPoint(day, 0, 0m))
            .ToList();

        var counts = await repository.GetPlatformCountsAsync(now, cancellationToken);
        var revenue30 = paid.Sum(f => f.Total);
        var kpis = new DashboardKpis(
            RevenueToday: paid.Where(f => ReportTime.DayOf(f.PaidAt) == today).Sum(f => f.Total),
            Revenue7Days: paid.Where(f => ReportTime.DayOf(f.PaidAt) >= today.AddDays(-6)).Sum(f => f.Total),
            Revenue30Days: revenue30,
            RevenueTotal: await repository.GetPaidRevenueTotalAsync(cancellationToken),
            OrdersPaid: paid.Count,
            AverageOrderValue: paid.Count == 0 ? 0m : Math.Round(revenue30 / paid.Count, 2),
            Refunded30Days: facts.Where(f => f.Status == OrderStatus.Refunded).Sum(f => f.Total),
            Customers: counts.Customers, NewCustomers30Days: counts.NewCustomers30Days, Invitations: counts.Invitations,
            UnlockedInvitations: counts.Unlocked, Downloads30Days: counts.Downloads30Days,
            PendingReviews: counts.PendingReviews, AwaitingPaymentOrders: counts.AwaitingPaymentOrders);

        var recent = await repository.ListOrdersAsync(new AdminOrderQuery { PageSize = 6 }, null, null, cancellationToken);
        return new AdminDashboardDto(kpis, series,
            await repository.GetTopTemplatesAsync(from, to, 5, cancellationToken),
            await repository.GetSalesByPackageAsync(from, to, cancellationToken),
            recent.Items);
    }

    public async Task<Result<SalesReportDto>> GetSalesAsync(SalesReportQuery query, CancellationToken cancellationToken = default)
    {
        var range = ParseRange(query);
        if (range.IsFailure)
            return Result.Failure<SalesReportDto>(range.Errors);
        var (from, to, groupBy) = range.Value;

        var facts = await repository.GetOrderFactsAsync(ReportTime.StartOf(from), ReportTime.StartOf(to.AddDays(1)), cancellationToken);
        string Key(DateTimeOffset at) => groupBy == "month"
            ? ReportTime.DayOf(at).ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : ReportTime.DayOf(at).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var groups = facts.GroupBy(f => Key(f.PaidAt)).ToDictionary(g => g.Key, g => g.ToList());
        var periods = new List<string>();
        for (var day = from; day <= to; day = groupBy == "month" ? day.AddMonths(1) : day.AddDays(1))
        {
            var key = groupBy == "month" ? day.ToString("yyyy-MM", CultureInfo.InvariantCulture) : day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (periods.Count == 0 || periods[^1] != key)
                periods.Add(key);
        }
        // a month range that does not start on the 1st still ends in the month of `to`
        var lastKey = groupBy == "month" ? to.ToString("yyyy-MM", CultureInfo.InvariantCulture) : to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (periods[^1] != lastKey)
            periods.Add(lastKey);

        var rows = periods.Select(p => ToRow(p, groups.TryGetValue(p, out var list) ? list : [])).ToList();
        return new SalesReportDto(from, to, groupBy, rows, ToRow("Total", facts));
    }

    public async Task<Result<string>> GetSalesCsvAsync(SalesReportQuery query, CancellationToken cancellationToken = default)
    {
        var report = await GetSalesAsync(query, cancellationToken);
        if (report.IsFailure)
            return Result.Failure<string>(report.Errors);

        var csv = new StringBuilder("Period,Orders,Gross,Discounts,GST included,Net of GST,Refunded orders,Refunded amount\r\n");
        foreach (var row in report.Value.Rows.Append(report.Value.Totals))
            csv.Append(row.Period).Append(',').Append(row.Orders).Append(',')
               .Append(Money(row.Gross)).Append(',').Append(Money(row.Discounts)).Append(',').Append(Money(row.Gst)).Append(',')
               .Append(Money(row.Net)).Append(',').Append(row.RefundedOrders).Append(',').Append(Money(row.Refunded)).Append("\r\n");
        return csv.ToString();
    }

    public async Task<Result<IReadOnlyList<TemplateSalesRow>>> GetTopTemplatesAsync(SalesReportQuery query, CancellationToken cancellationToken = default)
    {
        var range = ParseRange(query);
        if (range.IsFailure)
            return Result.Failure<IReadOnlyList<TemplateSalesRow>>(range.Errors);
        var (from, to, _) = range.Value;
        return Result.Success(await repository.GetTopTemplatesAsync(ReportTime.StartOf(from), ReportTime.StartOf(to.AddDays(1)), 20, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<CouponUsageRow>>> GetCouponUsageAsync(SalesReportQuery query, CancellationToken cancellationToken = default)
    {
        var range = ParseRange(query);
        if (range.IsFailure)
            return Result.Failure<IReadOnlyList<CouponUsageRow>>(range.Errors);
        var (from, to, _) = range.Value;
        return Result.Success(await repository.GetCouponUsageAsync(ReportTime.StartOf(from), ReportTime.StartOf(to.AddDays(1)), cancellationToken));
    }

    // ------------------------------------------------------------------ helpers

    private Result<(DateOnly From, DateOnly To, string GroupBy)> ParseRange(SalesReportQuery query)
    {
        var today = ReportTime.DayOf(timeProvider.GetUtcNow());
        var to = query.To ?? today;
        var from = query.From ?? to.AddDays(-29);
        var groupBy = string.IsNullOrWhiteSpace(query.GroupBy) ? "day" : query.GroupBy.Trim().ToLowerInvariant();

        if (groupBy is not ("day" or "month"))
            return Result.Failure<(DateOnly, DateOnly, string)>(Error.Validation("Report.InvalidGroupBy", "groupBy must be day or month.", "groupBy"));
        if (from > to)
            return Result.Failure<(DateOnly, DateOnly, string)>(AdminErrors.BadRange("The start date must not be after the end date."));
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
            return Result.Failure<(DateOnly, DateOnly, string)>(AdminErrors.BadRange($"Choose a range of at most {MaxRangeDays} days."));
        return (from, to, groupBy);
    }

    private static SalesRow ToRow(string period, IReadOnlyCollection<OrderFact> facts)
    {
        var paid = facts.Where(f => f.Status == OrderStatus.Paid).ToList();
        var refunded = facts.Where(f => f.Status == OrderStatus.Refunded).ToList();
        var gross = paid.Sum(f => f.Total);
        var gst = paid.Sum(f => f.Tax);
        return new SalesRow(period, paid.Count, gross, paid.Sum(f => f.Discount), gst, gross - gst, refunded.Count, refunded.Sum(f => f.Total));
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
