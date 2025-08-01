# MCP Pipe Client (C#)

C# implementation of the mcp_pipe.py functionality that provides bidirectional communication between WebSocket endpoints and MCP servers.

## Features

- 🔌 WebSocket to MCP server communication bridge
- 🔄 Automatic reconnection with exponential backoff
- 📊 Real-time message logging and forwarding
- 🛠️ Support for all MCP protocol methods (initialize, tools/list, tools/call)
- 🔒 Graceful shutdown handling

## Prerequisites

- .NET 8.0 or later
- MCP server running (e.g., ACPSseServer)
- WebSocket endpoint to connect to

## Configuration

Set the following environment variables:

```bash
# Required: WebSocket endpoint to connect to
export MCP_ENDPOINT=ws://localhost:8080/mcp

# Optional: MCP server URL (defaults to http://localhost:5000)
export MCP_SERVER_URL=http://localhost:5000
```

Or configure in `appsettings.json`:

```json
{
  "MCP_ENDPOINT": "ws://localhost:8080/mcp",
  "MCP_SERVER_URL": "http://localhost:5000"
}
```

## Usage

1. **Build the project:**
   ```bash
   dotnet build
   ```

2. **Run the MCP server (ACPSseServer):**
   ```bash
   cd ../ACPSseServer
   dotnet run
   ```

3. **Run the pipe client:**
   ```bash
   dotnet run
   ```

## Protocol Compliance Fixes

Based on Python implementation logs, the following fixes were applied to ensure JSON-RPC compatibility:

### 1. **Added `notifications/initialized` Handler**
```csharp
case "notifications/initialized":
    await HandleInitializedNotificationAsync(webSocket, mcpClient, cancellationToken);
    break;
```
Python logs showed this notification is sent after initialization and needs to be handled.

### 2. **Fixed Initialize Response Format**
```csharp
// Now matches Python output structure:
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
    version = "1.12.2"  // Matches Python version
}
```

### 3. **Fixed Tools List Response Format**
```csharp
// Now matches Python JSON Schema format:
inputSchema = new
{
    properties = properties,
    required = required,
    title = $"{tool.Name}Arguments",  // e.g., "calculatorArguments"
    type = "object"
}
```

### 4. **JSON Schema Property Extraction**
```csharp
// Proper handling of JsonElement for schema properties
if (tool.JsonSchema.TryGetProperty("properties", out var propsElement))
{
    properties = JsonElementToObject(propsElement) as Dictionary<string, object> ?? new Dictionary<string, object>();
}
```

## Python vs C# Message Flow Comparison

| Python Log | C# Implementation | Status |
|------------|------------------|---------|
| `{"method":"initialize"}` | ✅ HandleInitializeAsync | Fixed |
| `{"method":"notifications/initialized"}` | ✅ HandleInitializedNotificationAsync | Added |
| `{"method":"tools/list"}` | ✅ HandleToolsListAsync | Fixed format |
| Tool schema format | ✅ Matches Python output | Fixed |

## Key Implementation Details

### MCP Client Creation

The code uses the correct MCP client initialization pattern based on the official C# SDK:

```csharp
// Create HTTP client with proper configuration
var httpClient = new HttpClient();

// Create SSE transport for MCP communication
var transport = new SseClientTransport(new SseClientOptions
{
    Endpoint = new Uri(_mcpServerUrl),
    Name = "McpPipeClient",
    Version = "1.0.0"
}, httpClient, _loggerFactory);

// Create MCP client using the factory
mcpClient = await McpClientFactory.CreateAsync(transport, loggerFactory: _loggerFactory, cancellationToken);
```

### Tool Arguments Handling

Tool arguments are properly converted from JSON to Dictionary format:

```csharp
// Convert arguments to Dictionary<string, object?>
var arguments = new Dictionary<string, object?>();
if (paramsElement.TryGetProperty("arguments", out var argsElement))
{
    foreach (var property in argsElement.EnumerateObject())
    {
        arguments[property.Name] = JsonElementToObject(property.Value);
    }
}

// Call tool with proper arguments
var result = await mcpClient.CallToolAsync(toolName, arguments, cancellationToken);
```

### Error Handling

Both success and error responses are properly formatted according to JSON-RPC 2.0 specification:

```csharp
// Error response format
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
```

## Architecture

```
[WebSocket Client] ←→ [McpPipeClient] ←→ [MCP Server (ACPSseServer)]
                      ↓
                   [Logging & Monitoring]
```

## Comparison with Python Version

| Feature | Python (mcp_pipe.py) | C# (McpPipeClient) |
|---------|---------------------|-------------------|
| Language | Python 3.7+ | .NET 8.0 |
| Transport | stdio + WebSocket | HTTP + WebSocket |
| Process Management | subprocess | MCP Client Library |
| Reconnection | ✅ Exponential backoff | ✅ Exponential backoff |
| Logging | ✅ Configurable | ✅ Microsoft.Extensions.Logging |
| Error Handling | ✅ Exception handling | ✅ Structured error handling |
| Performance | Good | Better (compiled) |
| Type Safety | Runtime | Compile-time |

## Message Flow

1. **WebSocket → MCP Server:**
   ```
   WebSocket receives JSON-RPC message
   → Parse method (initialize/tools/list/tools/call)
   → Forward to MCP server via HTTP
   → Return response to WebSocket
   ```

2. **MCP Server → WebSocket:**
   ```
   MCP server processes request
   → Generate JSON-RPC response
   → Send back through WebSocket
   ```

## Supported MCP Methods

- `initialize` - Protocol handshake
- `tools/list` - List available tools
- `tools/call` - Execute tool with parameters

## Example Logs

```
info: McpPipeClient.McpPipeService[0]
      Connecting to WebSocket server: ws://localhost:8080/mcp
info: McpPipeClient.McpPipeService[0]
      Successfully connected to WebSocket server
info: McpPipeClient.McpPipeService[0]
      Connecting to MCP server: http://localhost:5000
info: McpPipeClient.McpPipeService[0]
      << WebSocket received: {"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
info: McpPipeClient.McpPipeService[0]
      >> WebSocket sending: {"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2024-11-05"}}
```

## Error Handling

- **Connection failures:** Automatic retry with exponential backoff
- **WebSocket errors:** Graceful reconnection
- **MCP server errors:** Error logging and response forwarding
- **Graceful shutdown:** Ctrl+C handling with proper cleanup

## Development

To extend functionality:

1. Add new MCP method handlers in `McpPipeService.cs`
2. Implement custom message processing logic
3. Add additional configuration options
4. Extend logging and monitoring capabilities

## Dependencies

- Microsoft.Extensions.Hosting - Background service hosting
- Microsoft.Extensions.Logging - Structured logging
- ModelContextProtocol - MCP client library
- System.Net.WebSockets.Client - WebSocket connectivity
- System.Text.Json - JSON serialization
