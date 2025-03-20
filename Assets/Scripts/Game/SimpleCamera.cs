// Optional: Simple camera control script
using UnityEngine;

public class SimpleCamera : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float zoomSpeed = 10f;
    [SerializeField] private float rotateSpeed = 100f;

    void Update()
    {
        // WASD movement
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        transform.Translate(new Vector3(horizontal, 0, vertical) * moveSpeed * Time.deltaTime);

        // QE for up/down
        if (Input.GetKey(KeyCode.Q)) transform.Translate(Vector3.down * moveSpeed * Time.deltaTime);
        if (Input.GetKey(KeyCode.E)) transform.Translate(Vector3.up * moveSpeed * Time.deltaTime);

        // Zoom with scroll wheel
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        transform.Translate(Vector3.forward * scroll * zoomSpeed * Time.deltaTime);

        // Rotate with right mouse button
        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            transform.RotateAround(transform.position, Vector3.up, mouseX * rotateSpeed * Time.deltaTime);
            transform.RotateAround(transform.position, transform.right, -mouseY * rotateSpeed * Time.deltaTime);
        }
    }
}