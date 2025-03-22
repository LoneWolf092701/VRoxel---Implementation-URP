// Optional: Simple camera control script
using UnityEngine;

public class SimpleCamera : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 10.0f;
    [SerializeField] private float rotateSpeed = 40.0f;
    [SerializeField] private float elevateSpeed = 5.0f;
    [SerializeField] private float accelerationMultiplier = 3.0f;

    [Header("Debug Visualization")]
    [SerializeField] private bool showChunkBoundaries = true;
    [SerializeField] private bool showChunkCorners = true;
    [SerializeField] private float boundaryLineThickness = 0.1f;
    [SerializeField] private Color boundaryColor = new Color(1f, 0.5f, 0f, 0.8f);
    [SerializeField] private Color cornerColor = new Color(1f, 0f, 0f, 1f);

    private Camera mainCamera;
    private WFC.Testing.WFCTestController wfcController;

    private void Start()
    {
        mainCamera = GetComponentInChildren<Camera>();
        wfcController = FindFirstObjectByType<WFC.Testing.WFCTestController>();

        // Lock cursor for FPS-style controls
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        // Movement
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        float elevate = 0;

        if (Input.GetKey(KeyCode.Space)) elevate += 1.0f;
        if (Input.GetKey(KeyCode.LeftShift)) elevate -= 1.0f;

        // Acceleration with Left Control
        float speedMultiplier = Input.GetKey(KeyCode.LeftControl) ? accelerationMultiplier : 1.0f;

        // Apply movement
        transform.Translate(new Vector3(horizontal, elevate, vertical) * moveSpeed * speedMultiplier * Time.deltaTime);

        // Rotation with mouse
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        transform.Rotate(Vector3.up, mouseX * rotateSpeed * Time.deltaTime);
        mainCamera.transform.Rotate(Vector3.right, -mouseY * rotateSpeed * Time.deltaTime);

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