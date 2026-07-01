using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SimulationUIController : MonoBehaviour
{
    [Header("Simulation References")]
    public SphericalPendulumController pendulumController;
    public MassSpringRope ropeController;
    public Simulation3D fluidSimulation;
    public PaintParticleEmitter paintEmitter;
    public BucketPaintReservoir paintReservoir;
    public PaintingSurface paintingSurface;
    public PaintExperimentManager experimentManager;
    public MouseCameraController mouseCameraController;
    public MouseGrabController mouseGrabController;
    public IndependentFluidVisualizer fluidPreview;

    [Header("Sliders - Pendulum")]
    public Slider ropeLengthSlider;
    public Slider initialAngleSlider;
    public Slider sidePushSlider;
    public Slider dampingSlider;
    public Slider gravitySlider;
    public Slider airResistanceSlider;
    public Slider pivotFrictionSlider;

    [Header("Sliders - Paint")]
    public Slider emissionRateSlider;
    public Slider exitSpeedSlider;
    public Slider viscositySlider;
    public Slider holeDiameterSlider;

    [Header("Value Texts - Pendulum")]
    public TMP_Text ropeLengthValueText;
    public TMP_Text initialAngleValueText;
    public TMP_Text sidePushValueText;
    public TMP_Text dampingValueText;
    public TMP_Text gravityValueText;
    public TMP_Text airResistanceValueText;
    public TMP_Text pivotFrictionValueText;

    [Header("Value Texts - Paint")]
    public TMP_Text emissionRateValueText;
    public TMP_Text exitSpeedValueText;
    public TMP_Text viscosityValueText;
    public TMP_Text holeDiameterValueText;

    [Header("Info Texts")]
    public TMP_Text timeText;
    public TMP_Text bucketSpeedText;
    public TMP_Text swingCountText;
    public TMP_Text paintRemainingText;

    [Header("Physical Output Texts")]
    public TMP_Text totalMassText;
    public TMP_Text ropeTensionText;
    public TMP_Text effectiveDampingText;
    public TMP_Text kineticEnergyText;
    public TMP_Text potentialEnergyText;

    [Header("Paint Values")]
    public float emissionRate = 15f;
    public float exitSpeed = 1.8f;
    public float viscosity = 0.65f;
    public float holeDiameter = 0.01f;
    public float bucketWeight = 2f;
    public float bucketRadius = 0.55f;
    public bool autoEstimatePaintCapacityFromBucketRadius;
    public Vector3 pivotPosition = new Vector3(0f, 4f, 0f);
    public float swingDirectionDegrees = 0f;
    public int targetSwingCount = 10;
    public bool unlimitedSwings;
    public float initialPaintAmount = 20f;
    public float paintCapacity = 30f;
    public float humidity = 0.35f;
    public float ropeFlexibility = 0.18f;
    public float simulationDurationLimit = 30f;
    public float canvasWidth = 10f;
    public float canvasHeight = 10f;
    public float canvasTiltDegrees = 28f;
    public Color paintColor = new Color(0.1f, 0.25f, 1f, 1f);

    [Header("Unified Canvas UI")]
    public bool buildUnifiedCanvasUI = true;
    public bool disableLegacyUIVisuals = true;
    public bool enableDebugOnGUI = false;
    public bool showLegacyFluidParticles = false;
    public bool advancedPaintMode = false;

    private float simulationTime;
    private bool isPaused = true;
    private int swingCount;
    private bool swingCounterReady;
    private bool targetSwingCompleted;
    private float previousRadialVelocity;
    private string statusText = "Paused";
    private string userMessage = "";
    private Vector2 toolScroll;
    private readonly string[] surfaceTypes = { "Canvas", "Wood", "Metal", "Paper" };
    private int surfaceTypeIndex;
    private float fpsTimer;
    private int fpsFrames;
    private float currentFps;
    private Canvas mainCanvas;
    private CanvasGroup mainCanvasGroup;
    private CanvasScaler mainCanvasScaler;
    private readonly Vector2 baseReferenceResolution = new Vector2(1920f, 1080f);
    private float currentUiScale = 1f;
    private TMP_Text topStatusText;
    private TMP_Text mouseCameraDebugText;
    private TMP_Text mouseGrabDebugText;
    private TMP_Text ropeDebugText;
    private TMP_Text referenceWarningText;
    private TMP_Text particleWarningText;
    private TMP_Text paintStatusText;
    private TMP_Text fluidPreviewStatsText;
    private TMP_Text lastImagePathText;
    private TMP_Text lastReportPathText;
    private TMP_Text comparisonText;
    private TMP_Text historyText;
    private TMP_Text canvasStatusText;
    private TMP_Dropdown surfaceTypeDropdown;
    private TMP_Dropdown canvasOrientationDropdown;
    private TMP_Dropdown trailModeDropdown;
    private TMP_Dropdown mixModeDropdown;
    private Slider canvasTiltSlider;
    private ScrollRect tabContentScrollRect;
    private Scrollbar tabContentScrollbar;
    private bool canvasTilted;
    private readonly List<TMP_Text> dynamicValueTexts = new List<TMP_Text>();
    private RectTransform tabContentRoot;
    private readonly List<Button> tabButtons = new List<Button>();
    private DashboardTab activeTab = DashboardTab.Motion;
    private bool uiVisible = true;
    private OpenBucketMesh openBucketMesh;
    private Transform paintExitPoint;

    private enum DashboardTab
    {
        Motion,
        Paint,
        FluidPreview,
        Environment,
        Canvas,
        Results,
        Performance
    }

    private void Awake()
    {
        if (fluidSimulation == null)
        {
            fluidSimulation = FindFirstObjectByType<Simulation3D>();
        }

        CreateRuntimeSystemsIfNeeded();

        if (buildUnifiedCanvasUI)
        {
            BuildUnifiedCanvasUI();
        }

        if (disableLegacyUIVisuals)
        {
            DisableLegacyUIVisuals();
        }
    }

    private void Start()
    {
        InitializeSliders();
        SynchronizeInitialPaintAmount();
        if (!buildUnifiedCanvasUI)
        {
            ConnectSliders();
        }
        ApplyPaintSettings(resetFluid: true);
        ApplyExtendedSettings(resetMotion: false);
        ResetCountersOnly();

        isPaused = true;
        statusText = "Paused";

        // This project uses one main scene simulation. Global time scale is centralized
        // here so pause/resume behavior is predictable for the academic demo.
        Time.timeScale = 0f;
        SetFluidPaused(true);
        SetPaintEmitterPaused(true);

        UpdateValueTexts();
        UpdateInfoTexts();
    }

    private void SynchronizeInitialPaintAmount()
    {
        initialPaintAmount = Mathf.Max(0f, initialPaintAmount);
        if (paintReservoir != null)
        {
            paintReservoir.SetCapacity(Mathf.Max(paintReservoir.capacity, initialPaintAmount));
            paintReservoir.SetPaintAmount(initialPaintAmount);
            paintReservoir.SetSelectedColor(paintColor);
        }
        else if (paintEmitter != null)
        {
            paintEmitter.SetPaintAmount(initialPaintAmount);
        }
    }

    private void ApplyInitialPaintAmountFromSlider(bool refillRuntimePaint)
    {
        initialPaintAmount = Mathf.Max(0f, initialPaintAmount);

        if (paintEmitter != null)
        {
            if (refillRuntimePaint)
            {
                paintEmitter.SetPaintAmount(initialPaintAmount);
            }
            else
            {
                paintEmitter.SetInitialPaintAmount(initialPaintAmount, refillRemaining: false);
            }
        }

        if (paintReservoir != null)
        {
            paintReservoir.SetCapacity(Mathf.Max(paintReservoir.capacity, initialPaintAmount));
            if (refillRuntimePaint)
            {
                paintReservoir.SetPaintAmount(initialPaintAmount);
            }
            else
            {
                paintReservoir.SetInitialPaintAmount(initialPaintAmount, refillCurrent: false);
                paintReservoir.BindEmitter(paintEmitter);
            }
            paintReservoir.SetSelectedColor(paintColor);
        }

        if (pendulumController != null)
        {
            pendulumController.SetPaintAmount(refillRuntimePaint && paintEmitter != null
                ? paintEmitter.remainingPaintAmount
                : initialPaintAmount);
        }
    }

    private void Update()
    {
        if (IsHideUiPressedThisFrame())
        {
            ToggleUnifiedUIVisibility();
        }

        if (!isPaused)
        {
            simulationTime += Time.unscaledDeltaTime;
            UpdateSwingCount();

            if (simulationDurationLimit > 0f && simulationTime >= simulationDurationLimit)
            {
                PauseSimulation();
                statusText = "Finished";
            }
        }

        if (paintEmitter != null && pendulumController != null)
        {
            pendulumController.paintMass = paintEmitter.remainingPaintAmount;
        }

        UpdateFpsCounter();
        UpdateBucketStatus();
        SyncLegacyPaintPreview();
        UpdateInfoTexts();
        UpdateUnifiedCanvasTexts();
    }

    private void UpdateFpsCounter()
    {
        fpsTimer += Time.unscaledDeltaTime;
        fpsFrames++;

        if (fpsTimer >= 0.5f)
        {
            currentFps = fpsFrames / fpsTimer;
            fpsTimer = 0f;
            fpsFrames = 0;
        }
    }

    private void InitializeSliders()
    {
        if (pendulumController != null)
        {
            SetSliderValueWithoutNotify(ropeLengthSlider, pendulumController.ropeLength);
            SetSliderValueWithoutNotify(initialAngleSlider, pendulumController.theta * Mathf.Rad2Deg);
            SetSliderValueWithoutNotify(sidePushSlider, pendulumController.phiDot);
            SetSliderValueWithoutNotify(dampingSlider, pendulumController.damping);
            SetSliderValueWithoutNotify(gravitySlider, pendulumController.gravity);
            bucketWeight = pendulumController.bucketEmptyMass;
            initialPaintAmount = pendulumController.paintMass;
            swingDirectionDegrees = NormalizeDegrees(pendulumController.phi * Mathf.Rad2Deg);
            if (pendulumController.pivotPoint != null)
            {
                pivotPosition = pendulumController.pivotPoint.position;
            }
        }
        else
        {
            Debug.LogWarning("Pendulum Controller is not assigned.");
        }

        if (fluidSimulation != null)
        {
            emissionRate = fluidSimulation.emissionRate;
            exitSpeed = fluidSimulation.exitSpeed;
            viscosity = fluidSimulation.viscosityStrength;
            holeDiameter = fluidSimulation.holeDiameter;
        }

        if (ropeController != null)
        {
            ropeFlexibility = ropeController.bendAmount;
        }

        if (paintEmitter != null)
        {
            initialPaintAmount = paintEmitter.initialPaintAmount;
            viscosity = paintEmitter.viscosity;
            holeDiameter = paintEmitter.holeDiameter;
            exitSpeed = paintEmitter.flowSpeed;
            emissionRate = paintEmitter.flowMultiplier > 0f ? paintEmitter.flowMultiplier : emissionRate;
            humidity = paintEmitter.humidity;
            paintColor = paintEmitter.paintColor;
        }

        if (paintReservoir != null)
        {
            paintCapacity = paintReservoir.capacity;
            initialPaintAmount = paintReservoir.initialPaintAmount;
            paintColor = paintReservoir.selectedPaintColor;
        }

        if (openBucketMesh == null)
        {
            openBucketMesh = FindFirstObjectByType<OpenBucketMesh>();
        }

        if (openBucketMesh != null)
        {
            bucketRadius = openBucketMesh.topRadius;
        }

        if (paintingSurface != null)
        {
            paintingSurface.strokeRadius = Mathf.Clamp(paintingSurface.strokeRadius <= 0.2f ? 1f : paintingSurface.strokeRadius, 0.3f, 3f);
            canvasWidth = paintingSurface.currentWidth > 0.001f
                ? paintingSurface.currentWidth
                : paintingSurface.localHalfExtents.x * 2f * paintingSurface.transform.localScale.x;
            canvasHeight = paintingSurface.currentHeight > 0.001f
                ? paintingSurface.currentHeight
                : paintingSurface.localHalfExtents.y * 2f * paintingSurface.transform.localScale.z;
            canvasTiltDegrees = paintingSurface.tiltAngle > 0.1f ? paintingSurface.tiltAngle : 28f;
            canvasTilted = paintingSurface.orientation == "Tilted";

            for (int i = 0; i < surfaceTypes.Length; i++)
            {
                if (surfaceTypes[i] == paintingSurface.surfaceType)
                {
                    surfaceTypeIndex = i;
                    break;
                }
            }
        }

        if (!advancedPaintMode && emissionRate > 40f)
        {
            emissionRate = 15f;
        }
        emissionRate = Mathf.Clamp(emissionRate <= 0f ? 15f : emissionRate, 1f, advancedPaintMode ? 120f : 40f);

        SetSliderValueWithoutNotify(emissionRateSlider, emissionRate);
        SetSliderValueWithoutNotify(exitSpeedSlider, exitSpeed);
        SetSliderValueWithoutNotify(viscositySlider, viscosity);
        SetSliderValueWithoutNotify(holeDiameterSlider, holeDiameter);
        SetSliderValueWithoutNotify(airResistanceSlider, pendulumController != null ? pendulumController.airResistanceCoefficient : 0f);
        SetSliderValueWithoutNotify(pivotFrictionSlider, pendulumController != null ? pendulumController.pivotFrictionCoefficient : 0f);
    }

    private void ConnectSliders()
    {
        AddSliderListener(ropeLengthSlider, ChangeRopeLength);
        AddSliderListener(initialAngleSlider, ChangeInitialAngle);
        AddSliderListener(sidePushSlider, ChangeSidePush);
        AddSliderListener(dampingSlider, ChangeDamping);
        AddSliderListener(gravitySlider, ChangeGravity);
        AddSliderListener(airResistanceSlider, ChangeAirResistance);
        AddSliderListener(pivotFrictionSlider, ChangePivotFriction);
        AddSliderListener(emissionRateSlider, ChangeEmissionRate);
        AddSliderListener(exitSpeedSlider, ChangeExitSpeed);
        AddSliderListener(viscositySlider, ChangeViscosity);
        AddSliderListener(holeDiameterSlider, ChangeHoleDiameter);
    }

    private void ChangeRopeLength(float value)
    {
        if (pendulumController != null)
        {
            pendulumController.ropeLength = value;
            pendulumController.ResetPendulum();
        }

        if (ropeController != null)
        {
            ropeController.ropeLength = value;
            ropeController.SnapToCurrentEndpoints();
        }

        ResetCountersOnly();
        RefreshTexts();
    }

    private void ChangeInitialAngle(float value)
    {
        if (pendulumController != null)
        {
            pendulumController.theta = value * Mathf.Deg2Rad;
            pendulumController.ResetPendulum();
        }

        ResetRope();
        ResetCountersOnly();
        RefreshTexts();
    }

    private void ChangeSidePush(float value)
    {
        if (pendulumController != null)
        {
            pendulumController.phiDot = value;
            pendulumController.ResetPendulum();
        }

        ResetRope();
        ResetCountersOnly();
        RefreshTexts();
    }

    private void ChangeDamping(float value)
    {
        if (pendulumController != null)
        {
            pendulumController.damping = value;
        }

        RefreshTexts();
    }

    private void ChangeAirResistance(float value)
    {
        if (pendulumController != null)
        {
            pendulumController.airResistanceCoefficient = Mathf.Max(0f, value);
        }

        if (paintEmitter != null)
        {
            paintEmitter.airDrag = Mathf.Max(0f, value);
        }

        RefreshTexts();
    }

    private void ChangePivotFriction(float value)
    {
        if (pendulumController != null)
        {
            pendulumController.pivotFrictionCoefficient = Mathf.Max(0f, value);
        }

        RefreshTexts();
    }

    private void ChangeGravity(float value)
    {
        if (pendulumController != null)
        {
            pendulumController.gravity = value;
            pendulumController.ResetPendulum();
        }

        if (ropeController != null)
        {
            ropeController.gravity = new Vector3(0f, -value, 0f);
            ropeController.ResetRope();
        }

        if (fluidSimulation != null)
        {
            fluidSimulation.gravity = -value;
        }

        if (paintEmitter != null)
        {
            paintEmitter.gravity = value;
        }

        ResetCountersOnly();
        RefreshTexts();
    }

    private void ChangeEmissionRate(float value)
    {
        emissionRate = Mathf.Clamp(value, 1f, advancedPaintMode ? 120f : 40f);
        SetSliderValueWithoutNotify(emissionRateSlider, emissionRate);

        if (pendulumController != null)
        {
            pendulumController.paintMassFlowRate = value / 1200f;
        }

        ApplyPaintSettings(resetFluid: false);
        ApplyExtendedSettings(resetMotion: false);
        RefreshTexts();
    }

    private void ChangeExitSpeed(float value)
    {
        exitSpeed = value;
        ApplyPaintSettings(resetFluid: true);
        ApplyExtendedSettings(resetMotion: false);
        RefreshTexts();
    }

    private void ChangeViscosity(float value)
    {
        viscosity = value;
        ApplyPaintSettings(resetFluid: false);
        ApplyExtendedSettings(resetMotion: false);
        RefreshTexts();
    }

    private void ChangeHoleDiameter(float value)
    {
        holeDiameter = value;
        ApplyPaintSettings(resetFluid: true);
        ApplyExtendedSettings(resetMotion: false);
        RefreshTexts();
    }

    private void ChangePaintAmount(float value)
    {
        initialPaintAmount = Mathf.Max(0f, value);
        bool refillRuntimePaint = isPaused && simulationTime <= 0.0001f;
        ApplyInitialPaintAmountFromSlider(refillRuntimePaint);

        ApplyExtendedSettings(resetMotion: false);
        SyncLegacyPaintPreview();
        RefreshTexts();
    }

    private void ApplyPaintSettings(bool resetFluid)
    {
        if (fluidSimulation == null)
        {
            return;
        }

        fluidSimulation.ApplyPaintSettings(emissionRate, exitSpeed, viscosity, holeDiameter);

        if (resetFluid)
        {
            fluidSimulation.ResetFluid();
        }
    }

    private void ApplyExtendedSettings(bool resetMotion)
    {
        bucketWeight = Mathf.Max(0.001f, bucketWeight);
        bucketRadius = Mathf.Clamp(bucketRadius, 0.15f, 1f);
        pivotPosition = ClampPivotPosition(pivotPosition);
        swingDirectionDegrees = NormalizeDegrees(swingDirectionDegrees);
        targetSwingCount = Mathf.Clamp(targetSwingCount, 1, 50);
        initialPaintAmount = Mathf.Max(0f, initialPaintAmount);
        holeDiameter = Mathf.Max(0.001f, holeDiameter);
        viscosity = Mathf.Max(0.001f, viscosity);
        emissionRate = Mathf.Clamp(emissionRate <= 0f ? 15f : emissionRate, 1f, advancedPaintMode ? 120f : 40f);
        humidity = Mathf.Clamp01(humidity);
        ropeFlexibility = Mathf.Clamp(ropeFlexibility, 0f, 1f);
        simulationDurationLimit = Mathf.Max(0f, simulationDurationLimit);

        if (pendulumController != null)
        {
            pendulumController.bucketEmptyMass = bucketWeight;
            if (pendulumController.pivotPoint != null)
            {
                pendulumController.pivotPoint.position = pivotPosition;
            }
            pendulumController.phi = swingDirectionDegrees * Mathf.Deg2Rad;
            pendulumController.paintMass = paintEmitter != null ? paintEmitter.remainingPaintAmount : initialPaintAmount;
            pendulumController.simulatePaintLoss = false;
            pendulumController.paintMassFlowRate = emissionRate / 1200f;
        }

        ApplyBucketRadiusToSystems();
        ApplyPivotToSystems(resetMotion);

        if (ropeController != null)
        {
            ApplyMassSpringRopeSettings();
        }

        if (paintingSurface != null)
        {
            paintingSurface.paintColor = paintColor;
            paintingSurface.strokeRadius = Mathf.Clamp(paintingSurface.strokeRadius <= 0.2f ? 1f : paintingSurface.strokeRadius, 0.3f, 3f);
            string selectedSurface = surfaceTypes[Mathf.Clamp(surfaceTypeIndex, 0, surfaceTypes.Length - 1)];
            if (canvasOrientationDropdown != null)
            {
                canvasTilted = canvasOrientationDropdown.value == 1;
            }

            string selectedOrientation = canvasTilted ? "Tilted" : "Horizontal";
            float appliedTilt = selectedOrientation == "Tilted" ? canvasTiltDegrees : 0f;
            paintingSurface.ApplyCanvasSettings(canvasWidth, canvasHeight, selectedSurface, selectedOrientation, appliedTilt);
            canvasWidth = paintingSurface.currentWidth;
            canvasHeight = paintingSurface.currentHeight;
            if (canvasOrientationDropdown != null)
            {
                canvasOrientationDropdown.SetValueWithoutNotify(paintingSurface.orientation == "Tilted" ? 1 : 0);
            }
        }

        if (paintEmitter != null)
        {
            float gravityValue = pendulumController != null ? pendulumController.gravity : Mathf.Abs(fluidSimulation != null ? fluidSimulation.gravity : 9.81f);
            float airValue = pendulumController != null ? pendulumController.airResistanceCoefficient : 0.05f;
            paintEmitter.flowMultiplier = emissionRate;
            paintEmitter.ApplySettings(initialPaintAmount, holeDiameter, viscosity, exitSpeed, gravityValue, airValue, humidity, paintColor);
        }

        if (paintReservoir != null)
        {
            paintReservoir.SetCapacity(paintCapacity);
            paintReservoir.SetSelectedColor(paintColor);
            paintReservoir.BindEmitter(paintEmitter);
        }

        SyncLegacyPaintPreview();

        if (experimentManager != null)
        {
            experimentManager.bucketRadius = bucketRadius;
            experimentManager.targetSwingCount = unlimitedSwings ? 0 : targetSwingCount;
            experimentManager.completedSwingCount = swingCount;
            experimentManager.swingTargetCompleted = targetSwingCompleted;
            experimentManager.swingDirectionDegrees = swingDirectionDegrees;
            experimentManager.pivotPosition = pivotPosition;
        }

        if (resetMotion)
        {
            ResetSimulation();
        }
    }

    private void ApplyBucketRadiusToSystems()
    {
        if (openBucketMesh == null)
        {
            openBucketMesh = FindFirstObjectByType<OpenBucketMesh>();
        }

        if (openBucketMesh != null)
        {
            float previousTopRadius = Mathf.Max(0.001f, openBucketMesh.topRadius);
            float bottomRatio = openBucketMesh.bottomRadius / previousTopRadius;
            float holeRatio = openBucketMesh.holeRadius / previousTopRadius;
            openBucketMesh.topRadius = bucketRadius;
            openBucketMesh.bottomRadius = Mathf.Clamp(bucketRadius * bottomRatio, 0.05f, bucketRadius * 0.95f);
            openBucketMesh.holeRadius = Mathf.Clamp(bucketRadius * holeRatio, 0.01f, bucketRadius * 0.45f);
            openBucketMesh.BuildBucket();

            if (paintExitPoint == null)
            {
                GameObject exitObject = GameObject.Find("PaintExitPoint");
                paintExitPoint = exitObject != null ? exitObject.transform : null;
            }

            if (paintExitPoint != null)
            {
                paintExitPoint.localPosition = new Vector3(0f, -openBucketMesh.height * 0.5f - 0.02f, 0f);
            }
        }

        if (paintReservoir != null)
        {
            float height = openBucketMesh != null ? openBucketMesh.height : 1.4f;
            float estimatedCapacity = Mathf.PI * bucketRadius * bucketRadius * height * 4.2f;
            if (autoEstimatePaintCapacityFromBucketRadius)
            {
                paintCapacity = Mathf.Max(initialPaintAmount, estimatedCapacity);
            }
            paintReservoir.SetCapacity(paintCapacity);
            paintReservoir.AutoFitLiquidToBucket();
        }
    }

    private void ApplyPivotToSystems(bool resetPendulumPose)
    {
        pivotPosition = ClampPivotPosition(pivotPosition);
        if (pendulumController != null && pendulumController.pivotPoint != null)
        {
            pendulumController.pivotPoint.position = pivotPosition;
            if (resetPendulumPose)
            {
                pendulumController.ResetPendulum();
            }
        }

        if (ropeController != null)
        {
            ropeController.SnapToCurrentEndpoints();
        }
    }

    private void ApplySwingDirection(bool resetPendulumPose)
    {
        if (pendulumController == null)
        {
            return;
        }

        swingDirectionDegrees = NormalizeDegrees(swingDirectionDegrees);
        pendulumController.phi = swingDirectionDegrees * Mathf.Deg2Rad;
        if (resetPendulumPose)
        {
            pendulumController.ResetPendulum();
            ResetCountersOnly();
        }
    }

    private static Vector3 ClampPivotPosition(Vector3 value)
    {
        return new Vector3(
            Mathf.Clamp(value.x, -8f, 8f),
            Mathf.Clamp(value.y, 0.5f, 12f),
            Mathf.Clamp(value.z, -8f, 8f)
        );
    }

    private static float NormalizeDegrees(float degrees)
    {
        degrees %= 360f;
        if (degrees < 0f)
        {
            degrees += 360f;
        }
        return degrees;
    }

    private void UpdateValueTexts()
    {
        if (ropeLengthValueText != null && ropeLengthSlider != null)
            ropeLengthValueText.text = ropeLengthSlider.value.ToString("0.00") + " m";

        if (initialAngleValueText != null && initialAngleSlider != null)
            initialAngleValueText.text = initialAngleSlider.value.ToString("0") + " deg";

        if (sidePushValueText != null && sidePushSlider != null)
            sidePushValueText.text = sidePushSlider.value.ToString("0.00");

        if (dampingValueText != null && dampingSlider != null)
            dampingValueText.text = dampingSlider.value.ToString("0.000");

        if (gravityValueText != null && gravitySlider != null)
            gravityValueText.text = gravitySlider.value.ToString("0.00") + " m/s2";

        if (airResistanceValueText != null && pendulumController != null)
            airResistanceValueText.text = pendulumController.airResistanceCoefficient.ToString("0.00");

        if (pivotFrictionValueText != null && pendulumController != null)
            pivotFrictionValueText.text = pendulumController.pivotFrictionCoefficient.ToString("0.00");

        if (emissionRateValueText != null && emissionRateSlider != null)
            emissionRateValueText.text = emissionRateSlider.value.ToString("0") + " particles/s";

        if (exitSpeedValueText != null && exitSpeedSlider != null)
            exitSpeedValueText.text = exitSpeedSlider.value.ToString("0.00") + " m/s";

        if (viscosityValueText != null && viscositySlider != null)
            viscosityValueText.text = viscositySlider.value.ToString("0.00");

        if (holeDiameterValueText != null && holeDiameterSlider != null)
            holeDiameterValueText.text = holeDiameterSlider.value.ToString("0.00") + " m";
    }

    private void UpdateInfoTexts()
    {
        if (timeText != null)
            timeText.text = "Time: " + simulationTime.ToString("0.0") + " s";

        if (bucketSpeedText != null && pendulumController != null)
        {
            float speed = pendulumController.attachPointVelocity.magnitude;
            bucketSpeedText.text = "Bucket Speed: " + speed.ToString("0.00") + " m/s";
        }

        if (swingCountText != null)
            swingCountText.text = "Swing Count: " + swingCount + " / " + (unlimitedSwings ? "Unlimited" : targetSwingCount.ToString());

        if (paintRemainingText != null && pendulumController != null)
        {
            float capacity = paintReservoir != null ? paintReservoir.capacity : paintCapacity;
            float percent = paintEmitter != null
                ? Mathf.Clamp01(paintEmitter.remainingPaintAmount / Mathf.Max(0.001f, capacity)) * 100f
                : pendulumController.PaintRemainingFraction * 100f;
            paintRemainingText.text = "Paint Remaining: " + percent.ToString("0") + "%";
        }

        if (pendulumController == null)
        {
            return;
        }

        if (totalMassText != null)
            totalMassText.text = "Total Mass: " + pendulumController.totalMass.ToString("0.00") + " kg";

        if (ropeTensionText != null)
            ropeTensionText.text = "Rope Tension: " + pendulumController.ropeTension.ToString("0.00") + " N";

        if (effectiveDampingText != null)
            effectiveDampingText.text = "Effective Damping: " + pendulumController.effectiveDamping.ToString("0.000");

        if (kineticEnergyText != null)
            kineticEnergyText.text = "Kinetic Energy: " + pendulumController.kineticEnergy.ToString("0.00") + " J";

        if (potentialEnergyText != null)
            potentialEnergyText.text = "Potential Energy: " + pendulumController.potentialEnergy.ToString("0.00") + " J";
    }

    private void UpdateSwingCount()
    {
        if (pendulumController == null || pendulumController.ropeAttachPoint == null || pendulumController.pivotPoint == null)
        {
            return;
        }

        Vector3 fromPivot = pendulumController.ropeAttachPoint.position - pendulumController.pivotPoint.position;
        Vector3 horizontalDisplacement = new Vector3(fromPivot.x, 0f, fromPivot.z);
        Vector3 horizontalVelocity = new Vector3(
            pendulumController.attachPointVelocity.x,
            0f,
            pendulumController.attachPointVelocity.z
        );

        if (horizontalDisplacement.magnitude < 0.05f)
        {
            return;
        }

        float radialVelocity = Vector3.Dot(horizontalDisplacement.normalized, horizontalVelocity);

        if (!swingCounterReady)
        {
            previousRadialVelocity = radialVelocity;
            swingCounterReady = true;
            return;
        }

        bool reachedOuterTurn =
            previousRadialVelocity > 0.02f &&
            radialVelocity <= -0.02f;

        if (reachedOuterTurn && simulationTime > 0.2f)
        {
            swingCount++;
            if (!unlimitedSwings && swingCount >= targetSwingCount)
            {
                CompleteSwingTarget();
            }
        }

        previousRadialVelocity = radialVelocity;
    }

    public void StartSimulation()
    {
        if (targetSwingCompleted)
        {
            targetSwingCompleted = false;
            swingCount = 0;
            swingCounterReady = false;
        }

        isPaused = false;
        statusText = paintEmitter != null && paintEmitter.IsBucketEmpty ? "Bucket Empty" : "Running";
        if (paintEmitter != null)
        {
            paintEmitter.emissionEnabled = true;
        }
        Time.timeScale = 1f;
        SyncLegacyPaintPreview();
        SetFluidPaused(false);
        SetPaintEmitterPaused(false);
    }

    private void CompleteSwingTarget()
    {
        if (targetSwingCompleted)
        {
            return;
        }

        targetSwingCompleted = true;
        statusText = "Completed";
        if (paintEmitter != null)
        {
            paintEmitter.emissionEnabled = false;
        }
    }

    public void PauseSimulation()
    {
        isPaused = true;
        statusText = "Paused";
        Time.timeScale = 0f;
        SetFluidPaused(true);
        SetPaintEmitterPaused(true);
    }

    public void ResetSimulation()
    {
        ResetCountersOnly();
        ApplyInitialPaintAmountFromSlider(refillRuntimePaint: true);

        if (pendulumController != null)
        {
            if (pendulumController.pivotPoint != null)
            {
                pendulumController.pivotPoint.position = ClampPivotPosition(pivotPosition);
            }
            pendulumController.phi = NormalizeDegrees(swingDirectionDegrees) * Mathf.Deg2Rad;
            pendulumController.ResetPendulum();
        }

        if (mouseGrabController != null)
        {
            mouseGrabController.ResetDragState();
        }

        ResetRope();

        if (fluidSimulation != null)
        {
            fluidSimulation.ResetFluid();
            SyncLegacyPaintPreview();
            fluidSimulation.SetPaused(true);
        }

        if (paintEmitter != null)
        {
            paintEmitter.ResetEmitter(resetPaintAmount: true);
            paintEmitter.SetPaused(true);
        }

        if (paintReservoir != null)
        {
            paintReservoir.SetPaintAmount(initialPaintAmount);
            paintReservoir.SetSelectedColor(paintColor);
        }

        SyncLegacyPaintPreview();

        if (paintingSurface != null)
        {
            paintingSurface.ClearPainting();
        }

        isPaused = true;
        statusText = "Paused";
        Time.timeScale = 0f;
        RefreshTexts();
    }

    private void ResetCountersOnly()
    {
        simulationTime = 0f;
        swingCount = 0;
        swingCounterReady = false;
        targetSwingCompleted = false;
        previousRadialVelocity = 0f;
        if (paintEmitter != null)
        {
            paintEmitter.emissionEnabled = true;
        }
    }

    private void RefreshTexts()
    {
        UpdateValueTexts();
        UpdateInfoTexts();
    }

    private void ResetRope()
    {
        if (ropeController != null)
        {
            ropeController.SnapToCurrentEndpoints();
        }
    }

    private void ResetBucketToPivotFromUI()
    {
        if (pendulumController == null)
        {
            userMessage = "Pendulum controller is missing.";
            return;
        }

        pendulumController.ResetBucketToPivot();
        if (ropeController != null)
        {
            ropeController.SnapToCurrentEndpoints();
        }

        userMessage = "Bucket reset from current pivot, rope length, start angle, and swing direction.";
        UpdateUnifiedCanvasTexts();
    }

    private void SyncLegacyPaintPreview()
    {
        if (fluidSimulation == null)
        {
            SyncIndependentFluidPreviewColor();
            return;
        }

        bool hasPaint = paintEmitter == null
            ? initialPaintAmount > 0f
            : paintEmitter.remainingPaintAmount > 0.0001f;
        if (paintReservoir != null)
        {
            hasPaint = !paintReservoir.IsEmpty;
        }
        Color visibleColor = paintReservoir != null ? paintReservoir.VisiblePaintColor : paintColor;
        fluidSimulation.ApplyLegacyPaintState(visibleColor, hasPaint);
        SyncIndependentFluidPreviewColor();
    }

    private void SyncIndependentFluidPreviewColor()
    {
        if (fluidPreview == null)
        {
            return;
        }

        fluidPreview.paintReservoir = paintReservoir;
        fluidPreview.paintEmitter = paintEmitter;
        fluidPreview.pendulumController = pendulumController;
        if (fluidPreview.colorSync)
        {
            fluidPreview.previewColor = paintReservoir != null ? paintReservoir.VisiblePaintColor : paintColor;
        }
    }

    private void UpdateBucketStatus()
    {
        if (paintEmitter == null)
        {
            return;
        }

        if (paintEmitter.IsBucketEmpty)
        {
            if (!isPaused)
            {
                statusText = "Bucket Empty";
            }
        }
        else if (!isPaused && statusText == "Bucket Empty")
        {
            statusText = "Running";
        }
    }

    private void ApplyMassSpringRopeSettings()
    {
        if (ropeController == null)
        {
            return;
        }

        int segments = Mathf.Max(2, ropeController.nodeCount - 1);
        ropeController.ApplyMassSpringSettings(
            segments,
            ropeController.stiffness,
            ropeController.springDamping,
            ropeFlexibility,
            ropeController.lengthCorrectionIterations
        );
    }

    private void SetFluidPaused(bool paused)
    {
        if (fluidSimulation != null)
        {
            SyncLegacyPaintPreview();
            bool forcePause = paused || (paintEmitter != null && paintEmitter.IsBucketEmpty);
            fluidSimulation.SetPaused(forcePause);
            fluidSimulation.SetLegacyParticleMode(fluidSimulation.legacyParticleMode);
        }
    }

    private void SetPaintEmitterPaused(bool paused)
    {
        if (paintEmitter != null)
        {
            paintEmitter.SetPaused(paused);
        }
    }

    private void CreateRuntimeSystemsIfNeeded()
    {
        if (pendulumController == null)
        {
            pendulumController = FindFirstObjectByType<SphericalPendulumController>();
        }

        if (ropeController == null)
        {
            ropeController = FindFirstObjectByType<MassSpringRope>();
        }

        if (mouseCameraController == null)
        {
            mouseCameraController = FindFirstObjectByType<MouseCameraController>();
        }

        if (mouseGrabController == null)
        {
            mouseGrabController = FindFirstObjectByType<MouseGrabController>();
        }

        if (mouseGrabController == null)
        {
            GameObject grabObject = GameObject.Find("MouseGrabController");
            if (grabObject == null)
            {
                grabObject = new GameObject("MouseGrabController");
            }
            mouseGrabController = grabObject.AddComponent<MouseGrabController>();
        }

        if (fluidPreview == null)
        {
            fluidPreview = FindFirstObjectByType<IndependentFluidVisualizer>();
        }

        if (fluidPreview == null)
        {
            GameObject previewObject = GameObject.Find("IndependentFluidPreview");
            if (previewObject == null)
            {
                previewObject = new GameObject("IndependentFluidPreview");
            }
            fluidPreview = previewObject.GetComponent<IndependentFluidVisualizer>();
            if (fluidPreview == null)
            {
                fluidPreview = previewObject.AddComponent<IndependentFluidVisualizer>();
            }
        }
        fluidPreview.pendulumController = pendulumController;
        fluidPreview.paintReservoir = paintReservoir;
        fluidPreview.paintEmitter = paintEmitter;

        if (paintReservoir == null)
        {
            paintReservoir = FindFirstObjectByType<BucketPaintReservoir>();
        }

        if (paintReservoir == null && pendulumController != null)
        {
            paintReservoir = pendulumController.GetComponent<BucketPaintReservoir>();
            if (paintReservoir == null)
            {
                paintReservoir = pendulumController.gameObject.AddComponent<BucketPaintReservoir>();
            }
        }

        if (openBucketMesh == null)
        {
            openBucketMesh = FindFirstObjectByType<OpenBucketMesh>();
        }

        Transform exitPoint = null;
        GameObject exitObject = GameObject.Find("PaintExitPoint");
        if (exitObject != null)
        {
            exitPoint = exitObject.transform;
        }
        paintExitPoint = exitPoint;

        GameObject boardObject = GameObject.Find("PaintBoard");
        if (paintingSurface == null && boardObject != null)
        {
            paintingSurface = boardObject.GetComponent<PaintingSurface>();
            if (paintingSurface == null)
            {
                paintingSurface = boardObject.AddComponent<PaintingSurface>();
            }
        }

        if (paintingSurface != null)
        {
            paintingSurface.Initialize();
        }

        if (fluidSimulation != null)
        {
            fluidSimulation.SetLegacyParticleMode(showLegacyFluidParticles ? Simulation3D.LegacyParticleMode.Preview : Simulation3D.LegacyParticleMode.Hidden);
            SyncLegacyPaintPreview();
        }

        if (paintEmitter == null)
        {
            GameObject emitterObject = GameObject.Find("PaintEmitter");
            if (emitterObject == null)
            {
                emitterObject = new GameObject("PaintEmitter");
            }

            paintEmitter = emitterObject.GetComponent<PaintParticleEmitter>();
            if (paintEmitter == null)
            {
                paintEmitter = emitterObject.AddComponent<PaintParticleEmitter>();
            }
        }

        paintEmitter.exitPoint = exitPoint;
        paintEmitter.pendulum = pendulumController;
        paintEmitter.paintingSurface = paintingSurface;
        paintEmitter.paintReservoir = paintReservoir;
        paintEmitter.Initialize();

        if (mouseGrabController != null)
        {
            mouseGrabController.sceneCamera = Camera.main;
            mouseGrabController.pendulum = pendulumController;
            mouseGrabController.rope = ropeController;
            mouseGrabController.paintEmitter = paintEmitter;
            mouseGrabController.cameraController = mouseCameraController;
            mouseGrabController.uiController = this;
        }

        if (paintReservoir != null)
        {
            paintReservoir.capacity = Mathf.Max(paintReservoir.capacity, initialPaintAmount);
            paintCapacity = paintReservoir.capacity;
            paintReservoir.BindEmitter(paintEmitter);
            paintReservoir.Initialize();
        }

        if (fluidPreview != null)
        {
            fluidPreview.pendulumController = pendulumController;
            fluidPreview.paintReservoir = paintReservoir;
            fluidPreview.paintEmitter = paintEmitter;
            fluidPreview.previewColor = paintReservoir != null ? paintReservoir.VisiblePaintColor : paintColor;
        }

        if (experimentManager == null)
        {
            GameObject managerObject = GameObject.Find("ExperimentManager");
            if (managerObject == null)
            {
                managerObject = new GameObject("ExperimentManager");
            }

            experimentManager = managerObject.GetComponent<PaintExperimentManager>();
            if (experimentManager == null)
            {
                experimentManager = managerObject.AddComponent<PaintExperimentManager>();
            }
        }

        experimentManager.pendulum = pendulumController;
        experimentManager.rope = ropeController;
        experimentManager.emitter = paintEmitter;
        experimentManager.paintReservoir = paintReservoir;
        experimentManager.paintingSurface = paintingSurface;
        experimentManager.fluidSimulation = fluidSimulation;
        experimentManager.bucketRadius = bucketRadius;
        experimentManager.targetSwingCount = unlimitedSwings ? 0 : targetSwingCount;
        experimentManager.completedSwingCount = swingCount;
        experimentManager.swingTargetCompleted = targetSwingCompleted;
        experimentManager.swingDirectionDegrees = swingDirectionDegrees;
        experimentManager.pivotPosition = pivotPosition;

        airResistanceSlider = airResistanceSlider == null ? FindSlider("Slider_AirResistance") : airResistanceSlider;
        pivotFrictionSlider = pivotFrictionSlider == null ? FindSlider("Slider_PivotFriction") : pivotFrictionSlider;
        airResistanceValueText = airResistanceValueText == null ? FindText("Text_AirResistanceValue") : airResistanceValueText;
        pivotFrictionValueText = pivotFrictionValueText == null ? FindText("Text_PivotFrictionValue") : pivotFrictionValueText;
    }

    private static Slider FindSlider(string objectName)
    {
        GameObject target = GameObject.Find(objectName);
        return target != null ? target.GetComponent<Slider>() : null;
    }

    private static TMP_Text FindText(string objectName)
    {
        GameObject target = GameObject.Find(objectName);
        return target != null ? target.GetComponent<TMP_Text>() : null;
    }

    private void BuildUnifiedCanvasUI()
    {
        EnsureEventSystem();

        GameObject existing = GameObject.Find("MainSimulationUI");
        if (existing != null)
        {
            mainCanvas = existing.GetComponent<Canvas>();
            if (mainCanvas != null)
            {
                mainCanvasGroup = existing.GetComponent<CanvasGroup>();
                if (mainCanvasGroup == null)
                {
                    mainCanvasGroup = existing.AddComponent<CanvasGroup>();
                }
                mainCanvasScaler = existing.GetComponent<CanvasScaler>();
                ApplyUiScaleToCanvasScaler();
                return;
            }
        }

        GameObject canvasObject = new GameObject("MainSimulationUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        mainCanvas = canvasObject.GetComponent<Canvas>();
        mainCanvasGroup = canvasObject.GetComponent<CanvasGroup>();
        mainCanvasScaler = canvasObject.GetComponent<CanvasScaler>();
        mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        mainCanvas.sortingOrder = 100;

        CanvasScaler scaler = mainCanvasScaler;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        ApplyUiScaleToCanvasScaler();

        RectTransform root = canvasObject.GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        CreateTopStatusBar(root);
        CreateTabbedDashboard(root);
        RefreshTexts();
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        eventSystemObject.transform.SetParent(null);
    }

    private void CreateTopStatusBar(RectTransform root)
    {
        RectTransform panel = CreatePanel("Top Status Bar", root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -86f), new Vector2(-18f, -12f));
        HorizontalLayoutGroup layout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 10, 10);
        layout.spacing = 14;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = true;

        topStatusText = CreateText("StatusText", panel, "Status", 22, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        referenceWarningText = CreateText("ReferenceWarnings", panel, "", 19, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        referenceWarningText.color = new Color(1f, 0.78f, 0.25f, 1f);
        mouseCameraDebugText = CreateText("MouseCameraDebug", panel, "", 15, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        Button hideButton = CreateButton(panel, "Hide UI", ToggleUnifiedUIVisibility);
        SetPreferredWidth(hideButton.GetComponent<RectTransform>(), 112f);
    }

    private void CreateTabbedDashboard(RectTransform root)
    {
        RectTransform tabBar = CreatePanel("Tab Button Row", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-600f, -138f), new Vector2(600f, -90f));
        HorizontalLayoutGroup tabLayout = tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabLayout.padding = new RectOffset(8, 8, 6, 6);
        tabLayout.spacing = 8;
        tabLayout.childControlWidth = true;
        tabLayout.childForceExpandWidth = true;
        tabLayout.childControlHeight = true;

        CreateTabButton(tabBar, "Motion", DashboardTab.Motion);
        CreateTabButton(tabBar, "Paint", DashboardTab.Paint);
        CreateTabButton(tabBar, "Fluid Preview", DashboardTab.FluidPreview);
        CreateTabButton(tabBar, "Environment", DashboardTab.Environment);
        CreateTabButton(tabBar, "Canvas", DashboardTab.Canvas);
        CreateTabButton(tabBar, "Results", DashboardTab.Results);
        CreateTabButton(tabBar, "Performance", DashboardTab.Performance);

        RectTransform tabPanel = CreatePanel("Active Tab Content", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(-600f, 190f), new Vector2(600f, -154f));
        tabContentScrollRect = tabPanel.gameObject.AddComponent<ScrollRect>();
        tabContentScrollRect.horizontal = false;
        tabContentScrollRect.vertical = true;
        tabContentScrollRect.movementType = ScrollRect.MovementType.Clamped;
        tabContentScrollRect.scrollSensitivity = 32f;

        RectTransform viewport = CreateUIObject("Tab Content Viewport", tabPanel);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(0f, 0f);
        viewport.offsetMax = new Vector2(-24f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        tabContentScrollRect.viewport = viewport;

        tabContentRoot = CreateUIObject("Tab Content", viewport);
        tabContentRoot.anchorMin = new Vector2(0f, 1f);
        tabContentRoot.anchorMax = new Vector2(1f, 1f);
        tabContentRoot.pivot = new Vector2(0.5f, 1f);
        tabContentRoot.offsetMin = Vector2.zero;
        tabContentRoot.offsetMax = Vector2.zero;
        tabContentScrollRect.content = tabContentRoot;

        tabContentScrollbar = CreateVerticalScrollbar(tabPanel);
        tabContentScrollRect.verticalScrollbar = tabContentScrollbar;
        tabContentScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        tabContentScrollRect.verticalScrollbarSpacing = 4f;
        RebuildActiveTab();
    }

    private void CreateTabButton(Transform parent, string label, DashboardTab tab)
    {
        Button button = CreateButton(parent, label, () =>
        {
            activeTab = tab;
            RebuildActiveTab();
        });
        tabButtons.Add(button);
    }

    private void UpdateTabButtonStyles()
    {
        for (int i = 0; i < tabButtons.Count; i++)
        {
            Image image = tabButtons[i].GetComponent<Image>();
            if (image == null)
            {
                continue;
            }

            image.color = i == (int)activeTab
                ? new Color(0.25f, 0.56f, 0.95f, 1f)
                : new Color(0.14f, 0.2f, 0.28f, 0.96f);
        }
    }

    private RectTransform CreateButtonRow(Transform parent)
    {
        RectTransform row = CreateUIObject("ButtonRow", parent);
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        SetPreferredHeight(row, 44f);
        return row;
    }

    private Scrollbar CreateVerticalScrollbar(RectTransform parent)
    {
        RectTransform scrollbarRect = CreateUIObject("Tab Content Scrollbar", parent);
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-18f, 8f);
        scrollbarRect.offsetMax = new Vector2(-8f, -8f);

        Image track = scrollbarRect.gameObject.AddComponent<Image>();
        track.color = new Color(0.08f, 0.1f, 0.14f, 0.92f);

        Scrollbar scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        RectTransform handleArea = CreateUIObject("Sliding Area", scrollbarRect);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(1f, 1f);
        handleArea.offsetMax = new Vector2(-1f, -1f);

        RectTransform handle = CreateUIObject("Handle", handleArea);
        handle.anchorMin = Vector2.zero;
        handle.anchorMax = Vector2.one;
        handle.offsetMin = Vector2.zero;
        handle.offsetMax = Vector2.zero;
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.42f, 0.62f, 0.86f, 0.95f);

        scrollbar.targetGraphic = handleImage;
        scrollbar.handleRect = handle;
        scrollbar.value = 1f;
        return scrollbar;
    }

    private RectTransform CreateResultScrollArea(Transform parent, float height)
    {
        RectTransform panel = CreatePanel("ScrollableTextArea", parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        SetPreferredHeight(panel, height);
        ScrollRect scrollRect = panel.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        RectTransform viewport = CreatePanel("Viewport", panel, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -8f));
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        scrollRect.viewport = viewport;

        RectTransform content = CreateUIObject("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 12;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = content;
        return content;
    }

    private void SetUiScale(float scale)
    {
        currentUiScale = Mathf.Clamp(scale, 0.85f, 1.15f);
        ApplyUiScaleToCanvasScaler();
        userMessage = "UI scale set to " + scale.ToString("0.00");
    }

    private void ApplyUiScaleToCanvasScaler()
    {
        if (mainCanvasScaler == null)
        {
            return;
        }

        mainCanvasScaler.referenceResolution = baseReferenceResolution / Mathf.Max(0.01f, currentUiScale);
    }

    private void ToggleUnifiedUIVisibility()
    {
        uiVisible = !uiVisible;
        if (mainCanvasGroup != null)
        {
            mainCanvasGroup.alpha = uiVisible ? 1f : 0f;
            mainCanvasGroup.interactable = uiVisible;
            mainCanvasGroup.blocksRaycasts = uiVisible;
        }
    }

    private void RebuildActiveTab()
    {
        if (tabContentRoot == null)
        {
            return;
        }

        for (int i = tabContentRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(tabContentRoot.GetChild(i).gameObject);
        }

        VerticalLayoutGroup layout = tabContentRoot.gameObject.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = tabContentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        layout.padding = new RectOffset(22, 22, 18, 56);
        layout.spacing = 12;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = tabContentRoot.gameObject.GetComponent<ContentSizeFitter>();
        if (fitter == null)
        {
            fitter = tabContentRoot.gameObject.AddComponent<ContentSizeFitter>();
        }

        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        UpdateTabButtonStyles();
        CreateSection(tabContentRoot, activeTab.ToString());

        switch (activeTab)
        {
            case DashboardTab.Motion:
                CreateMotionTab(tabContentRoot);
                break;
            case DashboardTab.Paint:
                CreatePaintTab(tabContentRoot);
                break;
            case DashboardTab.FluidPreview:
                CreateFluidPreviewTab(tabContentRoot);
                break;
            case DashboardTab.Environment:
                CreateEnvironmentTab(tabContentRoot);
                break;
            case DashboardTab.Canvas:
                CreateCanvasTab(tabContentRoot);
                break;
            case DashboardTab.Results:
                CreateResultsTab(tabContentRoot);
                break;
            case DashboardTab.Performance:
                CreatePerformanceTab(tabContentRoot);
                break;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(tabContentRoot);
        if (tabContentScrollRect != null)
        {
            tabContentScrollRect.verticalNormalizedPosition = 1f;
        }
    }

    private void CreateMotionTab(Transform parent)
    {
        RectTransform buttonRow = CreateButtonRow(parent);
        CreateButton(buttonRow, "Start", StartSimulation);
        CreateButton(buttonRow, "Pause / Resume", TogglePauseResume);
        CreateButton(buttonRow, "Reset All", ResetSimulation);
        ropeLengthSlider = CreateSliderRow(parent, "Rope Length", 0.5f, 8f, pendulumController != null ? pendulumController.ropeLength : 4f, "0.00 m", ChangeRopeLength, out ropeLengthValueText);
        CreateBucketRequirementControls(parent);
        CreateMassSpringRopeControls(parent);
        initialAngleSlider = CreateSliderRow(parent, "Start Angle", 0f, 80f, pendulumController != null ? pendulumController.theta * Mathf.Rad2Deg : 30f, "0 deg", ChangeInitialAngle, out initialAngleValueText);
        sidePushSlider = CreateSliderRow(parent, "Initial Velocity", -2f, 2f, pendulumController != null ? pendulumController.phiDot : 0f, "0.00", ChangeSidePush, out sidePushValueText);
        CreateMouseGrabControls(parent);
        dampingSlider = CreateSliderRow(parent, "Damping", 0f, 0.25f, pendulumController != null ? pendulumController.damping : 0.05f, "0.000", ChangeDamping, out dampingValueText);
        CreateSliderRow(parent, "Rope Flexibility", 0f, 1f, ropeFlexibility, "0.00", value => { ropeFlexibility = value; ApplyExtendedSettings(false); }, out _);
        mouseGrabDebugText = CreateText("MouseGrabDebug", parent, BuildMouseGrabDebugText(), 19, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        SetPreferredHeight(mouseGrabDebugText.rectTransform, 175f);
        ropeDebugText = CreateText("RopeDebug", parent, BuildRopeDebugText(), 19, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        SetPreferredHeight(ropeDebugText.rectTransform, 450f);
    }

    private void CreateBucketRequirementControls(Transform parent)
    {
        CreateSection(parent, "Required Inputs");

        CreateSliderRow(parent, "Bucket Weight / Mass", 0.2f, 10f, bucketWeight, "0.00 kg", value =>
        {
            bucketWeight = value;
            ApplyExtendedSettings(false);
        }, out _);

        CreateSliderRow(parent, "Bucket Radius", 0.15f, 1f, bucketRadius, "0.00 m", value =>
        {
            bucketRadius = value;
            ApplyExtendedSettings(false);
        }, out _);

        CreateDropdown(parent, "Auto Capacity From Radius", new[] { "Off", "On" }, autoEstimatePaintCapacityFromBucketRadius ? 1 : 0, value =>
        {
            autoEstimatePaintCapacityFromBucketRadius = value == 1;
            ApplyExtendedSettings(false);
        });

        CreateDropdown(parent, "Show Bucket Debug", new[] { "Off", "On" }, pendulumController != null && pendulumController.showBucketDebug ? 1 : 0, value =>
        {
            if (pendulumController != null)
            {
                pendulumController.showBucketDebug = value == 1;
            }
        });

        CreateSliderRow(parent, "Pivot X", -8f, 8f, pivotPosition.x, "0.00", value =>
        {
            pivotPosition.x = value;
            ApplyPivotToSystems(true);
        }, out _);

        CreateSliderRow(parent, "Pivot Y", 0.5f, 12f, pivotPosition.y, "0.00", value =>
        {
            pivotPosition.y = value;
            ApplyPivotToSystems(true);
        }, out _);

        CreateSliderRow(parent, "Pivot Z", -8f, 8f, pivotPosition.z, "0.00", value =>
        {
            pivotPosition.z = value;
            ApplyPivotToSystems(true);
        }, out _);

        CreateSliderRow(parent, "Swing Direction Angle", 0f, 360f, swingDirectionDegrees, "0 deg", value =>
        {
            swingDirectionDegrees = value;
            ApplySwingDirection(true);
        }, out _);

        CreateSliderRow(parent, "Number of Swings", 1f, 50f, targetSwingCount, "0", value =>
        {
            targetSwingCount = Mathf.Clamp(Mathf.RoundToInt(value), 1, 50);
            targetSwingCompleted = false;
            if (paintEmitter != null)
            {
                paintEmitter.emissionEnabled = true;
            }
        }, out _);

        CreateDropdown(parent, "Swing Limit", new[] { "Target", "Unlimited" }, unlimitedSwings ? 1 : 0, value =>
        {
            unlimitedSwings = value == 1;
            targetSwingCompleted = false;
            if (paintEmitter != null)
            {
                paintEmitter.emissionEnabled = true;
            }
        });

        CreateButton(parent, "Reset Bucket To Pivot", ResetBucketToPivotFromUI);
    }

    private void CreateMouseGrabControls(Transform parent)
    {
        CreateSection(parent, "Mouse Grab");
        CreateDropdown(parent, "Mouse Grab", new[] { "Off", "On" }, mouseGrabController != null && mouseGrabController.mouseGrabEnabled ? 1 : 0, value =>
        {
            if (mouseGrabController != null)
            {
                mouseGrabController.mouseGrabEnabled = value == 1;
                if (!mouseGrabController.mouseGrabEnabled)
                {
                    mouseGrabController.ResetDragState();
                }
            }
        });

        CreateDropdown(parent, "Grab Target", new[] { "Bucket", "Rope End" }, mouseGrabController != null ? (int)mouseGrabController.grabTarget : 0, value =>
        {
            if (mouseGrabController != null)
            {
                mouseGrabController.grabTarget = (MouseGrabController.GrabTarget)value;
            }
        });

        CreateDropdown(parent, "Apply Release Velocity", new[] { "Off", "On" }, mouseGrabController == null || mouseGrabController.applyReleaseVelocity ? 1 : 0, value =>
        {
            if (mouseGrabController != null)
            {
                mouseGrabController.applyReleaseVelocity = value == 1;
            }
        });

        CreateSliderRow(parent, "Grab Sensitivity", 0.25f, 3f, mouseGrabController != null ? mouseGrabController.grabSensitivity : 1f, "0.00", value =>
        {
            if (mouseGrabController != null)
            {
                mouseGrabController.grabSensitivity = value;
            }
        }, out _);

        CreateSliderRow(parent, "Max Drag Angle", 5f, 89f, mouseGrabController != null ? mouseGrabController.maxDragAngle : 75f, "0 deg", value =>
        {
            if (mouseGrabController != null)
            {
                mouseGrabController.maxDragAngle = value;
            }
        }, out _);

        CreateButton(parent, "Reset Drag State", () =>
        {
            if (mouseGrabController != null)
            {
                mouseGrabController.ResetDragState();
            }
        });
    }

    private void CreateMassSpringRopeControls(Transform parent)
    {
        CreateDropdown(parent, "Rope Type", new[] { MassSpringRope.RopeModelName }, 0, _ =>
        {
            if (ropeController != null)
            {
                ropeController.ropeType = MassSpringRope.RopeModelName;
            }
        });

        CreateSliderRow(parent, "Rope Segments", 4f, 80f, ropeController != null ? Mathf.Max(2, ropeController.nodeCount - 1) : 44f, "0", value =>
        {
            if (ropeController != null)
            {
                ropeController.ApplyMassSpringSettings(Mathf.RoundToInt(value), ropeController.stiffness, ropeController.springDamping, ropeFlexibility, ropeController.lengthCorrectionIterations);
            }
        }, out _);

        CreateSliderRow(parent, "Spring Stiffness", 200f, 6000f, ropeController != null ? ropeController.stiffness : 3000f, "0", value =>
        {
            if (ropeController != null)
            {
                ropeController.ApplyMassSpringSettings(Mathf.Max(2, ropeController.nodeCount - 1), value, ropeController.springDamping, ropeFlexibility, ropeController.lengthCorrectionIterations);
            }
        }, out _);

        CreateSliderRow(parent, "Rope Damping", 0f, 200f, ropeController != null ? ropeController.springDamping : 70f, "0", value =>
        {
            if (ropeController != null)
            {
                ropeController.ApplyMassSpringSettings(Mathf.Max(2, ropeController.nodeCount - 1), ropeController.stiffness, value, ropeFlexibility, ropeController.lengthCorrectionIterations);
            }
        }, out _);

        CreateSliderRow(parent, "Constraint Iterations", 0f, 30f, ropeController != null ? ropeController.lengthCorrectionIterations : 18f, "0", value =>
        {
            if (ropeController != null)
            {
                ropeController.ApplyMassSpringSettings(Mathf.Max(2, ropeController.nodeCount - 1), ropeController.stiffness, ropeController.springDamping, ropeFlexibility, Mathf.RoundToInt(value));
            }
        }, out _);
    }

    private void CreatePaintTab(Transform parent)
    {
        CreateSliderRow(parent, "Paint Capacity", 0.1f, 100f, paintReservoir != null ? paintReservoir.capacity : paintCapacity, "0.00 kg", value =>
        {
            paintCapacity = value;
            if (paintReservoir != null)
            {
                paintReservoir.SetCapacity(value);
            }
            ApplyExtendedSettings(false);
        }, out _);
        CreateSliderRow(parent, "Paint Amount", 0f, 100f, initialPaintAmount, "0.00 kg", ChangePaintAmount, out _);
        holeDiameterSlider = CreateSliderRow(parent, "Hole Diameter", 0.005f, 0.08f, holeDiameter, "0.000 m", ChangeHoleDiameter, out holeDiameterValueText);
        viscositySlider = CreateSliderRow(parent, "Viscosity", 0.01f, 3f, viscosity, "0.00", ChangeViscosity, out viscosityValueText);
        exitSpeedSlider = CreateSliderRow(parent, "Exit Speed", 0f, 8f, exitSpeed, "0.00 m/s", ChangeExitSpeed, out exitSpeedValueText);
        CreateDropdown(parent, "Advanced Paint Flow", new[] { "Off", "On" }, advancedPaintMode ? 1 : 0, value =>
        {
            advancedPaintMode = value == 1;
            if (!advancedPaintMode)
            {
                emissionRate = Mathf.Min(emissionRate, 40f);
            }
            if (emissionRateSlider != null)
            {
                emissionRateSlider.maxValue = advancedPaintMode ? 120f : 40f;
                SetSliderValueWithoutNotify(emissionRateSlider, emissionRate);
            }
            ApplyExtendedSettings(false);
            RefreshTexts();
        });
        emissionRateSlider = CreateSliderRow(parent, "Flow Multiplier", 1f, advancedPaintMode ? 120f : 40f, emissionRate, "0", ChangeEmissionRate, out emissionRateValueText);
        CreateColorButtons(parent);
        CreateColorMixControls(parent);
        CreateReservoirDebugControls(parent);
        CreateTrailControls(parent);
        CreateAirborneParticleControls(parent);
        paintStatusText = CreateText("PaintStatus", parent, BuildPaintStatusText(), 19, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        SetPreferredHeight(paintStatusText.rectTransform, 720f);
        CreateButton(parent, "Clear Canvas", ClearCanvas);
    }

    private void CreateFluidPreviewTab(Transform parent)
    {
        EnsureFluidPreview();

        CreateDropdown(parent, "Show Fluid Preview", new[] { "Off", "On" }, fluidPreview != null && fluidPreview.showPreview ? 1 : 0, value =>
        {
            if (fluidPreview != null)
            {
                fluidPreview.SetShowPreview(value == 1);
            }
        });

        CreateDropdown(parent, "Preview Mode", new[] { "Follow Bucket Liquid", "Manual Debug" }, fluidPreview != null ? (int)fluidPreview.previewMode : 0, value =>
        {
            if (fluidPreview != null)
            {
                fluidPreview.previewMode = (IndependentFluidVisualizer.PreviewMode)value;
                RebuildActiveTab();
            }
        });

        CreateDropdown(parent, "Preview Render Mode", new[] { "Auto", "CPU ParticleSystem", "GPU Preview" }, fluidPreview != null ? (int)fluidPreview.previewRenderMode : 0, value =>
        {
            if (fluidPreview != null)
            {
                fluidPreview.previewRenderMode = (IndependentFluidVisualizer.PreviewRenderMode)value;
                fluidPreview.ForceRebuildParticles();
            }
        });

        CreateDropdown(parent, "Preview Quality", new[] { "Low 8k", "Medium 50k", "High 200k", "Ultra 1M" }, fluidPreview != null ? (int)fluidPreview.particlePreset : 1, value =>
        {
            ApplyFluidPreviewPreset((IndependentFluidVisualizer.ParticlePreset)value);
        });

        RectTransform presetRow = CreateButtonRow(parent);
        CreateButton(presetRow, "Low 8k", () => ApplyFluidPreviewPreset(IndependentFluidVisualizer.ParticlePreset.Low));
        CreateButton(presetRow, "Medium 50k", () => ApplyFluidPreviewPreset(IndependentFluidVisualizer.ParticlePreset.Medium));
        CreateButton(presetRow, "High 200k", () => ApplyFluidPreviewPreset(IndependentFluidVisualizer.ParticlePreset.High));
        CreateButton(presetRow, "Ultra 1M", () => ApplyFluidPreviewPreset(IndependentFluidVisualizer.ParticlePreset.Ultra));

        CreateDropdown(parent, "Show Internal Particles", new[] { "Off", "On" }, fluidPreview != null && fluidPreview.showInternalParticles ? 1 : 0, value =>
        {
            if (fluidPreview != null)
            {
                fluidPreview.showInternalParticles = value == 1;
                fluidPreview.ForceRebuildParticles();
            }
        });

        CreateDropdown(parent, "Show Density Colors", new[] { "Off", "On" }, fluidPreview == null || fluidPreview.showDensityColors ? 1 : 0, value =>
        {
            if (fluidPreview != null)
            {
                fluidPreview.showDensityColors = value == 1;
            }
        });

        CreateDropdown(parent, "Show Collision Flashes", new[] { "Off", "On" }, fluidPreview == null || fluidPreview.showCollisionFlashes ? 1 : 0, value =>
        {
            if (fluidPreview != null)
            {
                fluidPreview.showCollisionFlashes = value == 1;
            }
        });

        CreateDropdown(parent, "Show Flow Layers", new[] { "Off", "On" }, fluidPreview == null || fluidPreview.showFlowLayers ? 1 : 0, value =>
        {
            if (fluidPreview != null)
            {
                fluidPreview.showFlowLayers = value == 1;
            }
        });

        CreateDropdown(parent, "Show Particle Count Proof", new[] { "Off", "On" }, fluidPreview != null && fluidPreview.showParticleCountProof ? 1 : 0, value =>
        {
            if (fluidPreview != null)
            {
                fluidPreview.showParticleCountProof = value == 1;
            }
        });

        if (fluidPreview != null)
        {
            CreateSection(parent, "Preview Follow Calibration");
            CreateDropdown(parent, "Preview Follow Model", new[] { "Exact Slosh Offsets", "Acceleration Response", "Enhanced Educational" }, (int)fluidPreview.previewFollowModel, value =>
            {
                fluidPreview.previewFollowModel = (IndependentFluidVisualizer.PreviewFollowModel)value;
            });
            CreateDropdown(parent, "Show Liquid Follow Debug", new[] { "Off", "On" }, fluidPreview.showLiquidFollowDebug ? 1 : 0, value =>
            {
                fluidPreview.showLiquidFollowDebug = value == 1;
            });
            CreateDropdown(parent, "Swap Slosh Axes", new[] { "Off", "On" }, fluidPreview.swapSloshAxes ? 1 : 0, value =>
            {
                fluidPreview.swapSloshAxes = value == 1;
            });
            CreateDropdown(parent, "Invert Slosh X", new[] { "Off", "On" }, fluidPreview.invertSloshX ? 1 : 0, value =>
            {
                fluidPreview.invertSloshX = value == 1;
            });
            CreateDropdown(parent, "Invert Slosh Z", new[] { "Off", "On" }, fluidPreview.invertSloshZ ? 1 : 0, value =>
            {
                fluidPreview.invertSloshZ = value == 1;
            });
            CreateSliderRow(parent, "Slosh Gain X", 0.1f, 3f, fluidPreview.sloshGainX, "0.00", value =>
            {
                fluidPreview.sloshGainX = value;
            }, out _);
            CreateSliderRow(parent, "Slosh Gain Z", 0.1f, 3f, fluidPreview.sloshGainZ, "0.00", value =>
            {
                fluidPreview.sloshGainZ = value;
            }, out _);
            CreateSliderRow(parent, "Liquid Lag", 0.02f, 0.8f, fluidPreview.liquidLag, "0.00", value =>
            {
                fluidPreview.liquidLag = value;
            }, out _);
            CreateSliderRow(parent, "Liquid Damping", 0.2f, 8f, fluidPreview.liquidDamping, "0.00", value =>
            {
                fluidPreview.liquidDamping = value;
            }, out _);
            CreateSliderRow(parent, "Max Surface Tilt", 12f, 18f, fluidPreview.maxSurfaceTilt, "0 deg", value =>
            {
                fluidPreview.maxSurfaceTilt = value;
            }, out _);
            RectTransform followTestRow = CreateButtonRow(parent);
            CreateButton(followTestRow, "Test Liquid Tilt X", () =>
            {
                fluidPreview.TestLiquidTiltX();
            });
            CreateButton(followTestRow, "Test Liquid Tilt Z", () =>
            {
                fluidPreview.TestLiquidTiltZ();
            });
            CreateButton(parent, "Reset Liquid Calibration", ResetFluidPreviewLiquidCalibration);

            CreateSection(parent, "Preview Box Controls");
            CreateSliderRow(parent, "Box Width", 0.8f, 5f, fluidPreview.previewBoxWidth, "0.00", value =>
            {
                fluidPreview.SetPreviewBoxSize(value, fluidPreview.previewBoxHeight, fluidPreview.previewBoxDepth);
            }, out _);
            CreateSliderRow(parent, "Box Height", 0.4f, 3f, fluidPreview.previewBoxHeight, "0.00", value =>
            {
                fluidPreview.SetPreviewBoxSize(fluidPreview.previewBoxWidth, value, fluidPreview.previewBoxDepth);
            }, out _);
            CreateSliderRow(parent, "Box Depth", 0.5f, 3f, fluidPreview.previewBoxDepth, "0.00", value =>
            {
                fluidPreview.SetPreviewBoxSize(fluidPreview.previewBoxWidth, fluidPreview.previewBoxHeight, value);
            }, out _);
            CreateSliderRow(parent, "Position X", -10f, 10f, fluidPreview.previewPositionX, "0.00", value =>
            {
                fluidPreview.SetPreviewPosition(value, fluidPreview.previewPositionY, fluidPreview.previewPositionZ);
            }, out _);
            CreateSliderRow(parent, "Position Y", -2f, 6f, fluidPreview.previewPositionY, "0.00", value =>
            {
                fluidPreview.SetPreviewPosition(fluidPreview.previewPositionX, value, fluidPreview.previewPositionZ);
            }, out _);
            CreateSliderRow(parent, "Position Z", -6f, 6f, fluidPreview.previewPositionZ, "0.00", value =>
            {
                fluidPreview.SetPreviewPosition(fluidPreview.previewPositionX, fluidPreview.previewPositionY, value);
            }, out _);

            RectTransform boxButtonRow = CreateButtonRow(parent);
            CreateButton(boxButtonRow, "Reset Box Transform", ResetFluidPreviewBoxTransform);
            CreateButton(boxButtonRow, "Force Rebuild Preview", ForceRebuildFluidPreview);

            CreateSection(parent, "Preview Motion Controls");
            CreateSliderRow(parent, "Motion Strength", 0f, 4f, fluidPreview.previewMotionStrength, "0.00", value =>
            {
                fluidPreview.previewMotionStrength = value;
            }, out _);
            CreateSliderRow(parent, "Wave Strength", 0f, 4f, fluidPreview.previewWaveStrength, "0.00", value =>
            {
                fluidPreview.previewWaveStrength = value;
            }, out _);
            CreateSliderRow(parent, "Turbulence", 0f, 2f, fluidPreview.previewTurbulenceStrength, "0.00", value =>
            {
                fluidPreview.previewTurbulenceStrength = value;
            }, out _);
            CreateSliderRow(parent, "Wall Bounce", 0f, 2f, fluidPreview.previewWallBounceStrength, "0.00", value =>
            {
                fluidPreview.previewWallBounceStrength = value;
            }, out _);
            CreateSliderRow(parent, "Flow Layers", 0f, 3f, fluidPreview.previewFlowLayerStrength, "0.00", value =>
            {
                fluidPreview.previewFlowLayerStrength = value;
            }, out _);
            CreateSliderRow(parent, "Particle Size", 0.3f, 3f, fluidPreview.previewParticleSizeMultiplier, "0.00x", value =>
            {
                fluidPreview.previewParticleSizeMultiplier = value;
            }, out _);
            CreateSliderRow(parent, "Particle Visibility", 0.2f, 3f, fluidPreview.previewParticleAlphaMultiplier, "0.00x", value =>
            {
                fluidPreview.previewParticleAlphaMultiplier = value;
            }, out _);
            CreateSliderRow(parent, "Glass Transparency", 0.02f, 0.25f, fluidPreview.previewGlassAlpha, "0.00", value =>
            {
                fluidPreview.previewGlassAlpha = value;
            }, out _);
            CreateSliderRow(parent, "Liquid Surface", 0.02f, 0.75f, fluidPreview.previewLiquidSurfaceAlpha, "0.00", value =>
            {
                fluidPreview.previewLiquidSurfaceAlpha = value;
            }, out _);

            RectTransform visualButtonRow = CreateButtonRow(parent);
            CreateButton(visualButtonRow, "Reset Preview Visuals", ResetFluidPreviewVisuals);
            CreateButton(visualButtonRow, "Reset Preview Motion", ResetFluidPreviewMotion);
        }

        CreateButton(parent, "Force Rebuild Preview Particles", ForceRebuildFluidPreviewParticles);

        if (fluidPreview != null && fluidPreview.previewMode == IndependentFluidVisualizer.PreviewMode.ManualDebug)
        {
            CreateSection(parent, "Manual Debug");
            CreateSliderRow(parent, "Preview Fill Percent", 5f, 100f, fluidPreview.fillPercent * 100f, "0%", value =>
            {
                fluidPreview.fillPercent = Mathf.Clamp01(value / 100f);
            }, out _);
            CreateSliderRow(parent, "Preview Particle Count", 25f, 1000000f, fluidPreview.previewParticleCount, "0", value =>
            {
                fluidPreview.SetPreviewParticleCount(Mathf.RoundToInt(value));
            }, out _);
            CreateSliderRow(parent, "Preview Slosh Strength", 0f, 1.5f, fluidPreview.sloshStrength, "0.00", value =>
            {
                fluidPreview.sloshStrength = value;
            }, out _);
            CreateSliderRow(parent, "Preview Motion Speed", 0.1f, 5f, fluidPreview.motionSpeed, "0.00", value =>
            {
                fluidPreview.motionSpeed = value;
            }, out _);
            CreateSliderRow(parent, "Preview Slosh Damping", 0.1f, 8f, fluidPreview.sloshDamping, "0.00", value =>
            {
                fluidPreview.sloshDamping = value;
            }, out _);
        }

        fluidPreviewStatsText = CreateText("FluidPreviewStats", parent, fluidPreview != null ? fluidPreview.StatsText : "Fluid preview missing.", 19, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        SetPreferredHeight(fluidPreviewStatsText.rectTransform, 780f);
    }

    private void CreateReservoirDebugControls(Transform parent)
    {
        CreateSection(parent, "Reservoir Debug");

        CreateDropdown(parent, "Force Show Liquid", new[] { "Off", "On" }, paintReservoir != null && paintReservoir.forceShowLiquidVisual ? 1 : 0, value =>
        {
            if (paintReservoir != null)
            {
                paintReservoir.forceShowLiquidVisual = value == 1;
            }
        });

        CreateDropdown(parent, "Liquid Axis", new[] { "Local X", "Local Y", "Local Z" }, paintReservoir != null ? (int)paintReservoir.liquidAxis : 1, value =>
        {
            if (paintReservoir != null)
            {
                paintReservoir.liquidAxis = (BucketPaintReservoir.LiquidAxis)value;
            }
        });

        CreateDropdown(parent, "Flip Liquid Axis", new[] { "Off", "On" }, paintReservoir != null && paintReservoir.flipAxis ? 1 : 0, value =>
        {
            if (paintReservoir != null)
            {
                paintReservoir.flipAxis = value == 1;
            }
        });

        CreateSliderRow(parent, "Liquid Anchor Local Position X", -1.5f, 1.5f, paintReservoir != null ? paintReservoir.liquidAnchorLocalPosition.x : 0f, "0.00", value =>
        {
            if (paintReservoir != null)
            {
                Vector3 position = paintReservoir.liquidAnchorLocalPosition;
                position.x = value;
                paintReservoir.SetLiquidAnchorLocalPosition(position);
            }
        }, out _);

        CreateSliderRow(parent, "Liquid Anchor Local Position Y", -1.5f, 1.5f, paintReservoir != null ? paintReservoir.liquidAnchorLocalPosition.y : 0f, "0.00", value =>
        {
            if (paintReservoir != null)
            {
                Vector3 position = paintReservoir.liquidAnchorLocalPosition;
                position.y = value;
                paintReservoir.SetLiquidAnchorLocalPosition(position);
            }
        }, out _);

        CreateSliderRow(parent, "Liquid Anchor Local Position Z", -1.5f, 1.5f, paintReservoir != null ? paintReservoir.liquidAnchorLocalPosition.z : 0f, "0.00", value =>
        {
            if (paintReservoir != null)
            {
                Vector3 position = paintReservoir.liquidAnchorLocalPosition;
                position.z = value;
                paintReservoir.SetLiquidAnchorLocalPosition(position);
            }
        }, out _);

        CreateSliderRow(parent, "Liquid Radius", 0.05f, 1.2f, paintReservoir != null ? paintReservoir.liquidRadius : 0.45f, "0.00", value =>
        {
            if (paintReservoir != null)
            {
                paintReservoir.liquidRadius = value;
                paintReservoir.RebuildLiquidMesh();
            }
        }, out _);

        CreateSliderRow(parent, "Liquid Thickness", 0.002f, 0.12f, paintReservoir != null ? paintReservoir.surfaceThickness : 0.035f, "0.000", value =>
        {
            if (paintReservoir != null)
            {
                paintReservoir.surfaceThickness = value;
                paintReservoir.RebuildLiquidMesh();
            }
        }, out _);

        CreateButton(parent, "Auto Fit Liquid To Bucket", () =>
        {
            if (paintReservoir != null)
            {
                paintReservoir.AutoFitLiquidToBucket();
            }
        });
    }

    private void CreateColorMixControls(Transform parent)
    {
        int modeIndex = paintReservoir != null ? (int)paintReservoir.mixMode : 0;
        mixModeDropdown = CreateDropdown(parent, "Mix Mode", new[] { "Single Color", "Mixed Bucket", "Sequential Colors" }, modeIndex, value =>
        {
            if (paintReservoir != null)
            {
                paintReservoir.SetMixMode((BucketPaintReservoir.PaintMixMode)value);
            }
            SyncLegacyPaintPreview();
        });

        CreateSliderRow(parent, "Color Mix Strength", 0f, 1f, paintReservoir != null ? paintReservoir.colorMixStrength : 0.55f, "0.00", value =>
        {
            if (paintReservoir != null)
            {
                paintReservoir.colorMixStrength = value;
                paintReservoir.SetMixMode(paintReservoir.mixMode);
            }
            SyncLegacyPaintPreview();
        }, out _);

        RectTransform row = CreateButtonRow(parent);
        CreateButton(row, "Add Color To Bucket", AddCurrentColorToBucket);
        CreateButton(row, "Clear Colors", ClearBucketColors);
    }

    private void CreateTrailControls(Transform parent)
    {
        int trailModeIndex = paintingSurface != null ? (int)paintingSurface.ActiveTrailMode : (int)PaintingSurface.TrailRenderMode.Ribbon;
        trailModeDropdown = CreateDropdown(parent, "Trail Mode", new[] { "Dots", "Trails", "Ribbon" }, Mathf.Clamp(trailModeIndex, 0, 2), value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.SetTrailMode((PaintingSurface.TrailRenderMode)value);
            }
        });

        CreateSliderRow(parent, "Stroke Radius Multiplier", 0.3f, 3f, paintingSurface != null ? paintingSurface.strokeRadius : 1f, "0.00x", value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.strokeRadius = Mathf.Clamp(value, 0.3f, 3f);
            }
        }, out _);

        CreateSliderRow(parent, "Stroke Smoothness", 0f, 1f, paintingSurface != null ? paintingSurface.strokeSmoothing : 0.65f, "0.00", value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.strokeSmoothing = value;
            }
        }, out _);

        CreateSliderRow(parent, "Connect Distance", 0.01f, 0.14f, paintingSurface != null ? paintingSurface.connectDistanceThreshold : 0.065f, "0.000", value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.connectDistanceThreshold = value;
            }
        }, out _);

        CreateSliderRow(parent, "Max Time Gap", 0.04f, 0.18f, paintingSurface != null ? paintingSurface.maxStrokeTimeGap : 0.1f, "0.00 s", value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.maxStrokeTimeGap = value;
            }
        }, out _);

        CreateDropdown(parent, "Trail Test", new[] { "Off", "Sine Test" }, paintEmitter != null && paintEmitter.deterministicTrailTestMode ? 1 : 0, value =>
        {
            if (paintEmitter != null)
            {
                paintEmitter.deterministicTrailTestMode = value == 1;
            }
        });

        CreateSection(parent, "Paint Mode Tests");
        RectTransform testRow = CreateButtonRow(parent);
        CreateButton(testRow, "Test Dots", () => DrawPaintModeTest(PaintingSurface.TrailRenderMode.Dots));
        CreateButton(testRow, "Test Trails", () => DrawPaintModeTest(PaintingSurface.TrailRenderMode.Trails));
        CreateButton(testRow, "Test Ribbon", () => DrawPaintModeTest(PaintingSurface.TrailRenderMode.Ribbon));
        CreateButton(testRow, "Clear Canvas", ClearCanvas);
    }

    private void CreateAirborneParticleControls(Transform parent)
    {
        CreateSliderRow(parent, "Airborne Particle Size", 0.005f, 0.05f, paintEmitter != null ? paintEmitter.airborneParticleSize : 0.015f, "0.000 m", value =>
        {
            if (paintEmitter != null)
            {
                paintEmitter.airborneParticleSize = value;
                paintEmitter.particleVisualScale = value;
            }
        }, out _);

        CreateDropdown(parent, "Show Airborne Particles", new[] { "Off", "On" }, paintEmitter != null && paintEmitter.showAirborneParticles ? 1 : 0, value =>
        {
            if (paintEmitter != null)
            {
                paintEmitter.showAirborneParticles = value == 1;
            }
        });
    }

    private void CreateEnvironmentTab(Transform parent)
    {
        gravitySlider = CreateSliderRow(parent, "Gravity", 0f, 20f, pendulumController != null ? pendulumController.gravity : 9.81f, "0.00 m/s2", ChangeGravity, out gravityValueText);
        airResistanceSlider = CreateSliderRow(parent, "Air Resistance", 0f, 2f, pendulumController != null ? pendulumController.airResistanceCoefficient : 0f, "0.00", ChangeAirResistance, out airResistanceValueText);
        pivotFrictionSlider = CreateSliderRow(parent, "Pivot Friction", 0f, 2f, pendulumController != null ? pendulumController.pivotFrictionCoefficient : 0f, "0.00", ChangePivotFriction, out pivotFrictionValueText);
        CreateSliderRow(parent, "Humidity", 0f, 1f, humidity, "0.00", value => { humidity = value; ApplyExtendedSettings(false); }, out _);
    }

    private void CreateCanvasTab(Transform parent)
    {
        CreateSliderRow(parent, "Canvas Width", 1f, 20f, canvasWidth, "0.0", value => { canvasWidth = value; ApplyExtendedSettings(false); }, out _);
        CreateSliderRow(parent, "Canvas Height", 1f, 20f, canvasHeight, "0.0", value => { canvasHeight = value; ApplyExtendedSettings(false); }, out _);
        surfaceTypeDropdown = CreateDropdown(parent, "Surface Type", surfaceTypes, surfaceTypeIndex, value => { surfaceTypeIndex = value; ApplyExtendedSettings(false); });
        canvasOrientationDropdown = CreateDropdown(parent, "Canvas Orientation", new[] { "Horizontal", "Tilted" }, canvasTilted ? 1 : 0, value =>
        {
            canvasTilted = value == 1;
            ApplyExtendedSettings(false);
        });
        canvasTiltSlider = CreateSliderRow(parent, "Tilt Angle", 0f, 60f, canvasTiltDegrees, "0 deg", value =>
        {
            canvasTiltDegrees = value;
            ApplyExtendedSettings(false);
        }, out _);
        CreateDropdown(parent, "Paint Render Mode", new[] { "World Decals Recommended", "Texture UV Legacy" }, paintingSurface != null && paintingSurface.paintRenderMode == PaintingSurface.PaintRenderMode.TextureUvLegacy ? 1 : 0, value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.SetPaintRenderMode(value == 1 ? PaintingSurface.PaintRenderMode.TextureUvLegacy : PaintingSurface.PaintRenderMode.WorldDecals);
                UpdateCanvasStatusText();
            }
        });
        PaintingSurface.BoardMappingPlane[] mappingPlaneOptions =
        {
            PaintingSurface.BoardMappingPlane.LocalXZ,
            PaintingSurface.BoardMappingPlane.LocalXY,
            PaintingSurface.BoardMappingPlane.LocalYZ
        };
        int mappingPlaneIndex = 0;
        if (paintingSurface != null)
        {
            for (int i = 0; i < mappingPlaneOptions.Length; i++)
            {
                if (paintingSurface.mappingPlane == mappingPlaneOptions[i])
                {
                    mappingPlaneIndex = i;
                    break;
                }
            }
        }

        CreateDropdown(parent, "Mapping Plane", new[] { "LocalXZ", "LocalXY", "LocalYZ" }, mappingPlaneIndex, value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.SetMappingPlane(mappingPlaneOptions[Mathf.Clamp(value, 0, mappingPlaneOptions.Length - 1)]);
                UpdateCanvasStatusText();
            }
        });
        CreateDropdown(parent, "Invert Right Axis", new[] { "Off", "On" }, paintingSurface != null && paintingSurface.invertRightAxis ? 1 : 0, value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.SetInvertRightAxis(value == 1);
                UpdateCanvasStatusText();
            }
        });
        CreateDropdown(parent, "Invert Up Axis", new[] { "Off", "On" }, paintingSurface != null && paintingSurface.invertUpAxis ? 1 : 0, value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.SetInvertUpAxis(value == 1);
                UpdateCanvasStatusText();
            }
        });
        CreateDropdown(parent, "Swap Axes", new[] { "Off", "On" }, paintingSurface != null && paintingSurface.swapAxes ? 1 : 0, value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.SetSwapAxes(value == 1);
                UpdateCanvasStatusText();
            }
        });
        CreateDropdown(parent, "Show Paint Hit Debug Markers", new[] { "Off", "On" }, paintingSurface != null && paintingSurface.showMappingDebugMarkers ? 1 : 0, value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.SetShowMappingDebugMarkers(value == 1);
                UpdateCanvasStatusText();
            }
        });
        CreateDropdown(parent, "Paint Hit Marker History", new[] { "Show Last Hit Only", "Show Last 10 Hits" }, paintingSurface != null ? (int)paintingSurface.debugMarkerHistoryMode : 0, value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.debugMarkerHistoryMode = (PaintingSurface.DebugMarkerHistoryMode)value;
                UpdateCanvasStatusText();
            }
        });
        CreateDropdown(parent, "Invert Paint Decal Normal", new[] { "Off", "On" }, paintingSurface != null && paintingSurface.invertPaintNormal ? 1 : 0, value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.SetInvertPaintNormal(value == 1);
                UpdateCanvasStatusText();
            }
        });
        CreateDropdown(parent, "Invert Collision Normal", new[] { "Off", "On" }, paintingSurface != null && paintingSurface.invertBoardNormalForCollision ? 1 : 0, value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.SetInvertBoardNormalForCollision(value == 1);
                UpdateCanvasStatusText();
            }
        });
        CreateDropdown(parent, "Paint Decals Always Visible Debug", new[] { "Off", "On" }, paintingSurface != null && paintingSurface.paintDecalsAlwaysVisibleDebug ? 1 : 0, value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.SetPaintDecalsAlwaysVisibleDebug(value == 1);
                UpdateCanvasStatusText();
            }
        });
        CreateDropdown(parent, "World Paint Fallback Geometry", new[] { "Off", "On" }, paintingSurface != null && paintingSurface.worldPaintFallbackGeometry ? 1 : 0, value =>
        {
            if (paintingSurface != null)
            {
                paintingSurface.SetWorldPaintFallbackGeometry(value == 1);
                UpdateCanvasStatusText();
            }
        });
        CreateButton(parent, "Test UV Mapping", DrawUvMappingTest);
        CreateButton(parent, "Test World Hit Mapping", DrawWorldHitMappingTest);
        CreateButton(parent, "Test Particle Board Collision", TestParticleBoardCollision);
        CreateButton(parent, "Test Visible World Paint", DrawVisibleWorldPaintTest);
        CreateButton(parent, "Paint Under Bucket Test", PaintUnderBucketTest);
        RectTransform worldTestRow = CreateButtonRow(parent);
        CreateButton(worldTestRow, "Test World Dots", () => DrawPaintModeTest(PaintingSurface.TrailRenderMode.Dots));
        CreateButton(worldTestRow, "Test World Trails", () => DrawPaintModeTest(PaintingSurface.TrailRenderMode.Trails));
        CreateButton(worldTestRow, "Test World Ribbon", () => DrawPaintModeTest(PaintingSurface.TrailRenderMode.Ribbon));
        CreateButton(parent, "Clear World Paint", ClearWorldPaint);
        CreateButton(parent, "Auto Try 8 Mapping Modes", AutoTryEightMappingModes);
        canvasStatusText = CreateText("CanvasStatus", parent, "", 19, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        SetPreferredHeight(canvasStatusText.rectTransform, 1080f);
        UpdateCanvasStatusText();
    }

    private void CreateResultsTab(Transform parent)
    {
        RectTransform buttonRow = CreateButtonRow(parent);
        CreateButton(buttonRow, "Save Image", SaveImageFromUI);
        CreateButton(buttonRow, "Save Experiment", SaveExperimentFromUI);
        CreateButton(buttonRow, "Compare Last Two", CompareLastTwoFromUI);
        CreateButton(buttonRow, "Generate Report", GenerateReportFromUI);

        RectTransform scroll = CreateResultScrollArea(parent, 350f);
        lastImagePathText = CreateText("LastImagePath", scroll, "Last Saved Image:\n-", 19, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        lastReportPathText = CreateText("LastReportPath", scroll, "Last Saved Report:\n-", 19, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        comparisonText = CreateText("ComparisonResult", scroll, "Comparison:\n-", 19, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        historyText = CreateText("History", scroll, "Session History:\n-", 19, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        SetPreferredHeight(lastImagePathText.rectTransform, 74f);
        SetPreferredHeight(lastReportPathText.rectTransform, 74f);
        SetPreferredHeight(comparisonText.rectTransform, 170f);
        SetPreferredHeight(historyText.rectTransform, 138f);
    }

    private void CreatePerformanceTab(Transform parent)
    {
        RectTransform presetRow = CreateButtonRow(parent);
        CreateButton(presetRow, "Low 8k", () => ApplyParticlePresetFromUI(Simulation3D.ParticleCountPreset.Low));
        CreateButton(presetRow, "Medium 50k", () => ApplyParticlePresetFromUI(Simulation3D.ParticleCountPreset.Medium));
        CreateButton(presetRow, "High 200k", () => ApplyParticlePresetFromUI(Simulation3D.ParticleCountPreset.High));
        CreateButton(presetRow, "Ultra 1M", () => ApplyParticlePresetFromUI(Simulation3D.ParticleCountPreset.Ultra));

        int legacyModeIndex = fluidSimulation != null ? (int)fluidSimulation.legacyParticleMode : 0;
        CreateDropdown(parent, "Legacy GPU Particles", new[] { "Hidden", "Preview", "Stress Test" }, legacyModeIndex, value =>
        {
            showLegacyFluidParticles = value != 0;
            if (fluidSimulation != null)
            {
                fluidSimulation.SetLegacyParticleMode((Simulation3D.LegacyParticleMode)value);
                SyncLegacyPaintPreview();
            }
        });

        TMP_Text perfText = CreateText("PerformanceDebug", parent, "", 20, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        SetPreferredHeight(perfText.rectTransform, 132f);
        particleWarningText = CreateText("ParticleWarning", parent, "", 20, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        particleWarningText.color = new Color(1f, 0.78f, 0.25f, 1f);
        SetPreferredHeight(particleWarningText.rectTransform, 108f);

        RectTransform scaleRow = CreateButtonRow(parent);
        CreateButton(scaleRow, "UI Small", () => SetUiScale(0.9f));
        CreateButton(scaleRow, "UI Medium", () => SetUiScale(1f));
        CreateButton(scaleRow, "UI Large", () => SetUiScale(1.1f));

        if (fluidSimulation != null)
        {
            perfText.text =
                "Current Preset: " + fluidSimulation.particlePreset +
                "\nGPU Particles: " + fluidSimulation.currentParticleCount.ToString("N0") +
                "\nLegacy Preview: " + fluidSimulation.LegacyPreviewState +
                "\nPreview Label: GPU Paint Particle Preview" +
                "\nEstimated GPU Buffer Memory: " + fluidSimulation.estimatedGpuBufferMegabytes.ToString("0.0") + " MB" +
                "\nFPS: " + currentFps.ToString("0") +
                "\nPress H to hide/show the UI.";
            if (fluidSimulation.legacyParticleMode == Simulation3D.LegacyParticleMode.StressTest)
            {
                perfText.text += "\nStress Test is performance visualization only; it does not affect painting metrics or reports.";
            }
        }
    }

    private void CreateLeftControlPanel(RectTransform root)
    {
        RectTransform panel = CreatePanel("Left Control Panel", root, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(12f, 92f), new Vector2(430f, -102f));
        ScrollRect scrollRect = panel.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        RectTransform viewport = CreatePanel("Viewport", panel, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -8f));
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        scrollRect.viewport = viewport;

        RectTransform content = CreateUIObject("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        VerticalLayoutGroup contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(12, 12, 12, 12);
        contentLayout.spacing = 10;
        contentLayout.childControlHeight = true;
        contentLayout.childControlWidth = true;
        contentLayout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = content;

        CreateSection(content, "Simulation Control");
        CreateButton(content, "Start", StartSimulation);
        CreateButton(content, "Pause / Resume", TogglePauseResume);
        CreateButton(content, "Reset All", ResetSimulation);

        CreateSection(content, "Pendulum and Rope");
        ropeLengthSlider = CreateSliderRow(content, "Rope Length", 0.5f, 8f, pendulumController != null ? pendulumController.ropeLength : 4f, "0.00 m", ChangeRopeLength, out ropeLengthValueText);
        initialAngleSlider = CreateSliderRow(content, "Start Angle", 0f, 80f, pendulumController != null ? pendulumController.theta * Mathf.Rad2Deg : 30f, "0 deg", ChangeInitialAngle, out initialAngleValueText);
        sidePushSlider = CreateSliderRow(content, "Initial Velocity", -2f, 2f, pendulumController != null ? pendulumController.phiDot : 0f, "0.00", ChangeSidePush, out sidePushValueText);
        CreateMouseGrabControls(content);
        dampingSlider = CreateSliderRow(content, "Damping", 0f, 0.25f, pendulumController != null ? pendulumController.damping : 0.05f, "0.000", ChangeDamping, out dampingValueText);
        CreateSliderRow(content, "Rope Flexibility", 0f, 1f, ropeFlexibility, "0.00", value => { ropeFlexibility = value; ApplyExtendedSettings(false); }, out _);

        CreateSection(content, "Environment");
        gravitySlider = CreateSliderRow(content, "Gravity", 0f, 20f, pendulumController != null ? pendulumController.gravity : 9.81f, "0.00 m/s2", ChangeGravity, out gravityValueText);
        airResistanceSlider = CreateSliderRow(content, "Air Resistance", 0f, 2f, pendulumController != null ? pendulumController.airResistanceCoefficient : 0f, "0.00", ChangeAirResistance, out airResistanceValueText);
        pivotFrictionSlider = CreateSliderRow(content, "Pivot Friction", 0f, 2f, pendulumController != null ? pendulumController.pivotFrictionCoefficient : 0f, "0.00", ChangePivotFriction, out pivotFrictionValueText);
        CreateSliderRow(content, "Humidity", 0f, 1f, humidity, "0.00", value => { humidity = value; ApplyExtendedSettings(false); }, out _);

        CreateSection(content, "Paint");
        CreateSliderRow(content, "Paint Amount", 0f, 100f, initialPaintAmount, "0.00 kg", ChangePaintAmount, out _);
        holeDiameterSlider = CreateSliderRow(content, "Hole Diameter", 0.005f, 0.08f, holeDiameter, "0.000 m", ChangeHoleDiameter, out holeDiameterValueText);
        viscositySlider = CreateSliderRow(content, "Viscosity", 0.01f, 3f, viscosity, "0.00", ChangeViscosity, out viscosityValueText);
        exitSpeedSlider = CreateSliderRow(content, "Exit Speed", 0f, 8f, exitSpeed, "0.00 m/s", ChangeExitSpeed, out exitSpeedValueText);
        emissionRateSlider = CreateSliderRow(content, "Flow Multiplier", 1f, advancedPaintMode ? 120f : 40f, emissionRate, "0", ChangeEmissionRate, out emissionRateValueText);
        CreateColorButtons(content);

        CreateSection(content, "Canvas / Painting Surface");
        CreateSliderRow(content, "Canvas Width", 1f, 20f, canvasWidth, "0.0", value => { canvasWidth = value; ApplyExtendedSettings(false); }, out _);
        CreateSliderRow(content, "Canvas Height", 1f, 20f, canvasHeight, "0.0", value => { canvasHeight = value; ApplyExtendedSettings(false); }, out _);
        surfaceTypeDropdown = CreateDropdown(content, "Surface Type", surfaceTypes, surfaceTypeIndex, value => { surfaceTypeIndex = value; ApplyExtendedSettings(false); });
        canvasOrientationDropdown = CreateDropdown(content, "Canvas Orientation", new[] { "Horizontal", "Tilted" }, canvasTilted ? 1 : 0, value => { canvasTilted = value == 1; ApplyExtendedSettings(false); });
        canvasTiltSlider = CreateSliderRow(content, "Tilt Angle", 0f, 60f, canvasTiltDegrees, "0 deg", value => { canvasTiltDegrees = value; ApplyExtendedSettings(false); }, out _);
        CreateButton(content, "Clear Canvas", ClearCanvas);
    }

    private void CreateRightResultsPanel(RectTransform root)
    {
        RectTransform panel = CreatePanel("Right Results Panel", root, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-430f, 92f), new Vector2(-12f, -102f));
        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 14, 14);
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateSection(panel, "Results and Reports");
        CreateButton(panel, "Save Image", SaveImageFromUI);
        CreateButton(panel, "Save Experiment", SaveExperimentFromUI);
        CreateButton(panel, "Compare Last Two", CompareLastTwoFromUI);
        CreateButton(panel, "Generate Report", GenerateReportFromUI);
        lastImagePathText = CreateText("LastImagePath", panel, "Last Saved Image: -", 17, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        lastReportPathText = CreateText("LastReportPath", panel, "Last Saved Report: -", 17, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        comparisonText = CreateText("ComparisonResult", panel, "Comparison: -", 17, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        historyText = CreateText("History", panel, "Session History: -", 17, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        SetPreferredHeight(comparisonText.rectTransform, 190f);
        SetPreferredHeight(historyText.rectTransform, 180f);
    }

    private void CreateBottomParticleBar(RectTransform root)
    {
        RectTransform panel = CreatePanel("Bottom Particle Preset Bar", root, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(440f, 12f), new Vector2(-440f, 82f));
        HorizontalLayoutGroup layout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 10);
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;

        CreateButton(panel, "Low 8k", () => ApplyParticlePresetFromUI(Simulation3D.ParticleCountPreset.Low));
        CreateButton(panel, "Medium 50k", () => ApplyParticlePresetFromUI(Simulation3D.ParticleCountPreset.Medium));
        CreateButton(panel, "High 200k", () => ApplyParticlePresetFromUI(Simulation3D.ParticleCountPreset.High));
        CreateButton(panel, "Ultra 1M", () => ApplyParticlePresetFromUI(Simulation3D.ParticleCountPreset.Ultra));
        particleWarningText = CreateText("ParticleWarning", panel, "", 17, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        particleWarningText.color = new Color(1f, 0.78f, 0.25f, 1f);
    }

    private void DisableLegacyUIVisuals()
    {
        GameObject legacyCanvas = GameObject.Find("UI_Canvas");
        if (legacyCanvas == null)
        {
            return;
        }

        for (int i = 0; i < legacyCanvas.transform.childCount; i++)
        {
            legacyCanvas.transform.GetChild(i).gameObject.SetActive(false);
        }

        Canvas legacyCanvasComponent = legacyCanvas.GetComponent<Canvas>();
        if (legacyCanvasComponent != null)
        {
            legacyCanvasComponent.enabled = false;
        }

        GraphicRaycaster raycaster = legacyCanvas.GetComponent<GraphicRaycaster>();
        if (raycaster != null)
        {
            raycaster.enabled = false;
        }
    }

    private RectTransform CreateUIObject(string objectName, Transform parent)
    {
        GameObject uiObject = new GameObject(objectName, typeof(RectTransform));
        RectTransform rectTransform = uiObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        return rectTransform;
    }

    private RectTransform CreatePanel(string objectName, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        RectTransform rectTransform = CreateUIObject(objectName, parent);
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;

        Image image = rectTransform.gameObject.AddComponent<Image>();
        image.color = new Color(0.04f, 0.05f, 0.07f, 0.82f);
        return rectTransform;
    }

    private TMP_Text CreateText(string objectName, Transform parent, string text, int fontSize, FontStyles style, TextAlignmentOptions alignment)
    {
        RectTransform rectTransform = CreateUIObject(objectName, parent);
        TMP_Text label = rectTransform.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = new Color(0.92f, 0.95f, 1f, 1f);
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    private void CreateSection(Transform parent, string title)
    {
        TMP_Text text = CreateText(title.Replace(" ", "") + "Title", parent, title, 25, FontStyles.Bold, TextAlignmentOptions.Left);
        text.color = new Color(0.68f, 0.85f, 1f, 1f);
        SetPreferredHeight(text.rectTransform, 36f);
    }

    private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rectTransform = CreateUIObject(label.Replace(" ", "") + "Button", parent);
        Image image = rectTransform.gameObject.AddComponent<Image>();
        image.color = new Color(0.16f, 0.25f, 0.36f, 0.96f);
        Button button = rectTransform.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        SetPreferredHeight(rectTransform, 40f);

        TMP_Text buttonText = CreateText("Text", rectTransform, label, 19, FontStyles.Bold, TextAlignmentOptions.Center);
        buttonText.raycastTarget = false;
        buttonText.rectTransform.anchorMin = Vector2.zero;
        buttonText.rectTransform.anchorMax = Vector2.one;
        buttonText.rectTransform.offsetMin = Vector2.zero;
        buttonText.rectTransform.offsetMax = Vector2.zero;
        return button;
    }

    private Slider CreateSliderRow(Transform parent, string label, float min, float max, float value, string format, UnityEngine.Events.UnityAction<float> onChanged, out TMP_Text valueText)
    {
        RectTransform row = CreateUIObject(label.Replace(" ", "") + "Row", parent);
        VerticalLayoutGroup rowLayout = row.gameObject.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 6;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;
        SetPreferredHeight(row, 66f);

        RectTransform labelRow = CreateUIObject("LabelRow", row);
        HorizontalLayoutGroup labelLayout = labelRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        labelLayout.childControlWidth = true;
        labelLayout.childForceExpandWidth = true;
        SetPreferredHeight(labelRow, 26f);

        CreateText("Label", labelRow, label, 19, FontStyles.Normal, TextAlignmentOptions.Left);
        valueText = CreateText("Value", labelRow, FormatValue(value, format), 20, FontStyles.Bold, TextAlignmentOptions.Right);
        TMP_Text capturedValueText = valueText;
        dynamicValueTexts.Add(capturedValueText);

        Slider slider = CreateSlider(row, min, max, value);
        slider.onValueChanged.AddListener(newValue =>
        {
            capturedValueText.text = FormatValue(newValue, format);
            onChanged?.Invoke(newValue);
        });

        return slider;
    }

    private Slider CreateSlider(Transform parent, float min, float max, float value)
    {
        RectTransform sliderTransform = CreateUIObject("Slider", parent);
        SetPreferredHeight(sliderTransform, 30f);
        Slider slider = sliderTransform.gameObject.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = Mathf.Clamp(value, min, max);

        RectTransform background = CreateUIObject("Background", sliderTransform);
        background.anchorMin = new Vector2(0f, 0.35f);
        background.anchorMax = new Vector2(1f, 0.65f);
        background.offsetMin = Vector2.zero;
        background.offsetMax = Vector2.zero;
        Image backgroundImage = background.gameObject.AddComponent<Image>();
        backgroundImage.color = new Color(0.12f, 0.15f, 0.19f, 1f);

        RectTransform fillArea = CreateUIObject("Fill Area", sliderTransform);
        fillArea.anchorMin = Vector2.zero;
        fillArea.anchorMax = Vector2.one;
        fillArea.offsetMin = new Vector2(8f, 0f);
        fillArea.offsetMax = new Vector2(-8f, 0f);

        RectTransform fill = CreateUIObject("Fill", fillArea);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = new Color(0.2f, 0.55f, 0.95f, 1f);

        RectTransform handleArea = CreateUIObject("Handle Slide Area", sliderTransform);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(8f, 0f);
        handleArea.offsetMax = new Vector2(-8f, 0f);

        RectTransform handle = CreateUIObject("Handle", handleArea);
        handle.sizeDelta = new Vector2(22f, 22f);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.9f, 0.95f, 1f, 1f);

        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private TMP_Dropdown CreateDropdown(Transform parent, string label, string[] options, int value, UnityEngine.Events.UnityAction<int> onChanged)
    {
        RectTransform row = CreateUIObject(label.Replace(" ", "") + "DropdownRow", parent);
        VerticalLayoutGroup layout = row.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        SetPreferredHeight(row, 72f);

        CreateText("Label", row, label, 19, FontStyles.Normal, TextAlignmentOptions.Left);

        RectTransform dropdownTransform = CreateUIObject("Dropdown", row);
        Image image = dropdownTransform.gameObject.AddComponent<Image>();
        image.color = new Color(0.12f, 0.16f, 0.22f, 0.96f);
        TMP_Dropdown dropdown = dropdownTransform.gameObject.AddComponent<TMP_Dropdown>();
        SetPreferredHeight(dropdownTransform, 38f);

        TMP_Text caption = CreateText("Label", dropdownTransform, "", 19, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        caption.rectTransform.anchorMin = Vector2.zero;
        caption.rectTransform.anchorMax = Vector2.one;
        caption.rectTransform.offsetMin = new Vector2(10f, 0f);
        caption.rectTransform.offsetMax = new Vector2(-28f, 0f);
        dropdown.captionText = caption;
        dropdown.options.Clear();
        foreach (string option in options)
        {
            dropdown.options.Add(new TMP_Dropdown.OptionData(option));
        }
        CreateDropdownTemplate(dropdown, dropdownTransform);
        dropdown.value = Mathf.Clamp(value, 0, Mathf.Max(0, options.Length - 1));
        dropdown.onValueChanged.AddListener(onChanged);
        dropdown.RefreshShownValue();
        return dropdown;
    }

    private void CreateDropdownTemplate(TMP_Dropdown dropdown, RectTransform dropdownTransform)
    {
        RectTransform template = CreateUIObject("Template", dropdownTransform);
        template.gameObject.SetActive(false);
        template.anchorMin = new Vector2(0f, 0f);
        template.anchorMax = new Vector2(1f, 0f);
        template.pivot = new Vector2(0.5f, 1f);
        template.offsetMin = new Vector2(0f, -132f);
        template.offsetMax = new Vector2(0f, 0f);
        Image templateImage = template.gameObject.AddComponent<Image>();
        templateImage.color = new Color(0.08f, 0.1f, 0.14f, 0.98f);
        ScrollRect scrollRect = template.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        RectTransform viewport = CreatePanel("Viewport", template, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        scrollRect.viewport = viewport;

        RectTransform content = CreateUIObject("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = content;

        RectTransform item = CreateUIObject("Item", content);
        Toggle toggle = item.gameObject.AddComponent<Toggle>();
        Image itemBackground = item.gameObject.AddComponent<Image>();
        itemBackground.color = new Color(0.12f, 0.16f, 0.22f, 1f);
        SetPreferredHeight(item, 28f);

        TMP_Text itemText = CreateText("Item Label", item, "Option", 19, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        itemText.rectTransform.anchorMin = Vector2.zero;
        itemText.rectTransform.anchorMax = Vector2.one;
        itemText.rectTransform.offsetMin = new Vector2(10f, 0f);
        itemText.rectTransform.offsetMax = new Vector2(-10f, 0f);

        toggle.targetGraphic = itemBackground;
        dropdown.template = template;
        dropdown.itemText = itemText;
    }

    private void CreateColorButtons(Transform parent)
    {
        TMP_Text title = CreateText("PaintColorTitle", parent, "Paint Color", 18, FontStyles.Normal, TextAlignmentOptions.Left);
        SetPreferredHeight(title.rectTransform, 26f);
        RectTransform row = CreateUIObject("PaintColorButtons", parent);
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        SetPreferredHeight(row, 42f);

        CreateColorButton(row, "Blue", new Color(0.1f, 0.25f, 1f, 1f));
        CreateColorButton(row, "Red", new Color(0.9f, 0.08f, 0.04f, 1f));
        CreateColorButton(row, "Yellow", new Color(1f, 0.82f, 0.08f, 1f));
        CreateColorButton(row, "Green", new Color(0.05f, 0.7f, 0.25f, 1f));
        CreateColorButton(row, "White", Color.white);
    }

    private void CreateColorButton(Transform parent, string label, Color color)
    {
        Button button = CreateButton(parent, label, () =>
        {
            paintColor = color;
            if (paintReservoir != null)
            {
                paintReservoir.SetSelectedColor(color);
            }
            ApplyExtendedSettings(false);
            SyncLegacyPaintPreview();
        });
        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = color;
        }
    }

    private void AddCurrentColorToBucket()
    {
        if (paintReservoir == null)
        {
            userMessage = "Paint reservoir is missing.";
            return;
        }

        float amount = Mathf.Max(0.001f, paintEmitter != null ? paintEmitter.remainingPaintAmount : initialPaintAmount);
        paintReservoir.AddColorToBucket(paintColor, amount);
        SyncLegacyPaintPreview();
        userMessage = "Added color #" + ColorUtility.ToHtmlStringRGB(paintColor) + " to bucket.";
    }

    private void ClearBucketColors()
    {
        if (paintReservoir == null)
        {
            userMessage = "Paint reservoir is missing.";
            return;
        }

        paintReservoir.ClearColors();
        SyncLegacyPaintPreview();
        userMessage = "Bucket color components cleared.";
    }

    private void DrawPaintModeTest(PaintingSurface.TrailRenderMode mode)
    {
        if (paintingSurface == null)
        {
            userMessage = "PaintingSurface is missing.";
            return;
        }

        Color testColor = paintReservoir != null ? paintReservoir.VisiblePaintColor : paintColor;
        if (mode == PaintingSurface.TrailRenderMode.Dots)
        {
            paintingSurface.DrawTestDots(testColor);
            userMessage = "Direct drawing test: Dots.";
        }
        else if (mode == PaintingSurface.TrailRenderMode.Trails)
        {
            paintingSurface.DrawTestTrails(testColor);
            userMessage = "Direct drawing test: Trails.";
        }
        else
        {
            paintingSurface.DrawTestRibbon(testColor);
            userMessage = "Direct drawing test: Ribbon.";
        }
    }

    private string FormatValue(float value, string format)
    {
        if (format.Contains(" "))
        {
            int splitIndex = format.IndexOf(' ');
            return value.ToString(format.Substring(0, splitIndex)) + format.Substring(splitIndex);
        }

        return value.ToString(format);
    }

    private string FormatVector3(Vector3 value)
    {
        return value.x.ToString("0.00") + ", " + value.y.ToString("0.00") + ", " + value.z.ToString("0.00");
    }

    private string FormatColor(Color value)
    {
        return value.r.ToString("0.00") + ", " + value.g.ToString("0.00") + ", " + value.b.ToString("0.00") + ", " + value.a.ToString("0.00");
    }

    private void SetPreferredHeight(RectTransform rectTransform, float height)
    {
        LayoutElement layoutElement = rectTransform.gameObject.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = rectTransform.gameObject.AddComponent<LayoutElement>();
        }

        layoutElement.preferredHeight = height;
        layoutElement.minHeight = height;
    }

    private void SetPreferredWidth(RectTransform rectTransform, float width)
    {
        if (rectTransform == null)
        {
            return;
        }

        LayoutElement layoutElement = rectTransform.gameObject.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = rectTransform.gameObject.AddComponent<LayoutElement>();
        }

        layoutElement.preferredWidth = width;
        layoutElement.minWidth = width;
    }

    private void TogglePauseResume()
    {
        if (isPaused)
        {
            StartSimulation();
        }
        else
        {
            PauseSimulation();
        }
    }

    private void ClearCanvas()
    {
        if (paintingSurface == null)
        {
            userMessage = "Painting surface is missing.";
            return;
        }

        paintingSurface.ClearPainting();
        userMessage = "Canvas cleared.";
        UpdateResultTexts();
    }

    private void ClearWorldPaint()
    {
        if (paintingSurface == null)
        {
            userMessage = "Painting surface is missing.";
            return;
        }

        paintingSurface.ClearWorldPaint();
        userMessage = "World paint cleared.";
        UpdateCanvasStatusText();
        UpdateResultTexts();
    }

    private void DrawUvMappingTest()
    {
        if (paintingSurface == null)
        {
            userMessage = "Painting surface is missing.";
            return;
        }

        paintingSurface.DrawMappingTestPattern();
        userMessage = "UV mapping test pattern drawn.";
        UpdateCanvasStatusText();
    }

    private void DrawWorldHitMappingTest()
    {
        if (paintingSurface == null)
        {
            userMessage = "Painting surface is missing.";
            return;
        }

        paintingSurface.DrawWorldHitMappingTest();
        userMessage = "World hit mapping test pattern drawn.";
        UpdateCanvasStatusText();
    }

    private void TestParticleBoardCollision()
    {
        if (paintingSurface == null)
        {
            userMessage = "Painting surface is missing.";
            return;
        }

        bool deposited = paintingSurface.TestParticleBoardCollision();
        userMessage = deposited
            ? "Test particle collision deposited at the board hit point."
            : "Test particle collision missed the board.";
        UpdateCanvasStatusText();
        UpdateResultTexts();
    }

    private void DrawVisibleWorldPaintTest()
    {
        if (paintingSurface == null)
        {
            userMessage = "Painting surface is missing.";
            return;
        }

        paintingSurface.DrawVisibleWorldPaintTest();
        userMessage = "Visible world paint test drawn.";
        UpdateCanvasStatusText();
    }

    private void PaintUnderBucketTest()
    {
        if (paintingSurface == null)
        {
            userMessage = "Painting surface is missing.";
            return;
        }

        Transform bucketTransform = pendulumController != null
            ? pendulumController.transform
            : paintExitPoint != null
                ? paintExitPoint
                : paintEmitter != null
                    ? paintEmitter.transform
                    : null;

        if (bucketTransform == null)
        {
            userMessage = "Bucket transform is missing.";
            return;
        }

        bool deposited = paintingSurface.PaintUnderBucketTest(bucketTransform.position);
        userMessage = deposited
            ? "Paint under bucket test drawn."
            : "Bucket projection is outside the board.";
        UpdateCanvasStatusText();
    }

    private void AutoTryEightMappingModes()
    {
        if (paintingSurface == null)
        {
            userMessage = "Painting surface is missing.";
            return;
        }

        paintingSurface.AutoTryNextMappingMode();
        userMessage = "Mapping mode: " + paintingSurface.mappingPlane +
            ", invert right " + (paintingSurface.invertRightAxis ? "On" : "Off") +
            ", invert up " + (paintingSurface.invertUpAxis ? "On" : "Off") +
            ", swap " + (paintingSurface.swapAxes ? "On" : "Off") + ".";
        UpdateCanvasStatusText();
    }

    private void SaveImageFromUI()
    {
        if (experimentManager == null)
        {
            userMessage = "Experiment manager is missing.";
            UpdateResultTexts();
            return;
        }

        string path = experimentManager.SaveImage();
        userMessage = string.IsNullOrEmpty(path) ? "Image save failed." : "Saved image.";
        if (lastImagePathText != null)
        {
            lastImagePathText.text = "Last Saved Image:\n" + ShortenPath(path);
        }
    }

    private void SaveExperimentFromUI()
    {
        if (experimentManager == null)
        {
            userMessage = "Experiment manager is missing.";
            UpdateResultTexts();
            return;
        }

        SyncExperimentRequirementFields();
        PaintExperimentManager.ExperimentRecord record = experimentManager.SaveExperiment(simulationTime);
        userMessage = "Saved " + record.experimentName;
        if (lastImagePathText != null)
        {
            lastImagePathText.text = "Last Saved Image:\n" + ShortenPath(record.savedImagePath);
        }
        UpdateResultTexts();
    }

    private void CompareLastTwoFromUI()
    {
        if (experimentManager == null)
        {
            userMessage = "Experiment manager is missing.";
            UpdateResultTexts();
            return;
        }

        string comparison = experimentManager.CompareLastTwoExperiments();
        userMessage = "Comparison updated.";
        if (comparisonText != null)
        {
            comparisonText.text = comparison;
        }
    }

    private void GenerateReportFromUI()
    {
        if (experimentManager == null)
        {
            userMessage = "Experiment manager is missing.";
            UpdateResultTexts();
            return;
        }

        SyncExperimentRequirementFields();
        string path = experimentManager.GenerateReport(simulationTime);
        userMessage = string.IsNullOrEmpty(path) ? "Report generation failed." : "Report generated.";
        if (lastReportPathText != null)
        {
            lastReportPathText.text = "Last Saved Report:\n" + ShortenPath(path);
        }
    }

    private void ApplyParticlePresetFromUI(Simulation3D.ParticleCountPreset preset)
    {
        if (fluidSimulation == null)
        {
            userMessage = "Fluid simulation is missing.";
            UpdateResultTexts();
            return;
        }

        bool wasPaused = isPaused;
        SetFluidPaused(true);
        SyncLegacyPaintPreview();
        fluidSimulation.ApplyParticlePreset(preset);
        fluidSimulation.ResetFluid();
        SyncLegacyPaintPreview();
        SetFluidPaused(wasPaused);
        userMessage = "Particle preset: " + fluidSimulation.particlePreset;
        if (activeTab == DashboardTab.Performance)
        {
            RebuildActiveTab();
        }
        UpdateResultTexts();
    }

    private void EnsureFluidPreview()
    {
        if (fluidPreview != null)
        {
            SyncIndependentFluidPreviewColor();
            return;
        }

        GameObject previewObject = GameObject.Find("IndependentFluidPreview");
        if (previewObject == null)
        {
            previewObject = new GameObject("IndependentFluidPreview");
        }

        fluidPreview = previewObject.GetComponent<IndependentFluidVisualizer>();
        if (fluidPreview == null)
        {
            fluidPreview = previewObject.AddComponent<IndependentFluidVisualizer>();
        }
        fluidPreview.pendulumController = pendulumController;
        fluidPreview.paintReservoir = paintReservoir;
        fluidPreview.paintEmitter = paintEmitter;
        SyncIndependentFluidPreviewColor();
    }

    private void ApplyFluidPreviewPreset(IndependentFluidVisualizer.ParticlePreset preset)
    {
        EnsureFluidPreview();
        if (fluidPreview == null)
        {
            userMessage = "Fluid preview is missing.";
            return;
        }

        fluidPreview.ApplyPreset(preset);
        userMessage = "Fluid preview preset: " + preset + " (" + fluidPreview.previewParticleCount.ToString("N0") + " particles).";
        if (activeTab == DashboardTab.FluidPreview)
        {
            RebuildActiveTab();
        }
    }

    private void ForceRebuildFluidPreviewParticles()
    {
        EnsureFluidPreview();
        if (fluidPreview == null)
        {
            userMessage = "Fluid preview is missing.";
            return;
        }

        fluidPreview.ForceRebuildParticles();
        userMessage = "Fluid preview particles rebuilt (" + fluidPreview.VisibleParticleCount.ToString("N0") + " visible).";
        UpdateUnifiedCanvasTexts();
    }

    private void ForceRebuildFluidPreview()
    {
        EnsureFluidPreview();
        if (fluidPreview == null)
        {
            userMessage = "Fluid preview is missing.";
            return;
        }

        fluidPreview.ForceRebuildPreview();
        userMessage = "Fluid preview rebuilt with current box and visual settings.";
        UpdateUnifiedCanvasTexts();
    }

    private void ResetFluidPreviewBoxTransform()
    {
        EnsureFluidPreview();
        if (fluidPreview == null)
        {
            userMessage = "Fluid preview is missing.";
            return;
        }

        fluidPreview.ResetBoxTransform();
        userMessage = "Fluid preview box transform reset.";
        if (activeTab == DashboardTab.FluidPreview)
        {
            RebuildActiveTab();
        }
        UpdateUnifiedCanvasTexts();
    }

    private void ResetFluidPreviewVisuals()
    {
        EnsureFluidPreview();
        if (fluidPreview == null)
        {
            userMessage = "Fluid preview is missing.";
            return;
        }

        fluidPreview.ResetPreviewVisuals();
        userMessage = "Fluid preview visuals reset.";
        if (activeTab == DashboardTab.FluidPreview)
        {
            RebuildActiveTab();
        }
        UpdateUnifiedCanvasTexts();
    }

    private void ResetFluidPreviewLiquidCalibration()
    {
        EnsureFluidPreview();
        if (fluidPreview == null)
        {
            userMessage = "Fluid preview is missing.";
            return;
        }

        fluidPreview.ResetLiquidCalibration();
        userMessage = "Fluid preview liquid calibration reset.";
        if (activeTab == DashboardTab.FluidPreview)
        {
            RebuildActiveTab();
        }
        UpdateUnifiedCanvasTexts();
    }

    private void ResetFluidPreviewMotion()
    {
        EnsureFluidPreview();
        if (fluidPreview == null)
        {
            userMessage = "Fluid preview is missing.";
            return;
        }

        fluidPreview.ResetPreviewMotion();
        userMessage = "Fluid preview motion reset.";
        UpdateUnifiedCanvasTexts();
    }

    private void SyncExperimentRequirementFields()
    {
        if (experimentManager == null)
        {
            return;
        }

        experimentManager.bucketRadius = bucketRadius;
        experimentManager.pivotPosition = pivotPosition;
        experimentManager.swingDirectionDegrees = swingDirectionDegrees;
        experimentManager.targetSwingCount = unlimitedSwings ? 0 : targetSwingCount;
        experimentManager.completedSwingCount = swingCount;
        experimentManager.swingTargetCompleted = targetSwingCompleted;
    }

    private void UpdateUnifiedCanvasTexts()
    {
        if (topStatusText != null)
        {
            float capacity = paintReservoir != null ? paintReservoir.capacity : paintCapacity;
            float remainingPercent = paintEmitter != null
                ? Mathf.Clamp01(paintEmitter.remainingPaintAmount / Mathf.Max(0.001f, capacity)) * 100f
                : 0f;
            int particleCount = fluidSimulation != null ? fluidSimulation.currentParticleCount : 0;
            string preset = fluidSimulation != null ? fluidSimulation.particlePreset.ToString() : "-";
            float bufferMb = fluidSimulation != null ? fluidSimulation.estimatedGpuBufferMegabytes : 0f;
            float flow = paintEmitter != null ? paintEmitter.CurrentFlowRateKgPerSecond : 0f;
            int marks = paintingSurface != null ? paintingSurface.MarkCount : 0;
            float area = paintingSurface != null ? paintingSurface.EstimatedPaintedArea01 * 100f : 0f;
            string bucketState = paintEmitter != null ? paintEmitter.BucketState : "-";
            string legacyState = fluidSimulation != null ? fluidSimulation.LegacyPreviewState : "-";

            topStatusText.text =
                "Status: " + statusText +
                "    Time: " + simulationTime.ToString("0.0") + " s" +
                "    FPS: " + currentFps.ToString("0") +
                "    GPU Particles: " + particleCount.ToString("N0") +
                "    Preset: " + preset +
                "    Buffers: " + bufferMb.ToString("0.0") + " MB" +
                "    Paint: " + remainingPercent.ToString("0") + "%" +
                "    Swings: " + swingCount + "/" + (unlimitedSwings ? "Unlimited" : targetSwingCount.ToString()) +
                "    Bucket: " + bucketState +
                "    Flow: " + flow.ToString("0.0000") + " kg/s" +
                "    Legacy: " + legacyState +
                "    Marks: " + marks +
                "    Area: " + area.ToString("0.0") + "%";
        }

        if (referenceWarningText != null)
        {
            referenceWarningText.text = BuildReferenceWarningText();
        }

        if (mouseCameraDebugText != null)
        {
            mouseCameraDebugText.text = BuildMouseCameraDebugText();
        }

        if (mouseGrabDebugText != null)
        {
            mouseGrabDebugText.text = BuildMouseGrabDebugText();
        }

        SyncMotionSlidersAfterMouseGrab();

        if (ropeDebugText != null)
        {
            ropeDebugText.text = BuildRopeDebugText();
        }

        if (paintStatusText != null)
        {
            paintStatusText.text = BuildPaintStatusText();
        }

        if (fluidPreviewStatsText != null)
        {
            fluidPreviewStatsText.text = fluidPreview != null ? fluidPreview.StatsText : "Fluid preview missing.";
        }

        UpdateCanvasStatusText();

        if (particleWarningText != null)
        {
            particleWarningText.text = fluidSimulation != null ? fluidSimulation.particlePresetWarning : "Fluid simulation missing.";
        }

        UpdateResultTexts();
    }

    private void UpdateResultTexts()
    {
        if (historyText != null && experimentManager != null)
        {
            historyText.text = experimentManager.BuildHistoryText();
        }

        if (comparisonText != null && string.IsNullOrWhiteSpace(comparisonText.text))
        {
            comparisonText.text = "Comparison: -";
        }
    }

    private string BuildReferenceWarningText()
    {
        string warnings = "";
        if (pendulumController == null) warnings += "Pendulum missing. ";
        if (ropeController == null) warnings += "Rope missing. ";
        if (fluidSimulation == null) warnings += "Fluid missing. ";
        if (paintEmitter == null) warnings += "Emitter missing. ";
        if (paintingSurface == null) warnings += "Painting surface missing. ";
        if (experimentManager == null) warnings += "Experiment manager missing. ";
        return warnings;
    }

    private string BuildMouseCameraDebugText()
    {
        if (mouseCameraController == null)
        {
            return "MouseCameraController active: false";
        }

        return
            "MouseCameraController active: " + mouseCameraController.controllerActive +
            "\nRight mouse pressed: " + mouseCameraController.rightMousePressed +
            "\nPointer over UI: " + mouseCameraController.pointerOverUi +
            "\nCurrent camera distance: " + mouseCameraController.CurrentDistance.ToString("0.00") +
            "\nCurrent target: " + mouseCameraController.currentTargetName;
    }

    private string BuildMouseGrabDebugText()
    {
        if (mouseGrabController == null)
        {
            return "Mouse Grab: Missing";
        }

        return mouseGrabController.DebugSummary;
    }

    private void SyncMotionSlidersAfterMouseGrab()
    {
        if (mouseGrabController == null || pendulumController == null)
        {
            return;
        }

        if (mouseGrabController.currentState == MouseGrabController.GrabState.Dragging ||
            mouseGrabController.currentState == MouseGrabController.GrabState.Released)
        {
            SetSliderValueWithoutNotify(initialAngleSlider, pendulumController.theta * Mathf.Rad2Deg);
            SetSliderValueWithoutNotify(sidePushSlider, pendulumController.phiDot);
        }
    }

    private string BuildRopeDebugText()
    {
        if (ropeController == null)
        {
            return "Rope type: Mass-Spring Rope\nRope component missing.";
        }

        int segments = Mathf.Max(0, ropeController.nodeCount - 1);
        string bucketDebug = pendulumController != null
            ? "\nRope Length: " + pendulumController.ropeLength.ToString("0.000") +
              "\nActual Pivot-To-Attach Distance: " + pendulumController.actualPivotToAttachDistance.ToString("0.000") +
              "\nDistance Error: " + pendulumController.pivotAttachDistanceError.ToString("0.0000") +
              "\nPivot Position: " + FormatVector3(pendulumController.debugPivotPosition) +
              "\nAttach Position: " + FormatVector3(pendulumController.debugAttachPosition) +
              "\nBucket Position: " + FormatVector3(pendulumController.debugBucketPosition) +
              "\nBucket Visual Offset: " + FormatVector3(pendulumController.debugBucketVisualOffset) +
              "\nShow Bucket Debug: " + (pendulumController.showBucketDebug ? "On" : "Off")
            : "\nBucket attachment debug: Pendulum missing";
        return
            "Rope type: " + ropeController.ropeType +
            "\nSegment count: " + segments +
            "\nNode count: " + ropeController.nodeCount +
            "\nSpring stiffness: " + ropeController.stiffness.ToString("0") +
            "\nRope damping: " + ropeController.springDamping.ToString("0") +
            "\nRope flexibility: " + ropeController.bendAmount.ToString("0.00") +
            "\nConstraint iterations: " + ropeController.lengthCorrectionIterations +
            "\nBucket Weight: " + bucketWeight.ToString("0.00") + " kg" +
            "\nBucket Radius: " + bucketRadius.ToString("0.00") + " m" +
            bucketDebug +
            "\nSwing Direction: " + swingDirectionDegrees.ToString("0") + " deg" +
            "\nTarget Swings: " + (unlimitedSwings ? "Unlimited" : targetSwingCount.ToString()) +
            "\nCompleted Swings: " + swingCount +
            "\nSwing Target Completed: " + (targetSwingCompleted ? "Yes" : "No") +
            "\nMass Response Scale: " + (pendulumController != null ? pendulumController.massResponseScale.ToString("0.00") : "-") +
            "\nCurrent average stretch: " + ropeController.currentAverageStretch.ToString("0.0000") + " m" +
            "\nAverage stretch percent: " + ropeController.currentAverageStretchPercent.ToString("0.00") + "%";
    }

    private string BuildPaintStatusText()
    {
        float remaining = paintEmitter != null ? paintEmitter.remainingPaintAmount : initialPaintAmount;
        float initial = paintEmitter != null ? paintEmitter.initialPaintAmount : initialPaintAmount;
        float capacity = paintReservoir != null ? paintReservoir.capacity : paintCapacity;
        float fillPercent = paintReservoir != null ? paintReservoir.FillPercent * 100f : Mathf.Clamp01(remaining / Mathf.Max(0.001f, capacity)) * 100f;
        float flow = paintEmitter != null ? paintEmitter.CurrentFlowRateKgPerSecond : 0f;
        float emittedMassThisFrame = paintEmitter != null ? paintEmitter.LastEmittedMassThisFrame : 0f;
        float reservoirCurrentAmount = paintReservoir != null ? paintReservoir.currentPaintAmount : 0f;
        float particlesPerSecond = paintEmitter != null ? paintEmitter.CurrentParticlesPerSecond : 0f;
        float effectiveRadius = paintingSurface != null
            ? paintingSurface.GetEffectivePaintDepositRadius(holeDiameter, viscosity, Mathf.Max(0f, exitSpeed), paintingSurface.CurrentSurfaceBehavior, paintingSurface.strokeRadius)
            : 0f;
        int activeAirborne = paintEmitter != null ? paintEmitter.ActiveAirborneParticleCount : 0;
        int deposited = paintEmitter != null ? paintEmitter.DepositedParticleCount : 0;
        int recycled = paintEmitter != null ? paintEmitter.RecycledParticleCount : 0;
        int missed = paintEmitter != null ? paintEmitter.MissedBoardParticleCount : 0;
        int collisions = paintEmitter != null ? paintEmitter.TotalCollisionCount : 0;
        int recycledAfterHit = paintEmitter != null ? paintEmitter.RecycledAfterHitCount : 0;
        string bucketState = paintEmitter != null ? paintEmitter.BucketState : "Full";
        if (paintReservoir != null)
        {
            bucketState = paintReservoir.BucketState;
        }
        string legacyState = fluidSimulation != null ? fluidSimulation.LegacyPreviewState : "Hidden";
        string uiMode = trailModeDropdown != null
            ? ((PaintingSurface.TrailRenderMode)Mathf.Clamp(trailModeDropdown.value, 0, 2)).ToString()
            : "No UI";
        string surfaceMode = paintingSurface != null ? paintingSurface.ActiveTrailMode.ToString() : "No Surface";
        string emitterMode = paintEmitter != null ? paintEmitter.ActiveDepositMode : "No Emitter";
        string lastDepositMode = paintingSurface != null ? paintingSurface.LastDepositModeUsed : "None";
        int totalDeposits = paintingSurface != null ? paintingSurface.TotalDepositCount : 0;
        int textureUpdates = paintingSurface != null ? paintingSurface.TextureUpdatedCount : 0;
        int connectedStrokes = paintingSurface != null ? paintingSurface.ConnectedStrokeCount : 0;
        int rejectedConnections = paintingSurface != null ? paintingSurface.RejectedConnectionCount : 0;
        int activeStrokes = paintingSurface != null ? paintingSurface.ActiveStreamCount : 0;
        Vector2 lastHitUv = paintingSurface != null ? paintingSurface.LastHitUv : Vector2.zero;
        Vector2 lastHitPixel = paintingSurface != null ? paintingSurface.LastPixelHit : Vector2.zero;
        bool modeMismatch = paintingSurface != null && (
            (trailModeDropdown != null && uiMode != surfaceMode) ||
            (paintEmitter != null && emitterMode != surfaceMode)
        );
        Color mixedColor = paintReservoir != null ? paintReservoir.mixedPaintColor : paintColor;
        string mixMode = paintReservoir != null ? paintReservoir.mixMode.ToString() : "SingleColor";
        string components = paintReservoir != null ? paintReservoir.ColorComponentsSummary : "#" + ColorUtility.ToHtmlStringRGB(paintColor);
        string reservoirDebug = paintReservoir != null
            ? paintReservoir.LiquidDebugSummary
            : "Reservoir component found: No";
        bool highFlow = emissionRate > 50f;
        bool blobRisk = effectiveRadius > 0.018f || emissionRate > 50f || particlesPerSecond > 180f;
        string blobWarning = highFlow
            ? "High flow may create large blobs."
            : blobRisk
                ? "Blob Warning: radius or particle flow is high."
                : "Blob Warning: none.";

        return
            "Selected Paint Color: #" + ColorUtility.ToHtmlStringRGB(paintColor) +
            "\nMixed Color: #" + ColorUtility.ToHtmlStringRGB(mixedColor) +
            "\nMix Mode: " + mixMode +
            "\nPaint Capacity: " + capacity.ToString("0.000") + " kg" +
            "\nInitial Paint Slider: " + initialPaintAmount.ToString("0.000") + " kg" +
            "\nRemaining Paint: " + remaining.ToString("0.000") + " / " + Mathf.Max(0f, capacity).ToString("0.000") + " kg" +
            "\nHole Diameter: " + holeDiameter.ToString("0.000") + " m" +
            "\nEffective Deposit Radius: " + effectiveRadius.ToString("0.000") + " m" +
            "\nStroke Radius Multiplier: " + (paintingSurface != null ? paintingSurface.strokeRadius.ToString("0.00") : "1.00") +
            "\nFlow Multiplier: " + emissionRate.ToString("0") + (advancedPaintMode ? " (Advanced)" : "") +
            "\nFill Percent: " + fillPercent.ToString("0.0") + "%" +
            "\nFlow Rate: " + flow.ToString("0.0000") + " kg/s" +
            "\nDebug Emitted Mass This Frame: " + emittedMassThisFrame.ToString("0.000000") + " kg" +
            "\nDebug Reservoir Current Amount: " + reservoirCurrentAmount.ToString("0.000000") + " kg" +
            "\nParticles Per Second: " + particlesPerSecond.ToString("0") +
            "\n" + blobWarning +
            "\nBucket State: " + bucketState +
            "\nActive Airborne Particles: " + activeAirborne +
            "\nDeposited Total: " + deposited +
            "\nRecycled Total: " + recycled +
            "\nPaintingSurface found: " + (paintEmitter != null && paintEmitter.HasPaintingSurface ? "yes" : "no") +
            "\nLast dPrev: " + (paintingSurface != null ? paintingSurface.LastCollisionDPrev.ToString("0.0000") : "-") +
            "\nLast dCurr: " + (paintingSurface != null ? paintingSurface.LastCollisionDCurr.ToString("0.0000") : "-") +
            "\nLast t: " + (paintingSurface != null ? paintingSurface.LastCollisionT.ToString("0.000") : "-") +
            "\nLast hit inside board: " + (paintingSurface != null && paintingSurface.LastHitInsideBoard ? "yes" : "no") +
            "\nLast hit world position: " + (paintingSurface != null ? FormatVector3(paintingSurface.LastCollisionWorldPosition) : "-") +
            "\nTotal collisions: " + collisions +
            "\nTotal missed board: " + missed +
            "\nTotal recycled after hit: " + recycledAfterHit +
            "\nStream clipped by board: " + (paintEmitter != null && paintEmitter.LastStreamClippedByBoard ? "yes" : "no") +
            "\nLegacy Preview: " + legacyState +
            "\nUI Selected Mode: " + uiMode +
            "\nPaintingSurface Active Mode: " + surfaceMode +
            "\nPaintParticleEmitter Active Mode: " + emitterMode +
            "\nLast Deposit Mode: " + lastDepositMode +
            "\nTotal Deposits: " + totalDeposits +
            "\nTexture Updated Count: " + textureUpdates +
            "\nLast Hit UV: " + lastHitUv.x.ToString("0.000") + ", " + lastHitUv.y.ToString("0.000") +
            "\nLast Hit Pixel: " + lastHitPixel.x.ToString("0") + ", " + lastHitPixel.y.ToString("0") +
            "\nConnected Strokes: " + connectedStrokes +
            "\nRejected Connections: " + rejectedConnections +
            "\nActive Stroke Count: " + activeStrokes +
            (modeMismatch ? "\nWARNING: Paint mode source-of-truth mismatch." : "") +
            "\nColor Components: " + components +
            "\n\n" + reservoirDebug;
    }

    private void UpdateCanvasStatusText()
    {
        if (canvasStatusText == null)
        {
            return;
        }

        if (paintingSurface == null)
        {
            canvasStatusText.text = "Canvas status: PaintingSurface missing.";
            return;
        }

        Vector3 rotation = paintingSurface.BoardRotationEuler;
        Vector3 scale = paintingSurface.BoardScale;
        int airborne = paintEmitter != null ? paintEmitter.ActiveAirborneParticleCount : 0;
        int deposited = paintEmitter != null ? paintEmitter.DepositedParticleCount : 0;
        int depositedThisFrame = paintEmitter != null ? paintEmitter.DepositedThisFrameCount : 0;
        int recycled = paintEmitter != null ? paintEmitter.RecycledParticleCount : 0;
        int missed = paintEmitter != null ? paintEmitter.MissedBoardParticleCount : 0;
        PaintingSurface.SurfaceBehavior behavior = paintingSurface.CurrentSurfaceBehavior;
        canvasStatusText.text =
            "Canvas Width: " + paintingSurface.currentWidth.ToString("0.0") +
            "\nCanvas Height: " + paintingSurface.currentHeight.ToString("0.0") +
            "\nSurface Type: " + paintingSurface.surfaceType +
            "\nOrientation: " + paintingSurface.orientation +
            "\nTilt Angle slider: " + canvasTiltDegrees.ToString("0") + " deg" +
            "\nApplied Tilt Angle: " + paintingSurface.tiltAngle.ToString("0") + " deg" +
            "\nPaint Render Mode: " + paintingSurface.paintRenderMode +
            "\nWorld Paint Renderer:" +
            "\nRender Mode: " + paintingSurface.paintRenderMode +
            "\nWorld Paint Draw Calls: " + paintingSurface.WorldPaintDrawCalls +
            "\nPaint Objects Count: " + paintingSurface.WorldPaintObjectCount +
            "\nLast Paint Object Position: " + FormatVector3(paintingSurface.LastPaintObjectPosition) +
            "\nLast Paint Radius: " + paintingSurface.LastPaintRadius.ToString("0.000") +
            "\nLast Paint Alpha: " + paintingSurface.LastPaintAlpha.ToString("0.000") +
            "\nLast Paint Material Color: " + FormatColor(paintingSurface.LastPaintMaterialColor) +
            "\nPaint Surface Offset: " + paintingSurface.ActivePaintSurfaceOffset.ToString("0.000") +
            "\nInvert Paint Normal: " + paintingSurface.invertPaintNormal +
            "\nInvert Collision Normal: " + paintingSurface.invertBoardNormalForCollision +
            "\nAlways Visible Debug: " + paintingSurface.paintDecalsAlwaysVisibleDebug +
            "\nFallback Geometry: " + paintingSurface.worldPaintFallbackGeometry +
            "\nVisible Paint Normal: " + FormatVector3(paintingSurface.LastVisiblePaintNormal) +
            "\nRenderer Diagnostic:\n" + paintingSurface.WorldPaintRendererDiagnostic +
            "\nMapping Plane: " + paintingSurface.mappingPlane +
            "\nboardRightAxis: " + FormatVector3(paintingSurface.BoardRightAxis) +
            "\nboardUpAxis: " + FormatVector3(paintingSurface.BoardUpAxis) +
            "\nboardNormal: " + FormatVector3(paintingSurface.BoardNormal) +
            "\ncollisionNormal: " + FormatVector3(paintingSurface.invertBoardNormalForCollision ? -paintingSurface.BoardNormal : paintingSurface.BoardNormal) +
            "\ninvertRightAxis: " + paintingSurface.invertRightAxis +
            "\ninvertUpAxis: " + paintingSurface.invertUpAxis +
            "\nswapAxes: " + paintingSurface.swapAxes +
            "\nshowHitDebugMarkers: " + paintingSurface.showMappingDebugMarkers +
            "\nmarkerHistory: " + paintingSurface.debugMarkerHistoryMode +
            "\n" + paintingSurface.MappingDebugSummary +
            "\nAbsorption: " + behavior.absorption.ToString("0.00") +
            "\nSpread Multiplier: " + behavior.spreadMultiplier.ToString("0.00") +
            "\nFriction: " + behavior.friction.ToString("0.00") +
            "\nWetness Retention: " + behavior.wetnessRetention.ToString("0.00") +
            "\nFlow Down Slope: " + behavior.flowDownSlope.ToString("0.00") +
            "\nLast impact speed: " + paintingSurface.LastImpactSpeed.ToString("0.00") + " m/s" +
            "\nAirborne particles: " + airborne +
            "\nDeposited this frame: " + depositedThisFrame +
            "\nDeposited particles: " + deposited +
            "\nRecycled particles: " + recycled +
            "\nMissed board particles: " + missed +
            "\nTotal missed board: " + missed +
            "\nPaintingSurface found: " + (paintEmitter != null && paintEmitter.HasPaintingSurface ? "yes" : "no") +
            "\nLast dPrev: " + paintingSurface.LastCollisionDPrev.ToString("0.0000") +
            "\nLast dCurr: " + paintingSurface.LastCollisionDCurr.ToString("0.0000") +
            "\nLast t: " + paintingSurface.LastCollisionT.ToString("0.000") +
            "\nLast hit inside board: " + (paintingSurface.LastHitInsideBoard ? "yes" : "no") +
            "\nLast hit world position: " + FormatVector3(paintingSurface.LastCollisionWorldPosition) +
            "\nTotal collisions: " + (paintEmitter != null ? paintEmitter.TotalCollisionCount : 0) +
            "\nTotal recycled after hit: " + (paintEmitter != null ? paintEmitter.RecycledAfterHitCount : 0) +
            "\nStream clipped by board: " + (paintEmitter != null && paintEmitter.LastStreamClippedByBoard ? "yes" : "no") +
            "\nShow airborne particles: " + (paintEmitter != null && paintEmitter.showAirborneParticles ? "On" : "Off") +
            "\nTrail mode: " + paintingSurface.ActiveTrailMode +
            "\nLast deposit mode: " + paintingSurface.LastDepositModeUsed +
            "\nTotal deposits: " + paintingSurface.TotalDepositCount +
            "\nTexture updated count: " + paintingSurface.TextureUpdatedCount +
            "\nConnected strokes: " + paintingSurface.ConnectedStrokeCount +
            "\nRejected connections: " + paintingSurface.RejectedConnectionCount +
            "\nAverage stroke length: " + paintingSurface.AverageStrokeLength.ToString("0.0") + " px" +
            "\nActive streams: " + paintingSurface.ActiveStreamCount +
            "\nLast hit UV: " + paintingSurface.LastHitUv.x.ToString("0.000") + ", " + paintingSurface.LastHitUv.y.ToString("0.000") +
            "\nCurrent hit UV: " + paintingSurface.CurrentHitUv.x.ToString("0.000") + ", " + paintingSurface.CurrentHitUv.y.ToString("0.000") +
            "\nparticle previousPosition: " + FormatVector3(paintingSurface.LastParticlePreviousPosition) +
            "\nparticle currentPosition: " + FormatVector3(paintingSurface.LastParticleCurrentPosition) +
            "\nlastWorldHit: " + FormatVector3(paintingSurface.LastWorldHit) +
            "\nlastUV: " + paintingSurface.LastHitUv.x.ToString("0.000") + ", " + paintingSurface.LastHitUv.y.ToString("0.000") +
            "\nLocal hit: " + FormatVector3(paintingSurface.LastLocalHit) +
            "\nlastPixel: " + paintingSurface.LastPixelHit.x.ToString("0") + ", " + paintingSurface.LastPixelHit.y.ToString("0") +
            "\nUV world check: " + FormatVector3(paintingSurface.LastUvToWorldCheck) +
            "\nuvToWorldError: " + paintingSurface.MappingErrorDistance.ToString("0.0000") + " m" +
            "\nConnect distance: " + paintingSurface.connectDistanceThreshold.ToString("0.000") +
            "\nPainted area: " + (paintingSurface.EstimatedPaintedArea01 * 100f).ToString("0.0") + "%" +
            "\nAverage wetness: " + paintingSurface.AverageWetness.ToString("0.000") +
            "\nSplat radius / viscosity effect: " + paintingSurface.LastAppliedSplatRadius.ToString("0.000") + " / " + paintingSurface.LastViscosityEffect.ToString("0.00") +
            "\nBoard rotation: " + rotation.x.ToString("0.0") + ", " + rotation.y.ToString("0.0") + ", " + rotation.z.ToString("0.0") +
            "\nBoard scale: " + scale.x.ToString("0.00") + ", " + scale.y.ToString("0.00") + ", " + scale.z.ToString("0.00");
    }

    private bool IsHideUiPressedThisFrame()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        pressed |= Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        pressed |= Input.GetKeyDown(KeyCode.H);
#endif
        return pressed;
    }

    private string ShortenPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "-";
        }

        string normalized = path.Replace("\\", "/");
        int resultIndex = normalized.LastIndexOf("/PaintBucketResults/");
        if (resultIndex >= 0)
        {
            return "..." + normalized.Substring(resultIndex);
        }

        return normalized.Length > 72 ? "..." + normalized.Substring(normalized.Length - 72) : normalized;
    }

    private void OnGUI()
    {
        if (!enableDebugOnGUI)
        {
            return;
        }

        const int width = 340;
        GUI.Box(new Rect(Screen.width - width - 12, 12, width, Screen.height - 24), "Swinging Paint Bucket Controls");
        GUILayout.BeginArea(new Rect(Screen.width - width, 40, width - 24, Screen.height - 64));
        toolScroll = GUILayout.BeginScrollView(toolScroll);

        GUILayout.Label("Status: " + statusText);
        GUILayout.Label("Time: " + simulationTime.ToString("0.0") + " s");
        GUILayout.Label("FPS: " + currentFps.ToString("0"));
        GUILayout.Label("GPU Particles: " + (fluidSimulation != null ? fluidSimulation.currentParticleCount.ToString("N0") : "0"));
        if (fluidSimulation != null)
        {
            GUILayout.Label("Preset: " + fluidSimulation.particlePreset + " | Buffers: " + fluidSimulation.estimatedGpuBufferMegabytes.ToString("0.0") + " MB");
            if (!string.IsNullOrEmpty(fluidSimulation.particlePresetWarning))
            {
                GUILayout.TextArea(fluidSimulation.particlePresetWarning, GUILayout.MinHeight(42));
            }
        }

        GUILayout.Label("Flow: " + (paintEmitter != null ? paintEmitter.CurrentFlowRateKgPerSecond.ToString("0.0000") : "0") + " kg/s");
        GUILayout.Label("Marks: " + (paintingSurface != null ? paintingSurface.MarkCount.ToString() : "0"));

        EnsureFluidPreview();
        if (fluidPreview != null)
        {
            GUILayout.Label("Standalone Fluid Preview");
            fluidPreview.showPreview = GUILayout.Toggle(fluidPreview.showPreview, "Show Fluid Preview");
            if (GUILayout.Button("Preview Mode: " + (fluidPreview.IsFollowMode ? "Follow Bucket Liquid" : "Manual Debug")))
            {
                fluidPreview.previewMode = fluidPreview.IsFollowMode
                    ? IndependentFluidVisualizer.PreviewMode.ManualDebug
                    : IndependentFluidVisualizer.PreviewMode.FollowBucketLiquid;
            }
            fluidPreview.showInternalParticles = GUILayout.Toggle(fluidPreview.showInternalParticles, "Show Internal Particles");
            fluidPreview.showDensityColors = GUILayout.Toggle(fluidPreview.showDensityColors, "Show Density Colors");
            fluidPreview.showCollisionFlashes = GUILayout.Toggle(fluidPreview.showCollisionFlashes, "Show Collision Flashes");
            fluidPreview.showFlowLayers = GUILayout.Toggle(fluidPreview.showFlowLayers, "Show Flow Layers");
            fluidPreview.showParticleCountProof = GUILayout.Toggle(fluidPreview.showParticleCountProof, "Show Particle Count Proof");
            if (GUILayout.Button("Preview Render Mode: " + fluidPreview.previewRenderMode))
            {
                int nextMode = ((int)fluidPreview.previewRenderMode + 1) % 3;
                fluidPreview.previewRenderMode = (IndependentFluidVisualizer.PreviewRenderMode)nextMode;
                fluidPreview.ForceRebuildParticles();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Low 8k")) ApplyFluidPreviewPreset(IndependentFluidVisualizer.ParticlePreset.Low);
            if (GUILayout.Button("Medium 50k")) ApplyFluidPreviewPreset(IndependentFluidVisualizer.ParticlePreset.Medium);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("High 200k")) ApplyFluidPreviewPreset(IndependentFluidVisualizer.ParticlePreset.High);
            if (GUILayout.Button("Ultra 1M")) ApplyFluidPreviewPreset(IndependentFluidVisualizer.ParticlePreset.Ultra);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Force Rebuild Preview Particles")) ForceRebuildFluidPreviewParticles();
            if (fluidPreview.previewMode == IndependentFluidVisualizer.PreviewMode.ManualDebug)
            {
                float fill = fluidPreview.fillPercent * 100f;
                DrawSlider("Preview Fill Percent", ref fill, 5f, 100f);
                fluidPreview.fillPercent = Mathf.Clamp01(fill / 100f);
                float count = fluidPreview.previewParticleCount;
                DrawSlider("Preview Particle Count", ref count, 25f, 1000000f);
                int roundedCount = Mathf.RoundToInt(count);
                if (roundedCount != fluidPreview.previewParticleCount)
                {
                    fluidPreview.SetPreviewParticleCount(roundedCount);
                }
                DrawSlider("Preview Slosh Strength", ref fluidPreview.sloshStrength, 0f, 1.5f);
                DrawSlider("Preview Motion Speed", ref fluidPreview.motionSpeed, 0.1f, 5f);
                DrawSlider("Preview Slosh Damping", ref fluidPreview.sloshDamping, 0.1f, 8f);
            }
            if (GUILayout.Button("Reset Preview Motion")) ResetFluidPreviewMotion();
            GUILayout.TextArea(fluidPreview.StatsText, GUILayout.MinHeight(110));
        }

        GUILayout.Label("Particle Count Presets");
        GUILayout.BeginHorizontal();
        DrawParticlePresetButton("Low 8k", Simulation3D.ParticleCountPreset.Low);
        DrawParticlePresetButton("Medium 50k", Simulation3D.ParticleCountPreset.Medium);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        DrawParticlePresetButton("High 200k", Simulation3D.ParticleCountPreset.High);
        DrawParticlePresetButton("Ultra 1M", Simulation3D.ParticleCountPreset.Ultra);
        GUILayout.EndHorizontal();

        DrawSlider("Bucket Weight kg", ref bucketWeight, 0.2f, 10f);
        DrawSlider("Initial Paint kg", ref initialPaintAmount, 0.1f, 100f);
        DrawSlider("Bucket Radius m", ref bucketRadius, 0.15f, 1.0f);
        DrawSlider("Hole Diameter m", ref holeDiameter, 0.01f, 0.25f);
        DrawSlider("Rope Flexibility", ref ropeFlexibility, 0f, 1f);
        DrawSlider("Humidity", ref humidity, 0f, 1f);
        DrawSlider("Duration Limit s", ref simulationDurationLimit, 0f, 180f);
        DrawSlider("Canvas Width", ref canvasWidth, 1f, 20f);
        DrawSlider("Canvas Height", ref canvasHeight, 1f, 20f);
        DrawSlider("Canvas Tilt deg", ref canvasTiltDegrees, 0f, 60f);

        if (GUILayout.Button("Surface Type: " + surfaceTypes[Mathf.Clamp(surfaceTypeIndex, 0, surfaceTypes.Length - 1)]))
        {
            surfaceTypeIndex = (surfaceTypeIndex + 1) % surfaceTypes.Length;
        }

        if (pendulumController != null)
        {
            DrawSlider("Air Resistance", ref pendulumController.airResistanceCoefficient, 0f, 2f);
            DrawSlider("Pivot Friction", ref pendulumController.pivotFrictionCoefficient, 0f, 2f);
            DrawSlider("Direction / Side Push", ref pendulumController.phiDot, -2f, 2f);
            float angleDegrees = pendulumController.theta * Mathf.Rad2Deg;
            DrawSlider("Start Angle deg", ref angleDegrees, 0f, 80f);
            pendulumController.theta = angleDegrees * Mathf.Deg2Rad;
        }

        DrawSlider("Paint Viscosity", ref viscosity, 0.01f, 3f);
        DrawSlider("Flow Speed", ref exitSpeed, 0f, 8f);

        GUILayout.Label("Paint Color");
        paintColor.r = GUILayout.HorizontalSlider(paintColor.r, 0f, 1f);
        paintColor.g = GUILayout.HorizontalSlider(paintColor.g, 0f, 1f);
        paintColor.b = GUILayout.HorizontalSlider(paintColor.b, 0f, 1f);

        if (GUILayout.Button("Apply Values"))
        {
            ApplyPaintSettings(resetFluid: true);
            ApplyExtendedSettings(resetMotion: false);
            RefreshTexts();
            userMessage = "Values applied.";
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Start")) StartSimulation();
        if (GUILayout.Button("Pause")) PauseSimulation();
        if (GUILayout.Button("Reset All")) ResetSimulation();
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Clear Canvas") && paintingSurface != null)
        {
            paintingSurface.ClearPainting();
            userMessage = "Canvas cleared.";
        }

        if (GUILayout.Button("Save Image") && experimentManager != null)
        {
            userMessage = "Saved image: " + experimentManager.SaveImage();
        }

        if (GUILayout.Button("Save Experiment") && experimentManager != null)
        {
            PaintExperimentManager.ExperimentRecord record = experimentManager.SaveExperiment(simulationTime);
            userMessage = "Saved " + record.experimentName;
        }

        if (GUILayout.Button("Compare Last Two") && experimentManager != null)
        {
            userMessage = experimentManager.CompareLastTwoExperiments();
        }

        if (GUILayout.Button("Generate Report") && experimentManager != null)
        {
            userMessage = "Report: " + experimentManager.GenerateReport(simulationTime);
        }

        if (experimentManager != null)
        {
            GUILayout.Label(experimentManager.BuildHistoryText());
        }

        GUILayout.TextArea(userMessage, GUILayout.MinHeight(80));
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private static void DrawSlider(string label, ref float value, float min, float max)
    {
        GUILayout.Label(label + ": " + value.ToString("0.###"));
        value = GUILayout.HorizontalSlider(value, min, max);
    }

    private void DrawParticlePresetButton(string label, Simulation3D.ParticleCountPreset preset)
    {
        if (!GUILayout.Button(label))
        {
            return;
        }

        if (fluidSimulation == null)
        {
            userMessage = "Fluid simulation is not assigned.";
            return;
        }

        bool wasPaused = isPaused;
        SetFluidPaused(true);
        fluidSimulation.ApplyParticlePreset(preset);
        fluidSimulation.ResetFluid();
        SetFluidPaused(wasPaused);

        userMessage = "Particle preset set to " + fluidSimulation.particlePreset +
            " (" + fluidSimulation.currentParticleCount.ToString("N0") + " particles).";

        if (!string.IsNullOrEmpty(fluidSimulation.particlePresetWarning))
        {
            userMessage += "\n" + fluidSimulation.particlePresetWarning;
        }
    }

    private static void AddSliderListener(Slider slider, UnityEngine.Events.UnityAction<float> listener)
    {
        if (slider != null)
        {
            slider.onValueChanged.AddListener(listener);
        }
    }

    private static void SetSliderValueWithoutNotify(Slider slider, float value)
    {
        if (slider != null)
        {
            slider.SetValueWithoutNotify(value);
        }
    }

    private void OnDisable()
    {
        Time.timeScale = 1f;
    }
}
