using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MouseCameraController : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Camera Movement")]
    public float distance = 10f;
    public float minDistance = 2f;
    public float maxDistance = 30f;
    public float rotationSpeed = 180f;
    public float zoomSpeed = 6f;
    public float panSpeed = 0.02f;
    public float smoothTime = 0.08f;

    [Header("Focus")]
    public Vector3 focusOffset = new Vector3(0f, 2f, 0f);

    [Header("Debug")]
    public bool controllerActive;
    public bool rightMousePressed;
    public bool pointerOverUi;
    public string currentTargetName;
    public float CurrentDistance => distance;

    private Vector3 targetPosition;
    private Vector3 smoothedTargetPosition;
    private Vector3 targetPositionVelocity;
    private float yaw;
    private float pitch = 20f;
    private float targetDistance;
    private float distanceVelocity;

    private void Awake()
    {
        EnsureTarget();
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = NormalizePitch(euler.x);
        targetDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        smoothedTargetPosition = targetPosition;
        FocusNow();
    }

    private void OnValidate()
    {
        minDistance = Mathf.Max(0.1f, minDistance);
        maxDistance = Mathf.Max(minDistance + 0.1f, maxDistance);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
        zoomSpeed = Mathf.Max(0f, zoomSpeed);
        panSpeed = Mathf.Max(0f, panSpeed);
        smoothTime = Mathf.Max(0.001f, smoothTime);
    }

    private void LateUpdate()
    {
        EnsureTarget();
        controllerActive = isActiveAndEnabled;
        rightMousePressed = IsRightMousePressed();
        currentTargetName = target != null ? target.name : "None";

        if (IsEscapePressedThisFrame())
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (IsFocusPressedThisFrame())
        {
            FocusOnSimulationRoot();
        }

        pointerOverUi = IsPointerOverUi();
        if (!pointerOverUi)
        {
            HandleInput();
        }

        smoothedTargetPosition = Vector3.SmoothDamp(
            smoothedTargetPosition,
            targetPosition,
            ref targetPositionVelocity,
            smoothTime
        );

        distance = Mathf.SmoothDamp(distance, targetDistance, ref distanceVelocity, smoothTime);
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = smoothedTargetPosition - rotation * Vector3.forward * distance;
        transform.rotation = rotation;
    }

    private void HandleInput()
    {
        Vector2 mouseDelta = GetMouseDelta();
        bool shiftHeld = IsShiftHeld();
        bool rotate = rightMousePressed && !shiftHeld;
        bool pan = IsMiddleMousePressed() || (rightMousePressed && shiftHeld);

        if (rotate)
        {
            yaw += mouseDelta.x * rotationSpeed * 0.01f;
            pitch -= mouseDelta.y * rotationSpeed * 0.01f;
            pitch = Mathf.Clamp(pitch, -80f, 80f);
        }

        if (pan)
        {
            Vector3 right = transform.right;
            Vector3 up = transform.up;
            float scale = Mathf.Max(0.1f, distance) * panSpeed;
            Vector3 panDelta =
                -right * mouseDelta.x * scale
                - up * mouseDelta.y * scale;
            targetPosition += panDelta;
        }

        float scroll = GetScrollY();
        if (Mathf.Abs(scroll) > 0.001f)
        {
            targetDistance = Mathf.Clamp(targetDistance - scroll * zoomSpeed, minDistance, maxDistance);
        }
    }

    private void FocusNow()
    {
        EnsureTarget();
        targetPosition = target != null ? target.position + focusOffset : focusOffset;
        smoothedTargetPosition = targetPosition;
        targetDistance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    private void FocusOnSimulationRoot()
    {
        GameObject existing = GameObject.Find("PresentationCameraTarget");
        if (existing == null)
        {
            existing = new GameObject("PresentationCameraTarget");
        }

        existing.transform.position = FindPresentationCenter();
        target = existing.transform;
        FocusNow();
    }

    private void EnsureTarget()
    {
        if (target != null)
        {
            if (targetPosition == Vector3.zero)
            {
                targetPosition = target.position + focusOffset;
            }
            return;
        }

        GameObject existing = GameObject.Find("PresentationCameraTarget");
        if (existing == null)
        {
            existing = new GameObject("PresentationCameraTarget");
        }

        existing.transform.position = FindPresentationCenter();
        target = existing.transform;
        targetPosition = target.position + focusOffset;
    }

    private Vector3 FindPresentationCenter()
    {
        GameObject bucket = GameObject.Find("Bucket");
        GameObject board = GameObject.Find("PaintBoard");

        if (board == null)
        {
            PaintingSurface surface = FindAnyObjectByType<PaintingSurface>();
            if (surface != null)
            {
                board = surface.gameObject;
            }
        }

        if (bucket != null && board != null)
        {
            return Vector3.Lerp(bucket.transform.position, board.transform.position, 0.5f);
        }

        GameObject simulationRoot = GameObject.Find("SimulationRoot");
        if (simulationRoot != null)
        {
            return simulationRoot.transform.position;
        }

        return Vector3.zero;
    }

    private bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private bool IsRightMousePressed()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        pressed |= Mouse.current != null && Mouse.current.rightButton.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetMouseButton(1);
#endif
        return pressed;
    }

    private bool IsMiddleMousePressed()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        pressed |= Mouse.current != null && Mouse.current.middleButton.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetMouseButton(2);
#endif
        return pressed;
    }

    private bool IsShiftHeld()
    {
        bool held = false;
#if ENABLE_INPUT_SYSTEM
        held |= Keyboard.current != null &&
            (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        held |= Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
        return held;
    }

    private bool IsEscapePressedThisFrame()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        pressed |= Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetKeyDown(KeyCode.Escape);
#endif
        return pressed;
    }

    private bool IsFocusPressedThisFrame()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        pressed |= Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetKeyDown(KeyCode.F);
#endif
        return pressed;
    }

    private Vector2 GetMouseDelta()
    {
        Vector2 delta = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            delta += Mouse.current.delta.ReadValue();
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        delta += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 12f;
#endif
        return delta;
    }

    private float GetScrollY()
    {
        float scroll = 0f;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            scroll += Mouse.current.scroll.ReadValue().y / 120f;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        scroll += Input.mouseScrollDelta.y;
#endif
        return scroll;
    }

    private static float NormalizePitch(float angle)
    {
        if (angle > 180f)
        {
            angle -= 360f;
        }

        return Mathf.Clamp(angle, -80f, 80f);
    }
}
