using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Net.Proto;
using Google.Protobuf;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Net.Sockets.Kcp.Simple;
using System.Net;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine.UI;
using Unity.Burst.Intrinsics;
using UnityEngine.SceneManagement;

public class Network : MonoBehaviour
{
    public static Network _Instance;

    private Hashtable Handlers = new Hashtable();
    public delegate void HandleFunc(CmdID cmdID, byte[] msg);

    public SimpleKcpClient client;
    public string SessionID = "";
    public BlockingCollection<byte[]> SendQueue = new BlockingCollection<byte[]>();

    //private string ServerAddr= "101.32.15.237";
    private string ServerAddr = "192.168.31.135";
    private int ServerPort = 22101;
    private IPEndPoint end;

    private ulong DelayPingSendTime;
    private ulong DelayPingGotTime;
    private ulong Delay;


    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        _Instance = this;
    }

    private void OnDestroy()
    {
        CloseConn();
    }

    private void FixedUpdate()
    {
        /*
        if (client != null && SessionID == "")
        {
            GetSessionID();
        }
        */
    }

    public void init(string server,int port)
    {
        ServerAddr = server;
        ServerPort = port;
        Init();
    }

    public void Init()
    {
        end = new IPEndPoint(IPAddress.Parse(ServerAddr), ServerPort);
        client = new SimpleKcpClient(0,end);

        //定期更新
        Task.Run(async () =>
        {
            while (true)
            {
                client.kcp.Update(DateTimeOffset.UtcNow);
                await Task.Delay(10);
            }
        });

        //接受
        Task.Run(async () =>
        {
            while (true)
            {
                var resp = await client.ReceiveAsync();
                if (resp.Length == 0)
                {
                    await Task.Delay(10);
                    UnityEngine.Debug.Log("ReceiveAsync=null");
                    continue;
                }


                Packet packet1 = Packet.Parser.ParseFrom(resp[0..resp.Length]);
                UnityEngine.Debug.Log("[RevWorker]收到数据：[CMD_" + packet1.CmdID + "]" + Encoding.UTF8.GetString(resp, 0, resp.Length));


                //CmdIDGreeting
                if (packet1.CmdID == (uint)CmdID.CmdIDGreeting)
                {
                    SessionID = packet1.SessionID;
                    if (DelayPingGotTime == 0 && DelayPingSendTime!=0)
                    {
                        DelayPingGotTime = (ulong)Utils.GetUnixMill();
                        Delay = DelayPingGotTime - DelayPingSendTime;
                    }
                    continue;
                }

                Handle((CmdID)packet1.CmdID, packet1.Msg.ToByteArray());
            }
        });

        //add handleFuncs

        //get sessionID
        //GetSessionID();

        UnityEngine.Debug.Log("network.cs init() - ok");

        //auto ping
        Task.Run(async () =>
        {
            while (true)
            {
                //UnityEngine.Debug.Log("send:"+DelayPingSendTime.ToString()+" Got:"+DelayPingGotTime.ToString()+" delay:"+Delay.ToString());
                Greeting greeting = new Greeting();
                greeting.Type = GreetingType.PingServer;
                greeting.Delay = Delay;
                greeting.Msg = "PING";

                DelayPingGotTime = 0;
                DelayPingSendTime = (ulong)Utils.GetUnixMill();

                PackAndSend(CmdID.CmdIDGreeting, greeting.ToByteArray());
                //UnityEngine.Debug.Log("PING:" + greeting.ToString());
                await Task.Delay(3000);
            }
        });
    }

    public void CloseConn()
    {
        //send to server
        SessionEndNotify pkg = new SessionEndNotify();
        pkg.CloseType = SessionCloseType.ClientClose;
        pkg.Msg = "Bye Bye!";
        PackAndSend(CmdID.CmdIDSessionEndNotify, pkg.ToByteArray());
        Thread.Sleep(500);

        client.close();
    }


    public  void PackAndSend(CmdID cmdID, byte[] data)
    {
        Packet packet = new Packet();
        packet.CmdID = (uint)cmdID;
        packet.CmdLen = (UInt32)data.Length;
        packet.Msg = ByteString.CopyFrom(data);
        packet.SessionID = SessionID;
        packet.SendTimeStampMill = (ulong)Utils.GetUnixMill();

        client.SendAsync(packet.ToByteArray(), packet.ToByteArray().Length);
    }

    private void GetSessionID()
    {
        Greeting greeting = new Greeting();
        greeting.Type = GreetingType.CreateSession;
        greeting.Msg = "Hello Server! Request a Session!";

        //send ping
        PackAndSend((uint)CmdID.CmdIDGreeting, greeting.ToByteArray());
        UnityEngine.Debug.Log("CreateSession PING Sent");
    }

    public void AddHandleFunc(CmdID cmdID, HandleFunc fun)
    {
        if (Handlers.ContainsKey(cmdID))
        {
            Handlers[cmdID] = fun;
            //UnityEngine.Debug.LogWarning("AddHandleFunc("+cmdID.ToString()+")已被覆盖");
        }
        else
        {
            Handlers.Add(cmdID, fun);

            UnityEngine.Debug.Log("AddHandleFunc(" + cmdID.ToString() + ")添加成功");
        }
        
    }

    public void Handle(CmdID cmdID, byte[] msg)
    {
        var fun = (HandleFunc)Handlers[cmdID];
        if(fun == null)
        {
            UnityEngine.Debug.Log("[CMD_"+cmdID+"]未找到其处理函数");
            return;
        }
        fun(cmdID,msg);
    }

    /// <summary>
    /// GetDelay 返回延迟数字
    /// </summary>
    /// <returns></returns>
    public string GetDelay()
    {
        return Delay.ToString();
    }

    
}
