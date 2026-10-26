using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Move : MonoBehaviour
{
    public Vector3 movementSpeed;
    public Space space;

    // Update is called once per frame
    private void Update()
    {
        transform.Translate(movementSpeed * Time.deltaTime, space);
    }
}