using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace McpPipeClient;

/// <summary>
/// C# implementation of mcp_pipe.py functionality
/// Connects WebSocket endpoint with MCP server and provides bidirectional communication
/// </summary>
public class McpPipeService : BackgroundService
{
    private readonly ILogger<McpPipeService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _mcpServerUrl;
    private readonly string _websocketEndpoint;

    private readonly JsonSerializerOptions _jsonOptions;

    // Reconnection settings
    private const int InitialBackoffMs = 1000;
    private const int MaxBackoffMs = 600000;
    private int _reconnectAttempt = 0;
    private int _currentBackoffMs = InitialBackoffMs;

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public McpPipeService(ILogger<McpPipeService> logger, IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _loggerFactory = loggerFactory;

        // Get configuration from environment variables or appsettings
        _mcpServerUrl = _configuration["MCP_SERVER_URL"] ??
                       "http://localhost:5000";

        _websocketEndpoint = _configuration["MCP_ENDPOINT"] ??
                           throw new InvalidOperationException("MCP_ENDPOINT environment variable is required");

        _logger.LogInformation("MCP Server URL: {McpServerUrl}", _mcpServerUrl);
        _logger.LogInformation("WebSocket Endpoint: {WebSocketEndpoint}", _websocketEndpoint);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            AllowTrailingCommas = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConnectWithRetryAsync(stoppingToken);
    }

    /// <summary>
    /// Connect to WebSocket server with retry mechanism (similar to connect_with_retry in Python)
    /// </summary>
    private async Task ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_reconnectAttempt > 0)
                {
                    // Add random jitter to prevent thundering herd
                    var jitter = Random.Shared.NextDouble() * 0.1;
                    var waitTime = (int)(_currentBackoffMs * (1 + jitter));

                    _logger.LogInformation("Waiting {WaitTimeMs}ms before reconnection attempt {AttemptNumber}...",
                                         waitTime, _reconnectAttempt);

                    await Task.Delay(waitTime, cancellationToken);
                }

                // Attempt to connect - this will maintain the connection until it fails
                await ConnectToServerAsync(cancellationToken);

                // If we reach here, the connection was closed
                // Check if it was due to cancellation (normal shutdown) or an error
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Service shutdown requested, exiting retry loop");
                    break;
                }

                // Connection was closed due to an error, prepare for retry
                _logger.LogInformation("Connection closed, will attempt to reconnect");
                _reconnectAttempt++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Service shutdown requested");
                break;
            }
            catch (Exception ex)
            {
                _reconnectAttempt++;
                _logger.LogWarning("Connection failed or closed (attempt: {AttemptNumber}): {Error}",
                                 _reconnectAttempt, ex.Message);

                // Calculate wait time for next reconnection (exponential backoff)
                _currentBackoffMs = Math.Min(_currentBackoffMs * 2, MaxBackoffMs);
            }
        }

        _logger.LogInformation("ConnectWithRetryAsync exiting");
    }

    /// <summary>
    /// Connect to WebSocket server and establish bidirectional communication with MCP server
    /// This method maintains the connection until it fails or is cancelled
    /// </summary>
    private async Task ConnectToServerAsync(CancellationToken cancellationToken)
    {
        using var webSocket = new ClientWebSocket();
        IMcpClient? mcpClient = null;

        try
        {
            _logger.LogInformation("Connecting to WebSocket server: {Endpoint}", _websocketEndpoint);

            // Connect to WebSocket
            await webSocket.ConnectAsync(new Uri(_websocketEndpoint), cancellationToken);
            _logger.LogInformation("Successfully connected to WebSocket server");

            // Reset reconnection counter on successful connection
            _reconnectAttempt = 0;
            _currentBackoffMs = InitialBackoffMs;

            // Create MCP client using proper transport and factory
            _logger.LogInformation("Connecting to MCP server: {ServerUrl}", _mcpServerUrl);

            // Create HTTP client with proper configuration
            var httpClient = new HttpClient();

            // Create SSE transport for MCP communication
            var transport = new SseClientTransport(new SseClientTransportOptions
            {
                Endpoint = new Uri(_mcpServerUrl),
                Name = "McpPipeClient",
            }, httpClient, _loggerFactory);

            // Create MCP client using the factory
            mcpClient = await McpClientFactory.CreateAsync(transport, null, loggerFactory: _loggerFactory, cancellationToken);
            _logger.LogInformation("Successfully connected to MCP server");

            // Start bidirectional communication tasks and wait for ANY to complete (indicating connection lost)
            var tasks = new[]
            {
                PipeWebSocketToMcpAsync(webSocket, mcpClient, cancellationToken),
                PipeMcpToWebSocketAsync(mcpClient, webSocket, cancellationToken)
            };

            // Wait for any task to complete - this indicates connection failure or normal shutdown
            var completedTask = await Task.WhenAny(tasks);

            // Log the reason for connection termination
            if (completedTask.IsFaulted)
            {
                _logger.LogWarning("Connection task faulted: {Error}", completedTask.Exception?.GetBaseException().Message);
                throw completedTask.Exception?.GetBaseException() ?? new Exception("Connection task faulted");
            }
            else if (completedTask.IsCanceled || cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Connection task was cancelled - service shutdown");
                // Don't throw here, let the method return normally for graceful shutdown
            }
            else
            {
                _logger.LogInformation("Connection task completed - connection lost, will retry");
                // Don't throw here either, this is a normal disconnection that should trigger retry
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogError("WebSocket connection error: {Error}", ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Connection error: {Error}", ex.Message);
            throw;
        }
        finally
        {
            // Cleanup resources
            if (mcpClient != null)
            {
                _logger.LogInformation("Disconnecting from MCP server");
                try
                {
                    await mcpClient.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Error disposing MCP client: {Error}", ex.Message);
                }
            }

            if (webSocket.State == WebSocketState.Open)
            {
                _logger.LogInformation("Closing WebSocket connection");
                try
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Service shutdown", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Error closing WebSocket: {Error}", ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// Read data from WebSocket and write to MCP server (similar to pipe_websocket_to_process)
    /// </summary>
    private async Task PipeWebSocketToMcpAsync(ClientWebSocket webSocket, IMcpClient mcpClient, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                // Read message from WebSocket
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket connection closed by server");
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                _logger.LogInformation("<< WebSocket received: {Message}", message);

                // Parse and forward to MCP server
                try
                {
                    var jsonDoc = JsonDocument.Parse(message);
                    var method = jsonDoc.RootElement.GetProperty("method").GetString();
                    var id = jsonDoc.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : (int?)null;

                    // Handle different MCP methods
                    switch (method)
                    {
                        case "initialize":
                            await HandleInitializeAsync(webSocket, mcpClient, id, jsonDoc, cancellationToken);
                            break;
                        case "notifications/initialized":
                            await HandleInitializedNotificationAsync(webSocket, mcpClient, cancellationToken);
                            break;
                        case "tools/list":
                            await HandleToolsListAsync(webSocket, mcpClient, id, cancellationToken);
                            break;
                        case "tools/call":
                            await HandleToolsCallAsync(webSocket, mcpClient, id, jsonDoc, cancellationToken);
                            break;
                        case "ping":
                            await HandlePingAsync(webSocket, id, cancellationToken);
                            break;
                        default:
                            _logger.LogWarning("Unknown method: {Method}", method);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error processing WebSocket message: {Error}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("WebSocket to MCP pipe cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError("Error in WebSocket to MCP pipe: {Error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Read data from MCP server and send to WebSocket (similar to pipe_process_to_websocket)
    /// </summary>
    private async Task PipeMcpToWebSocketAsync(IMcpClient mcpClient, ClientWebSocket webSocket, CancellationToken cancellationToken)
    {
        try
        {
            // This would typically listen for responses from the MCP server
            // For now, we'll implement a basic response forwarding mechanism
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                // In a real implementation, you would listen for MCP server responses
                // and forward them to the WebSocket
                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("MCP to WebSocket pipe cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError("Error in MCP to WebSocket pipe: {Error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Handle MCP initialize method
    /// </summary>
    private async Task HandleInitializeAsync(ClientWebSocket webSocket, IMcpClient mcpClient, int? id, JsonDocument request, CancellationToken cancellationToken)
    {
        try
        {
            // Parse the initialize request to get actual capabilities from MCP server
            var mcpInitResponse = await GetMcpServerCapabilitiesAsync(mcpClient, cancellationToken);

            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new
                    {
                        experimental = new { },
                        prompts = new { listChanged = false },
                        resources = new { subscribe = false, listChanged = false },
                        tools = new { listChanged = false }
                    },
                    serverInfo = new
                    {
                        name = "Calculator",
                        version = "1.12.2"
                    }
                }
            };

            await SendWebSocketResponseAsync(webSocket, response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error handling initialize: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Handle ping method
    /// </summary>
    private async Task HandlePingAsync(ClientWebSocket webSocket, int? id, CancellationToken cancellationToken)
    {
        try
        {
            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = new { }
            };

            await SendWebSocketResponseAsync(webSocket, response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error handling ping: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Handle notifications/initialized method
    /// </summary>
    private Task HandleInitializedNotificationAsync(ClientWebSocket webSocket, IMcpClient mcpClient, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Received notifications/initialized - MCP session is ready");
            // This is a notification, no response needed
            // But we can perform any post-initialization setup here
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error handling notifications/initialized: {Error}", ex.Message);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Get capabilities from MCP server (placeholder for actual implementation)
    /// </summary>
    private Task<object> GetMcpServerCapabilitiesAsync(IMcpClient mcpClient, CancellationToken cancellationToken)
    {
        try
        {
            // In a real implementation, you might query the MCP server for its capabilities
            // For now, return a default set matching the Python output
            return Task.FromResult<object>(new { });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to get MCP server capabilities: {Error}", ex.Message);
            return Task.FromResult<object>(new { });
        }
    }

    /// <summary>
    /// Handle MCP tools/list method
    /// </summary>
    private async Task HandleToolsListAsync(ClientWebSocket webSocket, IMcpClient mcpClient, int? id, CancellationToken cancellationToken)
    {
        try
        {
            // Get tools from MCP server
            var toolsResponse = await mcpClient.ListToolsAsync(null, cancellationToken);

            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    tools = toolsResponse.Select(tool =>
                    {
                        // Extract properties and required from JsonSchema
                        var properties = new Dictionary<string, object>();
                        var required = new string[0];

                        if (tool.JsonSchema.ValueKind != JsonValueKind.Undefined)
                        {
                            if (tool.JsonSchema.TryGetProperty("properties", out var propsElement))
                            {
                                properties = JsonElementToObject(propsElement) as Dictionary<string, object> ?? new Dictionary<string, object>();
                            }

                            if (tool.JsonSchema.TryGetProperty("required", out var reqElement))
                            {
                                required = reqElement.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                            }
                        }

                        return new
                        {
                            name = tool.Name,
                            description = tool.Description,
                            inputSchema = new
                            {
                                properties = properties,
                                required = required,
                                title = $"{tool.Name}Arguments",
                                type = "object"
                            }
                        };
                    }).ToArray()
                }
            };

            await SendWebSocketResponseAsync(webSocket, response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error handling tools/list: {Error}", ex.Message);

            // Send error response
            var errorResponse = new
            {
                jsonrpc = "2.0",
                id = id,
                error = new
                {
                    code = -32603,
                    message = "Internal error",
                    data = ex.Message
                }
            };

            await SendWebSocketResponseAsync(webSocket, errorResponse, cancellationToken);
        }
    }

    /// <summary>
    /// Handle MCP tools/call method
    /// </summary>
    private async Task HandleToolsCallAsync(ClientWebSocket webSocket, IMcpClient mcpClient, int? id, JsonDocument request, CancellationToken cancellationToken)
    {
        try
        {
            var paramsElement = request.RootElement.GetProperty("params");
            var toolName = paramsElement.GetProperty("name").GetString()!;

            // Convert arguments to Dictionary<string, object?>
            var arguments = new Dictionary<string, object?>();
            if (paramsElement.TryGetProperty("arguments", out var argsElement))
            {
                foreach (var property in argsElement.EnumerateObject())
                {
                    arguments[property.Name] = JsonElementToObject(property.Value);
                }
            }

            // Call tool on MCP server
            var result = await mcpClient.CallToolAsync(toolName, arguments);

            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = result
            };

            await SendWebSocketResponseAsync(webSocket, response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error handling tools/call: {Error}", ex.Message);

            // Send error response
            var errorResponse = new
            {
                jsonrpc = "2.0",
                id = id,
                error = new
                {
                    code = -32603,
                    message = "Internal error",
                    data = ex.Message
                }
            };

            await SendWebSocketResponseAsync(webSocket, errorResponse, cancellationToken);
        }
    }

    /// <summary>
    /// Convert JsonElement to object for tool arguments
    /// </summary>
    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToArray(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            _ => element.ToString()
        };
    }

    /// <summary>
    /// Send response to WebSocket
    /// </summary>
    private async Task SendWebSocketResponseAsync(ClientWebSocket webSocket, object response, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        _logger.LogInformation(">> WebSocket sending: {Response}", json);

        var bytes = Encoding.UTF8.GetBytes(json);
        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    public override void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        base.Dispose();
    }
}
