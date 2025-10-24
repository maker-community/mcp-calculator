using Microsoft.Extensions.Logging;
using McpPipeClient.Models;
using System.Collections.Concurrent;

namespace McpPipeClient.Services;

/// <summary>
/// Manages multiple MCP sessions (WebSocket connections)
/// Provides session lifecycle management and monitoring
/// </summary>
public class McpSessionManager : IAsyncDisposable
{
    private readonly ILogger<McpSessionManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly McpPlatformConfig _platformConfig;
    
    private readonly ConcurrentDictionary<string, McpSessionService> _sessions = new();
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    
    public McpSessionManager(
        McpPlatformConfig platformConfig,
        ILoggerFactory loggerFactory)
    {
        _platformConfig = platformConfig ?? throw new ArgumentNullException(nameof(platformConfig));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<McpSessionManager>();
    }

    /// <summary>
    /// Initialize and start all configured sessions
    /// </summary>
    public async Task StartAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting all configured sessions ({Count} endpoints)", _platformConfig.Endpoints.Count);

        var enabledEndpoints = _platformConfig.Endpoints.Where(e => e.Enabled).ToList();
        
        if (enabledEndpoints.Count == 0)
        {
            _logger.LogWarning("No enabled endpoints found in configuration");
            return;
        }

        var tasks = enabledEndpoints.Select(endpoint => StartSessionAsync(endpoint, cancellationToken));
        await Task.WhenAll(tasks);

        _logger.LogInformation("All sessions started ({Active}/{Total})", 
            _sessions.Count, _platformConfig.Endpoints.Count);
    }

    /// <summary>
    /// Start a specific session
    /// </summary>
    public async Task<McpSessionService> StartSessionAsync(
        McpEndpointConfig endpointConfig, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(endpointConfig.Id))
        {
            throw new ArgumentException("Endpoint ID cannot be empty", nameof(endpointConfig));
        }

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            // Check if session already exists
            if (_sessions.TryGetValue(endpointConfig.Id, out var existingSession))
            {
                _logger.LogWarning("Session {SessionId} already exists", endpointConfig.Id);
                return existingSession;
            }

            // Use default MCP server URL if not specified
            if (string.IsNullOrEmpty(endpointConfig.McpServerUrl))
            {
                endpointConfig.McpServerUrl = _platformConfig.DefaultMcpServerUrl 
                    ?? throw new InvalidOperationException($"No MCP server URL configured for endpoint {endpointConfig.Id}");
            }

            // Create new session
            var session = new McpSessionService(
                endpointConfig,
                _platformConfig.Reconnection,
                _loggerFactory);

            // Add to dictionary
            if (!_sessions.TryAdd(endpointConfig.Id, session))
            {
                throw new InvalidOperationException($"Failed to add session {endpointConfig.Id}");
            }

            _logger.LogInformation("Created session {SessionId} ({SessionName})", 
                session.SessionId, session.SessionName);

            // Start session in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await session.StartAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Session {SessionId} failed", endpointConfig.Id);
                    // Remove failed session
                    _sessions.TryRemove(endpointConfig.Id, out _);
                }
            }, cancellationToken);

            return session;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <summary>
    /// Stop a specific session
    /// </summary>
    public async Task<bool> StopSessionAsync(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogWarning("Session {SessionId} not found", sessionId);
            return false;
        }

        _logger.LogInformation("Stopping session {SessionId}", sessionId);
        
        try
        {
            await session.StopAsync();
            await session.DisposeAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping session {SessionId}", sessionId);
            return false;
        }
    }

    /// <summary>
    /// Stop all sessions
    /// </summary>
    public async Task StopAllSessionsAsync()
    {
        _logger.LogInformation("Stopping all sessions ({Count})", _sessions.Count);

        var stopTasks = _sessions.Values.Select(async session =>
        {
            try
            {
                await session.StopAsync();
                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping session {SessionId}", session.SessionId);
            }
        });

        await Task.WhenAll(stopTasks);
        _sessions.Clear();

        _logger.LogInformation("All sessions stopped");
    }

    /// <summary>
    /// Get a specific session
    /// </summary>
    public McpSessionService? GetSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// Get all active sessions
    /// </summary>
    public IReadOnlyDictionary<string, McpSessionService> GetAllSessions()
    {
        return _sessions;
    }

    /// <summary>
    /// Get session statistics
    /// </summary>
    public SessionStatistics GetStatistics()
    {
        var sessions = _sessions.Values.ToList();
        
        return new SessionStatistics
        {
            TotalSessions = sessions.Count,
            ConnectedSessions = sessions.Count(s => s.IsConnected),
            DisconnectedSessions = sessions.Count(s => !s.IsConnected),
            TotalReconnectAttempts = sessions.Sum(s => s.ReconnectAttempts),
            Sessions = sessions.Select(s => new SessionInfo
            {
                SessionId = s.SessionId,
                SessionName = s.SessionName,
                IsConnected = s.IsConnected,
                LastConnectedTime = s.LastConnectedTime,
                LastDisconnectedTime = s.LastDisconnectedTime,
                ReconnectAttempts = s.ReconnectAttempts
            }).ToList()
        };
    }

    /// <summary>
    /// Add a new endpoint dynamically
    /// </summary>
    public async Task<McpSessionService> AddEndpointAsync(
        McpEndpointConfig endpointConfig, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Adding new endpoint {EndpointId} ({EndpointName})", 
            endpointConfig.Id, endpointConfig.Name);

        // Add to configuration
        if (!_platformConfig.Endpoints.Any(e => e.Id == endpointConfig.Id))
        {
            _platformConfig.Endpoints.Add(endpointConfig);
        }

        // Start session
        return await StartSessionAsync(endpointConfig, cancellationToken);
    }

    /// <summary>
    /// Remove an endpoint dynamically
    /// </summary>
    public async Task<bool> RemoveEndpointAsync(string endpointId)
    {
        _logger.LogInformation("Removing endpoint {EndpointId}", endpointId);

        // Stop session
        var stopped = await StopSessionAsync(endpointId);

        // Remove from configuration
        var endpoint = _platformConfig.Endpoints.FirstOrDefault(e => e.Id == endpointId);
        if (endpoint != null)
        {
            _platformConfig.Endpoints.Remove(endpoint);
        }

        return stopped;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllSessionsAsync();
        _sessionLock.Dispose();
    }
}

/// <summary>
/// Statistics about all sessions
/// </summary>
public class SessionStatistics
{
    public int TotalSessions { get; set; }
    public int ConnectedSessions { get; set; }
    public int DisconnectedSessions { get; set; }
    public int TotalReconnectAttempts { get; set; }
    public List<SessionInfo> Sessions { get; set; } = new();
}

/// <summary>
/// Information about a single session
/// </summary>
public class SessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public string SessionName { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public DateTime? LastConnectedTime { get; set; }
    public DateTime? LastDisconnectedTime { get; set; }
    public int ReconnectAttempts { get; set; }
}
