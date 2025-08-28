using UnityEngine;
using Net.Proto;
using System;

namespace Multiplayer
{
    /// <summary>
    /// 远端玩家：基于 PlayerOpEvent 的 pressMs 与 tsMillis 时间积分，逼近真实状态；
    /// ApplyNetwork 用于静止/纠错的柔性校正。
    /// </summary>
    public class RemotePlayer2D : MonoBehaviour
    {
        public string PlayerName;
        public float MoveSpeed = 7f;
        public float JumpSpeed = 14f;
        public float Gravity   = 40f;

        [Tooltip("固定步长（秒）用于时间积分")]
        public float FixedStep = 0.02f; // 50Hz
        [Tooltip("最大延迟补偿（秒），从 tsMillis 估算并截断")]
        public float MaxLagComp = 0.08f; // 80ms
        public float NetLerp = 10f; // 视觉插值朝网络目标

        private Vector2 _vel;
        private bool _grounded;
        private float _simAccum;
        private float _activeDir;   // -1/0/+1
        private bool  _pendingJump;

        private Vector2 _netTargetPos;
        private SpriteRenderer _sr;
        private Animator _anim;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
            _anim = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
            _netTargetPos = transform.position;
        }

        public void ApplyOp(PlayerOpEvent op)
        {
            if (op == null) return;

            switch (op.Type)
            {
                case PlayerOpEvent.Types.OpType.MoveStart:
                case PlayerOpEvent.Types.OpType.MoveHold:
                    _activeDir = Mathf.Sign(op.DirX);
                    break;
                case PlayerOpEvent.Types.OpType.MoveStop:
                    _activeDir = 0f;
                    break;
                case PlayerOpEvent.Types.OpType.Jump:
                    _pendingJump = true;
                    break;
            }

            if (_sr != null)
            {
                if (_activeDir > 0.1f) _sr.flipX = false;
                else if (_activeDir < -0.1f) _sr.flipX = true;
            }

            // 利用 pressMs + 延迟的一半作为时间积分（上限 MaxLagComp），逼近真实对端进度
            float press = Mathf.Max(0f, op.PressMs / 1000f);
            float lag = 0f;
            try
            {
                if (op.TsMillis > 0)
                {
                    var nowMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var delta = (nowMs > op.TsMillis) ? (nowMs - op.TsMillis) / 1000f : 0f;
                    lag = Mathf.Min(MaxLagComp, Mathf.Max(0f, delta * 0.5f)); // 半延迟补偿，防止过冲
                }
            }
            catch {}

            _simAccum += press + lag;
        }

        public void ApplyNetwork(Vector2 pos, float dirX, int itemCount)
        {
            _netTargetPos = pos;

            if (_sr != null)
            {
                if (dirX > 0.1f) _sr.flipX = false;
                else if (dirX < -0.1f) _sr.flipX = true;
            }
            if (_anim != null)
            {
                bool moving = Mathf.Abs(_activeDir) > 0.1f || Mathf.Abs(_vel.x) > 0.1f;
                _anim.SetInteger("state", moving ? 1 : 0);
            }
        }

        private void Update()
        {
            // 视觉上趋近服务器广播位置（不强行覆盖）
            transform.position = Vector2.Lerp(transform.position, _netTargetPos, Time.deltaTime * NetLerp);

            float step = Mathf.Max(0.005f, FixedStep);
            while (_simAccum >= step)
            {
                Simulate(step);
                _simAccum -= step;
            }
        }

        private void Simulate(float dt)
        {
            _vel.x = _activeDir * MoveSpeed;

            if (_pendingJump && _grounded)
            {
                _vel.y = JumpSpeed;
            }
            _pendingJump = false;

            _vel.y -= Gravity * dt;

            Vector2 pos = transform.position;
            pos += _vel * dt;

            // 简单“地面”为 y=0，实际项目可替换为射线/碰撞
            if (pos.y <= 0f)
            {
                pos.y = 0f;
                _vel.y = 0f;
                _grounded = true;
            }
            else
            {
                _grounded = false;
            }

            transform.position = pos;

            if (_anim != null)
            {
                bool moving = Mathf.Abs(_vel.x) > 0.05f || !_grounded;
                _anim.SetInteger("state", moving ? 1 : 0);
            }
        }
    }
}