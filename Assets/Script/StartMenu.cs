using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Google.Protobuf;
using Net.Proto;  // Greeting / GreetingType

public class StartMenu : MonoBehaviour
{
    [Header("Server Settings")]
    [Tooltip("服务器地址，例如 127.0.0.1 或 10.0.0.5")]
    public Dropdown ServerAddrDropdown;
    [Tooltip("服务器端口，例如 22101")]
    public InputField ServerPortInput;

    [Header("Login")]
    public InputField UsernameInput;
    public InputField PasswordInput;
    public Button LoginButton;

    [Header("UI / Misc")]
    public Text MessageText;                // 用于提示“正在登录/错误/成功”
    public Button QuitButton;               // 可选：退出游戏按钮
    public Toggle FullscreenToggle;         // 可选：是否全屏

    [Header("Post Login")]
    [Tooltip("登录成功后是否自动切换场景")]
    public bool AutoLoadOnLogin = false;
    [Tooltip("登录成功后切换到的场景名")]
    public string NextSceneName = "MainScene";

    [Header("Network")]
    [Tooltip("可不填，运行时自动查找")]
    public Network Net;

    // 内部状态
    private bool _connected = false;

    // —— 登录期的临时状态与回调 —— 
    private bool _waitingLogin = false;
    private Action _loginOkCb;
    private Action<string> _loginFailCb;

    private void Awake()
    {
        if (LoginButton != null)
            LoginButton.onClick.AddListener(OnLoginClicked);

        if (QuitButton != null)
            QuitButton.onClick.AddListener(OnQuitClicked);

        if (FullscreenToggle != null)
            FullscreenToggle.onValueChanged.AddListener(OnFullscreenToggle);

        SetMsg("");
        SetInteractable(true);
    }

    private void OnDestroy()
    {
        if (LoginButton != null)
            LoginButton.onClick.RemoveListener(OnLoginClicked);
        if (QuitButton != null)
            QuitButton.onClick.RemoveListener(OnQuitClicked);
        if (FullscreenToggle != null)
            FullscreenToggle.onValueChanged.RemoveListener(OnFullscreenToggle);
    }

    private void OnFullscreenToggle(bool on)
    {
        Screen.fullScreen = on;
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnLoginClicked()
    {
        // 与原始 StartMenu.cs 一致：固定地址表来自代码
        string[] addrs = new string[] { "101.32.15.237", "127.0.0.1" };

        // 读取下拉选择地址（优先用固定数组；若为空则兜底读取 Dropdown.options）
        string addr = "";
        if (ServerAddrDropdown != null)
        {
            int idx = Mathf.Clamp(ServerAddrDropdown.value, 0, addrs.Length - 1);
            addr = addrs.Length > 0 ? addrs[idx] : "";
            if (string.IsNullOrEmpty(addr) &&
                ServerAddrDropdown.options != null &&
                ServerAddrDropdown.options.Count > ServerAddrDropdown.value)
            {
                addr = ServerAddrDropdown.options[ServerAddrDropdown.value].text;
            }
        }

        var portStr = ServerPortInput != null ? ServerPortInput.text?.Trim() : "";
        var user = UsernameInput != null ? UsernameInput.text?.Trim() : "";
        var pass = PasswordInput != null ? PasswordInput.text : "";

        if (string.IsNullOrEmpty(addr))
        {
            SetMsg("请填写服务器地址");
            return;
        }
        if (!int.TryParse(portStr, out var port) || port <= 0)
        {
            SetMsg("端口号不合法");
            return;
        }
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            SetMsg("用户名或密码不能为空");
            return;
        }

        // 确保 Net 存在（按原始做法：需要时创建 NetworkControl + Network）
        if (Net == null)
        {
            Net = FindObjectOfType<Network>();
            if (Net == null)
            {
                var go = new GameObject("NetworkRoot");
                DontDestroyOnLoad(go);
                var net = go.AddComponent<Network>();
                var nc = go.AddComponent<NetworkControl>();
                nc.NetClient = net;
                nc.addr = addr;
                nc.port = port;
                Net = net;
            }
        }

        SetInteractable(false);
        SetMsg($"正在连接服务器 {addr}:{port} ...");

        if (!_connected)
        {
            try
            {
                Net.init(addr, port);
                _connected = true;
            }
            catch (Exception ex)
            {
                Debug.Log("连接失败：" + ex.Message);
                SetMsg("连接失败");
                SetInteractable(true);
                return;
            }
        }

        // 通过本地的 Login(...) 函数发起认证（不依赖 Network.cs 的扩展）
        SetMsg("正在登录...");
        Login(user, pass,
            onOk: () =>
            {
                Debug.Log("登录成功，SessionID=" + (Net.SessionID ?? ""));
                SetMsg("登录成功，SessionID=" + (Net.SessionID ?? ""));
                if (AutoLoadOnLogin && !string.IsNullOrEmpty(NextSceneName))
                {
                    SceneManager.LoadScene(NextSceneName);
                }
                else
                {
                    SetInteractable(true);
                }
            },
            onFail: (err) =>
            {
                SetMsg(string.IsNullOrEmpty(err) ? "登录失败" : err);
                SetInteractable(true);
            });
    }

    /// <summary>
    /// 本地实现的登录流程：
    /// - 发送 Greeting(LoginReq)，Msg 放 {"u":"..","p":".."}
    /// - 临时等待 Greeting(LoginResp)，根据 Msg==OK 判定成功与否；
    /// - 会话ID从服务器外层 Packet.SessionID 注入（Network.cs 里会在收到 Greeting 时设置 SessionID）。
    /// </summary>
    private void Login(string username, string password, Action onOk, Action<string> onFail)
    {
        if (_waitingLogin)
        {
            onFail?.Invoke("正在登录中，请稍候…");
            return;
        }
        if (Net == null)
        {
            onFail?.Invoke("Network 未就绪");
            return;
        }

        _waitingLogin = true;
        _loginOkCb = onOk;
        _loginFailCb = onFail;

        // 注册临时的 Greeting 回调：仅在等待登录时解析 LoginResp
        Net.AddHandleFunc(CmdID.CmdIDGreeting, HandleGreetingForLogin);

        // 发送 LoginReq（使用 JSON 载荷；若你们已有 LoginReq.proto，可替换为 ToByteArray()）
        var json = $"{{\"u\":\"{username}\",\"p\":\"{password}\"}}";
        var g = new Greeting
        {
            Type = GreetingType.LoginReq, // 需要在 Define.cs 中声明 LoginReq = 5
            Msg = json
        };
        Net.PackAndSend(CmdID.CmdIDGreeting, g.ToByteArray());
    }

    /// <summary>
    /// 仅在等待登录阶段拦截 Greeting(LoginResp)。
    /// </summary>
    private void HandleGreetingForLogin(CmdID cmdID, byte[] msg)
    {
        if (!_waitingLogin) return;           // 不是登录阶段就直接忽略
        if (cmdID != CmdID.CmdIDGreeting) return;

        try
        {
            var g = Greeting.Parser.ParseFrom(msg);
            if (g.Type == GreetingType.LoginResp) // 需要在 Define.cs 中声明 LoginResp = 6
            {
                _waitingLogin = false;
                var ok = string.Equals(g.Msg, "OK", StringComparison.OrdinalIgnoreCase);

                // 此时 Net.SessionID 应由 Network.cs 在收到外层 Packet 时写入
                if (ok && !string.IsNullOrEmpty(Net.SessionID))
                    _loginOkCb?.Invoke();
                else
                    _loginFailCb?.Invoke(string.IsNullOrEmpty(g.Msg) ? "登录失败" : g.Msg);

                // 用完回调即释放
                _loginOkCb = null;
                _loginFailCb = null;
            }
        }
        catch (Exception ex)
        {
            _waitingLogin = false;
            var err = "登录响应解析失败：" + ex.Message;
            _loginFailCb?.Invoke(err);
            _loginOkCb = null;
            _loginFailCb = null;
        }
    }

    private void SetMsg(string s)
    {
        if (MessageText != null)
            MessageText.text = s ?? "";
        else
            Debug.Log(s);
    }

    private void SetInteractable(bool v)
    {
        if (LoginButton != null) LoginButton.interactable = v;
        if (ServerAddrDropdown != null) ServerAddrDropdown.interactable = v;
        if (ServerPortInput != null) ServerPortInput.interactable = v;
        if (UsernameInput != null) UsernameInput.interactable = v;
        if (PasswordInput != null) PasswordInput.interactable = v;
        if (QuitButton != null) QuitButton.interactable = v;
        if (FullscreenToggle != null) FullscreenToggle.interactable = v;
    }
}
