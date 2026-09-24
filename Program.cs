using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dotnetCampus.Ipc;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using ClassIsland.Shared.IPC;
using ClassIsland.Shared.IPC.Abstractions.Services;

namespace ClassIslandWebSocketBridge
{
    public class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
            WriteIndented = false
        };

        private static IPublicLessonsService? _lessonsService;
        private static readonly List<WebSocket> ActiveClients = new();

        public static async Task Main(string[] args)
        {
            // 解决 ClassIsland 内部 System.Text.Json 8.0.0.5 硬编码版本加载依赖问题
            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
            {
                if (eventArgs.Name.StartsWith("System.Text.Json"))
                {
                    return typeof(System.Text.Json.JsonSerializer).Assembly;
                }
                return null;
            };

            var client = new IpcClient();

            // 订阅上课与下课事件，必须在 client.Connect() 之前注册
            client.JsonIpcProvider.AddNotifyHandler(
                IpcRoutedNotifyIds.OnClassNotifyId,
                () => BroadcastEvent("OnClassNotifyId")
            );

            client.JsonIpcProvider.AddNotifyHandler(
                IpcRoutedNotifyIds.OnBreakingTimeNotifyId,
                () => BroadcastEvent("OnBreakingTimeNotifyId")
            );

            // 连接到 ClassIsland IPC
            await client.Connect();

            // 获取远程公开课程服务对象
            _lessonsService = client.Provider.CreateIpcProxy<IPublicLessonsService>(client.PeerProxy);

            // 启动 WebSocket 服务器 (6614 端口)
            _ = Task.Run(() => StartWebSocketServerAsync("http://localhost:6614/"));

            Console.WriteLine("ClassIsland WebSocket 桥接服务 v2.0 已启动在 ws://localhost:6614/");
            await Task.Delay(-1);
        }

        private static async Task StartWebSocketServerAsync(string prefix)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();

            while (true)
            {
                var context = await listener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    var wsContext = await context.AcceptWebSocketAsync(null);
                    _ = HandleClientAsync(wsContext.WebSocket);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }

        private static async Task HandleClientAsync(WebSocket webSocket)
        {
            lock (ActiveClients) { ActiveClients.Add(webSocket); }
            var buffer = new byte[4096];

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count).Trim();
                    await ProcessMessageAsync(webSocket, message);
                }
            }
            catch
            {
                // 忽略断开异常
            }
            finally
            {
                lock (ActiveClients) { ActiveClients.Remove(webSocket); }
            }
        }

        private static async Task ProcessMessageAsync(WebSocket webSocket, string message)
        {
            try
            {
                string command = message.Trim();
                List<string>? requestedKeys = null;

                if (command.StartsWith("{") && command.EndsWith("}"))
                {
                    using var doc = JsonDocument.Parse(command);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("action", out var actionElem)) command = actionElem.GetString() ?? command;
                    else if (root.TryGetProperty("command", out var cmdElem)) command = cmdElem.GetString() ?? command;

                    // 解析指定的 keys 筛选列表
                    if (root.TryGetProperty("keys", out var keysElem) && keysElem.ValueKind == JsonValueKind.Array)
                    {
                        requestedKeys = new List<string>();
                        foreach (var item in keysElem.EnumerateArray())
                        {
                            if (item.GetString() is string keyStr) requestedKeys.Add(keyStr);
                        }
                    }
                }

                if (command.Equals("capabilities", StringComparison.OrdinalIgnoreCase))
                {
                    var capabilities = GetCapabilities();
                    await SendJsonAsync(webSocket, new { type = "capabilities", data = capabilities });
                }
                else if (command.Equals("get_properties", StringComparison.OrdinalIgnoreCase))
                {
                    var properties = GetLessonsProperties(requestedKeys);
                    await SendJsonAsync(webSocket, new { type = "properties", data = properties });
                }
                else
                {
                    await SendJsonAsync(webSocket, new { type = "error", message = "未知命令，支持命令: capabilities, get_properties" });
                }
            }
            catch (Exception ex)
            {
                await SendJsonAsync(webSocket, new { type = "error", message = $"服务端处理异常: {ex.Message}" });
            }
        }

        public static Dictionary<string, List<string>> GetCapabilities()
        {
            return new Dictionary<string, List<string>>
            {
                ["课程服务"] = new List<string>
                {
                    "IsTimerRunning",
                    "CurrentSubject",
                    "NextClassSubject",
                    "CurrentState",
                    "CurrentTimeLayoutItem",
                    "CurrentClassPlan",
                    "NextBreakingTimeLayoutItem",
                    "NextClassTimeLayoutItem",
                    "CurrentSelectedIndex",
                    "OnClassLeftTime",
                    "OnBreakingTimeLeftTime",
                    "IsClassPlanEnabled",
                    "IsClassPlanLoaded",
                    "IsLessonConfirmed"
                },
                ["订阅"] = new List<string>
                {
                    "OnClassNotifyId",
                    "OnBreakingTimeNotifyId"
                }
            };
        }

        /// <summary>
        /// 读取 IPublicLessonsService 属性（支持全量或按 keys 筛选）
        /// </summary>
        public static Dictionary<string, object?> GetLessonsProperties(List<string>? requestedKeys = null)
        {
            if (_lessonsService == null) return new Dictionary<string, object?>();

            var getters = new Dictionary<string, Func<object?>>
            {
                ["IsTimerRunning"] = () => _lessonsService.IsTimerRunning,
                ["CurrentSubject"] = () => _lessonsService.CurrentSubject?.Name,
                ["NextClassSubject"] = () => _lessonsService.NextClassSubject?.Name,
                ["CurrentState"] = () => _lessonsService.CurrentState.ToString(),
                ["CurrentSelectedIndex"] = () => _lessonsService.CurrentSelectedIndex,
                ["OnClassLeftTime"] = () => _lessonsService.OnClassLeftTime.ToString(),
                ["OnBreakingTimeLeftTime"] = () => _lessonsService.OnBreakingTimeLeftTime.ToString(),
                ["IsClassPlanEnabled"] = () => _lessonsService.IsClassPlanEnabled,
                ["IsClassPlanLoaded"] = () => _lessonsService.IsClassPlanLoaded,
                ["IsLessonConfirmed"] = () => _lessonsService.IsLessonConfirmed,
                ["CurrentTimeLayoutItem"] = () => _lessonsService.CurrentTimeLayoutItem,
                ["CurrentClassPlan"] = () => _lessonsService.CurrentClassPlan,
                ["NextBreakingTimeLayoutItem"] = () => _lessonsService.NextBreakingTimeLayoutItem,
                ["NextClassTimeLayoutItem"] = () => _lessonsService.NextClassTimeLayoutItem
            };

            var result = new Dictionary<string, object?>();
            bool isFilter = requestedKeys != null && requestedKeys.Count > 0;

            foreach (var pair in getters)
            {
                // 若不筛选，或指定了对应的 key（忽略大小写），则读取
                if (!isFilter || requestedKeys!.Exists(k => k.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    result[pair.Key] = TryRead(pair.Value);
                }
            }

            return result;
        }

        private static object? TryRead(Func<object?> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception ex)
            {
                return $"[读取失败: {ex.Message}]";
            }
        }

        private static void BroadcastEvent(string eventName)
        {
            var json = JsonSerializer.Serialize(new { type = "event", eventName }, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            lock (ActiveClients)
            {
                foreach (var client in ActiveClients)
                {
                    if (client.State == WebSocketState.Open)
                    {
                        client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
            }
        }

        private static async Task SendJsonAsync(WebSocket webSocket, object data)
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}
