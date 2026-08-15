# ClassIsland WS Bridge（ClassIsland WebSocket 桥接器）

> 把 [ClassIsland](https://github.com/ClassIsland/ClassIsland) 的课间/上课事件通过 **IPC** 桥接到 **WebSocket**，让任何编程语言（Python、JS、Go……）写的客户端都能实时拿到"下课了/上课了"的课表事件。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%2B-512BD4)](ClassIsland.WSBridge.csproj)

## 为什么需要它

ClassIsland 本身通过 .NET IPC（`dotnetCampus.Ipc`）广播日程事件。想消费这些事件的程序必须用 .NET 并引用 `ClassIsland.Shared.IPC` 包——对 Python / JS 等其他技术栈的开发者很不友好。

这个桥接器是一个**极小的 .NET 控制台程序**：

```
ClassIsland  ──IPC──▶  本桥接器 (Program.exe)  ──WebSocket──▶  你的客户端
  (下课/上课事件)      (订阅事件, 转成文本消息)     ws://localhost:6614/status
```

任何语言只要会连 WebSocket，就能收到 ClassIsland 的课间状态。

## 功能

- ✅ 订阅 ClassIsland 的 **下课（课间开始）** 事件，广播下节课名称
- ✅ 订阅 ClassIsland 的 **上课** 事件
- ✅ WebSocket 广播：`ws://localhost:6614/status`（端口/路径可改，见下方说明）
- ✅ 零业务逻辑，纯转发，稳定可靠
- ✅ 自动连接，无需配置

## 消息协议

广播的消息是**纯文本**，用 `|` 分隔类型与载荷：

| 事件 | 消息格式 | 含义 |
|------|----------|------|
| 下课 | `BreakingTime|<下节课名>` | 课间开始。例如 `BreakingTime|语文` |
| 上课 | `OnClass|None` | 上课开始 |

示例：一个 Python 客户端收到 `BreakingTime|数学`，就可以弹出"下课了，下节课是数学"的提示。

## 环境要求

| 依赖 | 版本 |
|------|------|
| [ClassIsland](https://github.com/ClassIsland/ClassIsland) | 已安装并运行（提供 IPC 服务） |
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0+（仅构建需要；运行时自包含于发布产物） |
| NuGet 包 `ClassIsland.Shared.IPC` | 2.0.3（自动还原） |
| NuGet 包 `WebSocketSharp` | 1.0.3-rc11（自动还原） |
| 操作系统 | Windows（ClassIsland 仅支持 Windows） |

## 构建与运行

```bash
# 1. 构建
dotnet build -c Release

# 2. 运行（先启动 ClassIsland，再启动桥接器）
dotnet run -c Release --no-build
# 或直接运行编译产物：
# bin/Release/net8.0-windows/ClassIsland.WSBridge.exe
```

启动顺序：**先开 ClassIsland，再开桥接器**（桥接器启动时会立刻尝试连接 ClassIsland 的 IPC 服务；连接失败会给出提示并退出）。

看到以下输出即成功：

```
=== ClassIsland WS 桥接器启动中 ===
正在连接 ClassIsland...
IPC 连接成功。
WS 服务已启动: ws://localhost:6614/status
正在运行中... (按 Ctrl+C 退出)
```

## 修改端口 / 路径

编辑 `Program.cs` 顶部的两个常量后重新构建：

```csharp
private const int WsPort = 6614;      // WebSocket 端口
private const string WsPath = "/status"; // WebSocket 路径
```

客户端连接地址相应变为 `ws://localhost:<WsPort><WsPath>`。

## 客户端示例

### Python（websockets）

```python
import asyncio
import websockets

async def main():
    async with websockets.connect("ws://localhost:6614/status") as ws:
        async for raw in ws:
            kind, _, payload = raw.partition("|")
            if kind == "BreakingTime":
                print(f"下课了！下节课：{payload}")
            elif kind == "OnClass":
                print("上课了！")

asyncio.run(main())
```

### JavaScript（浏览器 / Node）

```js
const ws = new WebSocket("ws://localhost:6614/status");
ws.onmessage = (e) => {
  const [kind, payload] = String(e.data).split("|");
  if (kind === "BreakingTime") console.log(`下课了！下节课：${payload}`);
  if (kind === "OnClass") console.log("上课了！");
};
```

## 常见问题

**Q: 桥接器启动时报 "IPC 连接失败"？**
A: 请确认 ClassIsland 正在运行且版本较新（旧版本可能没有对应的 IPC 服务）。先启动 ClassIsland，再启动桥接器。

**Q: 客户端连不上 `ws://localhost:6614/status`？**
A: 确认桥接器窗口显示 "WS 服务已启动"。若本机防火墙拦截 6614 端口，请放行（仅本机使用可不放行）。

**Q: 收不到下课事件？**
A: 桥接器只有在下课/上课**发生的那一刻**广播，不会补发历史状态。若桥接器启动时已经在上课，需等下一次事件。需要"当前状态查询"的客户端，请在桥接器侧自行扩展（欢迎提 PR）。

## 集成案例

本项目最初是为家校沟通系统 **ClassBridge** 的教室客户端开发的：客户端用 Qt 常驻任务栏，收到课间事件后自动弹出课表提示，并将"是否处于课间"上报给服务器，让家长群的机器人回复更智能（上课中收到的消息提示"学生将在课间查看"）。

## 致谢

- [ClassIsland](https://github.com/ClassIsland/ClassIsland) — 开源课表软件，提供 IPC 事件与 `ClassIsland.Shared.IPC` 包
- [dotnetCampus.Ipc](https://github.com/dotnet-campus/dotnetCampus.Ipc) — 跨进程通信库
- [websocket-sharp](https://github.com/sta/websocket-sharp) — WebSocket 服务器库

## License

[MIT](LICENSE) © 2026 Zn-Jiang

本软件依赖 [ClassIsland.Shared.IPC](https://github.com/ClassIsland/ClassIsland)（LGPL-3.0-only）等第三方组件，详见 [NOTICE](NOTICE)。
