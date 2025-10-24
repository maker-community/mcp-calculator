# MCP平台架构说明

## 整体架构

```
┌─────────────────────────────────────────────────────────────────┐
│                      McpPlatformService                          │
│                     (Hosted Service)                             │
│                                                                   │
│  ┌────────────────────────────────────────────────────────┐     │
│  │              McpSessionManager                          │     │
│  │                                                          │     │
│  │  ┌──────────────────────────────────────────────────┐  │     │
│  │  │ Sessions Dictionary                               │  │     │
│  │  │ ┌────────────┐ ┌────────────┐ ┌────────────┐    │  │     │
│  │  │ │  Session1  │ │  Session2  │ │  SessionN  │    │  │     │
│  │  │ └────────────┘ └────────────┘ └────────────┘    │  │     │
│  │  └──────────────────────────────────────────────────┘  │     │
│  │                                                          │     │
│  │  方法:                                                   │     │
│  │  - StartAllSessionsAsync()                              │     │
│  │  - StartSessionAsync(config)                            │     │
│  │  - StopSessionAsync(id)                                 │     │
│  │  - AddEndpointAsync(config)                             │     │
│  │  - RemoveEndpointAsync(id)                              │     │
│  │  - GetStatistics()                                       │     │
│  └────────────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ 管理多个
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    McpSessionService                             │
│                   (单个会话实例)                                  │
│                                                                   │
│  属性:                                                            │
│  - SessionId: "user1_endpoint1"                                 │
│  - SessionName: "User1 Primary"                                 │
│  - IsConnected: true/false                                      │
│  - LastConnectedTime: DateTime?                                 │
│  - ReconnectAttempts: int                                       │
│                                                                   │
│  ┌─────────────────┐         ┌──────────────────┐              │
│  │   WebSocket     │◄───────►│   MCP Client     │              │
│  │   Client        │         │   (SSE)          │              │
│  └─────────────────┘         └──────────────────┘              │
│         │                             │                          │
│         │                             │                          │
│  ┌──────▼─────────────────────────────▼──────────┐             │
│  │         消息处理管道                            │             │
│  │  - PipeWebSocketToMcpAsync()                  │             │
│  │  - PipeMcpToWebSocketAsync()                  │             │
│  │  - ProcessWebSocketMessageAsync()             │             │
│  │  - HandleInitializeAsync()                    │             │
│  │  - HandleToolsListAsync()                     │             │
│  │  - HandleToolsCallAsync()                     │             │
│  └───────────────────────────────────────────────┘             │
│                                                                   │
│  重连机制:                                                        │
│  - 指数退避算法                                                   │
│  - 带随机抖动                                                     │
│  - 可配置最大重试次数                                             │
└─────────────────────────────────────────────────────────────────┘
         │                             │
         │                             │
         ▼                             ▼
┌──────────────────┐        ┌──────────────────┐
│  WebSocket       │        │  MCP Server      │
│  Endpoint        │        │  (http://...)    │
│  (wss://...)     │        │                  │
│                  │        │  Tools:          │
│  接收/发送MCP     │        │  - Calculator    │
│  JSON-RPC消息    │        │  - Echo          │
└──────────────────┘        └──────────────────┘
```

## 配置流程

```
appsettings.json
    │
    │ 读取
    ▼
McpPlatformConfig
    │
    │ 包含
    ▼
List<McpEndpointConfig>
    │
    │ 遍历创建
    ▼
McpSessionService (多个实例)
```

## 数据流

### 1. 启动流程

```
Program.cs
    └─> 配置 McpPlatformConfig
    └─> 注册 McpPlatformService (Hosted Service)
    └─> 启动 Host
         └─> McpPlatformService.ExecuteAsync()
              └─> SessionManager.StartAllSessionsAsync()
                   └─> 为每个Endpoint创建 McpSessionService
                   └─> 每个Session独立启动
                        └─> 连接WebSocket
                        └─> 连接MCP Server
                        └─> 启动消息管道
```

### 2. 消息处理流程

```
WebSocket收到消息
    │
    ▼
McpSessionService.PipeWebSocketToMcpAsync()
    │
    ▼
ProcessWebSocketMessageAsync()
    │
    ├─> method: "initialize" ──> HandleInitializeAsync()
    ├─> method: "tools/list" ──> HandleToolsListAsync()
    ├─> method: "tools/call" ──> HandleToolsCallAsync()
    └─> method: "ping" ──────────> HandlePingAsync()
         │
         ▼
    调用 MCP Client API
         │
         ▼
    MCP Server 处理
         │
         ▼
    返回结果
         │
         ▼
    SendWebSocketResponseAsync()
         │
         ▼
    发送到 WebSocket
```

### 3. 重连流程

```
连接断开
    │
    ▼
ConnectWithRetryAsync()
    │
    ├─> 检查最大重试次数
    ├─> 计算退避时间 (exponential + jitter)
    ├─> 等待
    └─> 重新连接
         │
         ├─> 成功 ──> 重置重试计数
         └─> 失败 ──> 增加退避时间，继续重试
```

## 使用场景示例

### 场景1: 单用户多通道

```
User: Alice (ID: user_alice)
    │
    ├─> Endpoint1: "alice_work"
    │   - WebSocket: wss://api.example.com/mcp/alice/work
    │   - 用途: 工作相关的工具调用
    │
    ├─> Endpoint2: "alice_personal"
    │   - WebSocket: wss://api.example.com/mcp/alice/personal
    │   - 用途: 个人项目的工具调用
    │
    └─> Endpoint3: "alice_test"
        - WebSocket: wss://test.example.com/mcp/alice
        - 用途: 测试环境
        - Enabled: false (暂时禁用)
```

### 场景2: 多用户SaaS平台

```
Platform
    │
    ├─> User1 (user_123)
    │   ├─> Endpoint: "user123_main"
    │   └─> Endpoint: "user123_backup"
    │
    ├─> User2 (user_456)
    │   └─> Endpoint: "user456_main"
    │
    └─> User3 (user_789)
        ├─> Endpoint: "user789_us"
        └─> Endpoint: "user789_eu"
```

## 优势

1. **隔离性**: 每个会话独立运行，互不影响
2. **可扩展性**: 动态添加/删除端点，无需重启
3. **容错性**: 单个会话失败不影响其他会话
4. **监控性**: 统一的状态监控和日志记录
5. **灵活性**: 支持不同的MCP服务器、不同的配置

## API示例

```csharp
// 启动时自动加载配置
// appsettings.json 中的所有 enabled=true 的端点会自动启动

// 运行时动态添加端点
var newEndpoint = new McpEndpointConfig
{
    Id = "runtime_endpoint_1",
    Name = "Runtime Added Endpoint",
    WebSocketEndpoint = "wss://api.example.com/mcp/new",
    McpServerUrl = "http://localhost:3001",
    UserId = "new_user",
    Enabled = true
};

var session = await sessionManager.AddEndpointAsync(newEndpoint);

// 查看所有会话
var sessions = sessionManager.GetAllSessions();
foreach (var (id, session) in sessions)
{
    Console.WriteLine($"{id}: {session.IsConnected}");
}

// 获取统计信息
var stats = sessionManager.GetStatistics();
Console.WriteLine($"Total: {stats.TotalSessions}");
Console.WriteLine($"Connected: {stats.ConnectedSessions}");

// 停止特定会话
await sessionManager.StopSessionAsync("runtime_endpoint_1");

// 移除端点
await sessionManager.RemoveEndpointAsync("runtime_endpoint_1");
```
