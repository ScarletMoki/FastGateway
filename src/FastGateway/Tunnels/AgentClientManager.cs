using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace FastGateway.Tunnels;

/// <summary>
///     客户端管理器
/// </summary>
[DebuggerDisplay("Count = {Count}")]
public sealed class AgentClientManager : IEnumerable, IAsyncDisposable
{
    private readonly AgentStateChannel _clientStateChannel;
    private readonly ConcurrentDictionary<string, AgentClient> _dictionary = new();
    private readonly object _lifecycleSync = new();
    private int _disposed;

    public AgentClientManager(AgentStateChannel clientStateChannel)
    {
        _clientStateChannel = clientStateChannel;
    }

    /// <inheritdoc />
    public int Count => _dictionary.Count;

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }


    /// <inheritdoc />
    public bool TryGetValue(string clientId, [MaybeNullWhen(false)] out AgentClient client)
    {
        return _dictionary.TryGetValue(clientId.ToLowerInvariant(), out client);
    }

    /// <summary>
    ///     添加客户端实例
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async ValueTask<bool> AddAsync(AgentClient client, CancellationToken cancellationToken)
    {
        var clientId = client.Id.ToLowerInvariant();
        AgentClient? existClient;
        lock (_lifecycleSync)
        {
            if (_disposed != 0) return false;
            _dictionary.TryRemove(clientId, out existClient);
            if (!_dictionary.TryAdd(clientId, client)) return false;
        }

        try
        {
            if (existClient is not null) await existClient.DisposeAsync();
            await _clientStateChannel.WriteAsync(client, true, cancellationToken);

            lock (_lifecycleSync)
            {
                if (_disposed == 0 && _dictionary.TryGetValue(clientId, out var current) &&
                    ReferenceEquals(current, client))
                    return true;
            }
        }
        catch
        {
        }

        lock (_lifecycleSync)
        {
            if (_dictionary.TryGetValue(clientId, out var current) && ReferenceEquals(current, client))
                _dictionary.TryRemove(clientId, out _);
        }

        try { await client.DisposeAsync(); }
        catch { /* ignored */ }
        return false;
    }

    /// <summary>
    ///     移除客户端实例
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async ValueTask<bool> RemoveAsync(AgentClient client, CancellationToken cancellationToken)
    {
        var clientId = client.Id.ToLowerInvariant();
        lock (_lifecycleSync)
        {
            if (!_dictionary.TryGetValue(clientId, out var existClient) ||
                !ReferenceEquals(existClient, client) ||
                !_dictionary.TryRemove(clientId, out _))
                return false;
        }

        await _clientStateChannel.WriteAsync(client, false, cancellationToken);
        return true;
    }


    /// <inheritdoc />
    public IEnumerator<AgentClient> GetEnumerator()
    {
        foreach (var keyValue in _dictionary) yield return keyValue.Value;
    }

    public async ValueTask DisposeAsync()
    {
        AgentClient[] clients;
        lock (_lifecycleSync)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            clients = _dictionary.Values.ToArray();
            _dictionary.Clear();
        }

        foreach (var client in clients)
            await client.DisposeAsync();
    }
}