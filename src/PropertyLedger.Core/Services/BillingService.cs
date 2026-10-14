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

    /// <summary>
    /// 某自然月的月租（含逐年递增；未配置递增时 = 租金原值）。
    /// 递增按「起租后第几个租约年」套用：第 1 年为原值，第 2 年起按比例复利或按金额累加。
    /// </summary>
    public static decimal EscalatedMonthlyRent(Lease lease, int year, int month)
    {
        var baseRent = lease.RentAmount;
        if (lease.EscMode == 0 || lease.EscValue <= 0m)
            return decimal.Round(baseRent, 2, MidpointRounding.AwayFromZero);

        var diff = PeriodCalendar.MonthIndex(PeriodCalendar.FirstDayOfMonth(year, month))
                   - PeriodCalendar.MonthIndex(lease.StartDate);
        if (diff < 0) diff = 0;
        var yearIndex = diff / 12;   // 0 = 租约第 1 年
        if (yearIndex <= 0)
            return decimal.Round(baseRent, 2, MidpointRounding.AwayFromZero);

        var escalated = lease.EscMode == 1
            ? baseRent * (decimal)Math.Pow(1.0 + (double)lease.EscValue / 100.0, yearIndex)
            : baseRent + lease.EscValue * yearIndex;
        return decimal.Round(escalated, 2, MidpointRounding.AwayFromZero);
    }

    // ───────────────────────── 出账周期计算（纯函数，便于单测）─────────────────────────

    /// <summary>
    /// 判断租约在指定自然月是否应出账，并计算账期。
    /// 月付：账期为该自然月；季付：仅在与起租月相隔 3 的整数倍的月份出账，账期连续 3 个月。
    /// 账期与租期必须有交集。月租按租约年套用租金递增。
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
                EscalatedMonthlyRent(lease, year, month));
            return true;
        }

        // 季付：以起租月为基准，每 3 个月一期，仅账期首月出账
        var delta = PeriodCalendar.MonthIndex(monthStart) - PeriodCalendar.MonthIndex(lease.StartDate);
        if (delta < 0 || delta % 3 != 0) return false;

        // 季账期：当月起连续 3 个自然月；金额 = 三个月递增后月租之和
        var periodEndDate = monthStart.AddMonths(2);
        var periodEnd = PeriodCalendar.LastDayOfMonth(periodEndDate);

        if (periodEnd < lease.StartDate) return false;
        if (lease.EndDate.HasValue && monthStart > lease.EndDate.Value) return false;

        var quarterlyRent = 0m;
        for (var k = 0; k < 3; k++)
        {
            var m = monthStart.AddMonths(k);
            quarterlyRent += EscalatedMonthlyRent(lease, m.Year, m.Month);
        }

        plan = new PeriodPlan(
            monthStart,
            periodEnd,
            PeriodCalendar.DueDate(year, month, lease.PaymentDay),
            quarterlyRent);
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

    // ───────────────────────── 租约变更后账单重算 ─────────────────────────

    /// <summary>账单在重算中的处置类别说明（供界面预览/汇总使用）。</summary>
    public sealed record LeaseBillItem(
        int BillId, DateTime PeriodStart, DateTime PeriodEnd, DateTime DueDate,
        decimal OldRent, decimal NewRent, bool HasPayments, bool IsManual, int ItemCount, string Kind);

    /// <summary>重算预览：重算 / 删除 / 冻结 / 补建 / 阻止 五类明细。</summary>
    public sealed record LeaseRecalcPreview(
        List<LeaseBillItem> Recalculate,
        List<LeaseBillItem> Delete,
        List<LeaseBillItem> Frozen,
        List<PeriodPlan> Create,
        List<LeaseBillItem> Blocked)
    {
        /// <summary>除冻结外是否真的有账单会变动。</summary>
        public bool HasChanges => Recalculate.Count > 0 || Delete.Count > 0 || Create.Count > 0;
    }

    /// <summary>重算执行结果计数。</summary>
    public readonly record struct LeaseRecalcResult(int Recalculated, int Deleted, int Created, int Frozen);

    /// <summary>
    /// 判断租约前后两个版本之间，是否改了会影响账单的关键字段
    /// （起租/到期、租金、递增方式与值、付租日、付租周期）。
    /// </summary>
    public static bool IsBillAffectingChange(Lease oldLease, Lease newLease)
        => oldLease.StartDate.Date != newLease.StartDate.Date
           || oldLease.EndDate?.Date != newLease.EndDate?.Date
           || oldLease.RentAmount != newLease.RentAmount
           || oldLease.EscMode != newLease.EscMode
           || oldLease.EscValue != newLease.EscValue
           || oldLease.PaymentDay != newLease.PaymentDay
           || oldLease.PaymentCycle != newLease.PaymentCycle;

    /// <summary>
    /// 按新租约条款对其全部账单做重算分类（纯计算，不落库）：
    /// ① 无收款的自动账单：新条款下账期仍成立 → Recalculate（更新账期/到期日/租金，杂费保留）；
    /// 账期不再成立（缩租期、月付↔季付）→ 无杂费 Delete、挂杂费 Blocked（阻止保存，防杂费悬空）；
    /// ② 有任意收款（含部分收）或人工补录账单：Frozen，金额/账期/到期日一律不动；
    /// ③ 新条款下应有、但现存账单未覆盖的账期（含起租日提前的历史月份）：Create。
    /// </summary>
    public static LeaseRecalcPreview BuildRecalcPreview(
        Lease lease, IReadOnlyList<Bill> bills, DateTime throughMonth)
    {
        var recalc = new List<LeaseBillItem>();
        var delete = new List<LeaseBillItem>();
        var frozen = new List<LeaseBillItem>();
        var blocked = new List<LeaseBillItem>();

        foreach (var b in bills)
        {
            var hasPayments = (b.Payments?.Count > 0) || b.PaidAmount > 0.001m;
            var itemCount = b.Items?.Count ?? 0;
            var matches = TryGetPeriod(lease, b.PeriodStart.Year, b.PeriodStart.Month, out var plan);

            if (hasPayments || b.IsManual)
            {
                frozen.Add(new LeaseBillItem(
                    b.Id, b.PeriodStart, b.PeriodEnd, b.DueDate,
                    b.RentAmount, b.RentAmount, hasPayments, b.IsManual, itemCount,
                    b.IsManual ? "frozen-manual" : "frozen-payment"));
            }
            else if (matches)
            {
                recalc.Add(Item(b, plan.RentAmount, plan.PeriodEnd, plan.DueDate, "recalc"));
            }
            else if (itemCount > 0)
            {
                blocked.Add(Item(b, b.RentAmount, b.PeriodEnd, b.DueDate, "blocked"));
            }
            else
            {
                delete.Add(Item(b, 0m, b.PeriodEnd, b.DueDate, "delete"));
            }
        }

        // 幸存账单（重算后按新账期 + 冻结账单）覆盖的月份，补建时跳过，避免双重出账
        var covered = new HashSet<(int Year, int Month)>();
        foreach (var b in bills)
        {
            var survives = frozen.Exists(x => x.BillId == b.Id);
            var recalcTo = recalc.FirstOrDefault(x => x.BillId == b.Id);
            DateTime coverFrom, coverTo;
            if (recalcTo is not null)
            {
                coverFrom = PeriodCalendar.FirstDayOfMonth(recalcTo.PeriodStart);
                coverTo = PeriodCalendar.FirstDayOfMonth(recalcTo.PeriodEnd);
            }
            else if (survives)
            {
                coverFrom = PeriodCalendar.FirstDayOfMonth(b.PeriodStart);
                coverTo = PeriodCalendar.FirstDayOfMonth(b.PeriodEnd);
            }
            else continue;

            for (var c = coverFrom; c <= coverTo; c = c.AddMonths(1))
                covered.Add((c.Year, c.Month));
        }

        var toCreate = new List<PeriodPlan>();
        var throughFirst = PeriodCalendar.FirstDayOfMonth(throughMonth);
        var cursor = PeriodCalendar.FirstDayOfMonth(lease.StartDate);
        while (cursor <= throughFirst)
        {
            if (TryGetPeriod(lease, cursor.Year, cursor.Month, out var plan))
            {
                var months = PlanMonths(plan);
                if (!months.Any(m => covered.Contains(m)))
                    toCreate.Add(plan);
            }
            cursor = cursor.AddMonths(1);
        }

        return new LeaseRecalcPreview(recalc, delete, frozen, toCreate, blocked);

        static LeaseBillItem Item(Bill b, decimal newRent, DateTime periodEnd, DateTime dueDate, string kind)
            => new(b.Id, b.PeriodStart, periodEnd, dueDate,
                   b.RentAmount, newRent, b.Payments?.Count > 0 || b.PaidAmount > 0.001m,
                   b.IsManual, b.Items?.Count ?? 0, kind);
    }

    /// <summary>
    /// 落库执行重算：按当前（已保存的）租约条款同步全部账单，单次事务提交。
    /// Blocked 非空时整体抛错不做任何改动；所有幸存账单同步房间/租客冗余字段。
    /// </summary>
    public async Task<LeaseRecalcResult> ApplyLeaseRecalcAsync(
        int leaseId, DateTime throughMonth, CancellationToken ct = default)
    {
        var lease = await _db.Leases.AsNoTracking().FirstOrDefaultAsync(l => l.Id == leaseId, ct)
                    ?? throw new DomainException("租约不存在。");

        var bills = await _db.Bills
            .Include(b => b.Items).Include(b => b.Payments)
            .Where(b => b.LeaseId == leaseId)
            .ToListAsync(ct);

        var preview = BuildRecalcPreview(lease, bills, throughMonth);
        if (preview.Blocked.Count > 0)
        {
            var periods = string.Join("、", preview.Blocked
                .Select(x => x.PeriodStart.ToString("yyyy-MM") + (x.PeriodEnd.Year != x.PeriodStart.Year || x.PeriodEnd.Month != x.PeriodStart.Month ? " 至 " + x.PeriodEnd.ToString("yyyy-MM") : "")));
            throw new DomainException(
                $"以下账期的账单含杂费明细，调整租期/付租周期后将不再成立：{periods}。请先到账单页移除或改挂这些杂费，再修改租约。");
        }

        var byId = bills.ToDictionary(b => b.Id);

        foreach (var x in preview.Frozen)
        {
            var b = byId[x.BillId];
            b.RoomId = lease.RoomId;
            b.TenantId = lease.TenantId;
            // 冻结：金额/账期/到期日/状态一律不碰
        }

        foreach (var x in preview.Recalculate)
        {
            var b = byId[x.BillId];
            b.RoomId = lease.RoomId;
            b.TenantId = lease.TenantId;
            b.PeriodEnd = x.PeriodEnd;
            b.DueDate = x.DueDate;
            b.RentAmount = x.NewRent;
            Recalculate(b);
        }

        foreach (var x in preview.Delete)
        {
            var b = byId[x.BillId];
            _db.BillItems.RemoveRange(b.Items);
            _db.Bills.Remove(b);
        }

        foreach (var plan in preview.Create)
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
        }

        await _db.SaveChangesAsync(ct);
        return new LeaseRecalcResult(
            preview.Recalculate.Count, preview.Delete.Count, preview.Create.Count, preview.Frozen.Count);
    }

    private static List<(int Year, int Month)> PlanMonths(PeriodPlan plan)
    {
        var list = new List<(int, int)>();
        for (var c = PeriodCalendar.FirstDayOfMonth(plan.PeriodStart);
             c <= PeriodCalendar.FirstDayOfMonth(plan.PeriodEnd);
             c = c.AddMonths(1))
        {
            list.Add((c.Year, c.Month));
        }
        return list;
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
    /// 收款日期模式：<paramref name="useBillDueDate"/> 为 true 时按各账单各自的到期日登记（「账单当月」），
    /// 否则全部使用同一日期。返回（结清张数，登记收款总额）。
    /// </summary>
    public async Task<(int Settled, decimal Total)> BatchSettleAsync(
        IEnumerable<int> billIds, PaymentMethod method, DateTime paidAt,
        bool useBillDueDate = false, CancellationToken ct = default)
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
                PaidAt = useBillDueDate ? bill.DueDate : paidAt,
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
    /// 编辑账单：调整租金金额与备注（杂费在账单面板单独增删）。保存后自动重算应收总额与状态；
    /// 若应收被改到低于已收，抛错提示先撤销多余收款。
    /// </summary>
    public async Task UpdateBillAsync(int billId, decimal rentAmount, string? note, CancellationToken ct = default)
    {
        if (rentAmount < 0m) throw new DomainException("租金金额不能为负数。");
        var bill = await _db.Bills.Include(b => b.Items).Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Id == billId, ct)
            ?? throw new DomainException("账单不存在。");
        bill.RentAmount = rentAmount;
        bill.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Recalculate(bill);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 删除账单（连同杂费明细）。已登记过收款的账单必须先撤销全部收款再删，
    /// 避免收款记录悬空、破坏「已收 = Σ收款」的报表口径。注意：自动出账范围内的账单
    /// 删除后可能在下次补账时按租约标准重新生成（如需当月免租，可编辑租金为 0）。
    /// </summary>
    public async Task DeleteBillAsync(int billId, CancellationToken ct = default)
    {
        var bill = await _db.Bills.Include(b => b.Items).Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Id == billId, ct)
            ?? throw new DomainException("账单不存在。");
        if (bill.Payments.Count > 0)
            throw new DomainException("该账单已有收款记录，请先撤销全部收款后再删除。");
        _db.BillItems.RemoveRange(bill.Items);
        _db.Bills.Remove(bill);
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
