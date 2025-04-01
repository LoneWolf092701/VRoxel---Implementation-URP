using UnityEngine;

/// <summary>
/// A first-person controller for Unity that uses gravity-based movement and 
/// directional control based on camera view.
/// </summary>
public class SimpleCamera : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("The maximum movement speed")]
    [SerializeField] private float moveSpeed = 10.0f;
    [Tooltip("Acceleration rate - how quickly the player reaches maximum speed")]
    [SerializeField] private float acceleration = 15.0f;
    [Tooltip("Deceleration rate - how quickly the player stops")]
    [SerializeField] private float deceleration = 20.0f;
    [Tooltip("Speed multiplier when sprinting")]
    [SerializeField] private float sprintSpeedMultiplier = 2.5f;

    [Header("Gravity Settings")]
    [Tooltip("Enable gravity")]
    [SerializeField] private bool useGravity = false;
    [Tooltip("Custom gravity strength (higher values = faster falling)")]
    [SerializeField] private float gravityStrength = 9.81f;
    [Tooltip("Jump height")]
    [SerializeField] private float jumpHeight = 2.0f; // New
    [Tooltip("Air control multiplier")]
    [SerializeField] private float airControlMultiplier = 0.5f;

    [Header("Look Settings")]
    [Tooltip("Mouse sensitivity for looking around")]
    [SerializeField] private float lookSensitivity = 3.0f;
    [Tooltip("Option to invert Y axis")]
    [SerializeField] private bool invertYAxis = false;
    [Tooltip("Enable camera rotation around the X-axis")]
    [SerializeField] private bool allowXAxisCameraRotation = true; // Now true by default

    // Component references
    private Camera playerCamera;
    private CharacterController characterController;
    private Transform cameraTransform;

    // Movement variables
    private Vector3 moveDirection;
    private Vector3 verticalMovement;
    private float currentSpeed;
    private bool isGrounded;
    private bool isFalling;

    // Look variables
    private float rotationX = 0;
    private float rotationY = 0;

    private void Awake()
    {
        // Get component references
        characterController = GetComponent<CharacterController>();
        playerCamera = GetComponentInChildren<Camera>();
        cameraTransform = playerCamera.transform;

        // Lock and hide cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        // Handle all player input and movement
        HandleMovementInput();
        HandleMouseLook();

        // Apply all movement
        ApplyFinalMovement();

        // Allow cursor toggle with Escape key
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ?
                CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = !Cursor.visible;
        }
    }

    private void HandleMovementInput()
    {
        // Get input values
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        bool isSprinting = Input.GetKey(KeyCode.LeftShift);

        // Check if player is grounded
        isGrounded = characterController.isGrounded;
        isFalling = !isGrounded && verticalMovement.y < 0;

        // Calculate move direction based on camera orientation
        Vector3 cameraForward = cameraTransform.forward;
        Vector3 cameraRight = cameraTransform.right;

        // Flatten the camera direction to horizontal plane
        cameraForward.y = 0;
        cameraForward.Normalize();

        // Calculate direction from input
        Vector3 targetDirection = (cameraForward * vertical + cameraRight * horizontal).normalized;

        // Apply air control if not grounded
        if (!isGrounded)
        {
            targetDirection *= airControlMultiplier;
        }

        // Calculate vertical movement
        if (useGravity)  // Apply gravity logic only if useGravity is enabled
        {
            if (isGrounded)
            {
                // Reset vertical velocity when grounded
                verticalMovement.y = 0f;

                if (Input.GetKeyDown(KeyCode.Space))
                {
                    // Calculate jump force based on gravity and desired jump height
                    verticalMovement.y = Mathf.Sqrt(jumpHeight * 2f * gravityStrength);
                }
            }
            else
            {
                // Apply gravity when not grounded
                verticalMovement.y -= gravityStrength * Time.deltaTime;
            }
        }
        else  // if gravity is disabled
        {
            verticalMovement.y = 0; // no gravity or jump, so vertical movement is always 0.
            if (Input.GetKey(KeyCode.Space)) // If you want to add some vertical movement when the gravity is disabled
            {
                verticalMovement.y = 2.0f;
            }

            if (Input.GetKey(KeyCode.LeftControl)) // If you want to add some vertical movement when the gravity is disabled
            {
                verticalMovement.y = -2.0f;
            }
        }

        // Calculate target speed based on sprint state
        float targetSpeed = isSprinting ? moveSpeed * sprintSpeedMultiplier : moveSpeed;

        // Smooth acceleration/deceleration
        if (targetDirection.magnitude > 0.1f)
        {
            // Accelerate
            currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, acceleration * Time.deltaTime);
        }
        else
        {
            // Decelerate
            currentSpeed = Mathf.Lerp(currentSpeed, 0, deceleration * Time.deltaTime);
        }

        // Save the move direction
        moveDirection = targetDirection * currentSpeed;
    }

    private void HandleMouseLook()
    {
        // Get mouse input with increased sensitivity for Unity 6
        float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;

        // Apply inversion if enabled
        mouseY = invertYAxis ? mouseY : -mouseY;

        // Update rotation values directly
        rotationX += mouseY;
        rotationY += mouseX;

        // No clamping for full 360 degree rotation

        // Apply rotations directly - this fixed approach works better with Unity 6
        // X rotation (looking up/down) is applied to the camera

        if (allowXAxisCameraRotation)
        {
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
        }
        else
        {
            //This keeps the camera from rotating on the X axis.
            rotationX = Mathf.Clamp(rotationX, -90, 90);
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
        }


        // Y rotation (looking left/right) is applied to the player body
        transform.rotation = Quaternion.Euler(0, rotationY, 0);
    }

    private void ApplyFinalMovement()
    {
        // Combine horizontal movement and vertical velocity
        Vector3 movement = (moveDirection + verticalMovement);

        // Apply movement to character controller
        characterController.Move(movement * Time.deltaTime);
    }
}