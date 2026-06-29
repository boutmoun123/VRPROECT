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
        public float paintUsed;
        public float ropeLength;
        public float ropeFlexibility;
        public float startAngleDegrees;
        public float initialVelocity;
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
        public float estimatedPaintedAreaPercent;
        public string surfaceType;
        public string savedImagePath;
    }

    [Header("References")]
    public SphericalPendulumController pendulum;
    public MassSpringRope rope;
    public PaintParticleEmitter emitter;
    public PaintingSurface paintingSurface;
    public float bucketRadius = 0.55f;

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
            "Gravity: " + a.gravity.ToString("0.00") + " | " + b.gravity.ToString("0.00") + "\n" +
            "Viscosity: " + a.viscosity.ToString("0.00") + " | " + b.viscosity.ToString("0.00") + "\n" +
            "Hole Diameter: " + a.holeDiameter.ToString("0.000") + " m | " + b.holeDiameter.ToString("0.000") + " m\n" +
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
            "Rope Length: " + record.ropeLength.ToString("0.000") + " m\n" +
            "Rope Flexibility: " + record.ropeFlexibility.ToString("0.000") + "\n" +
            "Start Angle: " + record.startAngleDegrees.ToString("0.00") + " degrees\n" +
            "Initial Velocity: " + record.initialVelocity.ToString("0.000") + "\n" +
            "Gravity: " + record.gravity.ToString("0.000") + " m/s^2\n" +
            "Air Resistance: " + record.airResistance.ToString("0.000") + "\n" +
            "Humidity: " + record.humidity.ToString("0.00") + "\n" +
            "Pivot Friction: " + record.pivotFriction.ToString("0.000") + "\n" +
            "Viscosity: " + record.viscosity.ToString("0.000") + "\n" +
            "Hole Diameter: " + record.holeDiameter.ToString("0.000") + " m\n" +
            "Flow Speed: " + record.flowSpeed.ToString("0.000") + " m/s\n\n" +
            "Results\n" +
            "Duration: " + record.simulationDuration.ToString("0.00") + " s\n" +
            "Current Flow Rate: " + record.currentFlowRate.ToString("0.0000") + " kg/s\n" +
            "Emitted Particles: " + record.emittedParticleCount + "\n" +
            "Marks/Paths: " + record.markCount + "\n" +
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
        }

        if (paintingSurface != null)
        {
            record.markCount = paintingSurface.MarkCount;
            record.estimatedPaintedAreaPercent = paintingSurface.EstimatedPaintedArea01 * 100f;
            record.surfaceType = paintingSurface.surfaceType;
        }

        record.bucketRadius = bucketRadius;
        record.simulationDuration = simulationDuration;
        record.savedImagePath = imagePath;
        return record;
    }
}
