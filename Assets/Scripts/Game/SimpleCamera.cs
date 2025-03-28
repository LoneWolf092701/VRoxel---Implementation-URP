// Smooth camera control script with unrestricted rotation
using UnityEngine;

public class SimpleCamera : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 10.0f;
    [SerializeField] private float rotateSpeed = 40.0f;
    [SerializeField] private float elevateSpeed = 5.0f;
    [SerializeField] private float accelerationMultiplier = 3.0f;

    [Header("Smoothing")]
    [SerializeField] private float movementSmoothTime = 0.1f;
    [SerializeField] private float rotationSmoothTime = 0.05f;

    [Header("Debug Visualization")]
    [SerializeField] private bool showChunkBoundaries = true;
    [SerializeField] private bool showChunkCorners = true;
    [SerializeField] private float boundaryLineThickness = 0.1f;
    [SerializeField] private Color boundaryColor = new Color(1f, 0.5f, 0f, 0.8f);
    [SerializeField] private Color cornerColor = new Color(1f, 0f, 0f, 1f);

    private Camera mainCamera;
    private WFC.Testing.WFCTestController wfcController;

    // Smoothing variables
    private Vector3 moveVelocity;
    private Vector3 targetPosition;
    private Vector3 rotationVelocity;

    private void Start()
    {
        mainCamera = GetComponentInChildren<Camera>();
        wfcController = FindFirstObjectByType<WFC.Testing.WFCTestController>();

        // Initialize position variable
        targetPosition = transform.position;

        // Lock cursor for FPS-style controls
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        // Handle movement input
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        float elevate = 0;

        if (Input.GetKey(KeyCode.Space)) elevate += 1.0f;
        if (Input.GetKey(KeyCode.LeftShift)) elevate -= 1.0f;

        // Acceleration with Left Control
        float speedMultiplier = Input.GetKey(KeyCode.LeftControl) ? accelerationMultiplier : 1.0f;

        // Calculate movement in local space
        Vector3 moveDirection = new Vector3(horizontal, 0, vertical);

        // Normalize if moving in multiple directions
        if (moveDirection.magnitude > 1f)
            moveDirection.Normalize();

        // Scale by speed
        moveDirection *= moveSpeed * speedMultiplier;

        // Transform to world space based on current orientation
        moveDirection = transform.TransformDirection(moveDirection);
        moveDirection.y = elevate * elevateSpeed * speedMultiplier; // Keep y-movement world-aligned

        // Update target position
        targetPosition = transform.position + moveDirection * Time.deltaTime;

        // Smooth movement
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref moveVelocity, movementSmoothTime);

        // Handle rotation input
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        // Apply rotation directly to the transforms like in the original code,
        // but store the desired rotation for smoothing
        Quaternion targetBodyRotation = transform.rotation * Quaternion.Euler(0, mouseX * rotateSpeed * Time.deltaTime, 0);
        Quaternion targetCameraRotation = mainCamera.transform.rotation * Quaternion.Euler(-mouseY * rotateSpeed * Time.deltaTime, 0, 0);

        // Apply smooth rotation
        transform.rotation = Quaternion.Slerp(transform.rotation, targetBodyRotation, 1 - Mathf.Exp(-rotationSmoothTime * 30 * Time.deltaTime));
        mainCamera.transform.rotation = Quaternion.Slerp(mainCamera.transform.rotation, targetCameraRotation, 1 - Mathf.Exp(-rotationSmoothTime * 30 * Time.deltaTime));

        // Toggle visualization modes
        if (Input.GetKeyDown(KeyCode.B)) showChunkBoundaries = !showChunkBoundaries;
        if (Input.GetKeyDown(KeyCode.C)) showChunkCorners = !showChunkCorners;

        // Escape to unlock cursor
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ?
                CursorLockMode.None : CursorLockMode.Locked;
        }
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 300, 20), "WASD: Move, Space/Shift: Up/Down, Mouse: Look");
        GUI.Label(new Rect(10, 30, 300, 20), "B: Toggle Boundaries, C: Toggle Corners, Esc: Cursor");
        GUI.Label(new Rect(10, 50, 300, 20), $"Position: {transform.position}");
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || wfcController == null) return;

        var chunks = wfcController.GetChunks();
        int chunkSize = wfcController.ChunkSize;

        foreach (var chunkEntry in chunks)
        {
            Vector3Int chunkPos = chunkEntry.Key;

            Vector3 worldPos = new Vector3(
                chunkPos.x * chunkSize,
                chunkPos.y * chunkSize,
                chunkPos.z * chunkSize
            );

            if (showChunkBoundaries)
            {
                // Draw chunk boundaries
                Gizmos.color = boundaryColor;
                Gizmos.DrawWireCube(
                    worldPos + new Vector3(chunkSize / 2f, chunkSize / 2f, chunkSize / 2f),
                    new Vector3(chunkSize, chunkSize, chunkSize)
                );
            }

            if (showChunkCorners)
            {
                // Draw corner markers
                Gizmos.color = cornerColor;
                float cornerSize = 0.5f;

                // Draw all 8 corners of the chunk
                for (int x = 0; x <= 1; x++)
                {
                    for (int y = 0; y <= 1; y++)
                    {
                        for (int z = 0; z <= 1; z++)
                        {
                            Vector3 cornerPos = worldPos + new Vector3(
                                x * chunkSize,
                                y * chunkSize,
                                z * chunkSize
                            );

                            Gizmos.DrawSphere(cornerPos, cornerSize);
                        }
                    }
                }
            }
        }
    }
}