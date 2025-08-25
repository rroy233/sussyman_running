using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StickyPlatform : MonoBehaviour
{
    //触发上层的碰撞盒
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.name == "Player")
        {
            //将玩家的position的父级设为该移动平台
            //实现玩家跟随平台移动，自身也能移动
            collision.gameObject.transform.transform.SetParent(transform);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.name == "Player")
        {
            collision.gameObject.transform.transform.SetParent(null);
        }
    }
}
