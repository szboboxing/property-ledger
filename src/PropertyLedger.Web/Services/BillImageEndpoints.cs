using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Data;
using PropertyLedger.Core.Models;
using PropertyLedger.Core.Services;

namespace PropertyLedger.Web.Services;

/// <summary>账单图片下载端点：/bills/{id}/bill.png（需登录）。</summary>
public static class BillImageEndpoints
{
    public static IEndpointConventionBuilder MapBillImageEndpoints(this IEndpointRouteBuilder routes)
    {
        return routes.MapGet("/bills/{id:int}/bill.png", async (
            int id, AppDbContext db, SettingsService settings, BillImageRenderer renderer, CancellationToken ct) =>
        {
            var bill = await db.Bills
                .Include(b => b.Room).ThenInclude(r => r!.Property)
                .Include(b => b.Tenant)
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.Id == id, ct);
            if (bill is null) return Results.NotFound();

            var sets = await settings.GetManyAsync(
                new[] { SettingKeys.LandlordName, SettingKeys.PaymentNotice, SettingKeys.PayQrCode }, ct);

            var lines = new List<(string, decimal)> { ("租金", bill.RentAmount) };
            foreach (var item in bill.Items.OrderBy(i => i.Id))
                lines.Add((item.Name, item.Amount));

            var view = new BillImageView
            {
                LandlordName = sets.GetValueOrDefault(SettingKeys.LandlordName) ?? "房东",
                PropertyName = bill.Room?.Property?.Name,
                RoomName = bill.Room?.Name ?? "-",
                TenantName = bill.Tenant?.Name ?? "-",
                TenantPhone = bill.Tenant?.Phone,
                Period = bill.PeriodStart.ToString("yyyy-MM"),
                PeriodRange = $"{bill.PeriodStart:yyyy-MM-dd} ~ {bill.PeriodEnd:yyyy-MM-dd}",
                DueDate = bill.DueDate.ToString("yyyy-MM-dd"),
                StatusText = bill.Status switch
                {
                    BillStatus.Paid => "已结清",
                    BillStatus.Partial => "部分已付",
                    _ => bill.IsOverdue(DateTime.Today) ? "已逾期" : "待付款",
                },
                Lines = lines,
                Total = bill.TotalAmount,
                Paid = bill.PaidAmount,
                Remaining = bill.RemainingAmount,
                PaymentNotice = sets.GetValueOrDefault(SettingKeys.PaymentNotice) ?? "",
                PayQrCodeDataUri = sets.GetValueOrDefault(SettingKeys.PayQrCode),
            };

            var png = renderer.Render(view);
            var fileName = $"bill-{bill.PeriodStart:yyyyMM}-{id}.png";
            return Results.File(png, "image/png", fileName);
        }).RequireAuthorization();
    }
}
