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

    [Header("Network")]
    private Network Net = null;

    // 内部状态
    private bool _connected = false;

    // —— 登录期的临时状态与回调 —— 
    private bool _waitingLogin = false;


    // 主线程待处理标记（在 Update() 消费）
    private bool _loginOkPending = false;          // 登录成功待处理（主线程）
    private string _loginErrPending = null;        // 登录失败待处理信息（主线程）
    private bool _pendingLoadNextScene = false;    // 切场景标记（主线程）

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

    private void Update()
    {
        //Debug.Log($"_loginOkPending={_loginOkPending}, string.IsNullOrEmpty(_loginErrPending)={string.IsNullOrEmpty(_loginErrPending)}, _pendingLoadNextScene={_pendingLoadNextScene}");
        // 消费登录成功标记（只在主线程处理 Unity API）
        if (_loginOkPending)
        {
            _loginOkPending = false;

            SetMsg("登录成功，SessionID=" + (Net != null ? (Net.SessionID ?? "") : ""));
            _pendingLoadNextScene = true;
        }

        // 消费登录失败标记
        if (!string.IsNullOrEmpty(_loginErrPending))
        {
            var err = _loginErrPending;
            _loginErrPending = null;
            SetMsg(err);
            SetInteractable(true);
            Utils.MessageBox(IntPtr.Zero, err, "登录失败", 0);
        }

        // 消费切场景标记
        if (_pendingLoadNextScene)
        {
            _pendingLoadNextScene = false;
            Debug.Log(SceneManager.GetActiveScene());
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
        }
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

        var nc = new GameObject("NetworkControl");
        nc.AddComponent<Network>();
        Net = nc.GetComponent<Network>();
        Net.init(addr, int.Parse(portStr));
        Debug.Log("startMenu.cs client.init() - ok");

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
                Utils.MessageBox(IntPtr.Zero, "连接服务器失败！", "提示", 0);
                return;
            }
        }

        // 通过本地的 Login(...) 函数发起认证（不依赖 Network.cs 的扩展）
        SetMsg("正在登录...");
        Login(user, pass);
    }

    /// <summary>
    /// 本地实现的登录流程（不使用传入回调）：
    /// - 发送 Greeting(LoginReq)，Msg 放 {"u":"..","p":".."}
    /// - 临时等待 Greeting(LoginResp)，根据 Msg==OK 设置主线程待处理标记；
    /// - 会话ID从服务器外层 Packet.SessionID 注入（Network.cs 收包时应写入）。
    /// </summary>
    private void Login(string username, string password)
    {
        if (_waitingLogin)
        {
            SetMsg("正在登录中，请稍候…");
            return;
        }
        if (Net == null)
        {
            SetMsg("Network 未就绪");
            return;
        }

        _waitingLogin = true;
        _loginOkPending = false;
        _loginErrPending = null;

        // 临时注册 Greeting 处理器（仅登录阶段关注 LoginResp）
        Net.AddHandleFunc(CmdID.CmdIDGreeting, HandleGreetingForLogin);

        // 发送 LoginReq（若你们已有 LoginReq.proto，可替换为 Protobuf 二进制）
        var json = $"{{\"u\":\"{username}\",\"p\":\"{password}\"}}";
        var g = new Greeting
        {
            Type = GreetingType.LoginReq, // 需在 Define.cs 中声明：LoginReq = 5
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
                if (ok && !string.IsNullOrEmpty(Net.SessionID)) {
                    _loginOkPending = true;
                    Net.Username = UsernameInput.text.Trim();
                }
                else
                {
                    _loginErrPending = "登录失败" + g.Msg;
                }
            }
        }
        catch (Exception ex)
        {
            _waitingLogin = false;
            _loginErrPending = "登录响应解析失败：" + ex.Message;
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
