using UnityEngine;

namespace Multiplayer
{
    /// <summary>
    /// Very simple remote ghost controller: follows network-updated target.
    /// </summary>
    public class RemotePlayer2D : MonoBehaviour
    {
        public string PlayerName;
        public float Lerp = 12f;

        private Vector2 _targetPos;
        private SpriteRenderer _sr;
        private Animator _anim;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
            _anim = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
            _targetPos = transform.position;
        }

        public void ApplyNetwork(Vector2 pos, float dirX, int itemCount)
        {
            _targetPos = pos;
            if (_sr != null)
            {
                if (dirX > 0.1f) _sr.flipX = false;
                else if (dirX < -0.1f) _sr.flipX = true;
            }
            if (_anim != null)
            {
                // map to running/idle roughly based on movement
                bool moving = Mathf.Abs(dirX) > 0.1f;
                _anim.SetInteger("state", moving ? 1 : 0);
            }
        }

        private void Update()
        {
            transform.position = Vector2.Lerp(transform.position, _targetPos, Time.deltaTime * Lerp);
        }
    }
}