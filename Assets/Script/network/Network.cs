
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using Google.Protobuf;
using Net.Proto;
using System.Net.Sockets.Kcp.Simple;

/// <summary>
/// KCP + Protobuf 网络客户端（Unity主线程安全、可取消、可复连、带心跳与延迟测量）
///
/// 兼容原有接口：
/// - public static Network _Instance;
/// - public string SessionID;
/// - public void init(string server, int port);
/// - public void PackAndSend(CmdID cmdID, byte[] data);
/// - public void AddHandleFunc(CmdID cmdID, HandleFunc fun);
/// - public string GetDelay();
/// - public void CloseConn();
/// - public delegate void HandleFunc(CmdID cmdID, byte[] msg);
/// </summary>
public class Network : MonoBehaviour
{
    public static Network _Instance;

    public delegate void HandleFunc(CmdID cmdID, byte[] msg);

    // === Public state ===
    public string SessionID = string.Empty;

    // === Config (set via NetControl.init or Inspector) ===
    [SerializeField] private string ServerAddr = "127.0.0.1";
    [SerializeField] private int ServerPort = 22101;

    // === KCP ===
    public SimpleKcpClient client;
    private IPEndPoint _remoteEndPoint;

    // === Tasks & cancellation ===
    private CancellationTokenSource _cts;
    private Task _kcpUpdateTask;
    private Task _recvTask;
    private Task _heartbeatTask;

    // === Dispatch to main thread ===
    private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

    // === Handlers ===
    private readonly ConcurrentDictionary<CmdID, HandleFunc> _handlers = new ConcurrentDictionary<CmdID, HandleFunc>();

    // === Delay/Ping ===
    private ulong _lastPingSendMill;
    private ulong _lastPingRecvMill;
    private ulong _delayMill;

    // === Lifecycle ===

    private void Awake()
    {
        // 确保全局单例
        if (_Instance != null && _Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        _Instance = this;
        DontDestroyOnLoad(this.gameObject);
    }

    private void OnDestroy()
    {
        CloseConn();
    }

    private void Update()
    {
        // 在主线程中分发网络事件（回调一定在主线程执行，避免Unity API跨线程调用）
        while (_mainThreadActions.TryDequeue(out var action))
        {
            try { action?.Invoke(); } catch (Exception ex) { Debug.LogException(ex); }
        }
    }

    /// <summary>
    /// 供外部调用：设置服务器地址并建立连接
    /// </summary>
    public void init(string server, int port)
    {
        ServerAddr = server;
        ServerPort = port;
        Init();
    }

    private void Init()
    {
        // 清理旧连接
        CloseConn();

        _cts = new CancellationTokenSource();

        try
        {
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ServerAddr), ServerPort);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Network] 无效的服务器地址: {ServerAddr}:{ServerPort} - {ex.Message}");
            return;
        }

        client = new SimpleKcpClient(0, _remoteEndPoint);

        // 启动KCP update循环（10ms）
        _kcpUpdateTask = Task.Run(async () =>
        {
            var token = _cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    client.kcp.Update(DateTimeOffset.UtcNow);
                    await Task.Delay(10, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex) { Debug.LogException(ex); }
        }, _cts.Token);

        // 启动接收循环
        _recvTask = Task.Run(async () =>
        {
            var token = _cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var resp = await client.ReceiveAsync().ConfigureAwait(false);
                    if (resp == null || resp.Length == 0)
                    {
                        await Task.Delay(10, token).ConfigureAwait(false);
                        continue;
                    }

                    try
                    {
                        var packet = Packet.Parser.ParseFrom(resp);
                        // 处理Greeting / Session / Ping
                        if (packet.CmdID == (uint)CmdID.CmdIDGreeting)
                        {
                            SessionID = packet.SessionID;
                            if (_lastPingSendMill != 0 && _lastPingRecvMill == 0)
                            {
                                _lastPingRecvMill = (ulong)Utils.GetUnixMill();
                                _delayMill = _lastPingRecvMill - _lastPingSendMill;
                            }
                            // 仍然分发给业务，若有需要
                            EnqueueOnMainThread(() => Handle((CmdID)packet.CmdID, packet.Msg.ToByteArray()));
                            continue;
                        }

                        // 其他消息投递给主线程处理
                        var cmd = (CmdID)packet.CmdID;
                        var payload = packet.Msg.ToByteArray();
                        EnqueueOnMainThread(() => Handle(cmd, payload));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Network] 收包解析失败：{ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex) { Debug.LogException(ex); }
        }, _cts.Token);

        // 心跳/延迟测量循环（3s）
        _heartbeatTask = Task.Run(async () =>
        {
            var token = _cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var greeting = new Greeting
                    {
                        Type = GreetingType.PingServer,
                        Delay = _delayMill,
                        Msg = "PING"
                    };
                    _lastPingRecvMill = 0;
                    _lastPingSendMill = (ulong)Utils.GetUnixMill();
                    PackAndSend(CmdID.CmdIDGreeting, greeting.ToByteArray());
                    await Task.Delay(3000, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex) { Debug.LogException(ex); }
        }, _cts.Token);

        // 首次建立连接后请求会话
        var greetingCreate = new Greeting
        {
            Type = GreetingType.CreateSession,
            Msg = "Hello Server! Request a Session!"
        };
        PackAndSend(CmdID.CmdIDGreeting, greetingCreate.ToByteArray());
    }

    // === Public API ===

    public void AddHandleFunc(CmdID cmdID, HandleFunc fun)
    {
        _handlers.AddOrUpdate(cmdID, fun, (k, old) => fun);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Network] 注册处理器: {cmdID}");
#endif
    }

    public void PackAndSend(CmdID cmdID, byte[] data)
    {
        if (client == null)
        {
            Debug.LogWarning("[Network] 尚未连接，丢弃发送包");
            return;
        }

        try
        {
            var packet = new Packet
            {
                CmdID = (uint)cmdID,
                CmdLen = (uint)(data?.Length ?? 0),
                Msg = ByteString.CopyFrom(data ?? Array.Empty<byte>()),
                SessionID = SessionID ?? string.Empty,
                SendTimeStampMill = (ulong)Utils.GetUnixMill()
            };

            client.SendAsync(packet.ToByteArray(), packet.ToByteArray().Length);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Network] 发送失败: {ex.Message}");
        }
    }

    public string GetDelay()
    {
        return _delayMill.ToString();
    }

    public void CloseConn()
    {
        // 幂等
        if (_cts == null) return;

        try
        {
            // 通知服务端会话结束
            var pkg = new SessionEndNotify
            {
                CloseType = SessionCloseType.ClientClose,
                Msg = "Bye Bye!"
            };
            PackAndSend(CmdID.CmdIDSessionEndNotify, pkg.ToByteArray());
        }
        catch { /* ignore */ }

        try { _cts.Cancel(); } catch { }
        try
        {
            Task.WaitAll(new[] { _kcpUpdateTask, _recvTask, _heartbeatTask }, 200);
        }
        catch { /* ignore */ }

        try { client?.close(); } catch { }
        client = null;

        _cts.Dispose();
        _cts = null;
    }

    // === Internal ===

    private void Handle(CmdID cmdID, byte[] msg)
    {
        if (_handlers.TryGetValue(cmdID, out var fun))
        {
            try { fun(cmdID, msg); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
        else
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Network] 未注册的指令: {cmdID}");
#endif
        }
    }

    private void EnqueueOnMainThread(Action action)
    {
        _mainThreadActions.Enqueue(action);
    }
}
