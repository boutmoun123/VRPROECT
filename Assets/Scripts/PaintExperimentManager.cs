using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PaintExperimentManager : MonoBehaviour
{
    [Serializable]
    public class ExperimentRecord
    {
        public string experimentName;
        public string dateTime;
        public float bucketMass;
        public float bucketRadius;
        public float initialPaintAmount;
        public float remainingPaintAmount;
        public float paintCapacity;
        public float fillPercent;
        public float paintUsed;
        public float ropeLength;
        public float ropeFlexibility;
        public Vector3 pivotPoint;
        public float startAngleDegrees;
        public float initialVelocity;
        public float swingDirectionDegrees;
        public int targetSwingCount;
        public int completedSwingCount;
        public bool swingTargetCompleted;
        public float gravity;
        public float airResistance;
        public float humidity;
        public float pivotFriction;
        public float viscosity;
        public float holeDiameter;
        public float flowSpeed;
        public float currentFlowRate;
        public float simulationDuration;
        public int markCount;
        public int emittedParticleCount;
        public int depositedParticleCount;
        public int recycledParticleCount;
        public float estimatedPaintedAreaPercent;
        public string surfaceType;
        public float tiltAngle;
        public string paintDrawMode;
        public string lastDepositMode;
        public int textureUpdatedCount;
        public int connectedStrokeCount;
        public int rejectedConnectionCount;
        public string selectedPaintColor;
        public string mixedPaintColor;
        public string mixMode;
        public string bucketState;
        public string paintColorsUsed;
        public bool bucketEmpty;
        public string legacyPreviewMode;
        public string savedImagePath;
    }

    [Header("References")]
    public SphericalPendulumController pendulum;
    public MassSpringRope rope;
    public PaintParticleEmitter emitter;
    public BucketPaintReservoir paintReservoir;
    public PaintingSurface paintingSurface;
    public Simulation3D fluidSimulation;
    public float bucketRadius = 0.55f;
    public Vector3 pivotPosition;
    public float swingDirectionDegrees;
    public int targetSwingCount = 10;
    public int completedSwingCount;
    public bool swingTargetCompleted;

    public IReadOnlyList<ExperimentRecord> History => history;
    public string ResultsDirectory => Path.Combine(Application.persistentDataPath, "PaintBucketResults");

    private readonly List<ExperimentRecord> history = new List<ExperimentRecord>();
    private int experimentCounter;

    public string SaveImage()
    {
        if (paintingSurface == null)
        {
            Debug.LogWarning("Cannot save painting because PaintingSurface is not assigned.");
            return string.Empty;
        }

        string path = paintingSurface.SavePng(ResultsDirectory, "painting");
        Debug.Log("Painting image saved: " + path);
        return path;
    }

    public ExperimentRecord SaveExperiment(float simulationDuration)
    {
        string imagePath = SaveImage();
        ExperimentRecord record = CreateRecord(simulationDuration, imagePath);
        history.Add(record);
        experimentCounter++;

        string jsonPath = Path.Combine(ResultsDirectory, record.experimentName + ".json");
        Directory.CreateDirectory(ResultsDirectory);
        File.WriteAllText(jsonPath, JsonUtility.ToJson(record, true));
        Debug.Log("Experiment saved: " + jsonPath);

        return record;
    }

    public string CompareLastTwoExperiments()
    {
        if (history.Count < 2)
        {
            return "Need at least two saved experiments to compare.";
        }

        ExperimentRecord a = history[history.Count - 2];
        ExperimentRecord b = history[history.Count - 1];

        return
            "Compare Last Two Experiments\n" +
            "A: " + a.experimentName + " | B: " + b.experimentName + "\n" +
            "Rope Length: " + a.ropeLength.ToString("0.00") + " m | " + b.ropeLength.ToString("0.00") + " m\n" +
            "Bucket Weight: " + a.bucketMass.ToString("0.00") + " kg | " + b.bucketMass.ToString("0.00") + " kg\n" +
            "Bucket Radius: " + a.bucketRadius.ToString("0.00") + " m | " + b.bucketRadius.ToString("0.00") + " m\n" +
            "Pivot Point: " + FormatVector3(a.pivotPoint) + " | " + FormatVector3(b.pivotPoint) + "\n" +
            "Swing Direction: " + a.swingDirectionDegrees.ToString("0") + " deg | " + b.swingDirectionDegrees.ToString("0") + " deg\n" +
            "Target Swings: " + FormatTargetSwings(a.targetSwingCount) + " | " + FormatTargetSwings(b.targetSwingCount) + "\n" +
            "Completed Swings: " + a.completedSwingCount + " | " + b.completedSwingCount + "\n" +
            "Swing Target Completed: " + BoolText(a.swingTargetCompleted) + " | " + BoolText(b.swingTargetCompleted) + "\n" +
            "Gravity: " + a.gravity.ToString("0.00") + " | " + b.gravity.ToString("0.00") + "\n" +
            "Viscosity: " + a.viscosity.ToString("0.00") + " | " + b.viscosity.ToString("0.00") + "\n" +
            "Hole Diameter: " + a.holeDiameter.ToString("0.000") + " m | " + b.holeDiameter.ToString("0.000") + " m\n" +
            "Paint Color(s): " + a.paintColorsUsed + " | " + b.paintColorsUsed + "\n" +
            "Initial Paint: " + a.initialPaintAmount.ToString("0.000") + " kg | " + b.initialPaintAmount.ToString("0.000") + " kg\n" +
            "Paint Capacity: " + a.paintCapacity.ToString("0.000") + " kg | " + b.paintCapacity.ToString("0.000") + " kg\n" +
            "Remaining Paint: " + a.remainingPaintAmount.ToString("0.000") + " kg | " + b.remainingPaintAmount.ToString("0.000") + " kg\n" +
            "Fill Percent: " + a.fillPercent.ToString("0.0") + "% | " + b.fillPercent.ToString("0.0") + "%\n" +
            "Mix Mode: " + a.mixMode + " | " + b.mixMode + "\n" +
            "Bucket Empty: " + BoolText(a.bucketEmpty) + " | " + BoolText(b.bucketEmpty) + "\n" +
            "Deposited Particles: " + a.depositedParticleCount + " | " + b.depositedParticleCount + "\n" +
            "Surface: " + a.surfaceType + " | " + b.surfaceType + "\n" +
            "Tilt Angle: " + a.tiltAngle.ToString("0.0") + " deg | " + b.tiltAngle.ToString("0.0") + " deg\n" +
            "Paint Used: " + a.paintUsed.ToString("0.000") + " kg | " + b.paintUsed.ToString("0.000") + " kg\n" +
            "Marks: " + a.markCount + " | " + b.markCount + "\n" +
            "Painted Area: " + a.estimatedPaintedAreaPercent.ToString("0.00") + "% | " + b.estimatedPaintedAreaPercent.ToString("0.00") + "%\n" +
            "Image A: " + a.savedImagePath + "\n" +
            "Image B: " + b.savedImagePath;
    }

    public string GenerateReport(float simulationDuration)
    {
        string imagePath = SaveImage();
        ExperimentRecord record = CreateRecord(simulationDuration, imagePath);
        Directory.CreateDirectory(ResultsDirectory);

        string reportPath = Path.Combine(ResultsDirectory, "report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
        string report =
            "Swinging Paint Bucket Simulation Report\n" +
            "Generated: " + record.dateTime + "\n\n" +
            "Inputs\n" +
            "Bucket Mass: " + record.bucketMass.ToString("0.000") + " kg\n" +
            "Bucket Radius: " + record.bucketRadius.ToString("0.000") + " m\n" +
            "Initial Paint: " + record.initialPaintAmount.ToString("0.000") + " kg\n" +
            "Paint Capacity: " + record.paintCapacity.ToString("0.000") + " kg\n" +
            "Final Fill Percent: " + record.fillPercent.ToString("0.0") + "%\n" +
            "Rope Length: " + record.ropeLength.ToString("0.000") + " m\n" +
            "Rope Flexibility: " + record.ropeFlexibility.ToString("0.000") + "\n" +
            "Pivot Point: " + FormatVector3(record.pivotPoint) + "\n" +
            "Start Angle: " + record.startAngleDegrees.ToString("0.00") + " degrees\n" +
            "Initial Velocity: " + record.initialVelocity.ToString("0.000") + "\n" +
            "Swing Direction: " + record.swingDirectionDegrees.ToString("0.00") + " degrees\n" +
            "Target Number of Swings: " + FormatTargetSwings(record.targetSwingCount) + "\n" +
            "Gravity: " + record.gravity.ToString("0.000") + " m/s^2\n" +
            "Air Resistance: " + record.airResistance.ToString("0.000") + "\n" +
            "Humidity: " + record.humidity.ToString("0.00") + "\n" +
            "Pivot Friction: " + record.pivotFriction.ToString("0.000") + "\n" +
            "Viscosity: " + record.viscosity.ToString("0.000") + "\n" +
            "Hole Diameter: " + record.holeDiameter.ToString("0.000") + " m\n" +
            "Flow Speed: " + record.flowSpeed.ToString("0.000") + " m/s\n\n" +
            "Selected Paint Color: " + record.selectedPaintColor + "\n" +
            "Mixed Paint Color: " + record.mixedPaintColor + "\n" +
            "Mix Mode: " + record.mixMode + "\n" +
            "Paint Colors Used: " + record.paintColorsUsed + "\n" +
            "Surface Type: " + record.surfaceType + "\n" +
            "Paint Draw Mode: " + record.paintDrawMode + "\n" +
            "Last Deposit Mode: " + record.lastDepositMode + "\n" +
            "Tilt Angle: " + record.tiltAngle.ToString("0.00") + " degrees\n\n" +
            "Results\n" +
            "Duration: " + record.simulationDuration.ToString("0.00") + " s\n" +
            "Current Flow Rate: " + record.currentFlowRate.ToString("0.0000") + " kg/s\n" +
            "Bucket Empty: " + BoolText(record.bucketEmpty) + "\n" +
            "Bucket State: " + record.bucketState + "\n" +
            "Completed Swings: " + record.completedSwingCount + "\n" +
            "Swing Target Completed: " + BoolText(record.swingTargetCompleted) + "\n" +
            "Legacy Preview Mode: " + record.legacyPreviewMode + "\n" +
            "Emitted Particles: " + record.emittedParticleCount + "\n" +
            "Deposited Particles: " + record.depositedParticleCount + "\n" +
            "Recycled Particles: " + record.recycledParticleCount + "\n" +
            "Marks/Paths: " + record.markCount + "\n" +
            "Texture Updated Count: " + record.textureUpdatedCount + "\n" +
            "Connected Strokes: " + record.connectedStrokeCount + "\n" +
            "Rejected Connections: " + record.rejectedConnectionCount + "\n" +
            "Estimated Painted Area: " + record.estimatedPaintedAreaPercent.ToString("0.00") + "%\n" +
            "Paint Used: " + record.paintUsed.ToString("0.000") + " kg\n" +
            "Remaining Paint: " + record.remainingPaintAmount.ToString("0.000") + " kg\n" +
            "Saved Image: " + record.savedImagePath + "\n";

        File.WriteAllText(reportPath, report);
        Debug.Log("Report generated: " + reportPath);
        return reportPath;
    }

    public string BuildHistoryText()
    {
        if (history.Count == 0)
        {
            return "No experiments saved yet.";
        }

        string text = "Session History\n";
        for (int i = 0; i < history.Count; i++)
        {
            ExperimentRecord record = history[i];
            text +=
                (i + 1) + ". " + record.experimentName +
                " | Marks: " + record.markCount +
                " | Area: " + record.estimatedPaintedAreaPercent.ToString("0.0") + "%" +
                " | Used: " + record.paintUsed.ToString("0.000") + " kg\n";
        }

        return text;
    }

    private ExperimentRecord CreateRecord(float simulationDuration, string imagePath)
    {
        ExperimentRecord record = new ExperimentRecord();
        record.experimentName = "experiment_" + (experimentCounter + 1).ToString("000");
        record.dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (pendulum != null)
        {
            record.bucketMass = pendulum.bucketEmptyMass;
            record.initialPaintAmount = pendulum.InitialPaintMass;
            record.remainingPaintAmount = pendulum.paintMass;
            record.ropeLength = pendulum.ropeLength;
            record.pivotPoint = pendulum.pivotPoint != null ? pendulum.pivotPoint.position : pivotPosition;
            record.startAngleDegrees = pendulum.theta * Mathf.Rad2Deg;
            record.initialVelocity = pendulum.phiDot;
            record.gravity = pendulum.gravity;
            record.airResistance = pendulum.airResistanceCoefficient;
            record.pivotFriction = pendulum.pivotFrictionCoefficient;
        }

        if (rope != null)
        {
            record.ropeFlexibility = rope.bendAmount;
        }

        if (emitter != null)
        {
            record.initialPaintAmount = emitter.initialPaintAmount;
            record.remainingPaintAmount = emitter.remainingPaintAmount;
            record.paintUsed = emitter.PaintUsed;
            record.humidity = emitter.humidity;
            record.viscosity = emitter.viscosity;
            record.holeDiameter = emitter.holeDiameter;
            record.flowSpeed = emitter.flowSpeed;
            record.currentFlowRate = emitter.CurrentFlowRateKgPerSecond;
            record.emittedParticleCount = emitter.EmittedParticleCount;
            record.depositedParticleCount = emitter.DepositedParticleCount;
            record.recycledParticleCount = emitter.RecycledParticleCount;
            record.selectedPaintColor = "#" + ColorUtility.ToHtmlStringRGB(emitter.paintColor);
            record.paintColorsUsed = emitter.UsedPaintColorSummary;
            record.bucketEmpty = emitter.IsBucketEmpty;
        }

        if (paintReservoir != null)
        {
            record.paintCapacity = paintReservoir.capacity;
            record.fillPercent = paintReservoir.FillPercent * 100f;
            record.selectedPaintColor = "#" + ColorUtility.ToHtmlStringRGB(paintReservoir.selectedPaintColor);
            record.mixedPaintColor = "#" + ColorUtility.ToHtmlStringRGB(paintReservoir.mixedPaintColor);
            record.mixMode = paintReservoir.mixMode.ToString();
            record.bucketState = paintReservoir.BucketState;
            record.paintColorsUsed = paintReservoir.GetColorsUsedSummary();
            record.bucketEmpty = paintReservoir.IsEmpty;
        }

        if (paintingSurface != null)
        {
            record.markCount = paintingSurface.MarkCount;
            record.estimatedPaintedAreaPercent = paintingSurface.EstimatedPaintedArea01 * 100f;
            record.surfaceType = paintingSurface.surfaceType;
            record.tiltAngle = paintingSurface.tiltAngle;
            record.paintDrawMode = paintingSurface.ActiveTrailMode.ToString();
            record.lastDepositMode = paintingSurface.LastDepositModeUsed;
            record.textureUpdatedCount = paintingSurface.TextureUpdatedCount;
            record.connectedStrokeCount = paintingSurface.ConnectedStrokeCount;
            record.rejectedConnectionCount = paintingSurface.RejectedConnectionCount;
        }

        record.bucketRadius = bucketRadius;
        record.pivotPoint = record.pivotPoint == Vector3.zero ? pivotPosition : record.pivotPoint;
        record.swingDirectionDegrees = swingDirectionDegrees;
        record.targetSwingCount = targetSwingCount;
        record.completedSwingCount = completedSwingCount;
        record.swingTargetCompleted = swingTargetCompleted;
        record.legacyPreviewMode = fluidSimulation != null ? fluidSimulation.LegacyPreviewState : "Hidden";
        record.simulationDuration = simulationDuration;
        record.savedImagePath = imagePath;
        return record;
    }

    private static string BoolText(bool value)
    {
        return value ? "Yes" : "No";
    }

    private static string FormatVector3(Vector3 value)
    {
        return value.x.ToString("0.00") + ", " + value.y.ToString("0.00") + ", " + value.z.ToString("0.00");
    }

    private static string FormatTargetSwings(int value)
    {
        return value <= 0 ? "Unlimited" : value.ToString();
    }
}
