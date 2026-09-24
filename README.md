# ClassIsland WS Bridge（ClassIsland WebSocket 桥接器）

> 把 [ClassIsland](https://github.com/ClassIsland/ClassIsland) 的课间/上课事件通过 **IPC** 桥接到 **WebSocket**，让任何编程语言（Python、JS、Go……）写的客户端都能实时拿到"下课了/上课了"的课表事件。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%2B-512BD4)](ClassIsland.WSBridge.csproj)

## 为什么需要它

ClassIsland 本身通过 .NET IPC（`dotnetCampus.Ipc`）广播日程事件。想消费这些事件的程序必须用 .NET 并引用 `ClassIsland.Shared.IPC` 包。由于 Python / JS 等其他技术栈调用 .NET 的能力较弱，因此 IPC 的形式对开发者不太友好。

这个桥接器是一个**极小的 .NET 控制台程序**：

```
ClassIsland  ──IPC──▶  本桥接器 (Program.exe)  ──WebSocket──▶  你的客户端
  (下课/上课事件)      (订阅事件, 转成文本消息)     ws://localhost:6614/status
```

任何语言只要会连 WebSocket，就能收到 ClassIsland 的课间状态。

## 通信协议与接口

服务端连接地址为：`ws://localhost:6614/`

客户端可以通过发送纯字符串或 JSON 格式命令与服务端交互。

### 1. 查询可调用的功能清单 (`capabilities`)

**发送请求：**
```json
"capabilities"
// 或
{"action": "capabilities"}
```

**响应示例：**

```json
{
  "type": "capabilities",
  "data": {
    "课程服务": [
      "IsTimerRunning",
      "CurrentSubject",
      "NextClassSubject",
      "CurrentState",
      "CurrentSelectedIndex",
      "OnClassLeftTime",
      "OnBreakingTimeLeftTime",
      "IsClassPlanEnabled",
      "IsClassPlanLoaded",
      "IsLessonConfirmed",
      "CurrentTimeLayoutItem",
      "CurrentClassPlan",
      "NextBreakingTimeLayoutItem",
      "NextClassTimeLayoutItem"
    ],
    "订阅": [
      "OnClassNotifyId",
      "OnBreakingTimeNotifyId"
    ]
  }
}

```

---

### 2. 获取课程属性 (`get_properties`)

提供两种调用方式：

#### 方式 A：全量获取（获取所有属性）

**发送请求：**

```json
"get_properties"
// 或
{"action": "get_properties"}
```

#### 方式 B：按需筛选获取（指定 `keys` 数组）

**发送请求：**

```json
{
  "action": "get_properties",
  "keys": ["CurrentSubject", "OnClassLeftTime", "CurrentState"]
}

```

**响应示例：**

```json
{
  "type": "properties",
  "data": {
    "CurrentSubject": "自习",
    "OnClassLeftTime": "00:00:00",
    "CurrentState": "OnClass"
  }
}

```

---

### 3. 可用属性说明列表

| 属性 Key | 数据类型 | 描述 |
| --- | --- | --- |
| `IsTimerRunning` | `bool` | 主倒计时定时器是否正在运行 |
| `CurrentSubject` | `string` | 当前课程名称（如“语文”、“自习”） |
| `NextClassSubject` | `string` | 下节课程名称 |
| `CurrentState` | `string` | 当前课程状态（如 `OnClass`, `BreakingTime`） |
| `CurrentSelectedIndex` | `int` | 当前课表中选中的节点序号 |
| `OnClassLeftTime` | `string` | 上课剩余时间（`HH:mm:ss`） |
| `OnBreakingTimeLeftTime` | `string` | 课间剩余时间（`HH:mm:ss`） |
| `IsClassPlanEnabled` | `bool` | 当前课表计划是否已启用 |
| `IsClassPlanLoaded` | `bool` | 课表计划是否已加载完成 |
| `IsLessonConfirmed` | `bool` | 课程调课/临时安排是否已确认 |
| `CurrentTimeLayoutItem` | `object` | 当前时间点对应的完整布局对象 |
| `CurrentClassPlan` | `object` | 当前生效的完整课表计划数据对象 |
| `NextBreakingTimeLayoutItem` | `object` | 下一次课间休息的时间布局数据对象 |
| `NextClassTimeLayoutItem` | `object` | 下一节课的时间布局数据对象 |

---

### 4. 实时事件推送（服务端主动广播）

当 ClassIsland 状态发生改变时，服务端会自动推送广播消息（无需客户端主动请求）：

```json
{"type": "event", "eventName": "OnClassNotifyId"}
{"type": "event", "eventName": "OnBreakingTimeNotifyId"}
```

---

## Python 调用示范

```python
import asyncio
import json
import websockets

async def test_websocket_bridge():
    uri = "ws://localhost:6614/"
    
    async with websockets.connect(uri) as websocket:
        print("连接服务端成功！\n")

        # 1. 查询接口功能清单
        print("--- 1. 调用 capabilities 接口 ---")
        await websocket.send(json.dumps({"action": "capabilities"}))
        res = await websocket.recv()
        print("响应:", json.loads(res))

        # 2. 全量读取所有属性
        print("\n--- 2. 全量读取课程属性 ---")
        await websocket.send(json.dumps({"action": "get_properties"}))
        res = await websocket.recv()
        print("全量属性响应:", json.loads(res))

        # 3. 按需读取指定属性
        print("\n--- 3. 按需指定读取 (CurrentSubject, OnClassLeftTime) ---")
        req_body = {
            "action": "get_properties",
            "keys": ["CurrentSubject", "OnClassLeftTime", "CurrentState"]
        }
        await websocket.send(json.dumps(req_body))
        res = await websocket.recv()
        print("筛选属性响应:", json.loads(res))

        # 4. 进入实时事件监听状态
        print("\n--- 4. 开始监听服务端实时事件推送 ---")
        async for msg in websocket:
            data = json.loads(msg)
            if data.get("type") == "event":
                print(f"[收到实时广播事件] -> {data.get('eventName')}")

if __name__ == "__main__":
    asyncio.run(test_websocket_bridge())
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
