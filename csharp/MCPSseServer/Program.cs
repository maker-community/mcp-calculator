var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer().WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapGet("/", () => "This is an MCP project template.");
app.MapMcp();

app.Run();
