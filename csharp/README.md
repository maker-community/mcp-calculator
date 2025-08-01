# MCP Calculator - C# Implementation

A Model Context Protocol (MCP) implementation in C# demonstrating calculator functionality with server-client architecture.

## 项目结构

该项目包含两个主要组件：

- **MCPSseServer**: MCP服务器，提供计算器工具和echo工具
- **McpPipeClient**: MCP客户端，连接到WebSocket端点并与MCP服务器通信

## 技术栈

- **.NET 9.0**: 最新的.NET框架
- **ASP.NET Core**: Web应用程序框架
- **ModelContextProtocol**: MCP协议的C#实现
- **WebSockets**: 客户端与外部系统的通信
- **Microsoft.Extensions.Hosting**: 后台服务托管

## 功能特性

### MCPSseServer

1. **Calculator Tool**: 数学表达式计算工具
   - 支持基本数学运算 (+, -, *, /, ^)
   - 支持数学函数 (sqrt, pow, abs)
   - 支持数学常量 (π, e)
   - Python表达式到C#表达式的转换

2. **Echo Tool**: 简单的回显工具
   - 接收消息并返回带有"hello"前缀的响应

### McpPipeClient

1. **WebSocket连接管理**
   - 自动重连机制
   - 指数退避策略
   - 连接状态监控

2. **MCP协议处理**
   - 双向消息传递
   - JSON消息序列化/反序列化
   - 错误处理和日志记录

## 快速开始

### 前置要求

- .NET 9.0 SDK
- Visual Studio 2022 或 VS Code

### 运行服务器

1. 导航到服务器目录：
```bash
cd MCPSseServer
```

2. 运行服务器：
```bash
dotnet run
```

服务器将在 `http://localhost:3001` 启动。

### 运行客户端

1. 导航到客户端目录：
```bash
cd McpPipeClient
```

2. 配置环境变量或修改 `appsettings.json`：
```json
{
  "MCP_SERVER_URL": "http://localhost:3001",
  "MCP_ENDPOINT": "your_websocket_endpoint_here"
}
```

3. 运行客户端：
```bash
dotnet run
```

## 配置说明

### 服务器配置

服务器配置在 `MCPSseServer/appsettings.json` 中：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### 客户端配置

客户端配置在 `McpPipeClient/appsettings.json` 中：

```json
{
  "MCP_SERVER_URL": "http://localhost:3001",
  "MCP_ENDPOINT": "your_websocket_endpoint"
}
```

也可以通过环境变量配置：
- `MCP_SERVER_URL`: MCP服务器地址
- `MCP_ENDPOINT`: WebSocket端点地址

## 工具使用示例

### Calculator Tool

Calculator工具支持各种数学表达式：

```python
# 基本运算
2 + 3 * 4

# 幂运算
2 ** 3

# 数学函数
math.sqrt(16)
math.pow(2, 3)
math.abs(-5)

# 数学常量
math.pi * 2
math.e ** 2
```

### Echo Tool

Echo工具简单地返回带前缀的消息：

```
输入: "world"
输出: "hello world"
```

## 项目架构

### 服务器架构

```
MCPSseServer/
├── Program.cs              # 应用程序入口点
├── Tools/
│   ├── CalculatorTool.cs   # 计算器工具实现
│   └── EchoTool.cs         # Echo工具实现
├── appsettings.json        # 应用配置
└── Properties/
    └── launchSettings.json # 启动配置
```

### 客户端架构

```
McpPipeClient/
├── Program.cs              # 应用程序入口点
├── McpPipeService.cs       # MCP管道服务实现
├── appsettings.json        # 应用配置
└── README.md               # 客户端说明文档
```

## 开发指南

### 添加新工具

1. 在 `MCPSseServer/Tools/` 目录下创建新的工具类
2. 使用 `[McpServerToolType]` 特性标记类
3. 使用 `[McpServerTool]` 和 `[Description]` 特性标记方法
4. 工具会自动被 `WithToolsFromAssembly()` 发现并注册

示例：
```csharp
[McpServerToolType]
public sealed class MyTool
{
    [McpServerTool, Description("Tool description")]
    public static string MyMethod(string input)
    {
        return $"Processed: {input}";
    }
}
```

### 错误处理

项目包含完善的错误处理机制：

- 服务器端工具异常会被捕获并返回错误响应
- 客户端连接失败会自动重试
- 所有关键操作都有日志记录

### 日志配置

可以通过修改 `appsettings.json` 调整日志级别：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "ModelContextProtocol": "Debug",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## 依赖包

### 服务器依赖

- `ModelContextProtocol` (0.3.0-preview.3)
- `ModelContextProtocol.AspNetCore` (0.3.0-preview.3)
- `Microsoft.Extensions.Hosting` (9.0.4)
- `Microsoft.Extensions.Logging` (9.0.4)

### 客户端依赖

- `ModelContextProtocol` (0.3.0-preview.3)
- `Microsoft.Extensions.Hosting` (9.0.4)
- `Microsoft.Extensions.Configuration` (9.0.4)
- `System.Net.WebSockets.Client` (4.3.2)
- `System.Text.Json` (9.0.4)

## 故障排除

### 常见问题

1. **路由冲突错误**
   - 确保没有重复的路由映射
   - `app.MapMcp()` 已经处理了根路径

2. **连接失败**
   - 检查服务器是否正在运行
   - 验证端口配置是否正确
   - 确认防火墙设置

3. **WebSocket连接问题**
   - 验证 `MCP_ENDPOINT` 配置
   - 检查WebSocket服务器状态
   - 查看客户端日志了解详细错误信息

### 调试技巧

1. 启用详细日志记录
2. 使用Visual Studio调试器
3. 检查网络连接和端口占用
4. 验证JSON消息格式

## 许可证

本项目采用MIT许可证 - 详情请查看LICENSE文件。

## 贡献

欢迎提交问题和改进建议！
