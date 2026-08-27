using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform player;

    private void Start()
    {
        ResolvePlayer();
    }

    private void LateUpdate()
    {
        ResolvePlayer();
        if (player == null)
        {
            return;
        }

        transform.position = new Vector3(player.position.x,player.position.y,transform.position.z);
    }

    private void ResolvePlayer()
    {
        if (player != null)
        {
            return;
        }

        if (PlayerMovement.Instance != null)
        {
            player = PlayerMovement.Instance.transform;
            return;
        }

        var playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
        }
    }
}
