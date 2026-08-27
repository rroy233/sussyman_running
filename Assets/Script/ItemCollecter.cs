using Google.Protobuf;
using Net.Proto;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ItemCollecter : MonoBehaviour
{
    public static ItemCollecter Instance;

    private static int cherryCnt;
    private static Queue<ItemStatusUpdate> itemStatusUpdateQueue;

    [SerializeField] private Text cherryCntText;
    private const string CherryCntTextPrefix = "樱桃数：";

    private void Start()
    {
        Instance = this;
        itemStatusUpdateQueue = new Queue<ItemStatusUpdate>();

        // 定义路由
        Network._Instance.AddHandleFunc(CmdID.CmdIDItemStatusUpdate, HandleItemStatusUpdate);
    }

    public void AddCherryCnt(int delta)
    {
        cherryCnt += delta;
        cherryCntText.text = CherryCntTextPrefix + cherryCnt.ToString();
    }

    public void EditCherryCnt(int val)
    {
        cherryCnt = val;
        cherryCntText.text = CherryCntTextPrefix + cherryCnt.ToString();
    }

    private void Update()
    {
        // check update queue
        if (itemStatusUpdateQueue.Count > 0)
        {
            var pkg = itemStatusUpdateQueue.Dequeue();
            if (pkg != null)
            {
                var obj = GameObject.Find(pkg.Info.Name);
                if (obj == null)
                {
                    Debug.Log("[HandleItemStatusUpdate]GameObject不存在");
                }
                else
                {
                    switch (pkg.UpdateType)
                    {
                        case ItemStatusUpdate.Types.ItemUpdateType.Destroy:
                            if (pkg.Info.Tag == "Cherry")
                            {
                                Destroy(obj);
                            }
                            break;
                        default:
                            break;
                    }
                }
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        /**
         *  需要在碰撞对象勾选 is trigger 选项
         */
        if (collision.gameObject.CompareTag("Cherry"))
        {
            // 发送拾取通知
            ItemPickedNotify pkg = new ItemPickedNotify();
            pkg.Info = new ItemBasicInfo();
            pkg.Info.Name = collision.gameObject.name;
            pkg.Info.Tag = collision.gameObject.tag;
            pkg.Info.Type = collision.gameObject.GetType().ToString();
            pkg.Info.Layer = collision.gameObject.layer.ToString();
            // pkg.Info.PrefabName = collision.gameObject.GetPrefabDefinition().name;
            pkg.SceneID = SceneController.CurrentSceneID;
            pkg.TimeStampMicro = Utils.GetUnixMicro();

            Network._Instance.PackAndSend(CmdID.CmdIDItemPickedNotify, pkg.ToByteArray());
        }
    }

    public void HandleItemStatusUpdate(CmdID cmdID, byte[] msg)
    {
        var pkg = ItemStatusUpdate.Parser.ParseFrom(msg);
        Debug.Log("HandleItemStatusUpdate" + pkg.ToString());

        itemStatusUpdateQueue.Enqueue(pkg);
    }

    public int GetCherryCnt()
    {
        return cherryCnt;
    }
}
