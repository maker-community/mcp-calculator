namespace McpPipeClient.Models;

/// <summary>
/// Configuration for a single MCP endpoint connection
/// </summary>
public class McpEndpointConfig
{
    /// <summary>
    /// Unique identifier for this endpoint (e.g., "user123_endpoint1")
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name for this endpoint
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// WebSocket endpoint URL
    /// </summary>
    public string WebSocketEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// MCP server URL for this endpoint
    /// </summary>
    public string McpServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// User ID who owns this endpoint
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Whether this endpoint is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Additional metadata for this endpoint
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
