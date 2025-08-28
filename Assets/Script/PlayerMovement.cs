using Google.Protobuf;
using Net.Proto;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerMovement : MonoBehaviour
{
    public static PlayerMovement Instance;

    private Rigidbody2D rb;
    private Animator anim;
    private SpriteRenderer sprite;
    private BoxCollider2D coll;

    private bool IsSelf = false;
    public int PlayerNameIndex = 0;
    public string[] PlayerNames = { "Player", "Player_1" };

    [SerializeField] private LayerMask jumpableGround;

    [SerializeField] private float jumpVelocity = 14f;
    [SerializeField] private float horizontalVelocity = 7f;

    private float dirX = 0f;
    private enum MovementState { idle, running, jumping, falling };

    // -------------------- [NET+] 操作广播（不改本机物理，仅发包） --------------------
    // 长按期间定期（分片）广播，避免“长时间无包”
    [Header("Net Sync")]
    [Tooltip("操作长按分片广播间隔（秒），建议 0.10s")]
    public float OpHoldInterval = 0.10f; // 100ms
    private bool _leftHeld;
    private bool _rightHeld;
    private float _opHoldTimer;
    // ---------------------------------------------------------------------------

    // Start is called before the first frame update
    void Start()
    {
        Instance = this;

        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        sprite = GetComponent<SpriteRenderer>();
        coll = GetComponent<BoxCollider2D>();
        if (PlayerNameIndex == 0)
        {
            IsSelf = true;
        }

        if(IsSelf) {
            //自身, 以 0 秒延迟，每 0.5 秒执行一次 SelfPlayerStatusUpdate
            InvokeRepeating("SelfPlayerStatusUpdate", 0f, 0.5f);
        }

        // -------------------- [NET+] 注册接收：服务器广播的 PlayerStatusUpdate --------------------
        // 交给 PlayerRegistry 在主线程创建/更新远端玩家（本脚本不直接操作远端对象，避免线程问题）
        try
        {
            Network._Instance.AddHandleFunc(CmdID.CmdIDPlayerStatusUpdate, (cmd, bytes) =>
            {
                Multiplayer.PlayerRegistry.Instance?.EnqueueRemoteBytes(bytes);
            });
            Debug.Log("[PlayerMovement] Registered handler for CmdIDPlayerStatusUpdate.");
        }
        catch (Exception ex)
        {
            Debug.LogError("[PlayerMovement] Failed to register PlayerStatusUpdate handler: " + ex.Message);
        }
        // ----------------------------------------------------------------------------------------
    }

    // Update is called once per frame
    private void Update()
    {
        if (rb.bodyType == RigidbodyType2D.Static)
        {
            return;
        }
        //控制自己的玩家
        if (IsSelf == true)
        {
            // -------------------- 本地单机控制（保持原有逻辑，不与网络耦合） --------------------
            //horizontal move
            dirX = Input.GetAxisRaw("Horizontal");
            rb.velocity = new Vector2(horizontalVelocity * dirX, rb.velocity.y);

            //jump
            bool jumpDown = Input.GetButtonDown("Jump");
            if (jumpDown && IsGrounded())
            {
                rb.velocity = new Vector2(rb.velocity.x, jumpVelocity);
                SelfPlayerStatusUpdate();   // 保持你原有：起跳即时发一次完整状态
            }
            // -------------------------------------------------------------------------------

            // -------------------- [NET+] 操作广播（仅观测输入并发包，不改本机物理） --------------------
            bool leftNow = (dirX < -0.5f) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            bool rightNow = (dirX > +0.5f) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);

            // 边缘：开始/停止移动
            if (leftNow && !_leftHeld) { SendOp(PlayerOpEvent.Types.OpType.MoveStart, -1f); _leftHeld = true; }
            if (!leftNow && _leftHeld) { SendOp(PlayerOpEvent.Types.OpType.MoveStop, 0f); _leftHeld = false; }

            if (rightNow && !_rightHeld) { SendOp(PlayerOpEvent.Types.OpType.MoveStart, +1f); _rightHeld = true; }
            if (!rightNow && _rightHeld) { SendOp(PlayerOpEvent.Types.OpType.MoveStop, 0f); _rightHeld = false; }

            // 边缘：跳跃（按键边沿，不改本机运动）
            if (Input.GetButtonDown("Jump"))
            {
                SendOp(PlayerOpEvent.Types.OpType.Jump, 0f);
            }

            // 长按分片：避免长按期间长时间无包
            _opHoldTimer += Time.deltaTime;
            if (_opHoldTimer >= OpHoldInterval)
            {
                _opHoldTimer = 0f;
                if (_leftHeld) SendOp(PlayerOpEvent.Types.OpType.MoveHold, -1f, (uint)(OpHoldInterval * 1000));
                if (_rightHeld) SendOp(PlayerOpEvent.Types.OpType.MoveHold, +1f, (uint)(OpHoldInterval * 1000));
            }
            // --------------------------------------------------------------------------------
        }
        updateAnimation();
    }
    private void updateAnimation()
    {
        MovementState state;
        if (dirX < 0f)
        {
            state = MovementState.running;
            sprite.flipX = true;
        }
        else if (dirX > 0f)
        {
            state = MovementState.running;
            sprite.flipX = false;
        }
        else
        {
            state = MovementState.idle;
        }

        //judge jump or fall base on v
        if (rb.velocity.y > 0.1f)
        {
            state = MovementState.jumping;
        }
        else if (rb.velocity.y < -0.1f)
        {
            state = MovementState.falling;
        }
        anim.SetInteger("state", (int)state);
    }

    public void HandlePlayerStatusUpdate(CmdID cmdID, byte[] msg)
    {
        PlayerStatusUpdate pkg = PlayerStatusUpdate.Parser.ParseFrom(msg);

        Debug.Log("收到服务器发来的玩家状态更新:" + pkg.ToString());

        //更新樱桃数
        GetComponent<ItemCollecter>().EditCherryCnt(pkg.ItemPickedCount);
    }

    public void SelfPlayerStatusUpdate()
    {
        //Debug.Log("PlayerStatusUpdate被调用！");
        PlayerStatusUpdate pkg = new();
        pkg.UID = (Network._Instance != null ? (uint)Network._Instance.UID : 0);
        pkg.Name = (Network._Instance != null && !string.IsNullOrEmpty(Network._Instance.Username)) ? Network._Instance.Username : PlayerNames[PlayerNameIndex];
        pkg.MovementStatus = new BasicMovementStatus();
        pkg.MovementStatus.Position = new Vector2D();
        pkg.MovementStatus.Speed = new Vector2D();
        pkg.MovementStatus.Position.X = transform.position.x;
        pkg.MovementStatus.Position.Y = transform.position.y;
        pkg.MovementStatus.Speed.X = rb.velocity.x;
        pkg.MovementStatus.Speed.Y = rb.velocity.y;
        pkg.MovementStatus.DirX = dirX;
        //add dirx

        if (rb.bodyType == RigidbodyType2D.Static)
        {
            pkg.Freeze = true;
        }
        pkg.SceneID = SceneManager.GetActiveScene().buildIndex;
        pkg.ItemPickedCount = gameObject.GetComponent<ItemCollecter>().GetCherryCnt();

        Network._Instance.PackAndSend(CmdID.CmdIDPlayerStatusUpdate, pkg.ToByteArray());
    }

    private bool IsGrounded()
    {
        /**
         * A BoxCast is conceptually like dragging a box through the Scene in a particular direction. 
         * Any object making contact with the box can be detected and reported.
         */
        /**
         * The layerMask can be used to detect objects selectively only on certain layers 
         * (this allows you to apply the detection only to enemy characters, for example).
         */
        return Physics2D.BoxCast(coll.bounds.center, coll.bounds.size, 0f, Vector2.down, 0.1f, jumpableGround);
    }

    // -------------------- [NET+] 发送操作事件（只发包，不改本机运动） --------------------
    private void SendOp(PlayerOpEvent.Types.OpType type, float dir, uint pressMs = 0)
    {
        try
        {
            var name = (PlayerNames != null && PlayerNames.Length > 0)
                ? PlayerNames[Mathf.Clamp(PlayerNameIndex, 0, PlayerNames.Length - 1)]
                : "Player";

            var op = new PlayerOpEvent
            {
                Name = name,
                Type = type,         // MoveStart / MoveHold / MoveStop / Jump
                DirX = dir,          // -1 / 0 / +1
                PressMs = pressMs,   // 长按分片的持续毫秒（用于接收端节奏对齐）
                TsMillis = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            Network._Instance.PackAndSend(CmdID.CmdIDPlayerOperation, op.ToByteArray());
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }
    // ------------------------------------------------------------------------------------
}
