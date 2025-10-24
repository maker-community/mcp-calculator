using McpPipeClient;
using McpPipeClient.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.WriteLine("Starting MCP Platform...");

// Create host builder
var builder = Host.CreateDefaultBuilder(args);

// Configure services
builder.ConfigureServices((context, services) =>
{
    // Bind platform configuration
    services.Configure<McpPlatformConfig>(context.Configuration.GetSection("McpPlatform"));
    
    // Register platform service
    services.AddHostedService<McpPlatformService>();
});

// Configure logging
builder.ConfigureLogging((context, logging) =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

// Configure configuration
builder.ConfigureAppConfiguration((context, config) =>
{
    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    config.AddUserSecrets<Program>(optional: true);
    config.AddEnvironmentVariables();
    config.AddCommandLine(args);
});

Console.WriteLine("Building host...");

// Build and run
var host = builder.Build();

Console.WriteLine("Host built successfully!");

// Handle Ctrl+C gracefully
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    Console.WriteLine("Received interrupt signal, shutting down...");
    host.StopAsync().Wait(TimeSpan.FromSeconds(10));
};

try
{
    Console.WriteLine("Starting host...");

    // Display configuration
    var config = host.Services.GetRequiredService<IConfiguration>();
    var platformConfig = config.GetSection("McpPlatform").Get<McpPlatformConfig>();
    
    if (platformConfig != null)
    {
        Console.WriteLine($"Platform Configuration:");
        Console.WriteLine($"  Default MCP Server: {platformConfig.DefaultMcpServerUrl}");
        Console.WriteLine($"  Configured Endpoints: {platformConfig.Endpoints.Count}");
        
        foreach (var endpoint in platformConfig.Endpoints.Where(e => e.Enabled))
        {
            Console.WriteLine($"    - {endpoint.Id} ({endpoint.Name})");
            Console.WriteLine($"      WebSocket: {endpoint.WebSocketEndpoint[..Math.Min(50, endpoint.WebSocketEndpoint.Length)]}...");
            Console.WriteLine($"      MCP Server: {endpoint.McpServerUrl ?? "Default"}");
        }
    }
    else
    {
        Console.WriteLine("Warning: No platform configuration found");
    }

    await host.RunAsync();
}
catch (Exception ex)
{
    var logger = host.Services.GetService<ILogger<Program>>();
    logger?.LogError("Program execution error: {Error}", ex.Message);
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}
