using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform player;
    // Start is called before the first frame update
    void Start()
    {
        if (player == null && PlayerMovement.Instance != null)
        {
            player = PlayerMovement.Instance.transform;
        }
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = new Vector3(player.position.x,player.position.y,transform.position.z);
    }
}
