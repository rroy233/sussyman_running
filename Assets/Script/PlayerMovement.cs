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

    private Queue<PlayerStatusUpdate> statusUpdateQueue;

    private bool IsSelf = false;
    public int PlayerNameIndex = 0;
    private int MaxPlayerNum   = 2;
    public string[] PlayerNames = { "Player" ,"Player_1"};

    [SerializeField] private LayerMask jumpableGround;

    [SerializeField] private float jumpVelocity = 14f;
    [SerializeField] private float horizontalVelocity = 7f;

    private float dirX = 0f;
    private enum MovementState {idle,running,jumping,falling};

    // Start is called before the first frame update
    void Start()
    {
        Instance = this;

        rb= GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        sprite = GetComponent<SpriteRenderer>();
        coll = GetComponent<BoxCollider2D>();
        if (PlayerNameIndex==0)
        {
            IsSelf = true;
        }

        if(!IsSelf) {
            //非自身
            statusUpdateQueue = new Queue<PlayerStatusUpdate>();
        }
        else
        {
            //自身, 以 0 秒延迟，每 0.05 秒执行一次 SelfPlayerStatusUpdate
            InvokeRepeating("SelfPlayerStatusUpdate", 0f, 0.05f);
            //接受服务器告知的玩家状态更新
            //Network._Instance.AddHandleFunc(CmdID.CmdIDPlayerStatusUpdate, HandlePlayerStatusUpdate);
        }
    }

    // Update is called once per frame
    private void Update()
    {
        if (rb.bodyType == RigidbodyType2D.Static)
        {
            return;
        }
        //更新非自身玩家的状态
        if(IsSelf==false&&statusUpdateQueue.Count > 0)
        {
            var pkg = statusUpdateQueue.Dequeue();
            if (pkg != null)
            {
                if(name == pkg.Name) {

                }
            }
        }
        //控制自己的玩家
        if (IsSelf == true)
        {
            //horizontal move
            dirX = Input.GetAxisRaw("Horizontal");
            rb.velocity = new Vector2(horizontalVelocity * dirX, rb.velocity.y);

            //jump
            if (Input.GetButtonDown("Jump") && IsGrounded())
            {
                rb.velocity = new Vector2(rb.velocity.x, jumpVelocity);
                SelfPlayerStatusUpdate();
            }
        }
        updateAnimation();
    }
    private void updateAnimation()
    {
        MovementState state;
        if(dirX < 0f)
        {
            state = MovementState.running;
            sprite.flipX = true;
        }
        else if(dirX > 0f)
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
        }else if(rb.velocity.y < -0.1f)
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
        pkg.UID = 0;
        pkg.Name = PlayerNames[PlayerNameIndex];
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
        return Physics2D.BoxCast(coll.bounds.center, coll.bounds.size, 0f,Vector2.down,0.1f,jumpableGround);
    }

    
}
