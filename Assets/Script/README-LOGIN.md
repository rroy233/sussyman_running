
# 登录页面接入教程（Unity + KCP + Protobuf）

> 目标：在游戏内新增“用户名/密码登录”，服务端校验后返回 `sessionID`，客户端据此作为后续通信的唯一凭证。

## 一、脚本与协议改动

### 1. 脚本新增/修改
- **新增**：`ui/LoginPanel.cs`（登录界面逻辑脚本）
- **修改**：`network/Network.cs`
  - 新增 `Login(string username, string password, Action onOk, Action<string> onFail)`
  - 接收端解析 `Greeting(LoginResp)`，并从 `Packet.SessionID` 接管会话
- **小改**：`cmd/Define.cs`
  - 在 `enum GreetingType` 中追加：`LoginReq=5, LoginResp=6`（临时约定）

> 说明：客户端将用户名/密码封装在 `Greeting.Msg` 的 JSON 中（示例：`{"u":"user","p":"pass"}`），使用 `CmdIDGreeting` 通道传输。
> 若你们服务端已有正式的 `LoginReq/LoginResp` Protobuf，请把 `Network.Login(...)` 中的 JSON 改为 `ToByteArray()` 即可。

### 2. 兼容性
- 未改动 `Packet.proto` 与现有 `Greeting` 字段（`Type`, `Msg`, `Delay`）。
- 新增的 `GreetingType` 枚举值仅在客户端使用数值 5、6，不影响既有消息。

## 二、Unity 界面搭建步骤

1. **创建画布**
   - `GameObject > UI > Canvas`，再创建 `EventSystem`（如果没有自动生成）
   - Canvas Scaler：`Scale With Screen Size`，参考分辨率例如 `1920x1080`

2. **在 Canvas 下创建一个 Panel（命名 LoginPanel）**
   - 添加 `Image` 组件作为半透明背景
   - 尺寸建议：锚点居中，宽 600，高 360

3. **添加子控件**
   - `Text`（标题）：内容“登录”，字体加粗，字号 28，居中
   - `InputField`（用户名）：占位提示“用户名”
   - `InputField`（密码）：启用 `Content Type = Password`
   - `Button`（登录）：文本“登录”
   - `Text`（消息提示）：用于显示“正在登录...”/错误信息

4. **挂载脚本与绑定引用**
   - 在 `LoginPanel` 物体上添加组件 `LoginPanel.cs`（位于 `ui/LoginPanel.cs`）
   - 将上述 `InputField` / `Button` / `Text` 拖到脚本的对应引用槽位
   - 在 `LoginPanel` 的 `Network` 引用中，**拖入场景中的 `Network` 物体**（若无，请创建空物体挂上 `Network` 脚本）
   - 在脚本中设置 `ServerAddr`、`ServerPort`，或在场景中添加 `NetworkControl` 并保持地址一致

5. **测试流程**
   - 运行场景 → 输入用户名/密码 → 点击“登录”
   - 登录成功：`MessageText` 显示“登录成功”，`Network.SessionID` 将被赋值为服务端返回的会话ID
   - 后续场景中，继续通过 `Network.PackAndSend(...)` 携带 `SessionID` 与服务器通信

## 三、与服务端的约定（建议）

- 使用现有 `Packet` 外层包（携带 `SessionID` 字段）
- 登录时：客户端发送 `CmdIDGreeting` + `Greeting{ Type=LoginReq, Msg=json }`
- 服务端：验证成功后，回复 `CmdIDGreeting` + `Greeting{ Type=LoginResp, Msg="OK" }`，并在外层 `Packet.SessionID` 写入新会话
- 失败时：`Greeting{ Type=LoginResp, Msg="错误原因" }`，`Packet.SessionID` 可为空

> 若要转为**纯 Protobuf** 的 `LoginReq/LoginResp`，只需：
> 1）在 proto 中定义二者；2）生成 C#；3）把 `Network.Login(...)` 中的 JSON 编码替换为 `LoginReq.ToByteArray()`；4）在接收端解析 `LoginResp` 并取出 `sessionID`（或继续使用外层 `Packet.SessionID`）。

## 四、常见问题

- **Q: Unity 跨线程报错？**  
  A: 本方案已将所有回调分发到主线程（`Network.Update()` 中执行），UI 代码只在主线程跑即可。

- **Q: 需要自动重连吗？**  
  A: 建议登录前建立连接，登录失败只重试认证；断线后可实现指数退避的自动重连（可在 `Network` 中新增）。

- **Q: 如何跳过登录（游客）？**  
  A: 你可以在服务端提供游客登录返回一个短期 `sessionID`；客户端照常走 `LoginResp` 取证。

