using System;
using System.Threading;
using ClassIsland.Shared.IPC;
using ClassIsland.Shared.IPC.Abstractions.Services;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using dotnetCampus.Ipc.Pipes;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace ClassIsland.WSBridge;

// 定义一个 WebSocket 行为（这里不需要写逻辑，主要利用服务器的广播功能）
public class BridgeBehavior : WebSocketBehavior
{
}

class Program
{
    private const int WsPort = 6614;
    private const string WsPath = "/status";

    private static WebSocketServer? _server;
    private static volatile bool _running = true;

    static void Main(string[] args)
    {
        Console.WriteLine("=== ClassIsland WS 桥接器启动中 ===");

        // 处理 Ctrl+C / Ctrl+Break 优雅退出
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            _running = false;
            Console.WriteLine("\n正在退出...");
        };

        // 1. 创建 IPC 客户端实例
        var ipcClient = new IpcClient();

        // 2. 【关键】先订阅事件，再连接！
        // 订阅下课事件
        ipcClient.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnBreakingTimeNotifyId, () =>
        {
            try
            {
                // 获取课程服务代理
                var lessonSc = ipcClient.Provider.CreateIpcProxy<IPublicLessonsService>(ipcClient.PeerProxy!);
                var nextSubject = lessonSc.NextClassSubject?.Name ?? "未知科目";

                string msg = $"BreakingTime|{nextSubject}";
                Console.WriteLine($"[事件] 下课了！下节课是: {nextSubject}");

                // 向所有连接的 Python 客户端广播
                BroadcastToPython(msg);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[事件] 下课事件处理失败: {ex.Message}");
            }
        });

        // 3. 订阅上课事件（可选，如果你以后想用的话）
        ipcClient.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnClassNotifyId, () =>
        {
            Console.WriteLine("[事件] 上课了！");
            BroadcastToPython("OnClass|None");
        });

        // 4. 启动 IPC 连接
        Console.WriteLine("正在连接 ClassIsland...");
        try
        {
            ipcClient.Connect().Wait();
            Console.WriteLine("IPC 连接成功。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"IPC 连接失败: {ex.Message}");
            Console.WriteLine("请确认 ClassIsland 已启动，然后重新运行本桥接器。");
            return;
        }

        // 5. 启动 WebSocket 服务器 (端口 6614)
        var wssv = new WebSocketServer(WsPort);
        wssv.AddWebSocketService<BridgeBehavior>(WsPath);
        _server = wssv; // 存一下引用以便广播
        wssv.Start();

        Console.WriteLine($"WS 服务已启动: ws://localhost:{WsPort}{WsPath}");
        Console.WriteLine("正在运行中... (按 Ctrl+C 退出)");

        // 保持程序不退出，直到收到退出信号
        while (_running)
        {
            Thread.Sleep(200);
        }

        try
        {
            wssv.Stop();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"清理资源时出错: {ex.Message}");
        }
        Console.WriteLine("桥接器已退出。");
    }

    private static void BroadcastToPython(string message)
    {
        if (_server != null)
        {
            // 向所有连接到 /status 路径的客户端发送消息
            _server.WebSocketServices[WsPath].Sessions.Broadcast(message);
        }
    }
}
