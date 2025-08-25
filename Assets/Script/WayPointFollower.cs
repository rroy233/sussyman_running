using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WayPointFollower : MonoBehaviour
{
    [SerializeField] private GameObject[] waypoints;
    private int curIndex=0;

    [SerializeField] private float speed = 2f;

    // Update is called once per frame
    private void Update()
    {
        if (waypoints.Length == 0)
        {
            return;
        }

        // 比较两个二维向量的距离
        if (Vector2.Distance(waypoints[curIndex].transform.position, transform.position) < 0.1f)
        {
            //到头了，切换下一个前进目标
            curIndex  = (curIndex + 1)%waypoints.Length;
        }

        //移动当前平台
        // Time.deltaTime * speed作为每一帧前进的距离
        transform.position = Vector2.MoveTowards(transform.position, waypoints[curIndex].transform.position, Time.deltaTime * speed);
    }
}
