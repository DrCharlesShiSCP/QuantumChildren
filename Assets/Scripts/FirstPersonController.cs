using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class FirstPersonController : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private bool synchronizeChildCameras = true;
    [SerializeField] private bool maintainCameraEyeHeight = true;
    [SerializeField] private float standingCameraHeight = 1.65f;
    [SerializeField] private float crouchingCameraHeight = 1.0f;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float crouchSpeed = 2.5f;
    [SerializeField] private float acceleration = 18f;
    [SerializeField] private float airControl = 0.35f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -24f;
    [SerializeField] private float groundedStickForce = -2f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.08f;
    [SerializeField] private float gamepadLookSensitivity = 140f;
    [SerializeField] private float verticalLookLimit = 85f;
    [SerializeField] private bool invertY;

    [Header("Crouch")]
    [SerializeField] private bool enableCrouch = true;
    [SerializeField] private float standingHeight = 1.8f;
    [SerializeField] private float crouchingHeight = 1.1f;
    [SerializeField] private float crouchBlendSpeed = 12f;

    [Header("Input Actions")]
    [Tooltip("Optional. Leave empty to use built-in WASD, mouse, Space, Left Shift, and C bindings.")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference sprintAction;
    [SerializeField] private InputActionReference crouchAction;
    [SerializeField] private InputActionReference interactAction;
    [SerializeField] private InputActionReference attackAction;

    [Header("Pickup")]
    [SerializeField] private string pickupTag = "Pickup";
    [SerializeField] private float pickupRange = 3f;
    [SerializeField] private float pickupRadius = 0.25f;
    [SerializeField] private float holdDistance = 2f;
    [SerializeField] private float holdMoveSpeed = 18f;
    [SerializeField] private float maxHoldDistance = 4f;
    [SerializeField] private float throwForce = 12f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursorOnEnable = true;
    [SerializeField] private bool unlockCursorWithEscape = true;

    private CharacterController characterController;
    private InputAction fallbackMoveAction;
    private InputAction fallbackLookAction;
    private InputAction fallbackJumpAction;
    private InputAction fallbackSprintAction;
    private InputAction fallbackCrouchAction;
    private InputAction fallbackInteractAction;
    private InputAction fallbackAttackAction;
    private Transform[] controlledCameras = System.Array.Empty<Transform>();
    private Collider[] heldColliders = System.Array.Empty<Collider>();
    private Vector3 horizontalVelocity;
    private Vector3 cameraLocalPosition;
    private float verticalVelocity;
    private float pitch;
    private float cameraYaw;
    private float cameraRoll;
    private Rigidbody heldRigidbody;
    private bool heldUseGravity;
    private bool heldWasKinematic;

    private InputAction MoveInput => moveAction != null ? moveAction.action : fallbackMoveAction;
    private InputAction LookInput => lookAction != null ? lookAction.action : fallbackLookAction;
    private InputAction JumpInput => jumpAction != null ? jumpAction.action : fallbackJumpAction;
    private InputAction SprintInput => sprintAction != null ? sprintAction.action : fallbackSprintAction;
    private InputAction CrouchInput => crouchAction != null ? crouchAction.action : fallbackCrouchAction;
    private InputAction InteractInput => interactAction != null ? interactAction.action : fallbackInteractAction;
    private InputAction AttackInput => attackAction != null ? attackAction.action : fallbackAttackAction;

    private void Reset()
    {
        characterController = GetComponent<CharacterController>();
        characterController.height = standingHeight;
        characterController.center = new Vector3(0f, standingHeight * 0.5f, 0f);

        Camera[] childCameras = GetComponentsInChildren<Camera>(true);
        if (childCameras.Length > 0)
        {
            cameraRoot = childCameras[0].transform;
        }
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (cameraRoot == null)
        {
            Camera[] childCameras = GetComponentsInChildren<Camera>(true);
            if (childCameras.Length > 0)
            {
                cameraRoot = childCameras[0].transform;
            }
        }

        controlledCameras = FindControlledCameras();

        if (cameraRoot != null)
        {
            cameraLocalPosition = cameraRoot.localPosition;
            Vector3 cameraAngles = cameraRoot.localEulerAngles;
            pitch = NormalizePitch(cameraAngles.x);
            cameraYaw = cameraAngles.y;
            cameraRoll = cameraAngles.z;

            if (maintainCameraEyeHeight)
            {
                cameraLocalPosition.y = standingCameraHeight;
                ApplyCameraPose();
            }
        }

        CreateFallbackActions();
    }

    private void OnEnable()
    {
        EnableInput(MoveInput);
        EnableInput(LookInput);
        EnableInput(JumpInput);
        EnableInput(SprintInput);
        EnableInput(CrouchInput);
        EnableInput(InteractInput);
        EnableInput(AttackInput);

        if (lockCursorOnEnable)
        {
            LockCursor();
        }
    }

    private void OnDisable()
    {
        DisableInput(MoveInput);
        DisableInput(LookInput);
        DisableInput(JumpInput);
        DisableInput(SprintInput);
        DisableInput(CrouchInput);
        DisableInput(InteractInput);
        DisableInput(AttackInput);

        ReleaseHeldObject();

        UnlockCursor();
    }

    private void OnDestroy()
    {
        fallbackMoveAction?.Dispose();
        fallbackLookAction?.Dispose();
        fallbackJumpAction?.Dispose();
        fallbackSprintAction?.Dispose();
        fallbackCrouchAction?.Dispose();
        fallbackInteractAction?.Dispose();
        fallbackAttackAction?.Dispose();
    }

    private void Update()
    {
        if (unlockCursorWithEscape && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            UnlockCursor();
        }

        UpdateLook();
        UpdateCrouch();
        UpdateMovement();
        UpdatePickupInput();
    }

    private void FixedUpdate()
    {
        UpdateHeldObject();
    }

    private void UpdateLook()
    {
        if (cameraRoot == null)
        {
            return;
        }

        Vector2 look = LookInput.ReadValue<Vector2>();
        bool usingMouse = LookInput.activeControl != null && LookInput.activeControl.device is Mouse;

        float sensitivity = usingMouse ? mouseSensitivity : gamepadLookSensitivity * Time.deltaTime;
        float ySign = invertY ? 1f : -1f;

        transform.Rotate(Vector3.up, look.x * sensitivity, Space.Self);
        pitch = Mathf.Clamp(pitch + look.y * sensitivity * ySign, -verticalLookLimit, verticalLookLimit);
        ApplyCameraPose();
    }

    private void UpdateMovement()
    {
        Vector2 move = MoveInput.ReadValue<Vector2>();
        move = Vector2.ClampMagnitude(move, 1f);

        bool isGrounded = characterController.isGrounded;
        bool wantsSprint = SprintInput.IsPressed();
        bool isCrouching = IsCrouching();
        float targetSpeed = isCrouching ? crouchSpeed : wantsSprint ? sprintSpeed : walkSpeed;

        Vector3 moveRight = transform.right;
        Vector3 moveForward = transform.forward;

        if (cameraRoot != null)
        {
            moveForward = Vector3.ProjectOnPlane(cameraRoot.forward, Vector3.up).normalized;
            moveRight = Vector3.ProjectOnPlane(cameraRoot.right, Vector3.up).normalized;
        }

        Vector3 targetVelocity = (moveRight * move.x + moveForward * move.y) * targetSpeed;
        float control = isGrounded ? 1f : airControl;
        horizontalVelocity = Vector3.Lerp(horizontalVelocity, targetVelocity, acceleration * control * Time.deltaTime);

        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = groundedStickForce;
        }

        if (isGrounded && JumpInput.WasPressedThisFrame() && !isCrouching)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity += gravity * Time.deltaTime;
        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);
    }

    private void UpdateCrouch()
    {
        if (!enableCrouch)
        {
            return;
        }

        bool isCrouching = IsCrouching();
        float targetHeight = isCrouching ? crouchingHeight : standingHeight;
        float targetCameraHeight = isCrouching ? crouchingCameraHeight : standingCameraHeight;

        characterController.height = Mathf.Lerp(characterController.height, targetHeight, crouchBlendSpeed * Time.deltaTime);
        characterController.center = new Vector3(0f, characterController.height * 0.5f, 0f);

        if (maintainCameraEyeHeight && cameraRoot != null)
        {
            cameraLocalPosition.y = Mathf.Lerp(cameraLocalPosition.y, targetCameraHeight, crouchBlendSpeed * Time.deltaTime);
            ApplyCameraPose();
        }
    }

    private bool IsCrouching()
    {
        return enableCrouch && CrouchInput.IsPressed();
    }

    private void UpdatePickupInput()
    {
        if (InteractInput.WasPressedThisFrame())
        {
            if (heldRigidbody != null)
            {
                ReleaseHeldObject();
            }
            else
            {
                TryPickupObject();
            }
        }

        if (heldRigidbody != null && AttackInput.WasPressedThisFrame())
        {
            ThrowHeldObject();
        }
    }

    private void UpdateHeldObject()
    {
        if (heldRigidbody == null)
        {
            return;
        }

        Vector3 holdTarget = GetHoldTarget();
        Vector3 toTarget = holdTarget - heldRigidbody.worldCenterOfMass;

        if (toTarget.magnitude > maxHoldDistance)
        {
            ReleaseHeldObject();
            return;
        }

        heldRigidbody.linearVelocity = toTarget * holdMoveSpeed;
        heldRigidbody.angularVelocity = Vector3.zero;
    }

    private void TryPickupObject()
    {
        Vector3 rayOrigin = cameraRoot != null ? cameraRoot.position : transform.position + Vector3.up * standingCameraHeight;
        Vector3 rayDirection = cameraRoot != null ? cameraRoot.forward : transform.forward;
        Ray ray = new Ray(rayOrigin, rayDirection);

        if (!Physics.SphereCast(ray, pickupRadius, out RaycastHit hit, pickupRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        Rigidbody candidate = ResolvePickupRigidbody(hit.collider);
        if (candidate == null)
        {
            return;
        }

        heldRigidbody = candidate;
        heldUseGravity = heldRigidbody.useGravity;
        heldWasKinematic = heldRigidbody.isKinematic;
        heldColliders = heldRigidbody.GetComponentsInChildren<Collider>(true);

        heldRigidbody.useGravity = false;
        heldRigidbody.isKinematic = false;
        heldRigidbody.linearVelocity = Vector3.zero;
        heldRigidbody.angularVelocity = Vector3.zero;

        SetHeldCollisionIgnored(true);
    }

    private Rigidbody ResolvePickupRigidbody(Collider hitCollider)
    {
        Transform current = hitCollider.transform;

        while (current != null)
        {
            if (current.CompareTag(pickupTag))
            {
                Rigidbody pickupBody = current.GetComponent<Rigidbody>();
                if (pickupBody != null)
                {
                    return pickupBody;
                }

                if (hitCollider.attachedRigidbody != null)
                {
                    return hitCollider.attachedRigidbody;
                }

                return current.GetComponentInChildren<Rigidbody>();
            }

            current = current.parent;
        }

        return null;
    }

    private void ThrowHeldObject()
    {
        if (heldRigidbody == null)
        {
            return;
        }

        Rigidbody thrownBody = heldRigidbody;
        ReleaseHeldObject();

        Vector3 throwDirection = cameraRoot != null ? cameraRoot.forward : transform.forward;
        thrownBody.useGravity = true;
        thrownBody.isKinematic = false;
        thrownBody.linearVelocity = throwDirection * throwForce;
    }

    private void ReleaseHeldObject()
    {
        if (heldRigidbody == null)
        {
            return;
        }

        SetHeldCollisionIgnored(false);
        heldRigidbody.useGravity = heldUseGravity;
        heldRigidbody.isKinematic = heldWasKinematic;
        heldRigidbody = null;
        heldColliders = System.Array.Empty<Collider>();
    }

    private void SetHeldCollisionIgnored(bool ignored)
    {
        for (int i = 0; i < heldColliders.Length; i++)
        {
            if (heldColliders[i] == null)
            {
                continue;
            }

            Physics.IgnoreCollision(characterController, heldColliders[i], ignored);
        }
    }

    private Vector3 GetHoldTarget()
    {
        Vector3 origin = cameraRoot != null ? cameraRoot.position : transform.position + Vector3.up * standingCameraHeight;
        Vector3 direction = cameraRoot != null ? cameraRoot.forward : transform.forward;
        return origin + direction * holdDistance;
    }

    private void CreateFallbackActions()
    {
        fallbackMoveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        fallbackMoveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/s")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/a")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/d")
            .With("Right", "<Keyboard>/rightArrow");
        fallbackMoveAction.AddBinding("<Gamepad>/leftStick");

        fallbackLookAction = new InputAction("Look", InputActionType.Value, expectedControlType: "Vector2");
        fallbackLookAction.AddBinding("<Mouse>/delta");
        fallbackLookAction.AddBinding("<Gamepad>/rightStick");

        fallbackJumpAction = new InputAction("Jump", InputActionType.Button);
        fallbackJumpAction.AddBinding("<Keyboard>/space");
        fallbackJumpAction.AddBinding("<Gamepad>/buttonSouth");

        fallbackSprintAction = new InputAction("Sprint", InputActionType.Button);
        fallbackSprintAction.AddBinding("<Keyboard>/leftShift");
        fallbackSprintAction.AddBinding("<Gamepad>/leftStickPress");

        fallbackCrouchAction = new InputAction("Crouch", InputActionType.Button);
        fallbackCrouchAction.AddBinding("<Keyboard>/c");
        fallbackCrouchAction.AddBinding("<Gamepad>/buttonEast");

        fallbackInteractAction = new InputAction("Interact", InputActionType.Button);
        fallbackInteractAction.AddBinding("<Keyboard>/e");
        fallbackInteractAction.AddBinding("<Gamepad>/buttonNorth");

        fallbackAttackAction = new InputAction("Attack", InputActionType.Button);
        fallbackAttackAction.AddBinding("<Mouse>/leftButton");
        fallbackAttackAction.AddBinding("<Gamepad>/buttonWest");
    }

    private Transform[] FindControlledCameras()
    {
        if (!synchronizeChildCameras)
        {
            return cameraRoot != null ? new[] { cameraRoot } : System.Array.Empty<Transform>();
        }

        Camera[] childCameras = GetComponentsInChildren<Camera>(true);
        Transform[] cameraTransforms = new Transform[childCameras.Length];
        for (int i = 0; i < childCameras.Length; i++)
        {
            cameraTransforms[i] = childCameras[i].transform;
        }

        return cameraTransforms;
    }

    private void ApplyCameraPose()
    {
        Quaternion rotation = Quaternion.Euler(pitch, cameraYaw, cameraRoll);

        for (int i = 0; i < controlledCameras.Length; i++)
        {
            if (controlledCameras[i] == null)
            {
                continue;
            }

            controlledCameras[i].localRotation = rotation;

            if (maintainCameraEyeHeight)
            {
                controlledCameras[i].localPosition = cameraLocalPosition;
            }
        }
    }

    private static void EnableInput(InputAction action)
    {
        if (action != null && !action.enabled)
        {
            action.Enable();
        }
    }

    private static void DisableInput(InputAction action)
    {
        if (action != null && action.enabled)
        {
            action.Disable();
        }
    }

    private static float NormalizePitch(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnValidate()
    {
        walkSpeed = Mathf.Max(0f, walkSpeed);
        sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
        crouchSpeed = Mathf.Max(0f, crouchSpeed);
        acceleration = Mathf.Max(0f, acceleration);
        airControl = Mathf.Clamp01(airControl);
        jumpHeight = Mathf.Max(0f, jumpHeight);
        gravity = Mathf.Min(-0.01f, gravity);
        groundedStickForce = Mathf.Min(0f, groundedStickForce);
        mouseSensitivity = Mathf.Max(0f, mouseSensitivity);
        gamepadLookSensitivity = Mathf.Max(0f, gamepadLookSensitivity);
        verticalLookLimit = Mathf.Clamp(verticalLookLimit, 1f, 89f);
        standingHeight = Mathf.Max(0.1f, standingHeight);
        crouchingHeight = Mathf.Clamp(crouchingHeight, 0.1f, standingHeight);
        standingCameraHeight = Mathf.Max(0f, standingCameraHeight);
        crouchingCameraHeight = Mathf.Max(0f, crouchingCameraHeight);
        crouchBlendSpeed = Mathf.Max(0f, crouchBlendSpeed);
        pickupRange = Mathf.Max(0.1f, pickupRange);
        pickupRadius = Mathf.Max(0f, pickupRadius);
        holdDistance = Mathf.Max(0.1f, holdDistance);
        holdMoveSpeed = Mathf.Max(0.1f, holdMoveSpeed);
        maxHoldDistance = Mathf.Max(holdDistance, maxHoldDistance);
        throwForce = Mathf.Max(0f, throwForce);
    }
}
