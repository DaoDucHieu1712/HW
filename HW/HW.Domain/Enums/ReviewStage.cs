namespace HW.Domain.Enums;

public enum ReviewStage
{
    New = 0,          // just added  — review due in 3 days
    Reviewed = 1,     // reviewed at day 3 — next due day 7
    Reinforced = 2,   // reviewed at day 7 — next due day 14
    Mastered = 3      // reviewed at day 14 — completed
}
