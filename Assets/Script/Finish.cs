using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Finish : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.name == "Player")
        {
            CompleteLevel();
        }
    }

    private void CompleteLevel()
    {
        // Wait for server to broadcast all-finished; do not change scene locally.
        Debug.Log("[Finish] Local player reached end; waiting for server to advance.");
    }
}
