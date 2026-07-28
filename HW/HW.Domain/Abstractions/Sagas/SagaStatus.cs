namespace HW.Domain.Abstractions.Sagas;

/// <summary>
/// Lifecycle of a saga instance. Only <see cref="Running"/> and <see cref="Compensating"/> are live —
/// the other three are terminal and a saga in one of them ignores every further message.
/// </summary>
public enum SagaStatus
{
    /// <summary>Moving forward through the happy path, waiting on a participant's reply.</summary>
    Running = 0,

    /// <summary>A step failed; undo commands have been sent for the steps that already succeeded.</summary>
    Compensating = 1,

    /// <summary>Every step succeeded. Terminal.</summary>
    Completed = 2,

    /// <summary>A step failed and every completed step was successfully undone. Terminal.</summary>
    Compensated = 3,

    /// <summary>
    /// A step failed and compensation itself could not complete. Terminal, and the one status that
    /// needs a human: the distributed transaction is now genuinely inconsistent.
    /// </summary>
    Failed = 4
}
