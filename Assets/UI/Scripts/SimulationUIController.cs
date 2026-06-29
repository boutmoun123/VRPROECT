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
    public PaintingSurface paintingSurface;
    public PaintExperimentManager experimentManager;
    public MouseCameraController mouseCameraController;

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
    public float emissionRate = 120f;
    public float exitSpeed = 3f;
    public float viscosity = 0.5f;
    public float holeDiameter = 0.08f;
    public float bucketWeight = 2f;
    public float bucketRadius = 0.55f;
    public float initialPaintAmount = 1f;
    public float humidity = 0.35f;
    public float ropeFlexibility = 0.18f;
    public float simulationDurationLimit = 30f;
    public float canvasWidth = 10f;
    public float canvasHeight = 10f;
    public float canvasTiltDegrees = 0f;
    public Color paintColor = new Color(0.1f, 0.25f, 1f, 1f);

    [Header("Unified Canvas UI")]
    public bool buildUnifiedCanvasUI = true;
    public bool disableLegacyUIVisuals = true;
    public bool enableDebugOnGUI = false;

    private float simulationTime;
    private bool isPaused = true;
    private int swingCount;
    private bool swingCounterReady;
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
    private TMP_Text referenceWarningText;
    private TMP_Text particleWarningText;
    private TMP_Text lastImagePathText;
    private TMP_Text lastReportPathText;
    private TMP_Text comparisonText;
    private TMP_Text historyText;
    private TMP_Text canvasStatusText;
    private TMP_Dropdown surfaceTypeDropdown;
    private TMP_Dropdown canvasOrientationDropdown;
    private readonly List<TMP_Text> dynamicValueTexts = new List<TMP_Text>();
    private RectTransform tabContentRoot;
    private readonly List<Button> tabButtons = new List<Button>();
    private DashboardTab activeTab = DashboardTab.Motion;
    private bool uiVisible = true;

    private enum DashboardTab
    {
        Motion,
        Paint,
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
            humidity = paintEmitter.humidity;
            paintColor = paintEmitter.paintColor;
        }

        if (paintingSurface != null)
        {
            canvasWidth = paintingSurface.currentWidth > 0.001f
                ? paintingSurface.currentWidth
                : paintingSurface.localHalfExtents.x * 2f * paintingSurface.transform.localScale.x;
            canvasHeight = paintingSurface.currentHeight > 0.001f
                ? paintingSurface.currentHeight
                : paintingSurface.localHalfExtents.y * 2f * paintingSurface.transform.localScale.z;
            canvasTiltDegrees = paintingSurface.orientation == "Tilted" ? paintingSurface.tiltAngle : 0f;

            for (int i = 0; i < surfaceTypes.Length; i++)
            {
                if (surfaceTypes[i] == paintingSurface.surfaceType)
                {
                    surfaceTypeIndex = i;
                    break;
                }
            }
        }

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
            ropeController.ropeLength = value + 0.08f;
            ropeController.ResetRope();
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
        emissionRate = value;

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

        if (paintEmitter != null)
        {
            paintEmitter.SetPaintAmount(initialPaintAmount);
        }

        if (pendulumController != null)
        {
            pendulumController.SetPaintAmount(initialPaintAmount);
        }

        ApplyExtendedSettings(resetMotion: false);
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
        bucketRadius = Mathf.Max(0.05f, bucketRadius);
        initialPaintAmount = Mathf.Max(0f, initialPaintAmount);
        holeDiameter = Mathf.Max(0.001f, holeDiameter);
        viscosity = Mathf.Max(0.001f, viscosity);
        humidity = Mathf.Clamp01(humidity);
        ropeFlexibility = Mathf.Clamp(ropeFlexibility, 0f, 1f);
        simulationDurationLimit = Mathf.Max(0f, simulationDurationLimit);

        if (pendulumController != null)
        {
            pendulumController.bucketEmptyMass = bucketWeight;
            pendulumController.paintMass = paintEmitter != null ? paintEmitter.remainingPaintAmount : initialPaintAmount;
            pendulumController.simulatePaintLoss = false;
            pendulumController.paintMassFlowRate = emissionRate / 1200f;
        }

        if (ropeController != null)
        {
            ropeController.bendAmount = ropeFlexibility;
        }

        if (paintingSurface != null)
        {
            paintingSurface.paintColor = paintColor;
            string selectedSurface = surfaceTypes[Mathf.Clamp(surfaceTypeIndex, 0, surfaceTypes.Length - 1)];
            string selectedOrientation = Mathf.Abs(canvasTiltDegrees) > 1f ? "Tilted" : "Horizontal";
            paintingSurface.ApplyCanvasSettings(canvasWidth, canvasHeight, selectedSurface, selectedOrientation);
            canvasWidth = paintingSurface.currentWidth;
            canvasHeight = paintingSurface.currentHeight;
            canvasTiltDegrees = paintingSurface.orientation == "Tilted" ? paintingSurface.tiltAngle : 0f;
        }

        if (paintEmitter != null)
        {
            float gravityValue = pendulumController != null ? pendulumController.gravity : Mathf.Abs(fluidSimulation != null ? fluidSimulation.gravity : 9.81f);
            float airValue = pendulumController != null ? pendulumController.airResistanceCoefficient : 0.05f;
            paintEmitter.ApplySettings(initialPaintAmount, holeDiameter, viscosity, exitSpeed, gravityValue, airValue, humidity, paintColor);
        }

        if (experimentManager != null)
        {
            experimentManager.bucketRadius = bucketRadius;
        }

        if (resetMotion)
        {
            ResetSimulation();
        }
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
            swingCountText.text = "Swing Count: " + swingCount;

        if (paintRemainingText != null && pendulumController != null)
        {
            float percent = paintEmitter != null
                ? Mathf.Clamp01(paintEmitter.remainingPaintAmount / Mathf.Max(0.001f, paintEmitter.initialPaintAmount)) * 100f
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
        }

        previousRadialVelocity = radialVelocity;
    }

    public void StartSimulation()
    {
        isPaused = false;
        statusText = "Running";
        Time.timeScale = 1f;
        SetFluidPaused(false);
        SetPaintEmitterPaused(false);
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

        if (pendulumController != null)
        {
            pendulumController.ResetPendulum();
        }

        ResetRope();

        if (fluidSimulation != null)
        {
            fluidSimulation.ResetFluid();
            fluidSimulation.SetPaused(true);
        }

        if (paintEmitter != null)
        {
            paintEmitter.ResetEmitter(resetPaintAmount: true);
            paintEmitter.SetPaused(true);
        }

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
        previousRadialVelocity = 0f;
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
            ropeController.ResetRope();
        }
    }

    private void SetFluidPaused(bool paused)
    {
        if (fluidSimulation != null)
        {
            fluidSimulation.SetPaused(paused);
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

        Transform exitPoint = null;
        GameObject exitObject = GameObject.Find("PaintExitPoint");
        if (exitObject != null)
        {
            exitPoint = exitObject.transform;
        }

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
        paintEmitter.Initialize();

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
        experimentManager.paintingSurface = paintingSurface;
        experimentManager.bucketRadius = bucketRadius;

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
        CreateTabButton(tabBar, "Environment", DashboardTab.Environment);
        CreateTabButton(tabBar, "Canvas", DashboardTab.Canvas);
        CreateTabButton(tabBar, "Results", DashboardTab.Results);
        CreateTabButton(tabBar, "Performance", DashboardTab.Performance);

        tabContentRoot = CreatePanel("Active Tab Content", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(-600f, 190f), new Vector2(600f, -154f));
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

        layout.padding = new RectOffset(22, 22, 18, 18);
        layout.spacing = 12;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

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
    }

    private void CreateMotionTab(Transform parent)
    {
        RectTransform buttonRow = CreateButtonRow(parent);
        CreateButton(buttonRow, "Start", StartSimulation);
        CreateButton(buttonRow, "Pause / Resume", TogglePauseResume);
        CreateButton(buttonRow, "Reset All", ResetSimulation);
        ropeLengthSlider = CreateSliderRow(parent, "Rope Length", 0.5f, 8f, pendulumController != null ? pendulumController.ropeLength : 4f, "0.00 m", ChangeRopeLength, out ropeLengthValueText);
        initialAngleSlider = CreateSliderRow(parent, "Start Angle", 0f, 80f, pendulumController != null ? pendulumController.theta * Mathf.Rad2Deg : 30f, "0 deg", ChangeInitialAngle, out initialAngleValueText);
        sidePushSlider = CreateSliderRow(parent, "Initial Velocity", -2f, 2f, pendulumController != null ? pendulumController.phiDot : 0f, "0.00", ChangeSidePush, out sidePushValueText);
        dampingSlider = CreateSliderRow(parent, "Damping", 0f, 0.25f, pendulumController != null ? pendulumController.damping : 0.05f, "0.000", ChangeDamping, out dampingValueText);
        CreateSliderRow(parent, "Rope Flexibility", 0f, 1f, ropeFlexibility, "0.00", value => { ropeFlexibility = value; ApplyExtendedSettings(false); }, out _);
    }

    private void CreatePaintTab(Transform parent)
    {
        CreateSliderRow(parent, "Paint Amount", 0f, 5f, initialPaintAmount, "0.00 kg", ChangePaintAmount, out _);
        holeDiameterSlider = CreateSliderRow(parent, "Hole Diameter", 0.01f, 0.25f, holeDiameter, "0.000 m", ChangeHoleDiameter, out holeDiameterValueText);
        viscositySlider = CreateSliderRow(parent, "Viscosity", 0.01f, 3f, viscosity, "0.00", ChangeViscosity, out viscosityValueText);
        exitSpeedSlider = CreateSliderRow(parent, "Exit Speed", 0f, 8f, exitSpeed, "0.00 m/s", ChangeExitSpeed, out exitSpeedValueText);
        emissionRateSlider = CreateSliderRow(parent, "Flow Multiplier", 1f, 360f, emissionRate, "0", ChangeEmissionRate, out emissionRateValueText);
        CreateColorButtons(parent);
        CreateButton(parent, "Clear Canvas", ClearCanvas);
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
        canvasOrientationDropdown = CreateDropdown(parent, "Canvas Orientation", new[] { "Horizontal", "Tilted" }, Mathf.Abs(canvasTiltDegrees) > 1f ? 1 : 0, value =>
        {
            canvasTiltDegrees = value == 0 ? 0f : GetCanvasTiltAngle();
            ApplyExtendedSettings(false);
        });
        canvasStatusText = CreateText("CanvasStatus", parent, "", 19, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        SetPreferredHeight(canvasStatusText.rectTransform, 190f);
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
                "\nEstimated GPU Buffer Memory: " + fluidSimulation.estimatedGpuBufferMegabytes.ToString("0.0") + " MB" +
                "\nFPS: " + currentFps.ToString("0") +
                "\nPress H to hide/show the UI.";
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
        dampingSlider = CreateSliderRow(content, "Damping", 0f, 0.25f, pendulumController != null ? pendulumController.damping : 0.05f, "0.000", ChangeDamping, out dampingValueText);
        CreateSliderRow(content, "Rope Flexibility", 0f, 1f, ropeFlexibility, "0.00", value => { ropeFlexibility = value; ApplyExtendedSettings(false); }, out _);

        CreateSection(content, "Environment");
        gravitySlider = CreateSliderRow(content, "Gravity", 0f, 20f, pendulumController != null ? pendulumController.gravity : 9.81f, "0.00 m/s2", ChangeGravity, out gravityValueText);
        airResistanceSlider = CreateSliderRow(content, "Air Resistance", 0f, 2f, pendulumController != null ? pendulumController.airResistanceCoefficient : 0f, "0.00", ChangeAirResistance, out airResistanceValueText);
        pivotFrictionSlider = CreateSliderRow(content, "Pivot Friction", 0f, 2f, pendulumController != null ? pendulumController.pivotFrictionCoefficient : 0f, "0.00", ChangePivotFriction, out pivotFrictionValueText);
        CreateSliderRow(content, "Humidity", 0f, 1f, humidity, "0.00", value => { humidity = value; ApplyExtendedSettings(false); }, out _);

        CreateSection(content, "Paint");
        CreateSliderRow(content, "Paint Amount", 0f, 5f, initialPaintAmount, "0.00 kg", ChangePaintAmount, out _);
        holeDiameterSlider = CreateSliderRow(content, "Hole Diameter", 0.01f, 0.25f, holeDiameter, "0.000 m", ChangeHoleDiameter, out holeDiameterValueText);
        viscositySlider = CreateSliderRow(content, "Viscosity", 0.01f, 3f, viscosity, "0.00", ChangeViscosity, out viscosityValueText);
        exitSpeedSlider = CreateSliderRow(content, "Exit Speed", 0f, 8f, exitSpeed, "0.00 m/s", ChangeExitSpeed, out exitSpeedValueText);
        emissionRateSlider = CreateSliderRow(content, "Flow Multiplier", 1f, 360f, emissionRate, "0", ChangeEmissionRate, out emissionRateValueText);
        CreateColorButtons(content);

        CreateSection(content, "Canvas / Painting Surface");
        CreateSliderRow(content, "Canvas Width", 1f, 20f, canvasWidth, "0.0", value => { canvasWidth = value; ApplyExtendedSettings(false); }, out _);
        CreateSliderRow(content, "Canvas Height", 1f, 20f, canvasHeight, "0.0", value => { canvasHeight = value; ApplyExtendedSettings(false); }, out _);
        surfaceTypeDropdown = CreateDropdown(content, "Surface Type", surfaceTypes, surfaceTypeIndex, value => { surfaceTypeIndex = value; ApplyExtendedSettings(false); });
        canvasOrientationDropdown = CreateDropdown(content, "Canvas Orientation", new[] { "Horizontal", "Tilted" }, Mathf.Abs(canvasTiltDegrees) > 1f ? 1 : 0, value => { canvasTiltDegrees = value == 0 ? 0f : 35f; ApplyExtendedSettings(false); });
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
    }

    private void CreateColorButton(Transform parent, string label, Color color)
    {
        Button button = CreateButton(parent, label, () =>
        {
            paintColor = color;
            ApplyExtendedSettings(false);
        });
        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = color;
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
        fluidSimulation.ApplyParticlePreset(preset);
        fluidSimulation.ResetFluid();
        SetFluidPaused(wasPaused);
        userMessage = "Particle preset: " + fluidSimulation.particlePreset;
        if (activeTab == DashboardTab.Performance)
        {
            RebuildActiveTab();
        }
        UpdateResultTexts();
    }

    private void UpdateUnifiedCanvasTexts()
    {
        if (topStatusText != null)
        {
            float remainingPercent = paintEmitter != null
                ? Mathf.Clamp01(paintEmitter.remainingPaintAmount / Mathf.Max(0.001f, paintEmitter.initialPaintAmount)) * 100f
                : 0f;
            int particleCount = fluidSimulation != null ? fluidSimulation.currentParticleCount : 0;
            string preset = fluidSimulation != null ? fluidSimulation.particlePreset.ToString() : "-";
            float bufferMb = fluidSimulation != null ? fluidSimulation.estimatedGpuBufferMegabytes : 0f;
            float flow = paintEmitter != null ? paintEmitter.CurrentFlowRateKgPerSecond : 0f;
            int marks = paintingSurface != null ? paintingSurface.MarkCount : 0;
            float area = paintingSurface != null ? paintingSurface.EstimatedPaintedArea01 * 100f : 0f;

            topStatusText.text =
                "Status: " + statusText +
                "    Time: " + simulationTime.ToString("0.0") + " s" +
                "    FPS: " + currentFps.ToString("0") +
                "    GPU Particles: " + particleCount.ToString("N0") +
                "    Preset: " + preset +
                "    Buffers: " + bufferMb.ToString("0.0") + " MB" +
                "    Paint: " + remainingPercent.ToString("0") + "%" +
                "    Flow: " + flow.ToString("0.0000") + " kg/s" +
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
        canvasStatusText.text =
            "Applied width: " + paintingSurface.currentWidth.ToString("0.0") +
            "\nApplied height: " + paintingSurface.currentHeight.ToString("0.0") +
            "\nApplied surface type: " + paintingSurface.surfaceType +
            "\nApplied orientation: " + paintingSurface.orientation +
            "\nBoard rotation: " + rotation.x.ToString("0.0") + ", " + rotation.y.ToString("0.0") + ", " + rotation.z.ToString("0.0") +
            "\nBoard scale: " + scale.x.ToString("0.00") + ", " + scale.y.ToString("0.00") + ", " + scale.z.ToString("0.00") +
            "\nAirborne / deposited: " + airborne + " / " + deposited +
            "\nPainted area / avg wetness: " + (paintingSurface.EstimatedPaintedArea01 * 100f).ToString("0.0") + "% / " + paintingSurface.AverageWetness.ToString("0.000") +
            "\nSplat radius / viscosity effect: " + paintingSurface.LastAppliedSplatRadius.ToString("0.000") + " / " + paintingSurface.LastViscosityEffect.ToString("0.00");
    }

    private float GetCanvasTiltAngle()
    {
        return paintingSurface != null ? paintingSurface.tiltAngle : 28f;
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
        DrawSlider("Initial Paint kg", ref initialPaintAmount, 0.1f, 5f);
        DrawSlider("Bucket Radius m", ref bucketRadius, 0.2f, 1.2f);
        DrawSlider("Hole Diameter m", ref holeDiameter, 0.01f, 0.25f);
        DrawSlider("Rope Flexibility", ref ropeFlexibility, 0f, 1f);
        DrawSlider("Humidity", ref humidity, 0f, 1f);
        DrawSlider("Duration Limit s", ref simulationDurationLimit, 0f, 180f);
        DrawSlider("Canvas Width", ref canvasWidth, 1f, 20f);
        DrawSlider("Canvas Height", ref canvasHeight, 1f, 20f);
        DrawSlider("Canvas Tilt deg", ref canvasTiltDegrees, -75f, 75f);

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
