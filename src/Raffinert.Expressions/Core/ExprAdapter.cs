using System.Linq.Expressions;

namespace Raffinert.Expressions;

internal static class ExprAdapter
{
    public static Expression<Func<TSource, TResult>> GetExpandedExpression<TSource, TResult>(
        IExpr<TSource, TResult> expression)
    {
        var expanded = expression.GetExpandedLambdaExpression();
        if (expanded is null ||
            expanded.Parameters.Count != 1 ||
            !expanded.Parameters[0].Type.IsAssignableFrom(typeof(TSource)) ||
            !typeof(TResult).IsAssignableFrom(expanded.ReturnType))
        {
            throw new InvalidOperationException(
                $"IExpr<{typeof(TSource).Name}, {typeof(TResult).Name}> must return a compatible " +
                "single-parameter lambda from GetExpandedLambdaExpression().");
        }

        return (Expression<Func<TSource, TResult>>)AdaptToDelegate(
            expanded, typeof(Func<TSource, TResult>));
    }

    // The source of a contravariant IExpr may be a base type of the requested source.
    // Preserve the original operand types with an upcast rather than replacing a base
    // parameter with a derived parameter inside arbitrary expression-tree nodes.
    internal static LambdaExpression AdaptToDelegate(LambdaExpression expanded, Type delegateType)
    {
        if (delegateType == typeof(Delegate) || delegateType == typeof(MulticastDelegate))
            return expanded;

        var invoke = delegateType.GetMethod(nameof(Action.Invoke));
        var parameters = invoke?.GetParameters();
        if (parameters is not { Length: 1 } ||
            expanded.Parameters.Count != 1 ||
            !expanded.Parameters[0].Type.IsAssignableFrom(parameters[0].ParameterType) ||
            !invoke!.ReturnType.IsAssignableFrom(expanded.ReturnType))
        {
            throw new InvalidOperationException(
                $"Expanded expression cannot be adapted to delegate type '{delegateType}'.");
        }

        if (expanded.Type == delegateType)
            return expanded;

        var originalParameter = expanded.Parameters[0];
        var requestedSource = parameters[0].ParameterType;
        var parameter = originalParameter.Type == requestedSource
            ? originalParameter
            : Expression.Parameter(requestedSource, originalParameter.Name);
        var body = parameter == originalParameter
            ? expanded.Body
            : new ReplaceExpressionVisitor(
                originalParameter, Expression.Convert(parameter, originalParameter.Type))
                .Visit(expanded.Body)!;

        // Value-type results require an explicit boxing conversion for a wider result type.
        if (body.Type != invoke.ReturnType && body.Type.IsValueType)
            body = Expression.Convert(body, invoke.ReturnType);

        return Expression.Lambda(delegateType, body, parameter);
    }
}
