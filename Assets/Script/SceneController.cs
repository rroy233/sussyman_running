using Google.Protobuf;
using Net.Proto;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneController : MonoBehaviour
{
    // === Added for server-driven level advance ===
    private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _levelAdvanceQueue = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
    private int _pendingNextSceneBuildIndex = -1;
    // =============================================
    
    public static SceneController Instance;

    [SerializeField] private bool IsMenu=false;
    [SerializeField] private int SceneID = -1;
    [SerializeField] private GameObject SpawnCherryPrefab;
    [SerializeField] private Text LevelInfoText;
    [SerializeField] private Text DelayInfoText;

    private string LevelName;
    private int CherryCntNeedChange = -1;
    private static Queue<ItemSpawnNotify> itemSpawnQueue;

    public static int CurrentSceneID
    {
        get
        {
            return Instance != null ? Instance.GetSceneID() : SceneManager.GetActiveScene().buildIndex;
        }
    }

    private int GetSceneID()
    {
        return SceneID >= 0 ? SceneID : SceneManager.GetActiveScene().buildIndex;
    }

    // Start is called before the first frame update
    void Start()
    {
        // Register handler for server broadcast: LevelAdvanceNotify -> advance scene
        try {
            Network._Instance.AddHandleFunc(CmdID.CmdIDLevelAdvanceNotify, (cmd, bytes) => {
                _levelAdvanceQueue.Enqueue(bytes);
            });
            Debug.Log("[SceneController] Listening to CmdIDLevelAdvanceNotify for scene advancing.");
        } catch (System.Exception ex) {
            Debug.LogError("[SceneController] Failed to register LevelAdvanceNotify handler: " + ex.Message);
        }
    
        Instance = this;

        //定义路由
        Network._Instance.AddHandleFunc(CmdID.CmdIDSceneDataResp, HandleSceneDataResp);
        if (!IsMenu)
        {
            itemSpawnQueue = new Queue<ItemSpawnNotify>();
            Network._Instance.AddHandleFunc(CmdID.CmdIDItemSpawnNotify, HandleSpawn);

            //接受物品数量更新通知
            Network._Instance.AddHandleFunc(CmdID.CmdIDItemPickedNumUpdate, handleItemPickedNumUpdate);
        }
        Network._Instance.AddHandleFunc(CmdID.CmdIDSessionEndNotify, HandleSessionEnd);

        //获取关卡信息
        GetLevelData();
    }

    // Update is called once per frame
    void Update()
    {
        
        // Consume server LevelAdvanceNotify on main thread
        while (_levelAdvanceQueue.TryDequeue(out var bytes))
        {
            try
            {
                var pkg = LevelAdvanceNotify.Parser.ParseFrom(bytes);
                int nextIdx = pkg.NextSceneID > 0 ? pkg.NextSceneID : (SceneManager.GetActiveScene().buildIndex + 1);
                _pendingNextSceneBuildIndex = nextIdx;
                Debug.Log($"[SceneController] Received LevelAdvanceNotify -> next build index {nextIdx}");
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        if (_pendingNextSceneBuildIndex >= 0)
        {
            int idx = _pendingNextSceneBuildIndex;
            _pendingNextSceneBuildIndex = -1;
            if (idx >= 0 && idx < SceneManager.sceneCountInBuildSettings)
            {
                SceneManager.LoadScene(idx);
            }
            else
            {
                Debug.LogWarning($"[SceneController] Next scene index {idx} out of range; fallback +1");
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
            }
        }
        if (IsMenu)
        {
            return;
        }

        if (itemSpawnQueue.Count > 0)
        {
            var pkg = itemSpawnQueue.Dequeue();
            for (int i = 0; i < pkg.Count; i++)
            {
                switch (pkg.List[i].PrefabName)
                {
                    case "Cherry":
                        var obj = Instantiate(SpawnCherryPrefab, new Vector3((float)pkg.List[i].Position.X, (float)pkg.List[i].Position.Y, 0f), Quaternion.identity);
                        obj.name = pkg.List[i].ItemName;
                        break;
                    default:
                        Debug.Log("PrefabName无法识别:"+ pkg.List[i].PrefabName);
                        break;
                }

            }
        }

        //UI
        LevelInfoText.text = LevelName;

        //cherry count
        if (CherryCntNeedChange != -1 && GameObject.Find("Player")!=null)
        {
            GameObject.Find("Player").GetComponent<ItemCollecter>().EditCherryCnt(CherryCntNeedChange);
            CherryCntNeedChange = -1;
        }
    }

    private void FixedUpdate()
    {
        if (!IsMenu)
        { DelayInfoText.text = Network._Instance.GetDelay() + "ms"; }
    }

    private void HandleSceneDataResp(CmdID cmdID, byte[] msg)
    {
        var pkg = SceneDataResp.Parser.ParseFrom(msg);
        LevelName = pkg.LevelName;
        CherryCntNeedChange = pkg.CherryCount;
    }

    private void handleItemPickedNumUpdate(CmdID cmdID, byte[] msg)
    {
        var pkg = ItemPickedNumUpdate.Parser.ParseFrom(msg);

        Debug.Log("拾取物数量更新->"+pkg.CherryCount.ToString());

        CherryCntNeedChange = pkg.CherryCount;
    }

    private void GetLevelData()
    {
        var pkg = new SceneDataReq();
        pkg.SceneID = GetSceneID();

        Network._Instance.PackAndSend(CmdID.CmdIDSceneDataReq,pkg.ToByteArray());
    }

    public void HandleSpawn(CmdID cmdID, byte[] msg)
    {
        ItemSpawnNotify pkg = ItemSpawnNotify.Parser.ParseFrom(msg);
        itemSpawnQueue.Enqueue(pkg);
    }

    public void HandleSessionEnd(CmdID cmdID, byte[] data)
    {
        SceneManager.LoadScene(0);
        var nc = GameObject.Find("NetworkControl");
        Destroy(nc);
        SessionEndNotify pkg = new SessionEndNotify();
        UnityEngine.Debug.Log("服务器终止连接:" + pkg.Msg);
        Utils.MessageBox(System.IntPtr.Zero, "服务器终止连接！", "提示", 0);
    }
}
