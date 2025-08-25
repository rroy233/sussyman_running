using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class StartMenu : MonoBehaviour
{

    private void Start()
    {
        
    }

    public async void StartGame()
    {
        GameObject.Find("StartButton").GetComponent<Button>().interactable = false;
        //server
        var value = GameObject.Find("ServerSelect").GetComponent<Dropdown>().value;
        var server = "";

        if (value == 0)
        {
            server = "101.32.15.237";
        }
        else
        {
            server = "127.0.0.1";
        }

        //port
        var port = GameObject.Find("portInputField").GetComponent<InputField>().text;

        Debug.Log("服务器：" + server + ":" + port);

        var nc = new GameObject("NetworkControl");
        nc.AddComponent<Network>();
        var ncNetwork = nc.GetComponent<Network>();
        ncNetwork.init(server, int.Parse(port));

        Debug.Log("startMenu.cs client.init() - ok");


        var success = false;
        await Task.Run(async () =>
        {
            Debug.Log("task - 等待检查sessionID");
            await Task.Delay(500);
            if (ncNetwork.SessionID == "")
            {
                Debug.Log("task - sessionID="+ ncNetwork.SessionID);
                Utils.MessageBox(System.IntPtr.Zero, "连接超时！", "提示", 0);
                return;
            }
            Debug.Log("task - sessionID检查完毕");
            success = true;
        });

        if (success)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
        }
        else
        {
            Destroy(nc);
        }
        GameObject.Find("StartButton").GetComponent<Button>().interactable = true;
    }
}
