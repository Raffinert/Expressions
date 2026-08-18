using System.Collections;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;

namespace Raffinert.Expressions;

internal static class MapToExistingBuilder
{
    private static readonly ConstructorInfo InvalidOperationConstructor =
        typeof(InvalidOperationException).GetConstructor([typeof(string)])!;
    private static readonly MethodInfo UpdateGenericCollectionMethod =
        typeof(MapToExistingBuilder).GetMethod(nameof(UpdateGenericCollection), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo UpdateNonGenericCollectionMethod =
        typeof(MapToExistingBuilder).GetMethod(nameof(UpdateNonGenericCollection), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Expression<Action<TIn, TOut>> Build<TIn, TOut>(
        Expression<Func<TIn, TOut>> projection)
    {
        if (projection == null) throw new ArgumentNullException(nameof(projection));
        if (typeof(TOut).IsValueType)
        {
            throw new NotSupportedException(
                $"MapToExisting does not support value-type destination '{typeof(TOut).FullName}'.");
        }

        var existing = Expression.Parameter(typeof(TOut),
            projection.Parameters[0].Name == "existing" ? "existing1" : "existing");
        var body = BuildRoot(projection.Body, existing);
        return Expression.Lambda<Action<TIn, TOut>>(
            body,
            projection.Parameters[0],
            existing);
    }

    private static Expression BuildRoot(Expression source, Expression destination)
    {
        return source switch
        {
            MemberInitExpression memberInit => BuildMemberInit(memberInit, destination),
            ConditionalExpression conditional => Expression.IfThenElse(conditional.Test,
                BuildRootBranch(conditional.IfTrue, destination), BuildRootBranch(conditional.IfFalse, destination)),
            _ => throw new NotSupportedException(
                $"MapToExisting does not support projection body node '{source.NodeType}'. A member initializer is required.")
        };
    }

    private static Expression BuildRootBranch(Expression source, Expression destination)
    {
        if (source is MemberInitExpression memberInit)
        {
            return BuildMemberInit(memberInit, destination);
        }

        if (IsNullOrDefault(source))
        {
            return Throw(
                "MapToExisting cannot update an existing root destination when the selected projection branch returns null.");
        }

        throw new NotSupportedException(
            $"MapToExisting conditional branches must be member initializers or null/default; found '{source.NodeType}'.");
    }

    private static Expression BuildMemberInit(MemberInitExpression source, Expression destination)
    {
        var updates = new List<Expression>();
        foreach (var binding in source.Bindings)
        {
            var target = Expression.MakeMemberAccess(destination, binding.Member);
            switch (binding)
            {
                case MemberAssignment assignment:
                    updates.Add(BuildMemberUpdate(target, assignment.Expression, assignment.Member));
                    break;
                case MemberListBinding listBinding:
                    updates.Add(BuildListUpdate(target, listBinding));
                    break;
                default:
                    throw new NotSupportedException(
                        $"MapToExisting supports member assignments and collection initializers; member '{binding.Member.Name}' uses '{binding.BindingType}'.");
            }
        }

        return updates.Count == 0 ? Expression.Empty() : Expression.Block(updates);
    }

    private static Expression BuildMemberUpdate(
        Expression target,
        Expression source,
        MemberInfo destinationMember)
    {
        if (TryBuildCollectionUpdate(target, source, destinationMember, out var collectionUpdate))
        {
            return collectionUpdate;
        }

        if (source is MemberInitExpression memberInit)
        {
            if (target.Type.IsValueType)
            {
                throw new NotSupportedException(
                    $"MapToExisting does not recursively update value-type member '{destinationMember.Name}'.");
            }

            var update = BuildMemberInit(memberInit, target);
            Expression create = CanAssign(destinationMember)
                ? Expression.Assign(target, memberInit)
                : Throw(
                    $"Cannot create nested destination member '{destinationMember.DeclaringType?.FullName}.{destinationMember.Name}' because it is read-only and its current value is null.");
            return Expression.IfThenElse(
                Expression.Equal(target, Expression.Default(target.Type)),
                create,
                update);
        }

        if (source is ConditionalExpression conditional)
        {
            return Expression.IfThenElse(
                conditional.Test,
                BuildMemberUpdate(target, conditional.IfTrue, destinationMember),
                BuildMemberUpdate(target, conditional.IfFalse, destinationMember));
        }

        EnsureWritable(destinationMember);
        return Expression.Assign(target, source);
    }

    private static bool TryBuildCollectionUpdate(
        Expression target,
        Expression source,
        MemberInfo destinationMember,
        out Expression update)
    {
        update = null!;
        if (target.Type == typeof(string) || target.Type.IsArray || target.Type.IsValueType ||
            IsKnownReadOnlyCollection(target.Type))
        {
            return false;
        }

        var canAssign = CanAssign(destinationMember);
        var collectionType = FindGenericInterface(target.Type, typeof(ICollection<>));
        if (collectionType != null)
        {
            var elementType = collectionType.GetGenericArguments()[0];
            var method = UpdateGenericCollectionMethod.MakeGenericMethod(target.Type, elementType);
            var call = Expression.Call(
                method,
                target,
                Expression.Convert(source, typeof(IEnumerable<>).MakeGenericType(elementType)),
                Expression.Constant(canAssign));
            update = canAssign
                ? Expression.Assign(target, call)
                : Expression.Block(call, Expression.Empty());
            return true;
        }

        if (typeof(IList).IsAssignableFrom(target.Type))
        {
            var method = UpdateNonGenericCollectionMethod.MakeGenericMethod(target.Type);
            var call = Expression.Call(
                method,
                target,
                Expression.Convert(source, typeof(IEnumerable)),
                Expression.Constant(canAssign));
            update = canAssign
                ? Expression.Assign(target, call)
                : Expression.Block(call, Expression.Empty());
            return true;
        }

        // AutoMapper replaces writable members exposed only as IEnumerable<T>, but maps
        // into a getter-only member when its runtime value is a mutable collection.
        if (!canAssign)
        {
            var enumerableType = FindGenericInterface(target.Type, typeof(IEnumerable<>));
            if (enumerableType != null)
            {
                var elementType = enumerableType.GetGenericArguments()[0];
                var method = UpdateGenericCollectionMethod.MakeGenericMethod(target.Type, elementType);
                var call = Expression.Call(
                    method,
                    target,
                    Expression.Convert(source, typeof(IEnumerable<>).MakeGenericType(elementType)),
                    Expression.Constant(false));
                update = Expression.Block(call, Expression.Empty());
                return true;
            }

            if (typeof(IEnumerable).IsAssignableFrom(target.Type))
            {
                var method = UpdateNonGenericCollectionMethod.MakeGenericMethod(target.Type);
                var call = Expression.Call(
                    method,
                    target,
                    Expression.Convert(source, typeof(IEnumerable)),
                    Expression.Constant(false));
                update = Expression.Block(call, Expression.Empty());
                return true;
            }
        }

        return false;
    }

    private static Expression BuildListUpdate(Expression target, MemberListBinding binding)
    {
        var clearMethod = FindClearMethod(target.Type);
        if (clearMethod == null)
        {
            throw new NotSupportedException(
                $"MapToExisting cannot update collection member '{binding.Member.DeclaringType?.FullName}.{binding.Member.Name}' because it has no Clear method.");
        }

        var collection = Expression.Variable(target.Type, $"{binding.Member.Name}Collection");
        var expressions = new List<Expression>
        {
            Expression.Assign(collection, target)
        };

        if (!target.Type.IsValueType)
        {
            Expression whenNull;
            if (CanAssign(binding.Member))
            {
                var create = Expression.Convert(
                    Expression.Call(
                        typeof(MapToExistingBuilder),
                        nameof(CreateMutableCollection),
                        Type.EmptyTypes,
                        Expression.Constant(target.Type),
                        Expression.Constant(GetCollectionElementType(target.Type) ?? typeof(object))),
                    target.Type);
                whenNull = Expression.Block(
                    Expression.Assign(target, create),
                    Expression.Assign(collection, target),
                    Expression.Empty());
            }
            else
            {
                whenNull = Throw(
                    $"Cannot create collection destination member '{binding.Member.DeclaringType?.FullName}.{binding.Member.Name}' because it is read-only and its current value is null.");
            }

            expressions.Add(Expression.IfThen(
                Expression.Equal(collection, Expression.Default(collection.Type)),
                whenNull));
        }

        expressions.Add(Expression.Call(collection, clearMethod));
        foreach (var initializer in binding.Initializers)
        {
            expressions.Add(Expression.Call(collection, initializer.AddMethod, initializer.Arguments));
        }

        expressions.Add(Expression.Empty());
        return Expression.Block([collection], expressions);
    }

    private static TCollection UpdateGenericCollection<TCollection, TElement>(
        TCollection destination,
        IEnumerable<TElement>? source,
        bool createIfNull)
    {
        ICollection<TElement>? collection = destination is ICollection<TElement> existing ? existing : null;
        if (collection == null)
        {
            if (!createIfNull)
            {
                throw new InvalidOperationException(
                    $"Existing collection destination '{typeof(TCollection).FullName}' is null or does not implement ICollection<{typeof(TElement).FullName}>.");
            }

            destination = (TCollection)CreateMutableCollection(typeof(TCollection), typeof(TElement));
            collection = (ICollection<TElement>)(object)destination!;
        }

        // Clearing a collection which is also the projected source would otherwise erase
        // the items before they can be copied back.
        var items = ReferenceEquals(collection, source) ? source!.ToArray() : source;
        collection.Clear();
        if (items != null)
        {
            foreach (var item in items)
            {
                collection.Add(item);
            }
        }

        return destination;
    }

    private static TCollection UpdateNonGenericCollection<TCollection>(
        TCollection destination,
        IEnumerable? source,
        bool createIfNull)
    {
        IList? collection = destination is IList existing ? existing : null;
        if (collection == null)
        {
            if (!createIfNull)
            {
                throw new InvalidOperationException(
                    $"Existing collection destination '{typeof(TCollection).FullName}' is null or does not implement IList.");
            }

            destination = (TCollection)CreateMutableCollection(typeof(TCollection), typeof(object));
            collection = (IList)(object)destination!;
        }

        var items = ReferenceEquals(collection, source) ? source!.Cast<object?>().ToArray() : source;
        collection.Clear();
        if (items != null)
        {
            foreach (var item in items)
            {
                collection.Add(item);
            }
        }

        return destination;
    }

    private static object CreateMutableCollection(Type collectionType, Type elementType)
    {
        if (!collectionType.IsInterface && !collectionType.IsAbstract)
        {
            try
            {
                return Activator.CreateInstance(collectionType)!;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Cannot create collection destination '{collectionType.FullName}'. A public parameterless constructor is required.",
                    exception);
            }
        }

        Type implementationType;
        if (collectionType.IsGenericType && collectionType.GetGenericTypeDefinition() == typeof(ISet<>))
        {
            implementationType = typeof(HashSet<>).MakeGenericType(elementType);
        }
        else if (collectionType.IsAssignableFrom(typeof(List<>).MakeGenericType(elementType)))
        {
            implementationType = typeof(List<>).MakeGenericType(elementType);
        }
        else if (collectionType.IsAssignableFrom(typeof(ArrayList)))
        {
            implementationType = typeof(ArrayList);
        }
        else
        {
            throw new InvalidOperationException(
                $"Cannot create collection destination for interface or abstract type '{collectionType.FullName}'.");
        }

        return Activator.CreateInstance(implementationType)!;
    }

    private static MethodInfo? FindClearMethod(Type type)
    {
        var direct = type.GetMethod(nameof(IList.Clear), Type.EmptyTypes);
        if (direct != null) return direct;

        var collectionType = FindGenericInterface(type, typeof(ICollection<>));
        return collectionType?.GetMethod(nameof(IList.Clear), Type.EmptyTypes);
    }

    private static Type? GetCollectionElementType(Type type) =>
        FindGenericInterface(type, typeof(ICollection<>))?.GetGenericArguments()[0] ??
        FindGenericInterface(type, typeof(IEnumerable<>))?.GetGenericArguments()[0];

    private static Type? FindGenericInterface(Type type, Type genericInterface)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == genericInterface)
        {
            return type;
        }

        return type.GetInterfaces().FirstOrDefault(candidate =>
            candidate.IsGenericType && candidate.GetGenericTypeDefinition() == genericInterface);
    }

    private static bool IsKnownReadOnlyCollection(Type type)
    {
        if (!type.IsGenericType) return false;

        var genericType = type.GetGenericTypeDefinition();
        return genericType == typeof(ReadOnlyCollection<>) ||
               genericType == typeof(ReadOnlyDictionary<,>);
    }

    private static void EnsureWritable(MemberInfo member)
    {
        if (member is PropertyInfo { SetMethod: null })
        {
            throw new NotSupportedException(
                $"MapToExisting cannot assign read-only property '{member.DeclaringType?.FullName}.{member.Name}'.");
        }

        if (member is FieldInfo { IsInitOnly: true })
        {
            throw new NotSupportedException(
                $"MapToExisting cannot assign readonly field '{member.DeclaringType?.FullName}.{member.Name}'.");
        }
    }

    private static bool CanAssign(MemberInfo member) =>
        member is PropertyInfo { SetMethod: not null } ||
        member is FieldInfo { IsInitOnly: false };

    private static UnaryExpression Throw(string message) =>
        Expression.Throw(Expression.New(InvalidOperationConstructor, Expression.Constant(message)));

    private static bool IsNullOrDefault(Expression expression) =>
        expression is DefaultExpression || expression is ConstantExpression { Value: null };
}
