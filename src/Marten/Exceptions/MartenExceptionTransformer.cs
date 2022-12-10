using System;
using JasperFx.Core.Exceptions;
using Marten.Services;
using Npgsql;

namespace Marten.Exceptions;

[Obsolete("Replace w/ JasperFx.Core version")]
internal static class MartenExceptionTransformer
{
    private static readonly ExceptionTransforms _transforms = new ExceptionTransforms();

    static MartenExceptionTransformer()
    {
        _transforms.AddTransform<EventStreamUnexpectedMaxEventIdExceptionTransform>();
        _transforms.AddTransform<MartenCommandNotSupportedExceptionTransform>();
        _transforms.AddTransform<UtcDateTimeUsageExceptionTransform>();
        _transforms.AddTransform<DateTimeUsageExceptionTransform>();

        _transforms.IfExceptionIs<PostgresException>()
            .If(e => e.SqlState == PostgresErrorCodes.SerializationFailure)
            .ThenTransformTo(e => throw new ConcurrentUpdateException(e));

        _transforms.IfExceptionIs<NpgsqlException>()
            .TransformTo(e =>
            {
                var command = e.ReadNpgsqlCommand();
                return new MartenCommandException(command, e);
            });
    }

    internal static NpgsqlCommand ReadNpgsqlCommand(this Exception ex)
    {
        return ex.Data.Contains(nameof(NpgsqlCommand))
            ? (NpgsqlCommand)ex.Data[nameof(NpgsqlCommand)]
            : null;
    }

    internal static void WrapAndThrow(NpgsqlCommand command, Exception exception)
    {
        if (command != null)
        {
            exception.Data[nameof(NpgsqlCommand)] = command;
        }

        _transforms.TransformAndThrow(exception);
    }

    internal static void WrapAndThrow(NpgsqlBatch batch, Exception exception)
    {
        if (batch != null)
        {
            exception.Data[nameof(NpgsqlBatch)] = batch;
        }

        _transforms.TransformAndThrow(exception);
    }

    internal static void WrapAndThrow(Exception exception)
    {
        _transforms.TransformAndThrow(exception);
    }
}
