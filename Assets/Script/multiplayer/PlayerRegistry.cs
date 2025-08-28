using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using Net.Proto;
using Google.Protobuf;

namespace Multiplayer
{
    /// <summary>
    /// Manages remote players. Network thread enqueues bytes; Update() (main thread) parses and applies.
    /// </summary>
    public class PlayerRegistry : MonoBehaviour
    {
        public static PlayerRegistry Instance { get; private set; }

        [Tooltip("Template to clone for remote players (if null, will try to use the local 'Player' as template).")]
        public GameObject PlayerTemplate;

        [Tooltip("Tint colors for distinguishing remote players.")]
        public Color[] RemoteTints = new Color[] { Color.cyan, Color.green, Color.magenta, Color.yellow };

        private readonly Dictionary<string, RemotePlayer2D> _remotes = new Dictionary<string, RemotePlayer2D>();
        private readonly ConcurrentQueue<byte[]> _queue = new ConcurrentQueue<byte[]>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (PlayerTemplate == null)
            {
                var local = GameObject.Find("Player");
                if (local != null)
                {
                    // create a disabled template clone
                    PlayerTemplate = Instantiate(local);
                    PlayerTemplate.name = "PlayerTemplate";
                    PlayerTemplate.SetActive(false);
                }
            }

            // Register handler for remote players' updates (server broadcasts)
            try
            {
                Network._Instance.AddHandleFunc(CmdID.CmdIDPlayerStatusUpdate, (cmd, bytes) => {
                    Instance?.EnqueueRemoteBytes(bytes);
                });
                Debug.Log("[PlayerRegistry] Registered handler for CmdIDPlayerStatusUpdate.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[PlayerRegistry] Failed to register PlayerStatusUpdate handler: " + ex.Message);
            }
        }

        /// <summary>Called from network handler thread.</summary>
        public void EnqueueRemoteBytes(byte[] bytes)
        {
            if (bytes != null && bytes.Length > 0) _queue.Enqueue(bytes);
        }

        private void Update()
        {
            while (_queue.TryDequeue(out var bytes))
            {
                try
                {
                    var pkg = PlayerStatusUpdate.Parser.ParseFrom(bytes);
                    // Skip self
                    string localName = PlayerMovement.Instance != null 
                        ? PlayerMovement.Instance.PlayerNames[PlayerMovement.Instance.PlayerNameIndex]
                        : null;
                    if (!string.IsNullOrEmpty(localName) && pkg.Name == localName) continue;

                    ApplyParsed(pkg);
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        private void ApplyParsed(PlayerStatusUpdate pkg)
        {
            var rp = EnsureRemote(pkg.Name);
            if (rp == null) return;

            var ms = pkg.MovementStatus;
            if (ms == null) return;
            var pos = new Vector2((float)ms.Position.X, (float)ms.Position.Y);
            float dirX = (float)ms.DirX;
            rp.ApplyNetwork(pos, dirX, (int)pkg.ItemPickedCount);
        }

        private RemotePlayer2D EnsureRemote(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_remotes.TryGetValue(name, out var rp)) return rp;

            if (PlayerTemplate == null)
            {
                Debug.LogWarning("[PlayerRegistry] No PlayerTemplate set; cannot spawn remote player.");
                return null;
            }

            var go = Instantiate(PlayerTemplate);
            go.name = $"Remote_{name}";
            go.SetActive(true);

            // Remove local-only components if present
            var pm = go.GetComponent<PlayerMovement>(); if (pm != null) Destroy(pm);
            var ic = go.GetComponent<ItemCollecter>(); if (ic != null) Destroy(ic);
            var life = go.GetComponent<PlayerLife>(); if (life != null) Destroy(life);

            // Add remote controller
            var remote = go.GetComponent<RemotePlayer2D>();
            if (remote == null) remote = go.AddComponent<RemotePlayer2D>();
            remote.PlayerName = name;

            // Tint for distinction
            var sr = go.GetComponent<SpriteRenderer>() ?? go.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && RemoteTints != null && RemoteTints.Length > 0)
            {
                int idx = Mathf.Abs(name.GetHashCode()) % RemoteTints.Length;
                var c = RemoteTints[idx];
                sr.color = new Color(c.r, c.g, c.b, sr.color.a);
            }

            _remotes[name] = remote;
            return remote;
        }
    }
}