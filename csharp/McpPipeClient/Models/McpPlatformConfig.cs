namespace McpPipeClient.Models;

/// <summary>
/// Platform-wide configuration supporting multiple MCP endpoints
/// </summary>
public class McpPlatformConfig
{
    /// <summary>
    /// List of all configured MCP endpoints
    /// </summary>
    public List<McpEndpointConfig> Endpoints { get; set; } = new();

    /// <summary>
    /// Default MCP server URL (can be overridden per endpoint)
    /// </summary>
    public string? DefaultMcpServerUrl { get; set; }

    /// <summary>
    /// Reconnection settings
    /// </summary>
    public ReconnectionSettings Reconnection { get; set; } = new();
}

/// <summary>
/// Reconnection settings for WebSocket connections
/// </summary>
public class ReconnectionSettings
{
    /// <summary>
    /// Initial backoff in milliseconds
    /// </summary>
    public int InitialBackoffMs { get; set; } = 1000;

    /// <summary>
    /// Maximum backoff in milliseconds
    /// </summary>
    public int MaxBackoffMs { get; set; } = 600000;

    /// <summary>
    /// Maximum reconnection attempts (0 = infinite)
    /// </summary>
    public int MaxAttempts { get; set; } = 0;
}
