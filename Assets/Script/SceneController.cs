using Google.Protobuf;
using Net.Proto;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneController : MonoBehaviour
{
    public static SceneController Instance;

    [SerializeField] private bool IsMenu=false;
    [SerializeField] private GameObject SpawnCherryPrefab;
    [SerializeField] private Text LevelInfoText;
    [SerializeField] private Text DelayInfoText;

    private string LevelName;
    private int CherryCntNeedChange = -1;
    private static Queue<ItemSpawnNotify> itemSpawnQueue;


    // Start is called before the first frame update
    void Start()
    {
        Instance = this;

        //定义路由
        Network._Instance.AddHandleFunc(CmdID.CmdIDSceneDataResp, HandleLevelDataResp);
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
        //若是菜单，则直接脱出
        if (IsMenu)
        {
            return;
        }

        //生成物品
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

        //更新UI
        LevelInfoText.text = LevelName;

        //更新cherry count
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

    private void HandleLevelDataResp(CmdID cmdID, byte[] msg)
    {
        var pkg = LevelDataResp.Parser.ParseFrom(msg);
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
        var pkg = new LevelDataReq();
        pkg.SceneID = SceneManager.GetActiveScene().buildIndex;

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
