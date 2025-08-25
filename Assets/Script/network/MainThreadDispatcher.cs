
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace Net.Unity
{
    /// <summary>
    /// 简单的主线程调度器（备用）。Network.cs 已集成了自己的队列，这个类供其他系统使用。
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour
    {
        public static MainThreadDispatcher Instance;
        private readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void Enqueue(Action a) => _actions.Enqueue(a);

        private void Update()
        {
            while (_actions.TryDequeue(out var a))
            {
                try { a?.Invoke(); } catch (Exception ex) { Debug.LogException(ex); }
            }
        }
    }
}
