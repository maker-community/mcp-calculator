using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using McpPipeClient.Models;
using McpPipeClient.Services;

namespace McpPipeClient;

/// <summary>
/// Platform service that manages multiple MCP sessions
/// Replaces the single-session McpPipeService
/// </summary>
public class McpPlatformService : BackgroundService
{
    private readonly ILogger<McpPlatformService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly McpSessionManager _sessionManager;
    private readonly McpPlatformConfig _platformConfig;

    public McpPlatformService(
        ILogger<McpPlatformService> logger,
        ILoggerFactory loggerFactory,
        IOptions<McpPlatformConfig> platformConfig)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _platformConfig = platformConfig?.Value ?? throw new ArgumentNullException(nameof(platformConfig));

        _sessionManager = new McpSessionManager(_platformConfig, _loggerFactory);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MCP Platform Service starting...");

        try
        {
            // Start all configured sessions
            await _sessionManager.StartAllSessionsAsync(stoppingToken);

            _logger.LogInformation("MCP Platform Service started successfully");

            // Log statistics periodically
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await timer.WaitForNextTickAsync(stoppingToken);
                    LogSessionStatistics();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in MCP Platform Service");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MCP Platform Service stopping...");

        try
        {
            await _sessionManager.StopAllSessionsAsync();
            await _sessionManager.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping MCP Platform Service");
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("MCP Platform Service stopped");
    }

    private void LogSessionStatistics()
    {
        var stats = _sessionManager.GetStatistics();
        
        _logger.LogInformation(
            "Session Statistics: Total={Total}, Connected={Connected}, Disconnected={Disconnected}, TotalReconnects={Reconnects}",
            stats.TotalSessions,
            stats.ConnectedSessions,
            stats.DisconnectedSessions,
            stats.TotalReconnectAttempts);

        foreach (var session in stats.Sessions)
        {
            var status = session.IsConnected ? "Connected" : "Disconnected";
            var lastActivity = session.IsConnected 
                ? $"Connected at {session.LastConnectedTime:HH:mm:ss}"
                : $"Disconnected at {session.LastDisconnectedTime:HH:mm:ss}";
            
            _logger.LogDebug(
                "  Session {SessionId} ({SessionName}): {Status}, {LastActivity}, Reconnects={Reconnects}",
                session.SessionId,
                session.SessionName,
                status,
                lastActivity,
                session.ReconnectAttempts);
        }
    }

    /// <summary>
    /// Get the session manager (for testing or external access)
    /// </summary>
    public McpSessionManager GetSessionManager() => _sessionManager;
}
