using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerLife : MonoBehaviour
{

    private Animator anim;
    private Rigidbody2D rb;

    [SerializeField] GameObject DeadLine;
    
    // Start is called before the first frame update
    private void Start()
    {
        anim= GetComponent<Animator>();
        rb= GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        if (DeadLine != null)
        {
            if(gameObject.transform.position.y < DeadLine.transform.position.y)
            {
                Die();
                RestartLevel();
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.gameObject.tag == "Trap")
        {
            Die();
        }
    }

    private void Die()
    {
        anim.SetTrigger("death");
        rb.bodyType = RigidbodyType2D.Static;
    }

    private void RestartLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        rb.bodyType = RigidbodyType2D.Dynamic;
    }
}
