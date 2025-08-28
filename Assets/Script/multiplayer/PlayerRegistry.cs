using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using Net.Proto;
using Google.Protobuf;

namespace Multiplayer
{
    public class PlayerRegistry : MonoBehaviour
    {
        public static PlayerRegistry Instance { get; private set; }
        public GameObject PlayerTemplate;
        public Color[] RemoteTints = new Color[] { Color.cyan, Color.green, Color.magenta, Color.yellow };

        private readonly Dictionary<string, RemotePlayer2D> _remotes = new Dictionary<string, RemotePlayer2D>();
        private string EnsureRemoteKey(string name, uint uid)
        {
            if (uid != 0) return "uid:" + uid.ToString();
            return "name:" + (name ?? string.Empty);
        }
        private readonly ConcurrentQueue<byte[]> _queue = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _opQueue = new ConcurrentQueue<byte[]>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (PlayerTemplate == null)
            {
                var local = GameObject.Find("Player");
                if (local != null)
                {
                    PlayerTemplate = Instantiate(local);
                    PlayerTemplate.name = "PlayerTemplate";
                    PlayerTemplate.SetActive(false);
                }
            }

            try {
                Network._Instance.AddHandleFunc(CmdID.CmdIDPlayerOperation, (cmd, bytes) => { _opQueue.Enqueue(bytes); });
                Debug.Log("[PlayerRegistry] Listening CmdIDPlayerOperation.");
            } catch (System.Exception ex) { Debug.LogError(ex); }
        }

        public void EnqueueRemoteBytes(byte[] bytes) { if (bytes != null && bytes.Length > 0) _queue.Enqueue(bytes); }

        private void Update()
        {
            while (_opQueue.TryDequeue(out var opb))
            {
                try
                {
                    var op = PlayerOpEvent.Parser.ParseFrom(opb);
                    ApplyOp(op);
                }
                catch (System.Exception ex) { Debug.LogException(ex); }
            }

            while (_queue.TryDequeue(out var bytes))
            {
                try
                {
                    var pkg = PlayerStatusUpdate.Parser.ParseFrom(bytes);
                    uint localUid = (Network._Instance != null) ? (uint)Network._Instance.UID : 0;
                        if (localUid != 0 && pkg.UID == localUid) continue;
                        string localName = (Network._Instance != null && !string.IsNullOrEmpty(Network._Instance.Username)) ? Network._Instance.Username : (PlayerMovement.Instance != null ? PlayerMovement.Instance.PlayerNames[PlayerMovement.Instance.PlayerNameIndex] : null);
                        if (!string.IsNullOrEmpty(localName) && pkg.UID == 0 && pkg.Name == localName) continue;
                    ApplyParsed(pkg);
                }
                catch (System.Exception ex) { Debug.LogException(ex); }
            }
        }

        private void ApplyParsed(PlayerStatusUpdate pkg)
        {
            var pkgUidCache = pkg.UID;
            var rp = EnsureRemote(pkg.Name);
            if (rp == null) return;
            if (pkg.MovementStatus == null) return;
            var ms = pkg.MovementStatus;
            var pos = new Vector2((float)ms.Position.X, (float)ms.Position.Y);
            float dirX = (float)ms.DirX;
            rp.ApplyNetwork(pos, dirX, (int)pkg.ItemPickedCount);
        }

        private void ApplyOp(PlayerOpEvent op)
        {
            string localName = PlayerMovement.Instance != null ? PlayerMovement.Instance.PlayerNames[PlayerMovement.Instance.PlayerNameIndex] : null;
            if (!string.IsNullOrEmpty(localName) && op.Name == localName) return;

            var rp = EnsureRemote(op.Name);
            if (rp == null) return;
            rp.ApplyOp(op);
        }

        private RemotePlayer2D EnsureRemote(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var key = EnsureRemoteKey(name, 0);
            if (_remotes.TryGetValue(key, out var rp)) return rp;

            if (PlayerTemplate == null)
            {
                Debug.LogWarning("[PlayerRegistry] No PlayerTemplate; cannot spawn remote.");
                return null;
            }

            var go = Instantiate(PlayerTemplate);
            go.name = $"Remote_{name}";
            go.SetActive(true);

            var pm = go.GetComponent<PlayerMovement>(); if (pm != null) Destroy(pm);
            var ic = go.GetComponent<ItemCollecter>(); if (ic != null) Destroy(ic);
            var life = go.GetComponent<PlayerLife>(); if (life != null) Destroy(life);

            var remote = go.GetComponent<RemotePlayer2D>(); if (remote == null) remote = go.AddComponent<RemotePlayer2D>();
            remote.PlayerName = name;

            var sr = go.GetComponent<SpriteRenderer>() ?? go.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && RemoteTints != null && RemoteTints.Length > 0)
            {
                int idx = Mathf.Abs(name.GetHashCode()) % RemoteTints.Length;
                var c = RemoteTints[idx];
                sr.color = new Color(c.r, c.g, c.b, sr.color.a);
            }

            _remotes[EnsureRemoteKey(name, (uint)(Network._Instance!=null?Network._Instance.UID:0))] = remote;
            return remote;
        }
    }
}