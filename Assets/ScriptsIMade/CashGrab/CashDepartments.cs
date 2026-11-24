using System;

/// <summary>
/// When departments arrive on the cash floor and when they buy upgrades (pure rules, shared by CashGrabSystem and the
/// tests). One at a time: Backend first (day 3, or day 2 after the CEO's first few deposits), then Frontend, then
/// Marketing (by day or headcount). An upgrade is bought from the department's own budget as soon as it covers the next
/// level's price (CashRules.LevelCost, scaled to the company's day income): the money leaves the company (company value
/// goes down by it) and the department gets faster, carries more and learns new interference.
/// </summary>
public static class CashDepartments
{
    /// <summary>
    /// Check every department once. <paramref name="levels"/> is changed in place; <paramref name="onChange"/> is told
    /// (dept, old level, new level) for each change. Returns true when anything changed.
    /// </summary>
    public static bool Tick(CashLedger ledger, int[] levels, int day, int staff, int deposits, double projectedDay, bool earning,
                            Action<int, int, int> onChange)
    {
        bool changed = false;
        for (int d = 0; d < CashLedger.DeptCount; d++)
        {
            CashDept dept = (CashDept)d;
            int level = levels[d];
            if (level == 0)
            {
                if (!earning) continue;
                bool join = day >= CashRules.JoinDay(dept) || staff >= CashRules.JoinHeadcount(dept)
                            || (dept == CashDept.Backend && day >= 2 && deposits >= CashRules.BackendJoinDeposits);
                if (!join || (d > 0 && levels[d - 1] == 0)) continue;
                levels[d] = 1;
                onChange?.Invoke(d, 0, 1);
                changed = true;
                continue;
            }
            if (level >= CashRules.MaxDeptLevel) continue;
            double cost = CashRules.LevelCost(level + 1, Math.Max(0.0, projectedDay));
            if (ledger.Budget(d) + 0.005 < cost || !ledger.SpendDept(d, cost)) continue;
            levels[d] = level + 1;
            onChange?.Invoke(d, level, level + 1);
            changed = true;
        }
        return changed;
    }
}
