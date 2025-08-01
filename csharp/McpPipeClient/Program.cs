using McpPipeClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.WriteLine("Starting MCP Pipe Client...");

// Create host builder
var builder = Host.CreateDefaultBuilder(args);

// Configure services
builder.ConfigureServices((context, services) =>
{
    services.AddHostedService<McpPipeService>();
    // ILoggerFactory is already provided by Host.CreateDefaultBuilder, no need to register again
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
    config.AddJsonFile("appsettings.json", optional: true);
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
    host.StopAsync().Wait(TimeSpan.FromSeconds(5));
};

try
{
    Console.WriteLine("Starting host...");

    // Verify configuration is loaded
    var config = host.Services.GetRequiredService<IConfiguration>();
    Console.WriteLine($"MCP Server URL from config: {config["MCP_SERVER_URL"]}");
    Console.WriteLine($"MCP Endpoint from config: {config["MCP_ENDPOINT"]?[..50]}..."); // Show first 50 chars for security

    await host.RunAsync();
}
catch (Exception ex)
{
    var logger = host.Services.GetService<ILogger<Program>>();
    logger?.LogError("Program execution error: {Error}", ex.Message);
    Console.WriteLine($"Error: {ex.Message}");
}
