namespace Raffinert.Expressions;

/// <summary>Specifies how projection binding merges resolve members assigned by both inputs.</summary>
public enum BindingConflictBehavior
{
    /// <summary>Uses the binding from the second input.</summary>
    UseLast,

    /// <summary>Uses the binding from the first input.</summary>
    UseFirst,

    /// <summary>Causes the merge to throw when both inputs bind the same member.</summary>
    Throw
}
