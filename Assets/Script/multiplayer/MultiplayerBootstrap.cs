using UnityEngine;

namespace Multiplayer
{
    public class MultiplayerBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            if (PlayerRegistry.Instance == null)
            {
                var go = new GameObject("PlayerRegistry");
                go.AddComponent<PlayerRegistry>();
                DontDestroyOnLoad(go);
            }
        }
    }
}