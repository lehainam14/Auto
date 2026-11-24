namespace Macro.Core;

/// <summary>An action failed for a logical reason (bad parameters, image not
/// found, ...). The engine stops the workflow and reports the message.</summary>
public class ActionFailureException : Exception
{
    public ActionFailureException(string message) : base(message) { }
}

/// <summary>Internal: raised when the user pressed Stop, to unwind out of the
/// running workflow as fast as possible.</summary>
public class StopRequestedException : Exception
{
    public StopRequestedException() : base("Stopped by user") { }
    public StopRequestedException(string message) : base(message) { }
}

/// <summary>Internal: raised by the 'Break' action to exit the closest Loop.</summary>
public class BreakLoopException : Exception
{
    public BreakLoopException() : base("break") { }
}

/// <summary>Internal: raised by jump actions (Goto Step, Find Image branching) to transfer execution.</summary>
public class GotoStepException : Exception
{
    public string TargetStepId { get; }
    public GotoStepException(string targetStepId) : base($"Goto step: {targetStepId}")
    {
        TargetStepId = targetStepId;
    }
}

