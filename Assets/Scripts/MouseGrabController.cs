using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class MouseGrabController : MonoBehaviour
{
    public enum GrabState
    {
        Idle,
        Hover,
        Dragging,
        Released
    }

    public enum GrabTarget
    {
        Bucket,
        RopeEnd
    }

    [Header("References")]
    public Camera sceneCamera;
    public SphericalPendulumController pendulum;
    public MassSpringRope rope;
    public PaintParticleEmitter paintEmitter;
    public MouseCameraController cameraController;
    public SimulationUIController uiController;

    [Header("Interaction")]
    public bool mouseGrabEnabled = true;
    public GrabTarget grabTarget = GrabTarget.Bucket;
    public bool applyReleaseVelocity = true;
    [Range(0.25f, 3f)] public float grabSensitivity = 1f;
    [Range(5f, 89f)] public float maxDragAngle = 75f;
    public float bucketPickRadius = 0.75f;
    public float ropeEndPickRadius = 0.45f;
    public float ropeSegmentPickRadius = 0.16f;
    public bool allowMiddleRopeHover;

    [Header("Feedback")]
    public Color idleColor = new Color(1f, 0.82f, 0.15f, 1f);
    public Color draggingColor = new Color(0.1f, 1f, 0.45f, 1f);
    public float markerRadius = 0.16f;
    public float feedbackLineWidth = 0.025f;

    [Header("Debug")]
    public GrabState currentState = GrabState.Idle;
    public string currentTargetName = "-";
    public float dragAngleDegrees;
    public float releaseVelocity;
    public Vector3 currentGrabPoint;
    public bool pointerOverUi;
    public bool usingBuiltInPhysics;

    private LineRenderer markerRenderer;
    private LineRenderer pivotLineRenderer;
    private Material feedbackMaterial;
    private Vector3 dragPlanePoint;
    private Vector3 dragPlaneNormal;
    private Vector3 lastAttachPosition;
    private Vector3 previousAttachPosition;
    private Vector3 releaseVelocityVector;
    private Vector3 grabStartDirection = Vector3.down;
    private float releasedUntilTime;
    private bool wasEmitterPaused;
    private bool wasDraggingLastFrame;

    public string DebugSummary
    {
        get
        {
            return
                "Mouse Grab: " + currentState +
                "\nGrab Target: " + currentTargetName +
                "\nDrag Angle: " + dragAngleDegrees.ToString("0.0") + " deg" +
                "\nRelease Velocity: " + releaseVelocity.ToString("0.00") + " m/s" +
                "\nApply Release Velocity: " + (applyReleaseVelocity ? "On" : "Off") +
                "\nPointer over UI: " + (pointerOverUi ? "Yes" : "No") +
                "\nCustom picking: ray/point/segment math only";
        }
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureFeedbackRenderers();
        HideFeedback();
    }

    private void OnValidate()
    {
        grabSensitivity = Mathf.Clamp(grabSensitivity, 0.25f, 3f);
        maxDragAngle = Mathf.Clamp(maxDragAngle, 5f, 89f);
        bucketPickRadius = Mathf.Max(0.05f, bucketPickRadius);
        ropeEndPickRadius = Mathf.Max(0.05f, ropeEndPickRadius);
        ropeSegmentPickRadius = Mathf.Max(0.02f, ropeSegmentPickRadius);
        markerRadius = Mathf.Max(0.02f, markerRadius);
        feedbackLineWidth = Mathf.Max(0.002f, feedbackLineWidth);
    }

    private void Update()
    {
        ResolveReferences();
        usingBuiltInPhysics = false;

        if (!mouseGrabEnabled || !Ready())
        {
            if (currentState == GrabState.Dragging)
            {
                CancelGrab();
            }
            else
            {
                SetIdle();
            }
            return;
        }

        pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (IsEscapePressedThisFrame())
        {
            CancelGrab();
            return;
        }

        if (currentState == GrabState.Dragging)
        {
            ContinueDrag();
            if (IsLeftMouseReleasedThisFrame())
            {
                ReleaseGrab();
            }
            return;
        }

        if (currentState == GrabState.Released && Time.unscaledTime < releasedUntilTime)
        {
            return;
        }

        UpdateHover();

        if (currentState == GrabState.Hover && IsLeftMousePressedThisFrame() && !pointerOverUi && !IsCameraOrbiting())
        {
            BeginGrab();
        }
    }

    public void ResetDragState()
    {
        if (currentState == GrabState.Dragging)
        {
            CancelGrab();
        }

        releaseVelocity = 0f;
        releaseVelocityVector = Vector3.zero;
        dragAngleDegrees = 0f;
        SetIdle();
    }

    private void ResolveReferences()
    {
        if (sceneCamera == null) sceneCamera = Camera.main;
        if (pendulum == null) pendulum = FindAnyObjectByType<SphericalPendulumController>();
        if (rope == null) rope = FindAnyObjectByType<MassSpringRope>();
        if (paintEmitter == null) paintEmitter = FindAnyObjectByType<PaintParticleEmitter>();
        if (cameraController == null) cameraController = FindAnyObjectByType<MouseCameraController>();
        if (uiController == null) uiController = FindAnyObjectByType<SimulationUIController>();
    }

    private bool Ready()
    {
        return sceneCamera != null
            && pendulum != null
            && pendulum.pivotPoint != null
            && pendulum.ropeAttachPoint != null;
    }

    private void UpdateHover()
    {
        if (pointerOverUi || IsCameraOrbiting())
        {
            SetIdle();
            return;
        }

        Ray ray = sceneCamera.ScreenPointToRay(GetMousePosition());
        if (TryPick(ray, out Vector3 point, out GrabTarget target))
        {
            currentState = GrabState.Hover;
            currentTargetName = target == GrabTarget.Bucket ? "Bucket" : "Rope End";
            currentGrabPoint = point;
            UpdateFeedback(point, idleColor, showPivotLine: false);
            return;
        }

        SetIdle();
    }

    private bool TryPick(Ray ray, out Vector3 point, out GrabTarget target)
    {
        point = Vector3.zero;
        target = grabTarget;

        Vector3 bucketPoint = pendulum.transform.position;
        Vector3 ropeEndPoint = pendulum.ropeAttachPoint.position;

        float bucketDistance = DistancePointToRay(bucketPoint, ray);
        float ropeEndDistance = DistancePointToRay(ropeEndPoint, ray);
        float bucketThreshold = bucketPickRadius * grabSensitivity;
        float endThreshold = ropeEndPickRadius * grabSensitivity;

        if (grabTarget == GrabTarget.Bucket && bucketDistance <= bucketThreshold)
        {
            point = bucketPoint;
            target = GrabTarget.Bucket;
            return true;
        }

        if (grabTarget == GrabTarget.RopeEnd && ropeEndDistance <= endThreshold)
        {
            point = ropeEndPoint;
            target = GrabTarget.RopeEnd;
            return true;
        }

        if (allowMiddleRopeHover && TryPickRopeSegment(ray, out point))
        {
            target = GrabTarget.RopeEnd;
            point = ropeEndPoint;
            return true;
        }

        return false;
    }

    private bool TryPickRopeSegment(Ray ray, out Vector3 point)
    {
        point = Vector3.zero;
        if (rope == null)
        {
            return false;
        }

        LineRenderer line = rope.GetComponent<LineRenderer>();
        if (line == null || line.positionCount < 2)
        {
            return false;
        }

        float bestDistance = float.MaxValue;
        Vector3 bestPoint = Vector3.zero;
        for (int i = 0; i < line.positionCount - 1; i++)
        {
            Vector3 a = line.GetPosition(i);
            Vector3 b = line.GetPosition(i + 1);
            float distance = DistanceRayToSegment(ray, a, b, out Vector3 segmentPoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestPoint = segmentPoint;
            }
        }

        if (bestDistance <= ropeSegmentPickRadius * grabSensitivity)
        {
            point = bestPoint;
            return true;
        }

        return false;
    }

    private void BeginGrab()
    {
        currentState = GrabState.Dragging;
        wasDraggingLastFrame = false;
        currentTargetName = grabTarget == GrabTarget.Bucket ? "Bucket" : "Rope End";
        dragPlanePoint = pendulum.pivotPoint.position;
        dragPlaneNormal = sceneCamera.transform.forward;
        if (Mathf.Abs(Vector3.Dot(dragPlaneNormal.normalized, Vector3.up)) > 0.92f)
        {
            dragPlaneNormal = sceneCamera.transform.up;
        }
        dragPlaneNormal.Normalize();

        Vector3 currentDirection = pendulum.ropeAttachPoint.position - pendulum.pivotPoint.position;
        grabStartDirection = currentDirection.sqrMagnitude > 0.000001f ? currentDirection.normalized : Vector3.down;
        lastAttachPosition = pendulum.ropeAttachPoint.position;
        previousAttachPosition = lastAttachPosition;
        releaseVelocityVector = Vector3.zero;
        releaseVelocity = 0f;

        if (paintEmitter != null)
        {
            wasEmitterPaused = paintEmitter.isPaused;
            paintEmitter.SetPaused(true);
        }

        pendulum.SetMouseHoldActive(true);
        ContinueDrag();
    }

    private void ContinueDrag()
    {
        Ray ray = sceneCamera.ScreenPointToRay(GetMousePosition());
        if (!TryRayPlane(ray, dragPlanePoint, dragPlaneNormal, out Vector3 planePoint))
        {
            planePoint = lastAttachPosition;
        }

        Vector3 pivot = pendulum.pivotPoint.position;
        Vector3 pivotToDrag = planePoint - pivot;
        if (pivotToDrag.sqrMagnitude < 0.000001f || !IsFinite(pivotToDrag))
        {
            pivotToDrag = grabStartDirection;
        }

        Vector3 direction = ClampDirectionToMaxAngle(pivotToDrag.normalized);
        Vector3 attachPosition = pivot + direction * Mathf.Max(0.001f, pendulum.ropeLength);
        float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);

        if (wasDraggingLastFrame)
        {
            releaseVelocityVector = (attachPosition - previousAttachPosition) / dt;
            if (!IsFinite(releaseVelocityVector))
            {
                releaseVelocityVector = Vector3.zero;
            }
        }

        previousAttachPosition = attachPosition;
        lastAttachPosition = attachPosition;
        wasDraggingLastFrame = true;
        currentGrabPoint = attachPosition;
        dragAngleDegrees = Vector3.Angle(Vector3.down, direction);
        releaseVelocity = releaseVelocityVector.magnitude;

        pendulum.SetStateFromAttachDirection(direction, Vector3.zero);
        pendulum.SetMouseHoldActive(true);
        if (rope != null)
        {
            rope.SnapToCurrentEndpoints();
        }

        UpdateFeedback(attachPosition, draggingColor, showPivotLine: true);
    }

    private void ReleaseGrab()
    {
        Vector3 direction = lastAttachPosition - pendulum.pivotPoint.position;
        Vector3 velocity = applyReleaseVelocity ? releaseVelocityVector : Vector3.zero;
        pendulum.SetMouseHoldActive(false);
        pendulum.SetStateFromAttachDirection(direction, velocity);
        if (rope != null)
        {
            rope.SnapToCurrentEndpoints();
        }

        currentState = GrabState.Released;
        releasedUntilTime = Time.unscaledTime + 0.45f;
        currentTargetName = grabTarget == GrabTarget.Bucket ? "Bucket" : "Rope End";
        releaseVelocity = velocity.magnitude;
        UpdateFeedback(lastAttachPosition, idleColor, showPivotLine: true);

        if (uiController != null)
        {
            uiController.StartSimulation();
        }
        else if (paintEmitter != null)
        {
            paintEmitter.SetPaused(wasEmitterPaused);
        }
    }

    private void CancelGrab()
    {
        if (Ready())
        {
            pendulum.SetMouseHoldActive(false);
            pendulum.SetStateFromAttachDirection(grabStartDirection, Vector3.zero);
            if (rope != null)
            {
                rope.SnapToCurrentEndpoints();
            }
        }

        if (paintEmitter != null)
        {
            paintEmitter.SetPaused(wasEmitterPaused);
        }

        SetIdle();
    }

    private Vector3 ClampDirectionToMaxAngle(Vector3 direction)
    {
        if (!IsFinite(direction) || direction.sqrMagnitude < 0.000001f)
        {
            direction = Vector3.down;
        }

        direction.Normalize();
        float maxRadians = maxDragAngle * Mathf.Deg2Rad;
        float theta = Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction, Vector3.down), -1f, 1f));
        if (theta <= maxRadians)
        {
            return direction;
        }

        Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.down);
        if (horizontal.sqrMagnitude < 0.000001f)
        {
            horizontal = Vector3.ProjectOnPlane(grabStartDirection, Vector3.down);
        }
        if (horizontal.sqrMagnitude < 0.000001f)
        {
            horizontal = Vector3.right;
        }
        horizontal.Normalize();
        return Vector3.down * Mathf.Cos(maxRadians) + horizontal * Mathf.Sin(maxRadians);
    }

    private void SetIdle()
    {
        currentState = GrabState.Idle;
        currentTargetName = "-";
        HideFeedback();
    }

    private void EnsureFeedbackRenderers()
    {
        if (feedbackMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            feedbackMaterial = new Material(shader);
            feedbackMaterial.name = "Mouse Grab Feedback Material";
        }

        if (markerRenderer == null)
        {
            GameObject marker = new GameObject("MouseGrabMarker");
            marker.transform.SetParent(transform, false);
            markerRenderer = marker.AddComponent<LineRenderer>();
            markerRenderer.loop = true;
            markerRenderer.useWorldSpace = true;
            markerRenderer.positionCount = 32;
            markerRenderer.sharedMaterial = feedbackMaterial;
            markerRenderer.startWidth = feedbackLineWidth;
            markerRenderer.endWidth = feedbackLineWidth;
        }

        if (pivotLineRenderer == null)
        {
            GameObject line = new GameObject("MouseGrabPivotLine");
            line.transform.SetParent(transform, false);
            pivotLineRenderer = line.AddComponent<LineRenderer>();
            pivotLineRenderer.loop = false;
            pivotLineRenderer.useWorldSpace = true;
            pivotLineRenderer.positionCount = 2;
            pivotLineRenderer.sharedMaterial = feedbackMaterial;
            pivotLineRenderer.startWidth = feedbackLineWidth;
            pivotLineRenderer.endWidth = feedbackLineWidth;
        }
    }

    private void UpdateFeedback(Vector3 position, Color color, bool showPivotLine)
    {
        EnsureFeedbackRenderers();
        markerRenderer.enabled = true;
        markerRenderer.startColor = color;
        markerRenderer.endColor = color;
        markerRenderer.startWidth = feedbackLineWidth;
        markerRenderer.endWidth = feedbackLineWidth;

        Vector3 right = sceneCamera != null ? sceneCamera.transform.right : Vector3.right;
        Vector3 up = sceneCamera != null ? sceneCamera.transform.up : Vector3.up;
        for (int i = 0; i < markerRenderer.positionCount; i++)
        {
            float angle = i / (float)markerRenderer.positionCount * Mathf.PI * 2f;
            Vector3 p = position + (Mathf.Cos(angle) * right + Mathf.Sin(angle) * up) * markerRadius;
            markerRenderer.SetPosition(i, p);
        }

        pivotLineRenderer.enabled = showPivotLine;
        pivotLineRenderer.startColor = color;
        pivotLineRenderer.endColor = color;
        if (showPivotLine && pendulum != null && pendulum.pivotPoint != null)
        {
            pivotLineRenderer.SetPosition(0, pendulum.pivotPoint.position);
            pivotLineRenderer.SetPosition(1, position);
        }
    }

    private void HideFeedback()
    {
        EnsureFeedbackRenderers();
        markerRenderer.enabled = false;
        pivotLineRenderer.enabled = false;
    }

    private bool IsCameraOrbiting()
    {
        return cameraController != null && cameraController.rightMousePressed;
    }

    private static float DistancePointToRay(Vector3 point, Ray ray)
    {
        Vector3 toPoint = point - ray.origin;
        float projected = Mathf.Max(0f, Vector3.Dot(toPoint, ray.direction));
        Vector3 closest = ray.origin + ray.direction * projected;
        return Vector3.Distance(point, closest);
    }

    private static float DistanceRayToSegment(Ray ray, Vector3 a, Vector3 b, out Vector3 segmentPoint)
    {
        Vector3 u = ray.direction.normalized;
        Vector3 v = b - a;
        Vector3 w = ray.origin - a;
        float aa = Vector3.Dot(u, u);
        float bb = Vector3.Dot(u, v);
        float cc = Vector3.Dot(v, v);
        float dd = Vector3.Dot(u, w);
        float ee = Vector3.Dot(v, w);
        float denominator = aa * cc - bb * bb;

        float rayT = 0f;
        float segT = 0f;
        if (denominator > 0.000001f)
        {
            rayT = Mathf.Max(0f, (bb * ee - cc * dd) / denominator);
            segT = Mathf.Clamp01((aa * ee - bb * dd) / denominator);
        }

        Vector3 rayPoint = ray.origin + u * rayT;
        segmentPoint = a + v * segT;
        return Vector3.Distance(rayPoint, segmentPoint);
    }

    private static bool TryRayPlane(Ray ray, Vector3 planePoint, Vector3 planeNormal, out Vector3 point)
    {
        point = Vector3.zero;
        float denom = Vector3.Dot(ray.direction, planeNormal);
        if (Mathf.Abs(denom) < 0.00001f)
        {
            return false;
        }

        float t = Vector3.Dot(planePoint - ray.origin, planeNormal) / denom;
        if (t < 0f)
        {
            return false;
        }

        point = ray.origin + ray.direction * t;
        return IsFinite(point);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }

    private static bool IsLeftMousePressedThisFrame()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        pressed |= Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetMouseButtonDown(0);
#endif
        return pressed;
    }

    private static bool IsLeftMouseReleasedThisFrame()
    {
        bool released = false;
#if ENABLE_INPUT_SYSTEM
        released |= Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        released |= Input.GetMouseButtonUp(0);
#endif
        return released;
    }

    private static bool IsEscapePressedThisFrame()
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

    private static Vector3 GetMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            return Mouse.current.position.ReadValue();
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#else
        return Vector3.zero;
#endif
    }

    private void OnDestroy()
    {
        if (feedbackMaterial != null)
        {
            if (Application.isPlaying) Destroy(feedbackMaterial);
            else DestroyImmediate(feedbackMaterial);
        }
    }
}
