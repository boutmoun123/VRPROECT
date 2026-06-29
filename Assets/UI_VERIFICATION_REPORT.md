# MainSimulationUI Verification Report

Scope: UI-to-simulation binding verification for the tabbed MainSimulationUI. This report is based on code-path verification and targeted binding fixes.

## Top Status Bar
[OK] Status is driven by Start/Pause/Reset/Finished state.
[OK] Time updates while simulation is running.
[OK] FPS is sampled live every 0.5 seconds.
[OK] GPU particle count reads Simulation3D.currentParticleCount.
[OK] Particle preset reads Simulation3D.particlePreset.
[OK] Buffer memory reads Simulation3D.estimatedGpuBufferMegabytes.
[FIXED] Remaining Paint now responds correctly when Paint Amount is changed, including 0 kg.
[OK] Flow Rate reads PaintParticleEmitter.CurrentFlowRateKgPerSecond.
[OK] Marks count reads PaintingSurface.MarkCount.
[OK] Painted Area reads PaintingSurface.EstimatedPaintedArea01.

## Motion Tab
[OK] Start button starts motion, unpauses fluid/emitter, sets status Running.
[OK] Pause / Resume toggles paused state and status.
[OK] Reset All resets pendulum, rope, fluid, emitter, canvas texture, timer, marks, and status.
[OK] Rope Length slider updates pendulum rope length, resets pendulum, updates rope length and rope visual.
[OK] Start Angle slider updates pendulum theta and resets the starting pose.
[OK] Initial Velocity slider updates pendulum phiDot and resets the starting pose.
[OK] Damping slider updates pendulum damping.
[OK] Rope Flexibility slider updates MassSpringRope.bendAmount.

## Paint Tab
[FIXED] Paint Amount slider now updates initial and remaining paint in both PaintParticleEmitter and SphericalPendulumController.
[FIXED] Paint Amount slider now allows 0 kg so flow stops when no paint remains.
[OK] Hole Diameter slider updates fluid settings and emitter settings.
[OK] Viscosity slider updates fluid settings and emitter settings.
[OK] Exit Speed slider updates fluid settings and emitter flow speed.
[OK] Flow Multiplier slider updates emission rate and pendulum mass-flow reference.
[OK] Color buttons update emitter color and PaintingSurface paint color for new marks.
[OK] Clear Canvas clears the painting texture and resets marks/painted area.

## Environment Tab
[OK] Gravity slider updates pendulum, rope, fluid, and paint emitter gravity.
[OK] Air Resistance slider updates pendulum air resistance and emitter air drag.
[OK] Pivot Friction slider updates pendulum pivot friction.
[OK] Humidity slider updates emitter humidity; emitter uses it for flow-rate and particle damping.

## Canvas Tab
[OK] Canvas Width slider calls PaintingSurface.ApplyCanvasSettings and changes board scale.
[OK] Canvas Height slider calls PaintingSurface.ApplyCanvasSettings and changes board scale.
[OK] Surface Type dropdown applies procedural material color/metallic/smoothness immediately.
[OK] Canvas Orientation dropdown applies Horizontal/Tilted through PaintingSurface.ApplyCanvasSettings.
[OK] Painting math uses transform.InverseTransformPoint, so resizing and tilting remain mapped to board-local coordinates.
[OK] Canvas status text shows applied width, height, surface, orientation, rotation, and scale.

## Results Tab
[OK] Save Image saves a PNG through PaintExperimentManager.SaveImage.
[OK] Saved image path is shortened before display.
[OK] Save Experiment saves JSON data and updates history.
[OK] Compare Last Two reports a clear message when fewer than two experiments exist.
[OK] Generate Report saves a readable text report with inputs, duration, marks, painted area, paint used, remaining paint, and saved image path.

## Performance Tab
[OK] Low 8k, Medium 50k, High 200k, and Ultra 1M call Simulation3D.ApplyParticlePreset.
[OK] Ultra checks compute-shader and GPU-memory safety before activation and falls back to High with a warning.
[FIXED] UI Small / Medium / Large now adjust CanvasScaler.referenceResolution so the scale control is stable under Scale With Screen Size.

## Mouse Camera Debug
[OK] Debug text shows active state, right mouse pressed, pointer over UI, distance, and target name.
[OK] Right mouse camera input is ignored while pointer is over UI.
[OK] H hides/shows UI and disables CanvasGroup raycasts while hidden.
[OK] F focuses a presentation target centered between bucket and board.
[OK] Escape unlocks and shows cursor.

## Visual Correctness
[OK] Legacy UI visuals are disabled when disableLegacyUIVisuals is true.
[OK] OnGUI debug panel is hidden by default because enableDebugOnGUI is false.
[OK] Long result paths are shortened with ShortenPath.
[WARNING] Unity Play Mode console could not be verified from BatchMode while the project is already open in another Unity Editor instance.
