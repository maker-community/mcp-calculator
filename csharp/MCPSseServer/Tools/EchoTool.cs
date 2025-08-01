using ModelContextProtocol.Server;
using System.ComponentModel;

namespace MCPSseServer.Tools;

// Commented out to use CalculatorTool instead

[McpServerToolType]
public sealed class EchoTool
{
    [McpServerTool, Description("Echoes the input back to the client.")]
    public static string Echo(string message)
    {
        return "hello " + message;
    }
}

