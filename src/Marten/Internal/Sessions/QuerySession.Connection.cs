#nullable enable

using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Exceptions;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Internal.Sessions;

public partial class QuerySession
{
    internal readonly IConnectionLifetime _connection;

    // TODO -- where is this hooked up? Should this be in the lifetimes?
    public int? CommandTimeout { get; set; }

    public int Execute(NpgsqlCommand cmd)
    {
        RequestCount++;

        _connection.Apply(cmd);

        try
        {
            var returnValue = _retryPolicy.Execute(cmd.ExecuteNonQuery);
            Logger.LogSuccess(cmd);

            return returnValue;
        }
        catch (Exception e)
        {
            handleCommandException(cmd, e);
            throw;
        }
    }

    public async Task<int> ExecuteAsync(NpgsqlCommand command, CancellationToken token = new())
    {
        RequestCount++;

        await _connection.ApplyAsync(command, token).ConfigureAwait(false);

        Logger.OnBeforeExecute(command);

        try
        {
            var returnValue = await _retryPolicy.ExecuteAsync(() => command.ExecuteNonQueryAsync(token), token)
                .ConfigureAwait(false);
            Logger.LogSuccess(command);

            return returnValue;
        }
        catch (Exception e)
        {
            handleCommandException(command, e);
            throw;
        }
    }

    public DbDataReader ExecuteReader(NpgsqlCommand command)
    {
        _connection.Apply(command);

        RequestCount++;

        try
        {
            var returnValue = _retryPolicy.Execute(command.ExecuteReader);
            Logger.LogSuccess(command);
            return returnValue;
        }
        catch (Exception e)
        {
            handleCommandException(command, e);
            throw;
        }
    }

    public DbDataReader ExecuteReader(NpgsqlBatch batch)
    {
        _connection.Apply(batch);

        RequestCount++;

        try
        {
            var returnValue = _retryPolicy.Execute(() => batch.ExecuteReader());
            Logger.LogSuccess(batch);
            return returnValue;
        }
        catch (Exception e)
        {
            handleCommandException(batch, e);
            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default)
    {
        await _connection.ApplyAsync(command, token).ConfigureAwait(false);

        Logger.OnBeforeExecute(command);

        RequestCount++;

        try
        {
            var reader = await _retryPolicy.ExecuteAsync(() => command.ExecuteReaderAsync(token), token)
                .ConfigureAwait(false);

            Logger.LogSuccess(command);

            return reader;
        }
        catch (Exception e)
        {
            handleCommandException(command, e);
            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, CancellationToken token = default)
    {
        await _connection.ApplyAsync(batch, token).ConfigureAwait(false);

        Logger.OnBeforeExecute(batch);

        RequestCount++;

        try
        {
            var reader = await _retryPolicy.ExecuteAsync(() => batch.ExecuteReaderAsync(token), token)
                .ConfigureAwait(false);

            Logger.LogSuccess(batch);

            return reader;
        }
        catch (Exception e)
        {
            handleCommandException(batch, e);
            throw;
        }
    }

    [Obsolete("Replace with ExceptionTransforms from Baseline")]
    private void handleCommandException(NpgsqlCommand cmd, Exception e)
    {
        this.SafeDispose();
        Logger.LogFailure(cmd, e);

        MartenExceptionTransformer.WrapAndThrow(cmd, e);
    }

    [Obsolete("Replace with ExceptionTransforms from Baseline")]
    private void handleCommandException(NpgsqlBatch batch, Exception e)
    {
        this.SafeDispose();
        Logger.LogFailure(batch, e);

        MartenExceptionTransformer.WrapAndThrow(batch, e);
    }

    internal T? LoadOne<T>(NpgsqlCommand command, ISelector<T> selector)
    {
        using var reader = ExecuteReader(command);
        return !reader.Read() ? default : selector.Resolve(reader);
    }

    internal async Task<T?> LoadOneAsync<T>(NpgsqlCommand command, ISelector<T> selector, CancellationToken token)
    {
        await using var reader = await ExecuteReaderAsync(command, token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return default;
        }

        return await selector.ResolveAsync(reader, token).ConfigureAwait(false);
    }

    internal async Task<bool> StreamOne(NpgsqlCommand command, Stream stream, CancellationToken token)
    {
        await using var reader = (NpgsqlDataReader)await ExecuteReaderAsync(command, token).ConfigureAwait(false);
        return await reader.StreamOne(stream, token).ConfigureAwait(false) == 1;
    }

    internal async Task<int> StreamMany(NpgsqlCommand command, Stream stream, CancellationToken token)
    {
        await using var reader = (NpgsqlDataReader)await ExecuteReaderAsync(command, token).ConfigureAwait(false);

        return await reader.StreamMany(stream, token).ConfigureAwait(false);
    }

    public async Task<T> ExecuteHandlerAsync<T>(IQueryHandler<T> handler, CancellationToken token)
    {
        var cmd = this.BuildCommand(handler);

        await using var reader = await ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
        return await handler.HandleAsync(reader, this, token).ConfigureAwait(false);
    }

    public T ExecuteHandler<T>(IQueryHandler<T> handler)
    {
        var cmd = this.BuildCommand(handler);

        using var reader = ExecuteReader(cmd);
        return handler.Handle(reader, this);
    }

    public void BeginTransaction()
    {
        _connection.BeginTransaction();
    }

    public ValueTask BeginTransactionAsync(CancellationToken token)
    {
        return _connection.BeginTransactionAsync(token);
    }
}

public interface IConnectionLifetime: IAsyncDisposable, IDisposable
{
    NpgsqlConnection? Connection { get; }
    void Apply(NpgsqlCommand command);
    void Apply(NpgsqlBatch batch);
    Task ApplyAsync(NpgsqlCommand command, CancellationToken token);
    Task ApplyAsync(NpgsqlBatch batch, CancellationToken token);

    void Commit();
    Task CommitAsync(CancellationToken token);

    void Rollback();
    Task RollbackAsync(CancellationToken token);

    void EnsureConnected();
    ValueTask EnsureConnectedAsync(CancellationToken token);
    void BeginTransaction();
    ValueTask BeginTransactionAsync(CancellationToken token);
}
