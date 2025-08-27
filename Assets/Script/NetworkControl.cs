using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NetworkControl : MonoBehaviour
{
    public Network NetClient;
    public string addr;
    public int port;

    private void Awake()
    {
        
    }

    private void OnDestroy()
    {
        NetClient.CloseConn();
    }

    // Start is called before the first frame update
    void Start()
    {
        DontDestroyOnLoad(gameObject);
        gameObject.AddComponent<Network>();
        Network._Instance = gameObject.GetComponent<Network>();
        NetClient = Network._Instance;
        Debug.Log("[NetworkControl]Start() done!");
    }

    public void connect(string addr,int port)
    {
        NetClient.init(addr, port);
    }

    public string GetSessionID()
    {
        return NetClient.SessionID;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
