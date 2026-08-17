namespace Raffinert.Expressions;

/// <summary>Controls how <see cref="Proj{TIn,TOut}.MergeBindings(Proj{TIn,TOut},BindingConflictBehavior)"/> handles duplicate members.</summary>
public enum BindingConflictBehavior
{
    /// <summary>Use the binding from the second projection.</summary>
    UseLast,

    /// <summary>Keep the binding from the first projection.</summary>
    UseFirst,

    /// <summary>Throw when both projections bind the same member.</summary>
    Throw
}
