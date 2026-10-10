using Microsoft.EntityFrameworkCore;
using PropertyLedger.Core.Data;
using PropertyLedger.Core.Models;

namespace PropertyLedger.Core.Services;

/// <summary>
/// 账单领域服务：按租约周期自动出账、登记/撤销收款、计算剩余与状态。
/// 所有金额变动都在这里收口，界面层不直接改 PaidAmount / TotalAmount / Status。
/// </summary>
public class BillingService
{
    private readonly AppDbContext _db;
    public BillingService(AppDbContext db) => _db = db;

    // ───────────────────────── 出账周期计算（纯函数，便于单测）─────────────────────────

    /// <summary>
    /// 判断租约在指定自然月是否应出账，并计算账期。
    /// 月付：账期为该自然月；季付：仅在与起租月相隔 3 的整数倍的月份出账，账期连续 3 个月。
    /// 账期与租期必须有交集。
    /// </summary>
    public static bool TryGetPeriod(Lease lease, int year, int month, out PeriodPlan plan)
    {
        plan = default;
        var monthStart = PeriodCalendar.FirstDayOfMonth(year, month);
        var monthEnd = PeriodCalendar.LastDayOfMonth(monthStart);

        if (lease.PaymentCycle == PaymentCycle.Monthly)
        {
            // 与租期无交集则不出账
            if (monthEnd < lease.StartDate) return false;
            if (lease.EndDate.HasValue && monthStart > lease.EndDate.Value) return false;

            plan = new PeriodPlan(
                monthStart,
                monthEnd,
                PeriodCalendar.DueDate(year, month, lease.PaymentDay),
                lease.RentAmount);
            return true;
        }

        // 季付：以起租月为基准，每 3 个月一期，仅账期首月出账
        var delta = PeriodCalendar.MonthIndex(monthStart) - PeriodCalendar.MonthIndex(lease.StartDate);
        if (delta < 0 || delta % 3 != 0) return false;

        // 季账期：当月起连续 3 个自然月
        var periodEndDate = monthStart.AddMonths(2);
        var periodEnd = PeriodCalendar.LastDayOfMonth(periodEndDate);

        if (periodEnd < lease.StartDate) return false;
        if (lease.EndDate.HasValue && monthStart > lease.EndDate.Value) return false;

        plan = new PeriodPlan(
            monthStart,
            periodEnd,
            PeriodCalendar.DueDate(year, month, lease.PaymentDay),
            lease.RentAmount * 3m);
        return true;
    }

    // ───────────────────────── 自动补账 ─────────────────────────

    /// <summary>
    /// 为所有生效租约补齐从起租月到 <paramref name="throughMonth"/> 的全部应出账单（幂等）。
    /// 通常在打开账单页 / 仪表盘时调用，确保当月账单存在。返回新建账单数。
    /// </summary>
    public async Task<int> EnsureBillsThroughAsync(DateTime throughMonth, CancellationToken ct = default)
    {
        var leases = await _db.Leases
            .AsNoTracking()
            .Where(l => l.Status == LeaseStatus.Active)
            .ToListAsync(ct);

        var created = 0;
        foreach (var lease in leases)
        {
            // 已存在的账期首月，避免重复（唯一索引兜底）
            var existing = await _db.Bills
                .Where(b => b.LeaseId == lease.Id)
                .Select(b => b.PeriodStart)
                .ToListAsync(ct);
            var existingSet = existing.Select(d => (d.Year, d.Month)).ToHashSet();

            var cursor = new DateTime(
                Math.Max(lease.StartDate.Year, 1),
                lease.StartDate.Month, 1);
            // 季付从起租月开始步进，月付逐月；统一用月步进 + TryGetPeriod 判定即可
            while (cursor <= throughMonth)
            {
                if (TryGetPeriod(lease, cursor.Year, cursor.Month, out var plan) &&
                    !existingSet.Contains((plan.PeriodStart.Year, plan.PeriodStart.Month)))
                {
                    _db.Bills.Add(new Bill
                    {
                        LeaseId = lease.Id,
                        RoomId = lease.RoomId,
                        TenantId = lease.TenantId,
                        PeriodStart = plan.PeriodStart,
                        PeriodEnd = plan.PeriodEnd,
                        DueDate = plan.DueDate,
                        RentAmount = plan.RentAmount,
                        TotalAmount = plan.RentAmount,
                        PaidAmount = 0m,
                        Status = BillStatus.Unpaid,
                    });
                    created++;
                }
                cursor = cursor.AddMonths(1);
            }
        }

        if (created > 0) await _db.SaveChangesAsync(ct);
        return created;
    }

    // ───────────────────────── 人工补录 ─────────────────────────

    /// <summary>补录结果统计：新建数 / 跳过数（该账期已有账单）/ 租期外月份数。</summary>
    public readonly record struct ManualRangeResult(int Created, int Skipped, int OutOfRange, List<Bill> CreatedBills);

    /// <summary>
    /// 补录过往账单（记录历史欠款）：为指定租约补录一段连续月份的账单（单月 = 起止相同）。
    /// 允许补录已结束的租约；逐月判定：与租期无交集的月份跳过并计数，已有账单的月份跳过（幂等），
    /// 其余新建。金额留空时每张按租约标准金额自动计算（季付 = 三个月租金）。
    /// </summary>
    public async Task<ManualRangeResult> CreateManualBillsRangeAsync(
        int leaseId, int startYear, int startMonth, int endYear, int endMonth,
        decimal? amountPerBill, string? note, CancellationToken ct = default)
    {
        var lease = await _db.Leases.AsNoTracking().FirstOrDefaultAsync(l => l.Id == leaseId, ct)
                    ?? throw new DomainException("租约不存在。");

        if (amountPerBill is { } amt && amt <= 0m)
            throw new DomainException("补录金额必须大于 0。");

        var startIdx = startYear * 12 + (startMonth - 1);
        var endIdx = endYear * 12 + (endMonth - 1);
        if (startIdx > endIdx)
            throw new DomainException("开始月份不能晚于结束月份。");

        // 已有账期集合（避免逐月查库）
        var startY = new DateTime(startYear, startMonth, 1);
        var endFirst = new DateTime(endYear, endMonth, 1);
        var existing = await _db.Bills
            .Where(b => b.LeaseId == leaseId && b.PeriodStart >= startY && b.PeriodStart <= endFirst)
            .Select(b => b.PeriodStart)
            .ToListAsync(ct);
        var existingSet = existing.Select(d => (d.Year, d.Month)).ToHashSet();

        var created = 0;
        var skipped = 0;
        var outOfRange = 0;
        var createdBills = new List<Bill>();

        var cursor = startY;
        while (cursor <= endFirst)
        {
            if (!TryGetPeriod(lease, cursor.Year, cursor.Month, out var plan))
            {
                outOfRange++;
            }
            else if (existingSet.Contains((plan.PeriodStart.Year, plan.PeriodStart.Month)))
            {
                skipped++;
            }
            else
            {
                var rent = amountPerBill ?? plan.RentAmount;
                var bill = new Bill
                {
                    LeaseId = lease.Id,
                    RoomId = lease.RoomId,
                    TenantId = lease.TenantId,
                    PeriodStart = plan.PeriodStart,
                    PeriodEnd = plan.PeriodEnd,
                    DueDate = plan.DueDate,
                    RentAmount = rent,
                    TotalAmount = rent,
                    PaidAmount = 0m,
                    Status = BillStatus.Unpaid,
                    IsManual = true,
                    Note = note,
                };
                _db.Bills.Add(bill);
                createdBills.Add(bill);
                created++;
            }
            cursor = cursor.AddMonths(1);
        }

        if (created > 0) await _db.SaveChangesAsync(ct);
        return new ManualRangeResult(created, skipped, outOfRange, createdBills);
    }

    /// <summary>
    /// 补录单月账单（范围补录的特例，保留语义化入口）：
    /// 该月不在租期内或已有账单时直接抛错（与多月批量补录的"跳过并汇总"不同）。
    /// </summary>
    public async Task<Bill> CreateManualBillAsync(
        int leaseId, int year, int month, decimal? amount, string? note, CancellationToken ct = default)
    {
        var result = await CreateManualBillsRangeAsync(leaseId, year, month, year, month, amount, note, ct);
        if (result.OutOfRange > 0)
            throw new DomainException($"{year} 年 {month} 月不在该租约的租期内，无法补录。");
        if (result.Skipped > 0)
            throw new DomainException("该账期已存在账单，无需重复补录。");
        return result.CreatedBills.Single();
    }

    // ───────────────────────── 收款 ─────────────────────────

    /// <summary>登记一笔收款（支持部分收款），自动累加并更新状态。</summary>
    public async Task<Payment> RecordPaymentAsync(
        int billId, decimal amount, PaymentMethod method, DateTime paidAt, string? note, CancellationToken ct = default)
    {
        if (amount <= 0m) throw new DomainException("收款金额必须大于 0。");

        var bill = await _db.Bills.Include(b => b.Payments).FirstOrDefaultAsync(b => b.Id == billId, ct)
                   ?? throw new DomainException("账单不存在。");

        if (amount > bill.RemainingAmount + 0.001m)
            throw new DomainException($"收款金额超出剩余应收（剩余 ¥{bill.RemainingAmount:0.00}）。");

        var payment = new Payment
        {
            BillId = bill.Id,
            Amount = amount,
            PaidAt = paidAt,
            Method = method,
            Note = note,
        };
        _db.Payments.Add(payment);
        // 注意：不要再 bill.Payments.Add(payment)——bill 已被跟踪，
        // Add 时 EF 关系修复会自动把 payment 加入导航集合，手动再加会重复计入。

        Recalculate(bill);
        await _db.SaveChangesAsync(ct);
        return payment;
    }

    /// <summary>撤销一笔收款，自动回退已收金额与状态。</summary>
    public async Task DeletePaymentAsync(int paymentId, CancellationToken ct = default)
    {
        var bill = await _db.Bills.Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Payments.Any(p => p.Id == paymentId), ct)
            ?? throw new DomainException("收款记录不存在。");

        var payment = bill.Payments.First(p => p.Id == paymentId);
        _db.Payments.Remove(payment);
        bill.Payments.Remove(payment);
        Recalculate(bill);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 批量结清：为指定账单逐张登记一笔金额 = 剩余应收的全额收款（备注「批量结清」），
    /// 与单笔收款走同一套重算逻辑，报表已收金额同步增加；已结清/不存在的账单自动跳过。
    /// 返回（结清张数，登记收款总额）。
    /// </summary>
    public async Task<(int Settled, decimal Total)> BatchSettleAsync(
        IEnumerable<int> billIds, PaymentMethod method, DateTime paidAt, CancellationToken ct = default)
    {
        var settled = 0;
        decimal total = 0m;

        foreach (var id in billIds.Distinct())
        {
            var bill = await _db.Bills.Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.Id == id, ct);
            if (bill is null || bill.RemainingAmount <= 0m) continue;

            var payment = new Payment
            {
                BillId = bill.Id,
                Amount = bill.RemainingAmount,
                PaidAt = paidAt,
                Method = method,
                Note = "批量结清",
            };
            _db.Payments.Add(payment);
            Recalculate(bill);
            settled++;
            total += payment.Amount;
        }

        if (settled > 0) await _db.SaveChangesAsync(ct);
        return (settled, total);
    }

    // ───────────────────────── 杂费 / 金额重算 ─────────────────────────

    /// <summary>新增或更新杂费后，重算账单总额与状态。</summary>
    public async Task RecalculateBillAsync(int billId, CancellationToken ct = default)
    {
        var bill = await _db.Bills.Include(b => b.Items).Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Id == billId, ct)
            ?? throw new DomainException("账单不存在。");

        Recalculate(bill);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 统一重算：总额 = 租金 + 杂费；已收 = 收款合计；状态随之迁移。
    /// 若杂费下调导致已收超过总额，抛错提示先撤销多余收款。
    /// </summary>
    public static void Recalculate(Bill bill)
    {
        var extra = bill.Items?.Sum(i => i.Amount) ?? 0m;
        bill.TotalAmount = bill.RentAmount + extra;

        var paid = bill.Payments?.Sum(p => p.Amount) ?? 0m;
        if (paid > bill.TotalAmount + 0.001m)
            throw new DomainException("已收金额超过应收总额，请先撤销多余的收款记录。");

        bill.PaidAmount = paid;
        if (paid <= 0m)
        {
            bill.Status = BillStatus.Unpaid;
            bill.PaidAt = null;
        }
        else if (paid < bill.TotalAmount - 0.001m)
        {
            bill.Status = BillStatus.Partial;
            bill.PaidAt = null;
        }
        else
        {
            bill.Status = BillStatus.Paid;
            bill.PaidAt ??= bill.Payments?.Count > 0 ? bill.Payments.Max(p => p.PaidAt) : DateTime.Now;
        }
    }
}
