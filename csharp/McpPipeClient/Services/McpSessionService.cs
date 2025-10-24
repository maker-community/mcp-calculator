using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using McpPipeClient.Models;

namespace McpPipeClient.Services;

/// <summary>
/// Manages a single MCP WebSocket session
/// Each instance handles one endpoint connection
/// </summary>
public class McpSessionService : IAsyncDisposable
{
    private readonly ILogger<McpSessionService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly McpEndpointConfig _endpointConfig;
    private readonly ReconnectionSettings _reconnectionSettings;
    
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    // Reconnection state
    private int _reconnectAttempt = 0;
    private int _currentBackoffMs;
    
    // Session state
    private ClientWebSocket? _webSocket;
    private IMcpClient? _mcpClient;
    private bool _isRunning = false;
    
    public string SessionId => _endpointConfig.Id;
    public string SessionName => _endpointConfig.Name;
    public bool IsConnected => _webSocket?.State == WebSocketState.Open && _mcpClient != null;
    public DateTime? LastConnectedTime { get; private set; }
    public DateTime? LastDisconnectedTime { get; private set; }
    public int ReconnectAttempts => _reconnectAttempt;

    public McpSessionService(
        McpEndpointConfig endpointConfig,
        ReconnectionSettings reconnectionSettings,
        ILoggerFactory loggerFactory)
    {
        _endpointConfig = endpointConfig ?? throw new ArgumentNullException(nameof(endpointConfig));
        _reconnectionSettings = reconnectionSettings ?? throw new ArgumentNullException(nameof(reconnectionSettings));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        
        _logger = loggerFactory.CreateLogger<McpSessionService>();
        _currentBackoffMs = reconnectionSettings.InitialBackoffMs;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            AllowTrailingCommas = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    /// <summary>
    /// Start the session with automatic reconnection
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning("Session {SessionId} is already running", SessionId);
            return;
        }

        _isRunning = true;
        _logger.LogInformation("Starting session {SessionId} ({SessionName})", SessionId, SessionName);

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
        
        try
        {
            await ConnectWithRetryAsync(linkedCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session {SessionId} failed to start", SessionId);
            throw;
        }
    }

    /// <summary>
    /// Stop the session gracefully
    /// </summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping session {SessionId}", SessionId);
        _isRunning = false;
        _cancellationTokenSource.Cancel();
        
        await CleanupConnectionAsync();
    }

    /// <summary>
    /// Connect with retry mechanism
    /// </summary>
    private async Task ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Wait before retry (skip on first attempt)
                if (_reconnectAttempt > 0)
                {
                    // Check if we've exceeded max attempts
                    if (_reconnectionSettings.MaxAttempts > 0 && _reconnectAttempt >= _reconnectionSettings.MaxAttempts)
                    {
                        _logger.LogWarning("Session {SessionId} reached max reconnection attempts ({MaxAttempts})", 
                            SessionId, _reconnectionSettings.MaxAttempts);
                        break;
                    }

                    var jitter = Random.Shared.NextDouble() * 0.1;
                    var waitTime = (int)(_currentBackoffMs * (1 + jitter));

                    _logger.LogInformation("Session {SessionId}: Waiting {WaitTimeMs}ms before reconnection attempt {AttemptNumber}...",
                        SessionId, waitTime, _reconnectAttempt);

                    await Task.Delay(waitTime, cancellationToken);
                }

                // Attempt to connect
                await ConnectAsync(cancellationToken);

                // If we reach here without exception, connection was closed normally
                if (cancellationToken.IsCancellationRequested || !_isRunning)
                {
                    _logger.LogInformation("Session {SessionId} shutdown requested", SessionId);
                    break;
                }

                _logger.LogInformation("Session {SessionId} connection closed, will retry", SessionId);
                _reconnectAttempt++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Session {SessionId} cancelled", SessionId);
                break;
            }
            catch (Exception ex)
            {
                _reconnectAttempt++;
                _logger.LogWarning("Session {SessionId} connection failed (attempt {AttemptNumber}): {Error}",
                    SessionId, _reconnectAttempt, ex.Message);

                // Exponential backoff
                _currentBackoffMs = Math.Min(_currentBackoffMs * 2, _reconnectionSettings.MaxBackoffMs);
            }
        }
    }

    /// <summary>
    /// Establish connection and maintain it
    /// </summary>
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Create WebSocket
            _webSocket = new ClientWebSocket();
            _logger.LogInformation("Session {SessionId}: Connecting to {Endpoint}", SessionId, _endpointConfig.WebSocketEndpoint);
            
            await _webSocket.ConnectAsync(new Uri(_endpointConfig.WebSocketEndpoint), cancellationToken);
            _logger.LogInformation("Session {SessionId}: WebSocket connected", SessionId);

            // Create MCP client
            var httpClient = new HttpClient();
            var transport = new SseClientTransport(new SseClientTransportOptions
            {
                Endpoint = new Uri(_endpointConfig.McpServerUrl),
                Name = $"McpSession_{SessionId}",
            }, httpClient, _loggerFactory);

            _mcpClient = await McpClientFactory.CreateAsync(transport, null, loggerFactory: _loggerFactory, cancellationToken);
            _logger.LogInformation("Session {SessionId}: MCP client connected to {McpServer}", SessionId, _endpointConfig.McpServerUrl);

            // Reset reconnection state on successful connection
            _reconnectAttempt = 0;
            _currentBackoffMs = _reconnectionSettings.InitialBackoffMs;
            LastConnectedTime = DateTime.UtcNow;

            // Run bidirectional communication
            var tasks = new[]
            {
                PipeWebSocketToMcpAsync(cancellationToken),
                PipeMcpToWebSocketAsync(cancellationToken)
            };

            var completedTask = await Task.WhenAny(tasks);

            LastDisconnectedTime = DateTime.UtcNow;

            if (completedTask.IsFaulted)
            {
                _logger.LogWarning("Session {SessionId} task faulted: {Error}", 
                    SessionId, completedTask.Exception?.GetBaseException().Message);
                throw completedTask.Exception?.GetBaseException() ?? new Exception("Task faulted");
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogError("Session {SessionId} WebSocket error: {Error}", SessionId, ex.Message);
            throw;
        }
        finally
        {
            await CleanupConnectionAsync();
        }
    }

    /// <summary>
    /// Cleanup WebSocket and MCP client
    /// </summary>
    private async Task CleanupConnectionAsync()
    {
        if (_mcpClient != null)
        {
            try
            {
                await _mcpClient.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error disposing MCP client: {Error}", ex.Message);
            }
            _mcpClient = null;
        }

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, 
                        "Session cleanup", CancellationToken.None);
                }
                _webSocket.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error closing WebSocket: {Error}", ex.Message);
            }
            _webSocket = null;
        }
    }

    /// <summary>
    /// Pipe WebSocket messages to MCP
    /// </summary>
    private async Task PipeWebSocketToMcpAsync(CancellationToken cancellationToken)
    {
        if (_webSocket == null || _mcpClient == null) return;

        var buffer = new byte[4096];

        try
        {
            while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Session {SessionId}: WebSocket closed by server", SessionId);
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                _logger.LogInformation("Session {SessionId} << Received: {Message}", SessionId, message);

                await ProcessWebSocketMessageAsync(message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Session {SessionId}: WebSocket to MCP pipe cancelled", SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError("Session {SessionId}: Error in WebSocket to MCP pipe: {Error}", SessionId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Pipe MCP responses to WebSocket
    /// </summary>
    private async Task PipeMcpToWebSocketAsync(CancellationToken cancellationToken)
    {
        if (_webSocket == null) return;

        try
        {
            while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                // Placeholder for MCP server response forwarding
                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Session {SessionId}: MCP to WebSocket pipe cancelled", SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError("Session {SessionId}: Error in MCP to WebSocket pipe: {Error}", SessionId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Process incoming WebSocket message
    /// </summary>
    private async Task ProcessWebSocketMessageAsync(string message, CancellationToken cancellationToken)
    {
        if (_webSocket == null || _mcpClient == null) return;

        try
        {
            var jsonDoc = JsonDocument.Parse(message);
            var method = jsonDoc.RootElement.GetProperty("method").GetString();
            var id = jsonDoc.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : (int?)null;

            switch (method)
            {
                case "initialize":
                    await HandleInitializeAsync(id, jsonDoc, cancellationToken);
                    break;
                case "notifications/initialized":
                    await HandleInitializedNotificationAsync(cancellationToken);
                    break;
                case "tools/list":
                    await HandleToolsListAsync(id, cancellationToken);
                    break;
                case "tools/call":
                    await HandleToolsCallAsync(id, jsonDoc, cancellationToken);
                    break;
                case "ping":
                    await HandlePingAsync(id, cancellationToken);
                    break;
                default:
                    _logger.LogWarning("Session {SessionId}: Unknown method: {Method}", SessionId, method);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Session {SessionId}: Error processing message: {Error}", SessionId, ex.Message);
        }
    }

    private async Task HandleInitializeAsync(int? id, JsonDocument request, CancellationToken cancellationToken)
    {
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
                    name = _endpointConfig.Name,
                    version = "1.0.0"
                }
            }
        };

        await SendWebSocketResponseAsync(response, cancellationToken);
    }

    private Task HandleInitializedNotificationAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Session {SessionId}: MCP session initialized", SessionId);
        return Task.CompletedTask;
    }

    private async Task HandlePingAsync(int? id, CancellationToken cancellationToken)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id = id,
            result = new { }
        };

        await SendWebSocketResponseAsync(response, cancellationToken);
    }

    private async Task HandleToolsListAsync(int? id, CancellationToken cancellationToken)
    {
        if (_mcpClient == null) return;

        try
        {
            var toolsResponse = await _mcpClient.ListToolsAsync(null, cancellationToken);

            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    tools = toolsResponse.Select(tool =>
                    {
                        var properties = new Dictionary<string, object>();
                        var required = Array.Empty<string>();

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

            await SendWebSocketResponseAsync(response, cancellationToken);
        }
        catch (Exception ex)
        {
            await SendErrorResponseAsync(id, -32603, "Internal error", ex.Message, cancellationToken);
        }
    }

    private async Task HandleToolsCallAsync(int? id, JsonDocument request, CancellationToken cancellationToken)
    {
        if (_mcpClient == null) return;

        try
        {
            var paramsElement = request.RootElement.GetProperty("params");
            var toolName = paramsElement.GetProperty("name").GetString()!;

            var arguments = new Dictionary<string, object?>();
            if (paramsElement.TryGetProperty("arguments", out var argsElement))
            {
                foreach (var property in argsElement.EnumerateObject())
                {
                    arguments[property.Name] = JsonElementToObject(property.Value);
                }
            }

            _logger.LogInformation("Session {SessionId}: Calling tool {ToolName} with arguments: {Arguments}", 
                SessionId, toolName, JsonSerializer.Serialize(arguments));

            var result = await _mcpClient.CallToolAsync(toolName, arguments);

            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = result
            };

            await SendWebSocketResponseAsync(response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError("Session {SessionId}: Error calling tool: {Error}", SessionId, ex.Message);
            await SendErrorResponseAsync(id, -32603, "Internal error", ex.Message, cancellationToken);
        }
    }

    private async Task SendWebSocketResponseAsync(object response, CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(response, _jsonOptions);
        _logger.LogInformation("Session {SessionId} >> Sending: {Response}", SessionId, json);

        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task SendErrorResponseAsync(int? id, int code, string message, string? data, CancellationToken cancellationToken)
    {
        var errorResponse = new
        {
            jsonrpc = "2.0",
            id = id,
            error = new
            {
                code = code,
                message = message,
                data = data
            }
        };

        await SendWebSocketResponseAsync(errorResponse, cancellationToken);
    }

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

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cancellationTokenSource.Dispose();
    }
}
