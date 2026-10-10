using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Raffinert.Expressions.EntityFrameworkCore.SqlServerTests;

internal sealed record LocalDbCommand(string Sql, string[] Names, string[] CommandNames,
    object?[] Values, bool SqlClientParameters, CancellationToken CancellationToken);

internal sealed class LocalDbCommandRecorder : DbCommandInterceptor
{
    public List<LocalDbCommand> Executed { get; } = [];
    private void Record(DbCommand command, CancellationToken cancellationToken)
    {
        var parameters = command.Parameters.Cast<DbParameter>().ToArray();
        Executed.Add(new(command.CommandText,
            parameters.Select(x => x.ParameterName.TrimStart('@', ':', '$')).ToArray(),
            parameters.Select(x => x.ParameterName).ToArray(),
            parameters.Select(x => x.Value is DBNull ? null : x.Value).ToArray(),
            parameters.All(x => x is SqlParameter), cancellationToken));
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command, CancellationToken.None);
        return result;
    }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record(command, cancellationToken);
        return ValueTask.FromResult(result);
    }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command, CancellationToken.None);
        return result;
    }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Record(command, cancellationToken);
        return ValueTask.FromResult(result);
    }
}
