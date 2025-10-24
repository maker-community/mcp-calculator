# MCP Platform Client - 多端点平台架构

一个支持多WebSocket连接通道的MCP（Model Context Protocol）平台客户端。

## 🌟 主要特性

- **多端点支持**：一个用户可以管理和绑定多个WebSocket连接端点
- **会话管理**：每个端点独立运行，互不干扰
- **自动重连**：支持指数退避的自动重连机制
- **动态管理**：支持运行时动态添加/删除端点
- **状态监控**：实时监控所有会话的连接状态和统计信息

## 📋 架构设计

### 核心组件

```
McpPlatformService (主服务)
    └── McpSessionManager (会话管理器)
        ├── McpSessionService (会话1)
        ├── McpSessionService (会话2)
        └── McpSessionService (会话N)
```

### 类说明

1. **McpEndpointConfig**: 单个端点的配置信息
   - ID、名称、WebSocket地址、MCP服务器地址
   - 用户ID、启用状态、元数据

2. **McpPlatformConfig**: 平台级配置
   - 端点列表
   - 默认MCP服务器地址
   - 重连设置

3. **McpSessionService**: 单个会话服务
   - 管理一个WebSocket连接
   - 处理MCP协议消息
   - 自动重连机制

4. **McpSessionManager**: 会话管理器
   - 管理多个会话实例
   - 提供会话生命周期管理
   - 统计信息收集

5. **McpPlatformService**: 平台主服务
   - 托管服务，启动和停止所有会话
   - 定期记录统计信息

## 🚀 快速开始

### 1. 配置端点

编辑 `appsettings.json` 文件：

```json
{
  "McpPlatform": {
    "DefaultMcpServerUrl": "http://localhost:3001",
    "Reconnection": {
      "InitialBackoffMs": 1000,
      "MaxBackoffMs": 600000,
      "MaxAttempts": 0
    },
    "Endpoints": [
      {
        "Id": "xiaozhi_agent_111757",
        "Name": "Xiaozhi Agent",
        "WebSocketEndpoint": "wss://api.xiaozhi.me/mcp/?token=YOUR_TOKEN",
        "McpServerUrl": "http://localhost:3001",
        "UserId": "184458",
        "Enabled": true
      }
    ]
  }
}
```

### 2. 运行程序

```bash
dotnet run
```

### 3. 添加更多端点

只需在配置文件的 `Endpoints` 数组中添加新条目：

```json
{
  "Id": "user123_endpoint2",
  "Name": "Secondary Connection",
  "WebSocketEndpoint": "wss://api.example.com/mcp/?token=TOKEN2",
  "McpServerUrl": "http://localhost:3002",
  "UserId": "user123",
  "Enabled": true,
  "Metadata": {
    "Region": "EU",
    "Priority": "High"
  }
}
```

## 📖 配置说明

### 端点配置 (McpEndpointConfig)

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| Id | string | ✅ | 端点唯一标识符 |
| Name | string | ✅ | 端点显示名称 |
| WebSocketEndpoint | string | ✅ | WebSocket连接地址 |
| McpServerUrl | string | ❌ | MCP服务器地址（可使用默认值） |
| UserId | string | ❌ | 所属用户ID |
| Enabled | bool | ❌ | 是否启用（默认true） |
| Metadata | object | ❌ | 自定义元数据 |

### 重连配置 (ReconnectionSettings)

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| InitialBackoffMs | int | 1000 | 初始退避时间（毫秒） |
| MaxBackoffMs | int | 600000 | 最大退避时间（毫秒） |
| MaxAttempts | int | 0 | 最大重连次数（0=无限） |

## 🔧 使用场景

### 场景1: 多用户平台

```json
{
  "Endpoints": [
    {
      "Id": "user1_main",
      "Name": "User1 Main",
      "WebSocketEndpoint": "wss://api.example.com/mcp/user1",
      "UserId": "user1",
      "Enabled": true
    },
    {
      "Id": "user2_main",
      "Name": "User2 Main",
      "WebSocketEndpoint": "wss://api.example.com/mcp/user2",
      "UserId": "user2",
      "Enabled": true
    }
  ]
}
```

### 场景2: 一个用户多个通道

```json
{
  "Endpoints": [
    {
      "Id": "user1_high_priority",
      "Name": "High Priority Channel",
      "WebSocketEndpoint": "wss://api.example.com/mcp/user1/high",
      "UserId": "user1",
      "Metadata": { "Priority": "High" }
    },
    {
      "Id": "user1_low_priority",
      "Name": "Low Priority Channel",
      "WebSocketEndpoint": "wss://api.example.com/mcp/user1/low",
      "UserId": "user1",
      "Metadata": { "Priority": "Low" }
    }
  ]
}
```

### 场景3: 多区域部署

```json
{
  "Endpoints": [
    {
      "Id": "us_east",
      "Name": "US East Region",
      "WebSocketEndpoint": "wss://us-east.example.com/mcp",
      "McpServerUrl": "http://localhost:3001",
      "Metadata": { "Region": "US-East" }
    },
    {
      "Id": "eu_west",
      "Name": "EU West Region",
      "WebSocketEndpoint": "wss://eu-west.example.com/mcp",
      "McpServerUrl": "http://localhost:3002",
      "Metadata": { "Region": "EU-West" }
    }
  ]
}
```

## 📊 监控和日志

程序会定期（每分钟）输出会话统计信息：

```
Session Statistics: Total=3, Connected=2, Disconnected=1, TotalReconnects=5
  Session user1_endpoint1 (User1 Primary): Connected, Connected at 10:30:45, Reconnects=0
  Session user1_endpoint2 (User1 Secondary): Connected, Connected at 10:31:20, Reconnects=2
  Session user2_endpoint1 (User2 Primary): Disconnected, Disconnected at 10:32:10, Reconnects=3
```

## 🔐 安全建议

1. **Token管理**: 不要在代码中硬编码token，使用环境变量或密钥管理服务
2. **HTTPS/WSS**: 生产环境必须使用加密连接
3. **最小权限**: 为每个端点分配最小必要权限
4. **定期轮换**: 定期更新WebSocket连接token

## 🛠️ 开发和扩展

### 动态管理端点（编程方式）

```csharp
// 获取会话管理器
var sessionManager = platformService.GetSessionManager();

// 添加新端点
var newEndpoint = new McpEndpointConfig
{
    Id = "dynamic_endpoint_1",
    Name = "Dynamic Endpoint",
    WebSocketEndpoint = "wss://api.example.com/mcp/dynamic",
    McpServerUrl = "http://localhost:3001",
    Enabled = true
};

await sessionManager.AddEndpointAsync(newEndpoint);

// 停止特定端点
await sessionManager.StopSessionAsync("dynamic_endpoint_1");

// 移除端点
await sessionManager.RemoveEndpointAsync("dynamic_endpoint_1");

// 获取统计信息
var stats = sessionManager.GetStatistics();
Console.WriteLine($"Total Sessions: {stats.TotalSessions}");
Console.WriteLine($"Connected: {stats.ConnectedSessions}");
```

## 📝 与原版的区别

### 原版 (McpPipeService)
- 单一WebSocket连接
- 硬编码的配置方式
- 一次只能处理一个端点

### 新版 (McpPlatformService)
- 支持多个WebSocket连接
- 灵活的配置文件管理
- 每个端点独立运行
- 可以动态添加/删除端点
- 统一的监控和管理

## 🏗️ 项目结构

```
McpPipeClient/
├── Models/
│   ├── McpEndpointConfig.cs      # 端点配置模型
│   └── McpPlatformConfig.cs      # 平台配置模型
├── Services/
│   ├── McpSessionService.cs      # 单个会话服务
│   └── McpSessionManager.cs      # 会话管理器
├── McpPlatformService.cs         # 平台主服务
├── Program.cs                    # 程序入口
├── appsettings.json              # 配置文件
└── appsettings.example.json      # 配置示例
```

## 🤝 贡献

欢迎提交Issue和Pull Request！

## 📄 许可

MIT License
