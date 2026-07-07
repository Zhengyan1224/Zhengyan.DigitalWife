using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Silk.NET.Input;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Input;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.GamePlayer;

internal sealed class PythonScriptInstance : IScriptInstance
{
    private const string CommandMarker = "__DW_COMMANDS__";
    private const string FlushMarker = "__DW_FLUSH__";
    private const string ToolResultMarker = "__DW_TOOL_RESULT__";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private static readonly Lazy<string> WorkerScriptPath = new(EnsureWorkerScriptPath, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Process _process;

    public PythonScriptInstance(string scriptPath, string projectDirectory)
    {
        _process = StartWorker(scriptPath, projectDirectory);
    }

    public void Start(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio)
    {
        SendEvent("start", entity, scene, input, audio, 0.0);
    }

    public void Update(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, double deltaSeconds)
    {
        SendEvent("update", entity, scene, input, audio, deltaSeconds);
    }

    public void GuiEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string controlId, string controlName, string eventName)
    {
        SendEvent("gui_event", entity, scene, input, audio, 0.0, controlId, controlName, eventName);
    }

    public void SpriteEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string spriteId, string spriteName, string eventName)
    {
        SendEvent(
            "sprite_event",
            entity,
            scene,
            input,
            audio,
            0.0,
            spriteId: spriteId,
            spriteName: spriteName,
            spriteEventName: eventName);
    }

    public void TrayMenuEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string itemId, string itemText, string eventName)
    {
        SendEvent(
            "tray_menu_event",
            entity,
            scene,
            input,
            audio,
            0.0,
            trayMenuItemId: itemId,
            trayMenuItemText: itemText,
            trayMenuEventName: eventName);
    }

    public void LoadingEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string eventName, float progress, string message)
    {
        SendEvent(eventName, entity, scene, input, audio, 0.0, controlId: string.Empty, controlName: string.Empty, guiEventName: string.Empty, loadingProgress: progress, loadingMessage: message);
    }

    public void SpeechEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string callbackName)
    {
        SpeechCompleted(entity, scene, input, audio, callbackName);
    }

    public void LlmEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeLlmScriptEvent llmEvent)
    {
        SendEvent(
            "llm_event",
            entity,
            scene,
            input,
            audio,
            0.0,
            llmEvent: llmEvent);
    }

    public string? InvokeLlmTool(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeLlmScriptEvent llmEvent)
    {
        return SendEventForToolResult(
            "llm_event",
            entity,
            scene,
            input,
            audio,
            llmEvent);
    }

    public void AsrEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeAsrScriptEvent asrEvent)
    {
        SendEvent(
            "asr_event",
            entity,
            scene,
            input,
            audio,
            0.0,
            asrEvent: asrEvent);
    }

    public void RealtimeVoiceEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, RuntimeRealtimeVoiceScriptEvent realtimeVoiceEvent)
    {
        SendEvent(
            "realtime_voice_event",
            entity,
            scene,
            input,
            audio,
            0.0,
            realtimeVoiceEvent: realtimeVoiceEvent);
    }

    private void SpeechCompleted(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string callbackName)
    {
        SendEvent(
            "speech_completed",
            entity,
            scene,
            input,
            audio,
            0.0,
            controlId: string.Empty,
            controlName: string.Empty,
            guiEventName: string.Empty,
            loadingProgress: 1.0f,
            loadingMessage: string.Empty,
            speechCallback: callbackName);
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(500))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private void SendEvent(
        string eventName,
        RuntimeEntity entity,
        RuntimeScene scene,
        RuntimeInput input,
        RuntimeAudio audio,
        double deltaSeconds,
        string controlId = "",
        string controlName = "",
        string guiEventName = "",
        string spriteId = "",
        string spriteName = "",
        string spriteEventName = "",
        string trayMenuItemId = "",
        string trayMenuItemText = "",
        string trayMenuEventName = "",
        float loadingProgress = 0.0f,
        string loadingMessage = "",
        string speechCallback = "",
        RuntimeLlmScriptEvent? llmEvent = null,
        RuntimeAsrScriptEvent? asrEvent = null,
        RuntimeRealtimeVoiceScriptEvent? realtimeVoiceEvent = null)
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException($"Python process exited with code {_process.ExitCode}.");
        }

        PythonEvent payload = PythonEvent.Create(eventName, entity, scene, input, deltaSeconds, controlId, controlName, guiEventName, spriteId, spriteName, spriteEventName, trayMenuItemId, trayMenuItemText, trayMenuEventName, loadingProgress, loadingMessage, speechCallback, llmEvent, asrEvent, realtimeVoiceEvent);
        _process.StandardInput.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
        _process.StandardInput.Flush();

        while (true)
        {
            string? line = _process.StandardOutput.ReadLine();
            if (line is null)
            {
                throw new InvalidOperationException("Python process closed stdout.");
            }

            if (line.StartsWith(CommandMarker, StringComparison.Ordinal))
            {
                ApplyCommands(line[CommandMarker.Length..], entity, scene, input, audio);
                return;
            }

            if (line.StartsWith(FlushMarker, StringComparison.Ordinal))
            {
                ApplyCommands(line[FlushMarker.Length..], entity, scene, input, audio);
                continue;
            }

            Console.WriteLine(line);
        }
    }

    private string? SendEventForToolResult(
        string eventName,
        RuntimeEntity entity,
        RuntimeScene scene,
        RuntimeInput input,
        RuntimeAudio audio,
        RuntimeLlmScriptEvent llmEvent)
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException($"Python process exited with code {_process.ExitCode}.");
        }

        PythonEvent payload = PythonEvent.Create(
            eventName,
            entity,
            scene,
            input,
            0.0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0.0f,
            string.Empty,
            string.Empty,
            llmEvent,
            null,
            null);
        _process.StandardInput.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
        _process.StandardInput.Flush();

        while (true)
        {
            string? line = _process.StandardOutput.ReadLine();
            if (line is null)
            {
                throw new InvalidOperationException("Python process closed stdout.");
            }

            if (line.StartsWith(ToolResultMarker, StringComparison.Ordinal))
            {
                PythonToolResult? result = JsonSerializer.Deserialize<PythonToolResult>(line[ToolResultMarker.Length..], JsonOptions);
                return result?.Result;
            }

            if (line.StartsWith(CommandMarker, StringComparison.Ordinal))
            {
                ApplyCommands(line[CommandMarker.Length..], entity, scene, input, audio);
                continue;
            }

            if (line.StartsWith(FlushMarker, StringComparison.Ordinal))
            {
                ApplyCommands(line[FlushMarker.Length..], entity, scene, input, audio);
                continue;
            }

            Console.WriteLine(line);
        }
    }

    private static Process StartWorker(string scriptPath, string projectDirectory)
    {
        string saveDirectory = Path.Combine(projectDirectory, "saves");
        Directory.CreateDirectory(saveDirectory);

        ProcessStartInfo startInfo = new()
        {
            FileName = ResolvePythonExecutable(),
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(WorkerScriptPath.Value);
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(saveDirectory);

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start python process.");
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Console.Error.WriteLine(args.Data);
            }
        };
        process.BeginErrorReadLine();
        return process;
    }

    private static string ResolvePythonExecutable()
    {
        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    private static string EnsureWorkerScriptPath()
    {
        string workerDirectory = Path.Combine(Path.GetTempPath(), "Zhengyan.DigitalWife", "python-worker");
        Directory.CreateDirectory(workerDirectory);

        string workerSource = BuildWorkerSource();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(workerSource));
        string hashText = Convert.ToHexString(hash[..8]).ToLowerInvariant();
        string workerPath = Path.Combine(workerDirectory, $"runtime_worker_{hashText}.py");
        File.WriteAllText(workerPath, workerSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return workerPath;
    }

    private static string BuildWorkerSource()
    {
        return """
               import importlib.util
               import datetime
               import inspect
               import json
               import math
               import os
               import random
               import re
               import socket
               import statistics
               import subprocess
               import sys
               import time
               import urllib.error
               import urllib.request

               COMMAND_MARKER = "__DW_COMMANDS__"
               FLUSH_MARKER = "__DW_FLUSH__"
               TOOL_RESULT_MARKER = "__DW_TOOL_RESULT__"
               script_path = sys.argv[1]
               save_directory = os.path.abspath(sys.argv[2])
               os.makedirs(save_directory, exist_ok=True)
               spec = importlib.util.spec_from_file_location("game_script", script_path)
               module = importlib.util.module_from_spec(spec)

               # Make common standard-library modules available without requiring
               # every gameplay script to repeat the same boilerplate imports.
               module.datetime = datetime
               module.json = json
               module.math = math
               module.random = random
               module.re = re
               module.statistics = statistics
               module.subprocess = subprocess
               module.time = time

               spec.loader.exec_module(module)

               def resolve_save_path(file_name):
                   if not file_name or str(file_name).strip() == "":
                       raise ValueError("save file name cannot be empty")
                   normalized = str(file_name).strip().strip("\"").replace("\\", os.sep).replace("/", os.sep)
                   full_path = os.path.abspath(os.path.join(save_directory, normalized))
                   root = save_directory
                   if full_path != root and not full_path.startswith(root + os.sep):
                       raise ValueError("save path is outside the save directory")
                   return full_path

               def render_texture(name):
                   text = str(name).strip()
                   if text.lower().startswith("rt:"):
                       return text
                   return "rt:" + text

               def combine_url(base_url, path):
                   base = str(base_url or "").rstrip("/")
                   route = str(path or "/v1/chat/completions").strip()
                   if route.startswith("http://") or route.startswith("https://"):
                       return route
                   return base + "/" + route.lstrip("/")

               def get_env(name):
                   if not name:
                       return ""
                   return os.environ.get(str(name), "")

               def execute_process(command, timeout_seconds=30, working_directory=None, shell=False):
                   timeout = None if timeout_seconds is None or float(timeout_seconds) <= 0 else float(timeout_seconds)
                   try:
                       completed = subprocess.run(
                           command,
                           cwd=working_directory or None,
                           capture_output=True,
                           text=True,
                           encoding="utf-8",
                           errors="replace",
                           timeout=timeout,
                           shell=bool(shell))
                       return {
                           "exit_code": completed.returncode,
                           "stdout": completed.stdout or "",
                           "stderr": completed.stderr or "",
                           "timed_out": False,
                           "success": completed.returncode == 0
                       }
                   except subprocess.TimeoutExpired as ex:
                       return {
                           "exit_code": -1,
                           "stdout": ex.stdout or "",
                           "stderr": ex.stderr or "",
                           "timed_out": True,
                           "success": False
                       }

               def emit_commands(commands):
                   print(FLUSH_MARKER + json.dumps(commands, ensure_ascii=False, separators=(",", ":")), flush=True)

               def tool_result_to_text(value):
                   if value is None:
                       return ""
                   if isinstance(value, str):
                       return value
                   try:
                       return json.dumps(value, ensure_ascii=False, separators=(",", ":"))
                   except Exception:
                       return str(value)

               def read_openai_sse(url, api_key, payload, timeout_seconds):
                   body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
                   request = urllib.request.Request(
                       url,
                       data=body,
                       headers={
                           "Content-Type": "application/json",
                           "Accept": "text/event-stream",
                           "Authorization": "Bearer " + str(api_key or "")
                       },
                       method="POST")
                   with urllib.request.urlopen(request, timeout=max(1, int(timeout_seconds or 300))) as response:
                       for raw_line in response:
                           line = raw_line.decode("utf-8", errors="replace").strip()
                           if not line or not line.lower().startswith("data:"):
                               continue
                           data = line[5:].strip()
                           if data == "[DONE]":
                               yield {"delta": "", "is_final": True}
                               return
                           try:
                               chunk = json.loads(data)
                           except Exception:
                               continue
                           choices = chunk.get("choices") or []
                           if not choices:
                               continue
                           delta = ((choices[0].get("delta") or {}).get("content")) or ""
                           if delta:
                               yield {"delta": delta, "is_final": False}
                       yield {"delta": "", "is_final": True}

               class Entity:
                   def __init__(self, data, commands):
                       self.id = data.get("id", "")
                       self.name = data.get("name", "")
                       self.type = data.get("type", "")
                       self.position = data.get("position", [0, 0, 0])
                       self.scale = data.get("scale", [1, 1, 1])
                       self.rotation = data.get("rotation", [0, 0, 0, 1])
                       self.material_names = data.get("materialNames", [])
                       self.morph_names = data.get("morphNames", [])
                       self.morph_weights = data.get("morphWeights", {})
                       self.morph_save_anim_weights = data.get("morphSaveAnimWeights", {})
                       self.node_names = data.get("nodeNames", [])
                       self.nodes = data.get("nodes", {})
                       self.colliders = data.get("colliders", [])
                       self.collider = data.get("collider", {})
                       self.enable_water_interaction = bool(data.get("enableWaterInteraction", False))
                       self.kill_on_water_contact = bool(data.get("killOnWaterContact", False))
                       self.water_interaction_enabled = bool(data.get("waterInteractionEnabled", False))
                       self.water_interaction_radius = float(data.get("waterInteractionRadius", 0.0))
                       self.water_interaction_strength = float(data.get("waterInteractionStrength", 0.0))
                       self.particle_ripple_min_interval_seconds = float(data.get("particleRippleMinIntervalSeconds", 0.0))
                       self.particle_ripple_merge_distance = float(data.get("particleRippleMergeDistance", 0.0))
                       self.mirror_reflection_enabled = bool(data.get("mirrorReflectionEnabled", False))
                       self.plane_mirror_reflection_enabled = bool(data.get("planeMirrorReflectionEnabled", False))
                       self.plane_mirror_reflection_strength = float(data.get("planeMirrorReflectionStrength", 0.0))
                       self.gerstner_waves_enabled = bool(data.get("gerstnerWavesEnabled", False))
                       self.gerstner_wave_count = int(data.get("gerstnerWaveCount", 0))
                       self.gerstner_amplitude = float(data.get("gerstnerAmplitude", 0.0))
                       self.gerstner_wavelength = float(data.get("gerstnerWavelength", 0.0))
                       self.gerstner_speed = float(data.get("gerstnerSpeed", 0.0))
                       self.gerstner_steepness = float(data.get("gerstnerSteepness", 0.0))
                       self.gerstner_direction_degrees = float(data.get("gerstnerDirectionDegrees", 0.0))
                       self.ripple_lifetime_seconds = float(data.get("rippleLifetimeSeconds", 0.0))
                       self.ripple_wave_speed = float(data.get("rippleWaveSpeed", 0.0))
                       self.ripple_frequency = float(data.get("rippleFrequency", 0.0))
                       self.ripple_normal_strength = float(data.get("rippleNormalStrength", 0.0))
                       self._commands = commands

                   def set_position(self, x, y, z):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_position", "x": x, "y": y, "z": z})

                   def translate(self, x, y, z):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "translate", "x": x, "y": y, "z": z})

                   def set_scale(self, x, y, z):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_scale", "x": x, "y": y, "z": z})

                   def rotate_x(self, degrees):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "rotate_x", "degrees": degrees})

                   def rotate_y(self, degrees):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "rotate_y", "degrees": degrees})

                   def rotate_z(self, degrees):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "rotate_z", "degrees": degrees})

                   def set_playing(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_playing", "flag": bool(enabled)})

                   def set_visible(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_visible", "flag": bool(enabled)})

                   def set_playback_speed(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_playback_speed", "value": value})

                   def set_loop_motion(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_loop_motion", "flag": bool(enabled)})

                   def set_reset_physics_on_motion_loop(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_reset_physics_on_motion_loop", "flag": bool(enabled)})

                   def set_edge_enabled(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_edge_enabled", "flag": bool(enabled)})

                   def set_shadow_enabled(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_shadow_enabled", "flag": bool(enabled)})

                   def set_enable_water_interaction(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_water_interaction_enabled", "flag": bool(enabled)})

                   def set_kill_on_water_contact(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_kill_on_water_contact", "flag": bool(enabled)})

                   def set_water_interaction_enabled(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_water_surface_interaction_enabled", "flag": bool(enabled)})

                   def set_water_interaction_radius(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_water_interaction_radius", "value": value})

                   def set_water_interaction_strength(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_water_interaction_strength", "value": value})

                   def set_particle_ripple_min_interval_seconds(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_particle_ripple_min_interval_seconds", "value": value})

                   def set_particle_ripple_merge_distance(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_particle_ripple_merge_distance", "value": value})

                   def set_mirror_reflection_enabled(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_mirror_reflection_enabled", "flag": bool(enabled)})

                   def set_plane_mirror_reflection_enabled(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_plane_mirror_reflection_enabled", "flag": bool(enabled)})

                   def set_plane_mirror_reflection_strength(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_plane_mirror_reflection_strength", "value": value})

                   def set_gerstner_waves_enabled(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_gerstner_waves_enabled", "flag": bool(enabled)})

                   def set_gerstner_wave_count(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_gerstner_wave_count", "value": value})

                   def set_gerstner_amplitude(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_gerstner_amplitude", "value": value})

                   def set_gerstner_wavelength(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_gerstner_wavelength", "value": value})

                   def set_gerstner_speed(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_gerstner_speed", "value": value})

                   def set_gerstner_steepness(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_gerstner_steepness", "value": value})

                   def set_gerstner_direction_degrees(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_gerstner_direction_degrees", "value": value})

                   def set_ripple_lifetime_seconds(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_ripple_lifetime_seconds", "value": value})

                   def set_ripple_wave_speed(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_ripple_wave_speed", "value": value})

                   def set_ripple_frequency(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_ripple_frequency", "value": value})

                   def set_ripple_normal_strength(self, value):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_ripple_normal_strength", "value": value})

                   def set_draw_shadow_in_main_pass(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_draw_shadow_in_main_pass", "flag": bool(enabled)})

                   def apply_motion(self, path):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "apply_motion", "path": path})

                   def add_motion_layer(self, path, weight=1.0):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "add_motion_layer", "path": path, "weight": weight})

                   def set_motion_layers(self, layers):
                       normalized_layers = []
                       for layer in layers or []:
                           if isinstance(layer, dict):
                               normalized_layers.append({
                                   "path": layer.get("path", ""),
                                   "weight": layer.get("weight", 1.0),
                                   "resetPhysicsOnLoop": layer.get("resetPhysicsOnLoop", None)
                               })
                           elif isinstance(layer, (list, tuple)) and len(layer) >= 2:
                               normalized_layers.append({
                                   "path": layer[0],
                                   "weight": layer[1],
                                   "resetPhysicsOnLoop": layer[2] if len(layer) >= 3 else None
                               })
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_motion_layers", "motionLayers": normalized_layers})

                   def set_motion_layer_weight(self, path, weight):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_motion_layer_weight", "path": path, "weight": weight})

                   def set_motion_layer_reset_physics_on_loop(self, path, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_motion_layer_reset_physics_on_loop", "path": path, "flag": bool(enabled)})

                   def remove_motion_layer(self, path):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "remove_motion_layer", "path": path})

                   def clear_motion(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_motion"})

                   def play_motion(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "play_motion"})

                   def pause_motion(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "pause_motion"})

                   def stop_motion(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "stop_motion"})

                   def reset_motion(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "reset_motion"})

                   def reset_motion_physics(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "reset_motion_physics"})

                   def seek_motion_time(self, time_seconds):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "seek_motion_time", "value": time_seconds})

                   def seek_motion_frame(self, frame):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "seek_motion_frame", "value": frame})

                   def play_motion_layer(self, path):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "play_motion_layer", "path": path})

                   def pause_motion_layer(self, path):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "pause_motion_layer", "path": path})

                   def set_motion_layer_time(self, path, time_seconds):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_motion_layer_time", "path": path, "value": time_seconds})

                   def set_motion_layer_frame(self, path, frame):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_motion_layer_frame", "path": path, "value": frame})

                   def get_morph_weight(self, name, default=0.0):
                       return self.morph_weights.get(str(name), default)

                   def get_morph_save_anim_weight(self, name, default=0.0):
                       return self.morph_save_anim_weights.get(str(name), default)

                   def set_morph_weight(self, name, weight, override_animation=True):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_morph_weight",
                           "name": str(name),
                           "weight": weight,
                           "flag": bool(override_animation)
                       })

                   def set_morph_save_anim_weight(self, name, weight):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_morph_save_anim_weight",
                           "name": str(name),
                           "weight": weight
                       })

                   def save_morph_anim_weight(self, name):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "save_morph_anim_weight", "name": str(name)})

                   def save_anim_weight(self, name):
                       self.save_morph_anim_weight(name)

                   def load_morph_anim_weight(self, name):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "load_morph_anim_weight", "name": str(name)})

                   def clear_morph_anim_weight(self, name):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_morph_anim_weight", "name": str(name)})

                   def clear_morph_weight_override(self, name):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_morph_weight_override", "name": str(name)})

                   def clear_morph_weight_overrides(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_morph_weight_overrides"})

                   def save_base_animation(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "save_base_animation"})

                   def load_base_animation(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "load_base_animation"})

                   def clear_base_animation(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_base_animation"})

                   def get_node_state(self, name):
                       return self.nodes.get(str(name))

                   def set_node_translate(self, name, x, y, z, override_animation=True):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_node_translate",
                           "name": str(name),
                           "x": x,
                           "y": y,
                           "z": z,
                           "flag": bool(override_animation)
                       })

                   def set_node_rotate(self, name, x, y, z, w, override_animation=True):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_node_rotate",
                           "name": str(name),
                           "x": x,
                           "y": y,
                           "z": z,
                           "w": w,
                           "flag": bool(override_animation)
                       })

                   def set_node_rotate_euler(self, name, x_degrees, y_degrees, z_degrees, override_animation=True):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_node_rotate_euler",
                           "name": str(name),
                           "x": x_degrees,
                           "y": y_degrees,
                           "z": z_degrees,
                           "flag": bool(override_animation)
                       })

                   def set_node_scale(self, name, x, y, z, override_animation=True):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_node_scale",
                           "name": str(name),
                           "x": x,
                           "y": y,
                           "z": z,
                           "flag": bool(override_animation)
                       })

                   def set_node_anim_translate(self, name, x, y, z, override_animation=True):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_node_anim_translate",
                           "name": str(name),
                           "x": x,
                           "y": y,
                           "z": z,
                           "flag": bool(override_animation)
                       })

                   def set_node_anim_rotate(self, name, x, y, z, w, override_animation=True):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_node_anim_rotate",
                           "name": str(name),
                           "x": x,
                           "y": y,
                           "z": z,
                           "w": w,
                           "flag": bool(override_animation)
                       })

                   def set_node_anim_rotate_euler(self, name, x_degrees, y_degrees, z_degrees, override_animation=True):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_node_anim_rotate_euler",
                           "name": str(name),
                           "x": x_degrees,
                           "y": y_degrees,
                           "z": z_degrees,
                           "flag": bool(override_animation)
                       })

                   def save_node_base_animation(self, name):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "save_node_base_animation", "name": str(name)})

                   def load_node_base_animation(self, name):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "load_node_base_animation", "name": str(name)})

                   def clear_node_base_animation(self, name):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_node_base_animation", "name": str(name)})

                   def clear_node_overrides(self, name):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_node_overrides", "name": str(name)})

                   def clear_all_node_overrides(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_all_node_overrides"})

                   def set_material_texture(self, material, texture):
                       command = {"target": "entity", "entity": self.id, "action": "set_material_texture", "texture": texture}
                       if isinstance(material, int):
                           command["index"] = int(material)
                       else:
                           command["name"] = str(material)
                       self._commands.append(command)

                   def set_material_render_texture(self, material, render_texture_name):
                       self.set_material_texture(material, render_texture(render_texture_name))

                   def clear_material_texture_override(self, material):
                       command = {"target": "entity", "entity": self.id, "action": "clear_material_texture_override"}
                       if isinstance(material, int):
                           command["index"] = int(material)
                       else:
                           command["name"] = str(material)
                       self._commands.append(command)

                   def clear_material_texture_overrides(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_material_texture_overrides"})

                   def set_custom_shader(self, vertex_shader, fragment_shader):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_custom_shader",
                           "vertexShader": str(vertex_shader),
                           "fragmentShader": str(fragment_shader)
                       })

                   def clear_custom_shader(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_custom_shader"})

                   def set_custom_shader_float(self, name, value):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_custom_shader_float",
                           "name": str(name),
                           "value": value
                       })

                   def set_custom_shader_int(self, name, value):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_custom_shader_int",
                           "name": str(name),
                           "index": int(value)
                       })

                   def set_custom_shader_vector2(self, name, x, y):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_custom_shader_vector2",
                           "name": str(name),
                           "x": x,
                           "y": y
                       })

                   def set_custom_shader_vector3(self, name, x, y, z):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_custom_shader_vector3",
                           "name": str(name),
                           "x": x,
                           "y": y,
                           "z": z
                       })

                   def set_custom_shader_vector4(self, name, x, y, z, w):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_custom_shader_vector4",
                           "name": str(name),
                           "x": x,
                           "y": y,
                           "z": z,
                           "w": w
                       })

                   def set_custom_shader_color(self, name, r, g, b, a=1.0):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_custom_shader_color",
                           "name": str(name),
                           "colorR": r,
                           "colorG": g,
                           "colorB": b,
                           "colorA": a
                       })

                   def clear_custom_shader_uniform(self, name):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "clear_custom_shader_uniform",
                           "name": str(name)
                       })

                   def clear_custom_shader_uniforms(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_custom_shader_uniforms"})

                   def speak(self, text, speaker_id=None, speed=None, volume=None, on_completed=None):
                       command = {"target": "entity", "entity": self.id, "action": "speak", "text": text}
                       if speaker_id is not None:
                           command["speakerId"] = speaker_id
                       if speed is not None:
                           command["speed"] = speed
                       if volume is not None:
                           command["volume"] = volume
                       if on_completed is not None:
                           command["callback"] = on_completed
                       self._commands.append(command)

                   def stop_speaking(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "stop_speaking"})

                   def bind_relation(self, target, bind_component_transform=True, bind_lighting=False):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "bind_relation",
                           "name": target,
                           "bindComponentTransform": bool(bind_component_transform),
                           "bindLighting": bool(bind_lighting)
                       })

                   def clear_relation(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_relation"})

                   def set_capsule_collider(self, radius, height, center_x=0.0, center_y=1.0, center_z=0.0, axis="y"):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "set_capsule_collider",
                           "radius": radius,
                           "height": height,
                           "x": center_x,
                           "y": center_y,
                           "z": center_z,
                           "axis": axis
                       })

                   def add_capsule_collider(self, name="Capsule Collider", radius=0.5, height=2.0, center_x=0.0, center_y=1.0, center_z=0.0, axis="y", rotation_x=0.0, rotation_y=0.0, rotation_z=0.0):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "add_capsule_collider",
                           "name": name,
                           "radius": radius,
                           "height": height,
                           "x": center_x,
                           "y": center_y,
                           "z": center_z,
                           "axis": axis,
                           "rotationX": rotation_x,
                           "rotationY": rotation_y,
                           "rotationZ": rotation_z
                       })

                   def add_box_collider(self, name="Box Collider", size_x=1.0, size_y=1.0, size_z=1.0, center_x=0.0, center_y=0.5, center_z=0.0, rotation_x=0.0, rotation_y=0.0, rotation_z=0.0):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "add_box_collider",
                           "name": name,
                           "sizeX": size_x,
                           "sizeY": size_y,
                           "sizeZ": size_z,
                           "x": center_x,
                           "y": center_y,
                           "z": center_z,
                           "rotationX": rotation_x,
                           "rotationY": rotation_y,
                           "rotationZ": rotation_z
                       })

                   def add_mesh_collider(self, name="Mesh Collider", walkable=True, max_slope_degrees=55.0, offset_x=0.0, offset_y=0.0, offset_z=0.0, scale_x=1.0, scale_y=1.0, scale_z=1.0, rotation_x=0.0, rotation_y=0.0, rotation_z=0.0):
                       self._commands.append({
                           "target": "entity",
                           "entity": self.id,
                           "action": "add_mesh_collider",
                           "name": name,
                           "flag": bool(walkable),
                           "value": max_slope_degrees,
                           "offsetX": offset_x,
                           "offsetY": offset_y,
                           "offsetZ": offset_z,
                           "sizeX": scale_x,
                           "sizeY": scale_y,
                           "sizeZ": scale_z,
                           "rotationX": rotation_x,
                           "rotationY": rotation_y,
                           "rotationZ": rotation_z
                       })

                   def remove_collider(self, id_or_name):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "remove_collider", "name": id_or_name})

                   def clear_colliders(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "clear_colliders"})

                   def disable_collider(self):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "disable_collider"})

                   def capsule(self):
                       for collider in make_colliders(self):
                           if collider.get("shape") == "capsule":
                               return collider
                       return None

                   def raycast(self, ray):
                       best = None
                       for collider in make_colliders(self):
                           hit = ray.intersect_collider(collider)
                           if hit is not None and (best is None or hit["distance"] < best["distance"]):
                               hit["collider"] = collider.get("name", "")
                               hit["shape"] = collider.get("shape", "")
                               best = hit
                       return best

                   def check_collision(self, other):
                       if other is None:
                           return False
                       for left in make_colliders(self):
                           for right in make_colliders(other):
                               if collider_distance(left, right) <= 0.0:
                                   return True
                       return False

                   def distance_to_collider(self, other):
                       if other is None:
                           return None
                       best = None
                       for left in make_colliders(self):
                           for right in make_colliders(other):
                               distance = collider_distance(left, right)
                               best = distance if best is None else min(best, distance)
                       return best

               class Ray:
                   def __init__(self, origin, direction):
                       self.origin = origin
                       self.direction = direction

                   def get_point(self, distance):
                       return [
                           self.origin[0] + (self.direction[0] * distance),
                           self.origin[1] + (self.direction[1] * distance),
                           self.origin[2] + (self.direction[2] * distance)
                       ]

                   def intersect_plane_y(self, y):
                       dy = self.direction[1]
                       if abs(dy) < 0.00001:
                           return None
                       distance = (y - self.origin[1]) / dy
                       if distance < 0:
                           return None
                       return self.get_point(distance)

                   def intersect_sphere(self, center, radius):
                       ox = self.origin[0] - center[0]
                       oy = self.origin[1] - center[1]
                       oz = self.origin[2] - center[2]
                       dx = self.direction[0]
                       dy = self.direction[1]
                       dz = self.direction[2]
                       a = (dx * dx) + (dy * dy) + (dz * dz)
                       b = 2.0 * ((ox * dx) + (oy * dy) + (oz * dz))
                       c = (ox * ox) + (oy * oy) + (oz * oz) - (radius * radius)
                       discriminant = (b * b) - (4.0 * a * c)
                       if discriminant < 0:
                           return None
                       sqrt_value = discriminant ** 0.5
                       near = (-b - sqrt_value) / (2.0 * a)
                       far = (-b + sqrt_value) / (2.0 * a)
                       distance = near if near >= 0 else far
                       return distance if distance >= 0 else None

                   def intersect_capsule(self, capsule):
                       distance = ray_capsule_distance(self.origin, self.direction, capsule)
                       if distance is None:
                           return None
                       return {
                           "distance": distance,
                           "point": self.get_point(distance)
                       }

                   def intersect_box(self, box):
                       distance = ray_box_distance(self.origin, self.direction, box)
                       if distance is None:
                           return None
                       return {
                           "distance": distance,
                           "point": self.get_point(distance)
                       }

                   def intersect_collider(self, collider):
                       if collider.get("shape") == "box":
                           return self.intersect_box(collider)
                       return self.intersect_capsule(collider)

               class Physics:
                   def __init__(self, scene):
                       self._scene = scene

                   def raycast(self, ray, max_distance=None, ignore_entity=None, entity_type=None):
                       best = None
                       safe_max_distance = float(max_distance) if max_distance is not None and float(max_distance) > 0.0 else 1.0e30
                       ignored_id = ""
                       ignored_name = ""
                       if ignore_entity is not None:
                           if hasattr(ignore_entity, "id"):
                               ignored_id = ignore_entity.id
                               ignored_name = ignore_entity.name
                           else:
                               ignored_id = str(ignore_entity)
                               ignored_name = str(ignore_entity)

                       normalized_type = normalize_type(entity_type)
                       for item in self._scene._entities:
                           candidate = Entity(item, self._scene._commands)
                           if ignored_id and (candidate.id == ignored_id or candidate.name == ignored_name):
                               continue
                           if normalized_type and normalize_type(candidate.type) != normalized_type:
                               continue

                           for collider in make_colliders(candidate):
                               hit = ray.intersect_collider(collider)
                               if hit is None:
                                   continue
                               distance = hit["distance"]
                               if distance > safe_max_distance:
                                   continue
                               if best is None or distance < best["distance"]:
                                   best = {
                                       "entity": candidate,
                                       "entityId": candidate.id,
                                       "entityName": candidate.name,
                                       "entityType": candidate.type,
                                       "colliderId": collider.get("id", ""),
                                       "colliderName": collider.get("name", ""),
                                       "colliderShape": collider.get("shape", ""),
                                       "distance": distance,
                                       "point": hit["point"]
                                   }
                       return best

                   def sample_ground(self, x, z, origin_y=1000.0, max_distance=2000.0, ignore_entity=None, entity_type=None):
                       ray = Ray([float(x), float(origin_y), float(z)], [0.0, -1.0, 0.0])
                       return self.raycast(ray, max_distance=max_distance, ignore_entity=ignore_entity, entity_type=entity_type)

               def dot(a, b):
                   return (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2])

               def sub(a, b):
                   return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]

               def add(a, b):
                   return [a[0] + b[0], a[1] + b[1], a[2] + b[2]]

               def mul(a, s):
                   return [a[0] * s, a[1] * s, a[2] * s]

               def length(a):
                   return max(dot(a, a), 0.0) ** 0.5

               def normalize(a, fallback=None):
                   value_len = length(a)
                   if value_len <= 0.000001:
                       return fallback or [0, 1, 0]
                   return [a[0] / value_len, a[1] / value_len, a[2] / value_len]

               def normalize_type(value):
                   return str(value or "").strip().lower().replace("-", "_").replace(" ", "_")

               def transform_point(local, position, rotation, scale):
                   return add(rotate_vector([local[0] * scale[0], local[1] * scale[1], local[2] * scale[2]], rotation), position)

               def transform_direction(local, rotation, scale):
                   return rotate_vector([local[0] * scale[0], local[1] * scale[1], local[2] * scale[2]], rotation)

               def rotate_vector(v, q):
                   x, y, z, w = q
                   tx = 2.0 * ((y * v[2]) - (z * v[1]))
                   ty = 2.0 * ((z * v[0]) - (x * v[2]))
                   tz = 2.0 * ((x * v[1]) - (y * v[0]))
                   return [
                       v[0] + (w * tx) + ((y * tz) - (z * ty)),
                       v[1] + (w * ty) + ((z * tx) - (x * tz)),
                       v[2] + (w * tz) + ((x * ty) - (y * tx))
                   ]

               def cross(a, b):
                   return [
                       (a[1] * b[2]) - (a[2] * b[1]),
                       (a[2] * b[0]) - (a[0] * b[2]),
                       (a[0] * b[1]) - (a[1] * b[0])
                   ]

               def quat_mul(a, b):
                   ax, ay, az, aw = a
                   bx, by, bz, bw = b
                   return [
                       (aw * bx) + (ax * bw) + (ay * bz) - (az * by),
                       (aw * by) - (ax * bz) + (ay * bw) + (az * bx),
                       (aw * bz) + (ax * by) - (ay * bx) + (az * bw),
                       (aw * bw) - (ax * bx) - (ay * by) - (az * bz)
                   ]

               def quat_from_degrees(degrees):
                   import math
                   rx = math.radians(float(degrees[0]))
                   ry = math.radians(float(degrees[1]))
                   rz = math.radians(float(degrees[2]))
                   sx, cx = math.sin(rx * 0.5), math.cos(rx * 0.5)
                   sy, cy = math.sin(ry * 0.5), math.cos(ry * 0.5)
                   sz, cz = math.sin(rz * 0.5), math.cos(rz * 0.5)
                   qx = [sx, 0.0, 0.0, cx]
                   qy = [0.0, sy, 0.0, cy]
                   qz = [0.0, 0.0, sz, cz]
                   return quat_mul(quat_mul(qz, qx), qy)

               def make_colliders(entity):
                   result = []
                   for collider in entity.colliders or []:
                       if not collider.get("enabled", False):
                           continue
                       shape = str(collider.get("shape", "capsule")).lower()
                       world = collider.get("world")
                       if isinstance(world, dict):
                           world_shape = str(world.get("shape", shape)).lower()
                           if world_shape == "box" or world_shape == "capsule":
                               result.append(world)
                               continue
                       if shape == "box":
                           result.append(make_box(entity, collider))
                       elif shape == "mesh":
                           continue
                       else:
                           result.append(make_capsule(entity, collider))
                   return [item for item in result if item is not None]

               def make_capsule(entity):
                   collider = entity.collider or {}
                   return make_capsule(entity, collider)

               def make_capsule(entity, collider):
                   if not collider.get("enabled", False) or collider.get("shape", "capsule") != "capsule":
                       return None
                   axis_name = str(collider.get("axis", "y")).lower()
                   axis = [1, 0, 0] if axis_name == "x" else ([0, 0, 1] if axis_name == "z" else [0, 1, 0])
                   local_rotation = quat_from_degrees(collider.get("rotationDegrees", [0.0, 0.0, 0.0]))
                   world_rotation = quat_mul(local_rotation, entity.rotation)
                   axis_vector = transform_direction(axis, world_rotation, entity.scale)
                   axis_scale = max(length(axis_vector), 0.0001)
                   axis_direction = normalize(axis_vector)
                   radius_scale = max(abs(entity.scale[1]), abs(entity.scale[2])) if axis_name == "x" else (max(abs(entity.scale[0]), abs(entity.scale[1])) if axis_name == "z" else max(abs(entity.scale[0]), abs(entity.scale[2])))
                   radius = max(float(collider.get("radius", 0.5)) * radius_scale, 0.0001)
                   height = max(float(collider.get("height", 2.0)) * axis_scale, 0.0)
                   center = transform_point(collider.get("position", collider.get("center", [0.0, 1.0, 0.0])), entity.position, entity.rotation, entity.scale)
                   half_segment = max((height * 0.5) - radius, 0.0)
                   return {
                       "id": collider.get("id", ""),
                       "name": collider.get("name", ""),
                       "shape": "capsule",
                       "center": center,
                       "start": sub(center, mul(axis_direction, half_segment)),
                       "end": add(center, mul(axis_direction, half_segment)),
                       "radius": radius
                   }

               def make_box(entity, collider):
                   local_rotation = quat_from_degrees(collider.get("rotationDegrees", [0.0, 0.0, 0.0]))
                   world_rotation = quat_mul(local_rotation, entity.rotation)
                   size = collider.get("size", [1.0, 1.0, 1.0])
                   half_extents = [
                       max(abs(float(size[0]) * float(entity.scale[0])) * 0.5, 0.0001),
                       max(abs(float(size[1]) * float(entity.scale[1])) * 0.5, 0.0001),
                       max(abs(float(size[2]) * float(entity.scale[2])) * 0.5, 0.0001)
                   ]
                   return {
                       "id": collider.get("id", ""),
                       "name": collider.get("name", ""),
                       "shape": "box",
                       "center": transform_point(collider.get("position", [0.0, 0.5, 0.0]), entity.position, entity.rotation, entity.scale),
                       "axisX": normalize(rotate_vector([1, 0, 0], world_rotation), [1, 0, 0]),
                       "axisY": normalize(rotate_vector([0, 1, 0], world_rotation), [0, 1, 0]),
                       "axisZ": normalize(rotate_vector([0, 0, 1], world_rotation), [0, 0, 1]),
                       "halfExtents": half_extents
                   }

               def ray_capsule_distance(origin, direction, capsule):
                   direction = normalize(direction, [0, 0, -1])
                   closest, ray_t = closest_distance_ray_segment(origin, direction, capsule["start"], capsule["end"])
                   return ray_t if ray_t >= 0.0 and closest <= capsule["radius"] else None

               def ray_box_distance(origin, direction, box):
                   direction = normalize(direction, [0, 0, -1])
                   local_origin = to_box_local(origin, box)
                   local_direction = to_box_local_direction(direction, box)
                   return ray_aabb_distance(local_origin, local_direction, [-v for v in box["halfExtents"]], box["halfExtents"])

               def ray_aabb_distance(origin, direction, min_v, max_v):
                   t_min = 0.0
                   t_max = 1.0e30
                   for i in range(3):
                       if abs(direction[i]) < 0.000001:
                           if origin[i] < min_v[i] or origin[i] > max_v[i]:
                               return None
                       else:
                           inv = 1.0 / direction[i]
                           t1 = (min_v[i] - origin[i]) * inv
                           t2 = (max_v[i] - origin[i]) * inv
                           if t1 > t2:
                               t1, t2 = t2, t1
                           t_min = max(t_min, t1)
                           t_max = min(t_max, t2)
                           if t_min > t_max:
                               return None
                   return t_min

               def segment_aabb_intersects(start, end, min_v, max_v):
                   direction = sub(end, start)
                   t_min = 0.0
                   t_max = 1.0
                   for i in range(3):
                       if abs(direction[i]) < 0.000001:
                           if start[i] < min_v[i] or start[i] > max_v[i]:
                               return False
                       else:
                           inv = 1.0 / direction[i]
                           t1 = (min_v[i] - start[i]) * inv
                           t2 = (max_v[i] - start[i]) * inv
                           if t1 > t2:
                               t1, t2 = t2, t1
                           t_min = max(t_min, t1)
                           t_max = min(t_max, t2)
                           if t_min > t_max:
                               return False
                   return True

               def to_box_local(point, box):
                   delta = sub(point, box["center"])
                   return [dot(delta, box["axisX"]), dot(delta, box["axisY"]), dot(delta, box["axisZ"])]

               def to_box_local_direction(direction, box):
                   return [dot(direction, box["axisX"]), dot(direction, box["axisY"]), dot(direction, box["axisZ"])]

               def capsule_distance(left, right):
                   axis_distance = closest_distance_segment_segment(left["start"], left["end"], right["start"], right["end"])
                   return max(axis_distance - left["radius"] - right["radius"], 0.0)

               def collider_distance(left, right):
                   if left.get("shape") == "capsule" and right.get("shape") == "capsule":
                       return capsule_distance(left, right)
                   if left.get("shape") == "box" and right.get("shape") == "box":
                       return 0.0 if box_box_intersects(left, right) else max(length(sub(left["center"], right["center"])) - length(left["halfExtents"]) - length(right["halfExtents"]), 0.0)
                   capsule = right if right.get("shape") == "capsule" else left
                   box = left if left.get("shape") == "box" else right
                   expanded = [box["halfExtents"][0] + capsule["radius"], box["halfExtents"][1] + capsule["radius"], box["halfExtents"][2] + capsule["radius"]]
                   start = to_box_local(capsule["start"], box)
                   end = to_box_local(capsule["end"], box)
                   if segment_aabb_intersects(start, end, [-v for v in expanded], expanded):
                       return 0.0
                   return max(length(sub(capsule["center"], box["center"])) - capsule["radius"] - length(box["halfExtents"]), 0.0)

               def box_box_intersects(left, right):
                   left_axes = [left["axisX"], left["axisY"], left["axisZ"]]
                   right_axes = [right["axisX"], right["axisY"], right["axisZ"]]
                   left_extents = left["halfExtents"]
                   right_extents = right["halfExtents"]
                   rotation = [[dot(left_axes[i], right_axes[j]) for j in range(3)] for i in range(3)]
                   abs_rotation = [[abs(rotation[i][j]) + 0.00001 for j in range(3)] for i in range(3)]
                   delta = sub(right["center"], left["center"])
                   t = [dot(delta, left_axes[i]) for i in range(3)]

                   for i in range(3):
                       ra = left_extents[i]
                       rb = (right_extents[0] * abs_rotation[i][0]) + (right_extents[1] * abs_rotation[i][1]) + (right_extents[2] * abs_rotation[i][2])
                       if abs(t[i]) > ra + rb:
                           return False

                   for j in range(3):
                       ra = (left_extents[0] * abs_rotation[0][j]) + (left_extents[1] * abs_rotation[1][j]) + (left_extents[2] * abs_rotation[2][j])
                       rb = right_extents[j]
                       projection = abs((t[0] * rotation[0][j]) + (t[1] * rotation[1][j]) + (t[2] * rotation[2][j]))
                       if projection > ra + rb:
                           return False

                   for i in range(3):
                       for j in range(3):
                           ra = (left_extents[(i + 1) % 3] * abs_rotation[(i + 2) % 3][j]) + (left_extents[(i + 2) % 3] * abs_rotation[(i + 1) % 3][j])
                           rb = (right_extents[(j + 1) % 3] * abs_rotation[i][(j + 2) % 3]) + (right_extents[(j + 2) % 3] * abs_rotation[i][(j + 1) % 3])
                           projection = abs((t[(i + 2) % 3] * rotation[(i + 1) % 3][j]) - (t[(i + 1) % 3] * rotation[(i + 2) % 3][j]))
                           if projection > ra + rb:
                               return False

                   return True

               def closest_distance_ray_segment(origin, direction, segment_start, segment_end):
                   u = direction
                   v = sub(segment_end, segment_start)
                   w = sub(origin, segment_start)
                   a = dot(u, u)
                   b = dot(u, v)
                   c = dot(v, v)
                   d = dot(u, w)
                   e = dot(v, w)
                   denom = (a * c) - (b * b)
                   if denom < 0.000001:
                       s = 0.0
                       t = max(0.0, min(1.0, e / c)) if c > 0.000001 else 0.0
                   else:
                       s = ((b * e) - (c * d)) / denom
                       t = ((a * e) - (b * d)) / denom
                       if s < 0.0:
                           s = 0.0
                           t = max(0.0, min(1.0, e / c)) if c > 0.000001 else 0.0
                       elif t < 0.0:
                           t = 0.0
                           s = max(0.0, -d / a)
                       elif t > 1.0:
                           t = 1.0
                           s = max(0.0, (b - d) / a)
                   ray_point = add(origin, mul(u, s))
                   segment_point = add(segment_start, mul(v, t))
                   return length(sub(ray_point, segment_point)), max(0.0, s)

               def closest_distance_segment_segment(p1, q1, p2, q2):
                   d1 = sub(q1, p1)
                   d2 = sub(q2, p2)
                   r = sub(p1, p2)
                   a = dot(d1, d1)
                   e = dot(d2, d2)
                   f = dot(d2, r)
                   if a <= 0.000001 and e <= 0.000001:
                       return length(sub(p1, p2))
                   if a <= 0.000001:
                       s = 0.0
                       t = max(0.0, min(1.0, f / e))
                   else:
                       c = dot(d1, r)
                       if e <= 0.000001:
                           t = 0.0
                           s = max(0.0, min(1.0, -c / a))
                       else:
                           b = dot(d1, d2)
                           denom = (a * e) - (b * b)
                           s = max(0.0, min(1.0, ((b * f) - (c * e)) / denom)) if denom != 0.0 else 0.0
                           t = (b * s + f) / e
                           if t < 0.0:
                               t = 0.0
                               s = max(0.0, min(1.0, -c / a))
                           elif t > 1.0:
                               t = 1.0
                               s = max(0.0, min(1.0, (b - c) / a))
                   c1 = add(p1, mul(d1, s))
                   c2 = add(p2, mul(d2, t))
                   return length(sub(c1, c2))

               class Camera:
                   def __init__(self, data, commands):
                       self.position = data.get("position", [0, 0, 0])
                       self.target = data.get("target", [0, 0, -1])
                       self.forward = data.get("forward", [0, 0, -1])
                       self.up = data.get("up", [0, 1, 0])
                       self.right = data.get("right", [1, 0, 0])
                       self.main_camera = data.get("mainCamera", "")
                       self.camera_names = data.get("cameraNames", [])
                       self.render_texture_names = data.get("renderTextureNames", [])
                       self.control_mode = data.get("controlMode", "editor")
                       self.projection_mode = data.get("projectionMode", "perspective")
                       self.fov = data.get("fov", 45)
                       self.orthographic_size = data.get("orthographicSize", 5)
                       self.near_clip_plane = data.get("nearClipPlane", 0.1)
                       self.far_clip_plane = data.get("farClipPlane", 1000)
                       self.width = max(data.get("width", 1), 1)
                       self.height = max(data.get("height", 1), 1)
                       self._commands = commands

                   def set_control_mode(self, mode):
                       self._commands.append({"target": "camera", "action": "set_control_mode", "mode": mode})

                   def set_mode(self, mode):
                       self.set_control_mode(mode)

                   def configure_control(self, distance=None, height=None, shoulder_offset=None, smoothing=None, move_speed=None, mouse_sensitivity=None, safe_radius=None, auto_orbit_speed=None):
                       command = {"target": "camera", "action": "configure_control"}
                       if distance is not None:
                           command["distance"] = distance
                       if height is not None:
                           command["height"] = height
                       if shoulder_offset is not None:
                           command["shoulderOffset"] = shoulder_offset
                       if smoothing is not None:
                           command["smoothing"] = smoothing
                       if move_speed is not None:
                           command["moveSpeed"] = move_speed
                       if mouse_sensitivity is not None:
                           command["mouseSensitivity"] = mouse_sensitivity
                       if safe_radius is not None:
                           command["safeRadius"] = safe_radius
                       if auto_orbit_speed is not None:
                           command["autoOrbitSpeed"] = auto_orbit_speed
                       self._commands.append(command)

                   def set_mouse_look(self, enabled, require_right_mouse=True):
                       self._commands.append({"target": "camera", "action": "set_mouse_look", "flag": bool(enabled), "requireRightMouse": bool(require_right_mouse)})

                   def set_yaw_pitch(self, yaw, pitch):
                       self._commands.append({"target": "camera", "action": "set_yaw_pitch", "yaw": yaw, "pitch": pitch})

                   def set_look_at(self, px, py, pz, tx, ty, tz):
                       self._commands.append({"target": "camera", "action": "set_look_at", "x": px, "y": py, "z": pz, "targetX": tx, "targetY": ty, "targetZ": tz})

                   def set_main_camera(self, camera_name):
                       self._commands.append({"target": "camera", "action": "set_main_camera", "name": camera_name})

                   def set_camera_look_at(self, camera_name, px, py, pz, tx, ty, tz):
                       self._commands.append({"target": "camera", "action": "set_camera_look_at", "name": camera_name, "x": px, "y": py, "z": pz, "targetX": tx, "targetY": ty, "targetZ": tz})

                   def bind_render_texture_camera(self, render_texture_name, camera_name):
                       self._commands.append({"target": "camera", "action": "bind_render_texture_camera", "name": render_texture_name, "camera": camera_name})

                   def set_camera_viewport(self, camera_name, x, y, width, height, layout_mode="relative"):
                       self._commands.append({"target": "camera", "action": "set_camera_viewport", "name": camera_name, "x": x, "y": y, "width": width, "height": height, "mode": layout_mode})

                   def enable_camera_viewport(self, camera_name, enabled=True):
                       self._commands.append({"target": "camera", "action": "enable_camera_viewport", "name": camera_name, "flag": bool(enabled)})

                   def render_texture(self, name):
                       return render_texture(name)

                   def use_custom_mode(self):
                       self.set_control_mode("custom")

                   def use_editor_orbit_mode(self, orbit_sensitivity=0.2, pan_sensitivity=1.0, zoom_sensitivity=1.0):
                       self._commands.append({"target": "camera", "action": "use_editor_orbit_mode", "orbitSensitivity": orbit_sensitivity, "panSensitivity": pan_sensitivity, "zoomSensitivity": zoom_sensitivity})

                   def use_max_editor_mode(self, orbit_sensitivity=0.2, pan_sensitivity=1.0, zoom_sensitivity=1.0):
                       self.use_editor_orbit_mode(orbit_sensitivity, pan_sensitivity, zoom_sensitivity)

                   def use_tps_mode(self, target, distance=5.0, height=1.5, shoulder_offset=0.0, smoothing=12.0):
                       self._commands.append({"target": "camera", "action": "use_tps_mode", "targetEntity": target, "distance": distance, "height": height, "shoulderOffset": shoulder_offset, "smoothing": smoothing})

                   def use_third_person_mode(self, target, distance=5.0, height=1.5, shoulder_offset=0.0, smoothing=12.0):
                       self.use_tps_mode(target, distance, height, shoulder_offset, smoothing)

                   def use_shoulder_mode(self, target, distance=4.0, height=1.6, shoulder_offset=0.55, smoothing=12.0):
                       self._commands.append({"target": "camera", "action": "use_shoulder_mode", "targetEntity": target, "distance": distance, "height": height, "shoulderOffset": shoulder_offset, "smoothing": smoothing})

                   def use_lock_on_mode(self, subject, target, distance=5.0, height=1.6, smoothing=12.0, safe_radius=0.25, shoulder_offset=0.0):
                       self._commands.append({"target": "camera", "action": "use_lock_on_mode", "subjectEntity": subject, "targetEntity": target, "distance": distance, "height": height, "smoothing": smoothing, "safeRadius": safe_radius, "shoulderOffset": shoulder_offset})

                   def use_fps_mode(self, target, eye_height=1.65, smoothing=18.0):
                       self._commands.append({"target": "camera", "action": "use_fps_mode", "targetEntity": target, "height": eye_height, "smoothing": smoothing})

                   def use_first_person_mode(self, target, eye_height=1.65, smoothing=18.0):
                       self.use_fps_mode(target, eye_height, smoothing)

                   def use_fps_control_mode(self, target, eye_height=1.65, smoothing=18.0, mouse_sensitivity=0.15):
                       self._commands.append({"target": "camera", "action": "use_fps_control_mode", "targetEntity": target, "height": eye_height, "smoothing": smoothing, "mouseSensitivity": mouse_sensitivity})

                   def use_locked_fps_mode(self, target, eye_height=1.65, smoothing=18.0, mouse_sensitivity=0.15):
                       self.use_fps_control_mode(target, eye_height, smoothing, mouse_sensitivity)

                   def use_free_fly_mode(self, move_speed=5.0, mouse_sensitivity=0.15):
                       self._commands.append({"target": "camera", "action": "use_free_fly_mode", "moveSpeed": move_speed, "mouseSensitivity": mouse_sensitivity})

                   def use_rts_mode(self, height=12.0, pitch=55.0, move_speed=8.0):
                       self._commands.append({"target": "camera", "action": "use_rts_mode", "height": height, "pitch": pitch, "moveSpeed": move_speed})

                   def use_top_down_mode(self, target="", height=12.0, smoothing=12.0):
                       self._commands.append({"target": "camera", "action": "use_top_down_mode", "targetEntity": target, "height": height, "smoothing": smoothing})

                   def use_isometric_mode(self, target="", distance=12.0, height=0.0, smoothing=12.0):
                       self._commands.append({"target": "camera", "action": "use_isometric_mode", "targetEntity": target, "distance": distance, "height": height, "smoothing": smoothing})

                   def use_side_scroller_mode(self, target, distance=10.0, height=1.5, smoothing=12.0):
                       self._commands.append({"target": "camera", "action": "use_side_scroller_mode", "targetEntity": target, "distance": distance, "height": height, "smoothing": smoothing})

                   def use_fixed_mode(self, px, py, pz, tx, ty, tz):
                       self._commands.append({"target": "camera", "action": "use_fixed_mode", "x": px, "y": py, "z": pz, "targetX": tx, "targetY": ty, "targetZ": tz})

                   def use_cinematic_follow_mode(self, target, offset_x=0.0, offset_y=1.8, offset_z=5.0, look_height=1.5, smoothing=8.0):
                       self._commands.append({"target": "camera", "action": "use_cinematic_follow_mode", "targetEntity": target, "offsetX": offset_x, "offsetY": offset_y, "offsetZ": offset_z, "height": look_height, "smoothing": smoothing})

                   def use_orbital_follow_mode(self, target, distance=6.0, height=1.5, yaw_speed=30.0, smoothing=12.0):
                       self._commands.append({"target": "camera", "action": "use_orbital_follow_mode", "targetEntity": target, "distance": distance, "height": height, "autoOrbitSpeed": yaw_speed, "smoothing": smoothing})

                   def orbit(self, delta_yaw, delta_pitch):
                       self._commands.append({"target": "camera", "action": "orbit", "yaw": delta_yaw, "pitch": delta_pitch})

                   def pan(self, delta_x, delta_y):
                       self._commands.append({"target": "camera", "action": "pan", "x": delta_x, "y": delta_y})

                   def dolly(self, delta):
                       self._commands.append({"target": "camera", "action": "dolly", "value": delta})

                   def screen_point_to_ray(self, x, y):
                       return self.viewport_point_to_ray(x / self.width, y / self.height)

                   def viewport_point_to_ray(self, viewport_x, viewport_y):
                       ndc_x = (viewport_x * 2.0) - 1.0
                       ndc_y = 1.0 - (viewport_y * 2.0)
                       aspect = self.width / self.height
                       if self.projection_mode == "orthographic":
                           origin = [
                               self.position[0] + (self.right[0] * ndc_x * self.orthographic_size * aspect) + (self.up[0] * ndc_y * self.orthographic_size),
                               self.position[1] + (self.right[1] * ndc_x * self.orthographic_size * aspect) + (self.up[1] * ndc_y * self.orthographic_size),
                               self.position[2] + (self.right[2] * ndc_x * self.orthographic_size * aspect) + (self.up[2] * ndc_y * self.orthographic_size)
                           ]
                           return Ray(origin, self.forward)

                       import math
                       tan_half_fov = math.tan(math.radians(self.fov) * 0.5)
                       direction = [
                           self.forward[0] + (self.right[0] * ndc_x * aspect * tan_half_fov) + (self.up[0] * ndc_y * tan_half_fov),
                           self.forward[1] + (self.right[1] * ndc_x * aspect * tan_half_fov) + (self.up[1] * ndc_y * tan_half_fov),
                           self.forward[2] + (self.right[2] * ndc_x * aspect * tan_half_fov) + (self.up[2] * ndc_y * tan_half_fov)
                       ]
                       length = max(((direction[0] * direction[0]) + (direction[1] * direction[1]) + (direction[2] * direction[2])) ** 0.5, 0.00001)
                       direction = [direction[0] / length, direction[1] / length, direction[2] / length]
                       return Ray(self.position, direction)

                   def mouse_point_to_ray(self, input):
                       return self.screen_point_to_ray(input.mouse_x, input.mouse_y)

                   def touch_point_to_ray(self, touch):
                       return self.screen_point_to_ray(touch.x, touch.y)

               class Scene:
                   def __init__(self, data, commands):
                       self.name = data.get("name", "")
                       self.main_camera = data.get("mainCamera", "")
                       self.camera_names = data.get("cameraNames", [])
                       self.render_texture_names = data.get("renderTextureNames", [])
                       self._entities = data.get("entities", [])
                       self._gui_controls = data.get("guiControls", [])
                       self._sprites = data.get("sprites", [])
                       self._llm = data.get("llm", {})
                       self._asr = data.get("asr", {})
                       self._realtime_voice = data.get("realtimeVoice", {})
                       self._bubble = data.get("bubble", {})
                       self.performance = Performance(data.get("performance", {}))
                       self.fps = self.performance.fps
                       self.raw_fps = self.performance.raw_fps
                       self.delta_seconds = self.performance.delta_seconds
                       self.frame_count = self.performance.frame_count
                       self.window = Window(data.get("window", {}), commands)
                       self.runtime = Runtime(data.get("runtime", {}), commands)
                       self.camera = Camera(data.get("camera", {}), commands)
                       self.physics = Physics(self)
                       self.debug = Debug(commands)
                       self.save = SaveStore()
                       self.network = Network()
                       self.asr = AsrClient(self, commands)
                       self.realtime_voice = RealtimeVoiceClient(self, commands)
                       self.bubble = BubbleManager(self, self._bubble, commands)
                       self._commands = commands

                   def get_entity(self, id_or_name):
                       for item in self._entities:
                           if item.get("id") == id_or_name or item.get("name") == id_or_name:
                               return Entity(item, self._commands)
                       return None

                   def get_gui_control(self, id_or_name):
                       for item in self._gui_controls:
                           if item.get("id") == id_or_name or item.get("name") == id_or_name:
                               return GuiControl(item, self._commands)
                       return None

                   def get_sprite(self, id_or_name):
                       for item in self._sprites:
                           if item.get("id") == id_or_name or item.get("name") == id_or_name:
                               return Sprite(self, item, self._commands)
                       return None

                   def load_scene(self, scene_path):
                       self._commands.append({"target": "scene", "action": "load_scene", "path": scene_path})

                   def render_texture(self, name):
                       return render_texture(name)

                   @property
                   def llm(self):
                       return LlmClient(self, self._commands)

                   def flush(self):
                       if not self._commands:
                           return
                       pending = list(self._commands)
                       self._commands.clear()
                       emit_commands(pending)

               class BubbleManager:
                   def __init__(self, scene, data, commands):
                       self._scene = scene
                       self._data = dict(data or {})
                       self._commands = commands

                   @property
                   def count(self):
                       return int(self._data.get("count", len(self.names)))

                   @property
                   def names(self):
                       return list(self._data.get("names", []))

                   @property
                   def visible_names(self):
                       return list(self._data.get("visibleNames", []))

                   def contains(self, name):
                       bubble_name = str(name or "").strip()
                       return bubble_name in self.names

                   def get(self, name):
                       bubble_name = str(name or "").strip()
                       if not bubble_name:
                           raise ValueError("Bubble name is required")
                       self._track_name(bubble_name)
                       return Bubble(self, bubble_name)

                   def get_or_create(self, name):
                       return self.get(name)

                   def create(self, name):
                       return self.get(name)

                   def show(self, name, text="", header_text="", footer_text=""):
                       bubble = self.get(name)
                       bubble.show(text=text, header_text=header_text, footer_text=footer_text)
                       return bubble

                   def hide(self, name):
                       self.get(name).hide()

                   def remove(self, name):
                       bubble_name = str(name or "").strip()
                       if not bubble_name:
                           return
                       self._commands.append({"target": "bubble", "action": "remove", "name": bubble_name})
                       self._data["names"] = [item for item in self.names if item != bubble_name]
                       self._data["visibleNames"] = [item for item in self.visible_names if item != bubble_name]
                       self._data["count"] = len(self._data["names"])

                   def hide_all(self):
                       self._commands.append({"target": "bubble", "action": "hide_all"})
                       self._data["visibleNames"] = []

                   def clear(self):
                       self._commands.append({"target": "bubble", "action": "clear"})
                       self._data["names"] = []
                       self._data["visibleNames"] = []
                       self._data["count"] = 0

                   def _track_name(self, name, visible=None):
                       names = self._data.setdefault("names", [])
                       if name not in names:
                           names.append(name)

                       visible_names = self._data.setdefault("visibleNames", [])
                       if visible is True and name not in visible_names:
                           visible_names.append(name)
                       elif visible is False:
                           self._data["visibleNames"] = [item for item in visible_names if item != name]

                       self._data["count"] = len(names)

               class Bubble:
                   def __init__(self, manager, name):
                       self._manager = manager
                       self.name = name

                   def show(self, text="", header_text="", footer_text=""):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "show",
                           "name": self.name,
                           "text": "" if text is None else str(text),
                           "headerText": "" if header_text is None else str(header_text),
                           "footerText": "" if footer_text is None else str(footer_text)
                       })
                       self._manager._track_name(self.name, visible=True)
                       return self

                   def hide(self):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "hide",
                           "name": self.name
                       })
                       self._manager._track_name(self.name, visible=False)
                       return self

                   def remove(self):
                       self._manager.remove(self.name)

                   def set_visible(self, value):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_visible",
                           "name": self.name,
                           "flag": bool(value)
                       })
                       self._manager._track_name(self.name, visible=bool(value))
                       return self

                   def set_text(self, text):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_text",
                           "name": self.name,
                           "text": "" if text is None else str(text)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_header_text(self, text):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_header_text",
                           "name": self.name,
                           "headerText": "" if text is None else str(text)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_footer_text(self, text):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_footer_text",
                           "name": self.name,
                           "footerText": "" if text is None else str(text)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_layout_mode(self, layout_mode):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_layout_mode",
                           "name": self.name,
                           "mode": str(layout_mode or "absolute")
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_anchor_mode(self, anchor_mode):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_anchor_mode",
                           "name": self.name,
                           "mode": str(anchor_mode or "screen")
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_screen_position(self, x, y, layout_mode=None):
                       payload = {
                           "target": "bubble",
                           "action": "set_screen_position",
                           "name": self.name,
                           "x": float(x),
                           "y": float(y)
                       }
                       if layout_mode is not None:
                           payload["mode"] = str(layout_mode)
                       self._manager._commands.append(payload)
                       self._manager._track_name(self.name)
                       return self

                   def use_screen_space(self, x, y, layout_mode=None):
                       return self.set_screen_position(x, y, layout_mode=layout_mode)

                   def set_screen_offset(self, x, y):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_screen_offset",
                           "name": self.name,
                           "x": float(x),
                           "y": float(y)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_world_position(self, x, y, z):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_world_position",
                           "name": self.name,
                           "x": float(x),
                           "y": float(y),
                           "z": float(z)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def use_world_space(self, x, y, z):
                       return self.set_world_position(x, y, z)

                   def set_world_offset(self, x, y, z):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_world_offset",
                           "name": self.name,
                           "x": float(x),
                           "y": float(y),
                           "z": float(z)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def attach_to_entity(self, entity_id_or_name, use_model_top_anchor=True):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "attach_to_entity",
                           "name": self.name,
                           "targetEntity": str(entity_id_or_name or ""),
                           "flag": bool(use_model_top_anchor)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_width(self, width):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_width",
                           "name": self.name,
                           "width": float(width)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_padding(self, x, y):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_padding",
                           "name": self.name,
                           "x": float(x),
                           "y": float(y)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_pivot(self, x, y):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_pivot",
                           "name": self.name,
                           "x": float(x),
                           "y": float(y)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_text_alignment(self, alignment):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_text_alignment",
                           "name": self.name,
                           "mode": str(alignment or "left")
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_font_size(self, value):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_font_size",
                           "name": self.name,
                           "value": float(value)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_header_font_size(self, value):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_header_font_size",
                           "name": self.name,
                           "value": float(value)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_footer_font_size(self, value):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_footer_font_size",
                           "name": self.name,
                           "value": float(value)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_background_color(self, r, g, b, a=1.0):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_background_color",
                           "name": self.name,
                           "colorR": float(r),
                           "colorG": float(g),
                           "colorB": float(b),
                           "colorA": float(a)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_border_color(self, r, g, b, a=1.0):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_border_color",
                           "name": self.name,
                           "colorR": float(r),
                           "colorG": float(g),
                           "colorB": float(b),
                           "colorA": float(a)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_text_color(self, r, g, b, a=1.0):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_text_color",
                           "name": self.name,
                           "colorR": float(r),
                           "colorG": float(g),
                           "colorB": float(b),
                           "colorA": float(a)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_header_text_color(self, r, g, b, a=1.0):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_header_text_color",
                           "name": self.name,
                           "colorR": float(r),
                           "colorG": float(g),
                           "colorB": float(b),
                           "colorA": float(a)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_footer_text_color(self, r, g, b, a=1.0):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_footer_text_color",
                           "name": self.name,
                           "colorR": float(r),
                           "colorG": float(g),
                           "colorB": float(b),
                           "colorA": float(a)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_rounding(self, value):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_rounding",
                           "name": self.name,
                           "value": float(value)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_border_thickness(self, value):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_border_thickness",
                           "name": self.name,
                           "value": float(value)
                       })
                       self._manager._track_name(self.name)
                       return self

                   def set_draw_order(self, value):
                       self._manager._commands.append({
                           "target": "bubble",
                           "action": "set_draw_order",
                           "name": self.name,
                           "index": int(value)
                       })
                       self._manager._track_name(self.name)
                       return self

               class Network:
                   def http_get(self, url, timeout=15, headers=None):
                       return self.http_send("GET", url, timeout=timeout, headers=headers)

                   def http_post_text(self, url, text, content_type="text/plain; charset=utf-8", timeout=15, headers=None):
                       return self.http_send("POST", url, body="" if text is None else str(text), content_type=content_type, timeout=timeout, headers=headers)

                   def http_post_json(self, url, value, timeout=15, headers=None):
                       return self.http_send("POST", url, body=json.dumps(value, ensure_ascii=False), content_type="application/json; charset=utf-8", timeout=timeout, headers=headers)

                   def http_send(self, method, url, body=None, content_type=None, timeout=15, headers=None):
                       method = str(method or "GET").upper()
                       parsed_url = str(url or "")
                       if not parsed_url.startswith(("http://", "https://")):
                           raise ValueError("URL must start with http:// or https://")
                       data = None
                       request_headers = dict(headers or {})
                       if body is not None:
                           data = str(body).encode("utf-8")
                           if content_type:
                               request_headers["Content-Type"] = str(content_type)
                       request = urllib.request.Request(parsed_url, data=data, headers=request_headers, method=method)
                       try:
                           with urllib.request.urlopen(request, timeout=max(1, int(timeout or 15))) as response:
                               raw = response.read()
                               return {
                                   "status_code": int(response.status),
                                   "is_success_status_code": 200 <= int(response.status) <= 299,
                                   "reason_phrase": getattr(response, "reason", "") or "",
                                   "body": raw.decode("utf-8", errors="replace"),
                                   "headers": dict(response.headers)
                               }
                       except urllib.error.HTTPError as ex:
                           raw = ex.read()
                           return {
                               "status_code": int(ex.code),
                               "is_success_status_code": False,
                               "reason_phrase": getattr(ex, "reason", "") or "",
                               "body": raw.decode("utf-8", errors="replace"),
                               "headers": dict(ex.headers)
                           }

                   def tcp_send_text(self, host, port, text, timeout=5, encoding="utf-8", receive_bytes=65536):
                       data = self.tcp_send(host, port, ("" if text is None else str(text)).encode(encoding), timeout=timeout, receive_bytes=receive_bytes)
                       return data.decode(encoding, errors="replace")

                   def tcp_send(self, host, port, data, timeout=5, receive_bytes=65536):
                       payload = bytes(data or b"")
                       with socket.create_connection((str(host), int(port)), timeout=max(1, float(timeout or 5))) as sock:
                           sock.settimeout(max(1, float(timeout or 5)))
                           if payload:
                               sock.sendall(payload)
                           if receive_bytes is None or int(receive_bytes) <= 0:
                               return b""
                           return sock.recv(max(1, int(receive_bytes)))

                   def tcp_receive_text_once(self, port, timeout=10, encoding="utf-8", receive_bytes=65536, listen_address="0.0.0.0"):
                       message = self.tcp_receive_once(port, timeout=timeout, receive_bytes=receive_bytes, listen_address=listen_address)
                       message["text"] = message["data"].decode(encoding, errors="replace")
                       return message

                   def tcp_receive_once(self, port, timeout=10, receive_bytes=65536, listen_address="0.0.0.0"):
                       with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server:
                           server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                           server.settimeout(max(1, float(timeout or 10)))
                           server.bind((str(listen_address or "0.0.0.0"), int(port)))
                           server.listen(1)
                           conn, address = server.accept()
                           with conn:
                               conn.settimeout(max(1, float(timeout or 10)))
                               data = conn.recv(max(1, int(receive_bytes or 65536)))
                               return {
                                   "text": data.decode("utf-8", errors="replace"),
                                   "data": data,
                                   "remote_host": str(address[0]),
                                   "remote_port": int(address[1])
                               }

                   def udp_send_text(self, host, port, text, timeout=5, encoding="utf-8", receive_bytes=65536, wait_for_response=True):
                       data = self.udp_send(host, port, ("" if text is None else str(text)).encode(encoding), timeout=timeout, receive_bytes=receive_bytes, wait_for_response=wait_for_response)
                       return data.decode(encoding, errors="replace")

                   def udp_send(self, host, port, data, timeout=5, receive_bytes=65536, wait_for_response=True):
                       payload = bytes(data or b"")
                       with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
                           sock.settimeout(max(1, float(timeout or 5)))
                           sock.sendto(payload, (str(host), int(port)))
                           if not wait_for_response or receive_bytes is None or int(receive_bytes) <= 0:
                               return b""
                           data, _ = sock.recvfrom(max(1, int(receive_bytes)))
                           return data

                   def udp_receive_text(self, port, timeout=10, encoding="utf-8", receive_bytes=65536, listen_address="0.0.0.0"):
                       message = self.udp_receive(port, timeout=timeout, receive_bytes=receive_bytes, listen_address=listen_address)
                       message["text"] = message["data"].decode(encoding, errors="replace")
                       return message

                   def udp_receive(self, port, timeout=10, receive_bytes=65536, listen_address="0.0.0.0"):
                       with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
                           sock.settimeout(max(1, float(timeout or 10)))
                           sock.bind((str(listen_address or "0.0.0.0"), int(port)))
                           data, address = sock.recvfrom(max(1, int(receive_bytes or 65536)))
                           return {
                               "text": data.decode("utf-8", errors="replace"),
                               "data": data,
                               "remote_host": str(address[0]),
                               "remote_port": int(address[1])
                           }

               class Performance:
                   def __init__(self, data):
                       self.fps = data.get("fps", 0.0)
                       self.raw_fps = data.get("rawFps", 0.0)
                       self.delta_seconds = data.get("deltaSeconds", 0.0)
                       self.total_seconds = data.get("totalSeconds", 0.0)
                       self.frame_count = data.get("frameCount", 0)

               class LlmClient:
                   def __init__(self, scene, commands):
                       self._scene = scene
                       self._commands = commands
                       self._settings = scene._llm

                   @property
                   def enabled(self):
                       return bool(self._settings.get("enabled", False))

                   @property
                   def model(self):
                       return self._settings.get("model", "")

                   @property
                   def skills_enabled(self):
                       return bool(self._settings.get("skillsEnabled", False))

                   @property
                   def memory_enabled(self):
                       return bool(self._settings.get("memoryEnabled", False))

                   @property
                   def skills_directory(self):
                       return self._settings.get("skillsDirectory", "")

                   @property
                   def memory_directory(self):
                       return self._settings.get("memoryDirectory", "")

                   def get_character_memory_path(self, entity_or_name):
                       name = entity_or_name
                       if hasattr(entity_or_name, "name"):
                           name = entity_or_name.name
                       raw = str(name or "character").strip() or "character"
                       chars = []
                       previous_dash = False
                       for ch in raw:
                           if ch.isalnum() or ch in "_-":
                               chars.append(ch)
                               previous_dash = False
                           elif ch.isspace() or ch in "./\\:;":
                               if chars and not previous_dash:
                                   chars.append("-")
                                   previous_dash = True
                       safe = "".join(chars).strip("-") or "character"
                       return "character/" + safe + ".md"

                   def chat(self, text, system_prompt=None, model=None, temperature=None):
                       result = ""
                       for update in self.stream_chat(text, system_prompt=system_prompt, model=model, temperature=temperature):
                           result = update["accumulated_text"]
                       return result

                   def stream_chat(self, text, system_prompt=None, model=None, temperature=None):
                       messages = []
                       if system_prompt:
                           messages.append({"role": "system", "content": str(system_prompt)})
                       messages.append({"role": "user", "content": str(text)})
                       yield from self.stream_messages(messages, model=model, temperature=temperature)

                   def stream_messages(self, messages, model=None, temperature=None):
                       if not self.enabled:
                           raise RuntimeError("Project LLM is disabled")
                       model_name = model or self._settings.get("model", "")
                       if not model_name:
                           raise RuntimeError("Project LLM model is required")
                       api_key = self._settings.get("apiKey", "") or get_env(self._settings.get("apiKeyEnvironmentVariable", ""))
                       payload = {
                           "model": model_name,
                           "messages": messages,
                           "stream": True
                       }
                       if temperature is not None:
                           payload["temperature"] = float(temperature)
                       elif self._settings.get("defaultTemperature") is not None:
                           payload["temperature"] = float(self._settings.get("defaultTemperature"))
                       accumulated = ""
                       url = combine_url(self._settings.get("baseUrl", ""), self._settings.get("chatCompletionsPath", "/v1/chat/completions"))
                       for chunk in read_openai_sse(url, api_key, payload, self._settings.get("timeoutSeconds", 300)):
                           delta = chunk.get("delta", "")
                           accumulated += delta
                           yield {
                               "delta": delta,
                               "accumulated_text": accumulated,
                               "is_final": bool(chunk.get("is_final", False))
                           }

                   def tool(self, name, description, parameters_json_schema, callback):
                       schema = parameters_json_schema
                       if not isinstance(schema, str):
                           schema = json.dumps(schema or {"type": "object", "properties": {}}, ensure_ascii=False, separators=(",", ":"))
                       return {
                           "name": str(name or ""),
                           "description": str(description or ""),
                           "parametersJsonSchema": schema,
                           "callback": str(callback or "")
                       }

                   def start_chat(self, text, system_prompt=None, model=None, temperature=None, request_id=None, on_delta="llm_delta", on_completed="llm_completed", on_error="llm_error"):
                       self._commands.append({
                           "target": "llm",
                           "action": "start_chat",
                           "text": text,
                           "systemPrompt": system_prompt or "",
                           "model": model or "",
                           "temperature": temperature,
                           "requestId": request_id or "",
                           "onDelta": on_delta or "",
                           "onCompleted": on_completed or "",
                           "onError": on_error or ""
                       })

                   def start_chat_with_tools(self, text, tools, system_prompt=None, model=None, temperature=None, request_id=None, on_delta="llm_delta", on_completed="llm_completed", on_error="llm_error", on_tool_call="llm_tool_call", on_tool_result="llm_tool_result", max_tool_rounds=4):
                       normalized_tools = []
                       for tool in tools or []:
                           if isinstance(tool, dict):
                               normalized_tools.append(tool)
                       self._commands.append({
                           "target": "llm",
                           "action": "start_chat_with_tools",
                           "text": text,
                           "systemPrompt": system_prompt or "",
                           "model": model or "",
                           "temperature": temperature,
                           "requestId": request_id or "",
                           "onDelta": on_delta or "",
                           "onCompleted": on_completed or "",
                           "onError": on_error or "",
                           "onToolCall": on_tool_call or "",
                           "onToolResult": on_tool_result or "",
                           "maxToolRounds": int(max_tool_rounds or 4),
                           "tools": normalized_tools
                       })

                   def cancel_request(self, request_id):
                       self._commands.append({
                           "target": "llm",
                           "action": "cancel_request",
                           "requestId": request_id or ""
                       })

                   def cancel_all_requests(self):
                       self._commands.append({
                           "target": "llm",
                           "action": "cancel_all_requests"
                       })

               class RealtimeVoiceClient:
                   def __init__(self, scene, commands):
                       self._scene = scene
                       self._commands = commands
                       self._settings = scene._realtime_voice

                   @property
                   def enabled(self):
                       return bool(self._settings.get("enabled", False))

                   @property
                   def base_url(self):
                       return self._settings.get("baseUrl", "")

                   @property
                   def model(self):
                       return self._settings.get("model", "")

                   @property
                   def voice(self):
                       return self._settings.get("voice", "")

                   @property
                   def wake_word_enabled(self):
                       return bool(self._settings.get("wakeWordEnabled", False))

                   @property
                   def wake_words(self):
                       return list(self._settings.get("wakeWords", []))

                   @property
                   def input_device_index(self):
                       return self._settings.get("inputDeviceIndex", None)

                   @property
                   def microphone_input_available(self):
                       return bool(self._settings.get("microphoneInputAvailable", False))

                   @property
                   def microphone_unavailable_reason(self):
                       return self._settings.get("microphoneUnavailableReason", "")

                   def start_wake_word_monitoring(self, on_detected="wake_word_detected", on_error="wake_word_error"):
                       self._commands.append({
                           "target": "realtime_voice",
                           "action": "start_wake_word_monitoring",
                           "onCompleted": on_detected or "",
                           "onError": on_error or ""
                       })

                   def stop_wake_word_monitoring(self):
                       self._commands.append({
                           "target": "realtime_voice",
                           "action": "stop_wake_word_monitoring"
                       })

                   def start_transcription(self, request_id=None, timeout_seconds=None, on_completed="realtime_voice_transcription_completed", on_timeout="realtime_voice_timeout", on_error="realtime_voice_error"):
                       self._commands.append({
                           "target": "realtime_voice",
                           "action": "start_transcription",
                           "requestId": request_id or "",
                           "timeoutSeconds": timeout_seconds,
                           "onCompleted": on_completed or "",
                           "onTimeout": on_timeout or "",
                           "onError": on_error or ""
                       })

                   def start_response(self, user_text, request_id=None, on_delta="realtime_voice_delta", on_completed="realtime_voice_completed", on_error="realtime_voice_error"):
                       self._commands.append({
                           "target": "realtime_voice",
                           "action": "start_response",
                           "text": user_text or "",
                           "requestId": request_id or "",
                           "onDelta": on_delta or "",
                           "onCompleted": on_completed or "",
                           "onError": on_error or ""
                       })

                   def start_voice_turn(self, request_id=None, timeout_seconds=30, on_transcription_completed="realtime_voice_transcription_completed", on_delta="realtime_voice_delta", on_completed="realtime_voice_completed", on_timeout="realtime_voice_timeout", on_error="realtime_voice_error"):
                       self._commands.append({
                           "target": "realtime_voice",
                           "action": "start_voice_turn",
                           "requestId": request_id or "",
                           "timeoutSeconds": timeout_seconds,
                           "callback": on_transcription_completed or "",
                           "onDelta": on_delta or "",
                           "onCompleted": on_completed or "",
                           "onTimeout": on_timeout or "",
                           "onError": on_error or ""
                       })

                   def start_speak_text(self, text, speed=None, request_id=None, on_completed="realtime_voice_speech_completed", on_error="realtime_voice_error"):
                       self._commands.append({
                           "target": "realtime_voice",
                           "action": "start_speak_text",
                           "text": text or "",
                           "speed": speed,
                           "requestId": request_id or "",
                           "onCompleted": on_completed or "",
                           "onError": on_error or ""
                       })

                   def reset_conversation(self):
                       self._commands.append({
                           "target": "realtime_voice",
                           "action": "reset_conversation"
                       })

                   def cancel_request(self, request_id):
                       self._commands.append({
                           "target": "realtime_voice",
                           "action": "cancel_request",
                           "requestId": request_id or ""
                       })

                   def cancel_all_requests(self):
                       self._commands.append({
                           "target": "realtime_voice",
                           "action": "cancel_all_requests"
                       })

               class AsrClient:
                   def __init__(self, scene, commands):
                       self._scene = scene
                       self._commands = commands
                       self._settings = scene._asr

                   @property
                   def enabled(self):
                       return bool(self._settings.get("enabled", False))

                   @property
                   def provider(self):
                       return self._settings.get("provider", "")

                   @property
                   def input_device_index(self):
                       return self._settings.get("inputDeviceIndex", None)

                   @property
                   def partial_result_interval_seconds(self):
                       return float(self._settings.get("partialResultIntervalSeconds", 0.75))

                   @property
                   def is_recording(self):
                       return bool(self._settings.get("isRecording", False))

                   @property
                   def is_wake_word_monitoring(self):
                       return bool(self._settings.get("isWakeWordMonitoring", False))

                   @property
                   def microphone_input_available(self):
                       return bool(self._settings.get("microphoneInputAvailable", False))

                   @property
                   def microphone_unavailable_reason(self):
                       return self._settings.get("microphoneUnavailableReason", "")

                   def start_streaming_recognition(self, request_id=None, on_partial="asr_partial", on_completed="asr_completed", on_error="asr_error"):
                       self._commands.append({
                           "target": "asr",
                           "action": "start_streaming_recognition",
                           "requestId": request_id or "",
                           "onPartial": on_partial or "",
                           "onCompleted": on_completed or "",
                           "onError": on_error or ""
                       })

                   def stop_streaming_recognition(self, request_id=None):
                       self._commands.append({
                           "target": "asr",
                           "action": "stop_streaming_recognition",
                           "requestId": request_id or ""
                       })

                   def start_wake_word_monitoring(self, wake_words, request_id=None, chunk_duration_seconds=None, extension_duration_seconds=None, trailing_silence_padding_seconds=None, on_detected="asr_wake_word_detected", on_error="asr_wake_word_error"):
                       if isinstance(wake_words, str):
                           resolved_wake_words = [wake_words]
                       else:
                           resolved_wake_words = list(wake_words or [])
                       self._commands.append({
                           "target": "asr",
                           "action": "start_wake_word_monitoring",
                           "requestId": request_id or "",
                           "wakeWords": resolved_wake_words,
                           "chunkDurationSeconds": chunk_duration_seconds,
                           "extensionDurationSeconds": extension_duration_seconds,
                           "trailingSilencePaddingSeconds": trailing_silence_padding_seconds,
                           "onCompleted": on_detected or "",
                           "onError": on_error or ""
                       })

                   def stop_wake_word_monitoring(self):
                       self._commands.append({
                           "target": "asr",
                           "action": "stop_wake_word_monitoring"
                       })

               class SaveStore:
                   @property
                   def directory(self):
                       return save_directory

                   def write_text(self, file_name, text):
                       path = resolve_save_path(file_name)
                       os.makedirs(os.path.dirname(path), exist_ok=True)
                       with open(path, "w", encoding="utf-8") as f:
                           f.write("" if text is None else str(text))

                   def read_text(self, file_name, fallback=""):
                       path = resolve_save_path(file_name)
                       if not os.path.exists(path):
                           return fallback
                       with open(path, "r", encoding="utf-8") as f:
                           return f.read()

                   def write_json(self, file_name, value):
                       self.write_text(file_name, json.dumps(value, ensure_ascii=False, indent=2))

                   def read_json(self, file_name, fallback=None):
                       path = resolve_save_path(file_name)
                       if not os.path.exists(path):
                           return fallback
                       with open(path, "r", encoding="utf-8") as f:
                           return json.load(f)

                   def exists(self, file_name):
                       return os.path.exists(resolve_save_path(file_name))

                   def delete(self, file_name):
                       path = resolve_save_path(file_name)
                       if not os.path.exists(path):
                           return False
                       os.remove(path)
                       return True

               class Debug:
                   def __init__(self, commands):
                       self._commands = commands

                   def draw_ray(self, origin, direction, length=10.0, color=None, duration=0.1):
                       color = color or [1.0, 0.2, 0.1, 1.0]
                       self._commands.append({
                           "target": "debug",
                           "action": "draw_ray",
                           "x": origin[0],
                           "y": origin[1],
                           "z": origin[2],
                           "directionX": direction[0],
                           "directionY": direction[1],
                           "directionZ": direction[2],
                           "length": length,
                           "colorR": color[0],
                           "colorG": color[1],
                           "colorB": color[2],
                           "colorA": color[3] if len(color) > 3 else 1.0,
                           "duration": duration
                       })

                   def draw_line(self, start, end, color=None, duration=0.1):
                       color = color or [1.0, 1.0, 0.1, 1.0]
                       self._commands.append({
                           "target": "debug",
                           "action": "draw_line",
                           "x": start[0],
                           "y": start[1],
                           "z": start[2],
                           "targetX": end[0],
                           "targetY": end[1],
                           "targetZ": end[2],
                           "colorR": color[0],
                           "colorG": color[1],
                           "colorB": color[2],
                           "colorA": color[3] if len(color) > 3 else 1.0,
                           "duration": duration
                       })

               class Window:
                   def __init__(self, data, commands):
                       self.title = data.get("title", "")
                       self.width = data.get("width", 1280)
                       self.height = data.get("height", 720)
                       self.actual_width = data.get("actualWidth", self.width)
                       self.actual_height = data.get("actualHeight", self.height)
                       self.fullscreen = bool(data.get("fullscreen", False))
                       self.resizable = bool(data.get("resizable", True))
                       self.timing_mode = data.get("timingMode", "time_synchronized")
                       self._commands = commands

                   def set_size(self, width, height):
                       self._commands.append({"target": "window", "action": "set_size", "width": width, "height": height})

                   def set_title(self, title):
                       self._commands.append({"target": "window", "action": "set_title", "text": title})

                   def set_fullscreen(self, enabled):
                       self._commands.append({"target": "window", "action": "set_fullscreen", "flag": bool(enabled)})

                   def set_resizable(self, enabled):
                       self._commands.append({"target": "window", "action": "set_resizable", "flag": bool(enabled)})

                   def set_timing_mode(self, mode):
                       self._commands.append({"target": "window", "action": "set_timing_mode", "name": mode})

                   def set_visible(self, enabled):
                       self._commands.append({"target": "window", "action": "set_visible", "flag": bool(enabled)})

                   def toggle_visible(self):
                       self._commands.append({"target": "window", "action": "toggle_visible"})

                   def exit(self):
                       self._commands.append({"target": "window", "action": "exit"})

                   def quit(self):
                       self.exit()

               class Runtime:
                   def __init__(self, data, commands):
                       self.use_opencl = bool(data.get("useOpenCl", True))
                       self.is_using_opencl = bool(data.get("isUsingOpenCl", False))
                       self.compute_backend = data.get("computeBackend", "CPU")
                       self._commands = commands

                   def set_use_opencl(self, enabled):
                       self.use_opencl = bool(enabled)
                       self._commands.append({"target": "runtime", "action": "set_use_opencl", "flag": self.use_opencl})

                   def execute_command(self, file_name, args=None, timeout_seconds=30, working_directory=None, shell=False):
                       command_args = [] if args is None else args
                       if isinstance(command_args, str):
                           command = [str(file_name)] + ([command_args] if command_args else [])
                       else:
                           command = [str(file_name)] + [str(item) for item in command_args]
                       return execute_process(command if not shell else str(file_name), timeout_seconds, working_directory, bool(shell))

                   def execute_shell_command(self, command, timeout_seconds=30, working_directory=None):
                       return execute_process(str(command), timeout_seconds, working_directory, True)

               class Sprite:
                   def __init__(self, scene, data, commands):
                       self._scene = scene
                       self.id = data.get("id", "")
                       self.name = data.get("name", "")
                       self.path = data.get("path", "")
                       self.texture = data.get("texture", self.path)
                       self.layout_mode = data.get("layoutMode", "absolute")
                       self.x = data.get("x", 0)
                       self.y = data.get("y", 0)
                       self.width = data.get("width", 1)
                       self.height = data.get("height", 1)
                       self.rotation_degrees = float(data.get("rotationDegrees", 0.0))
                       self.opacity = data.get("opacity", 1)
                       self.visible = bool(data.get("visible", True))
                       self._commands = commands

                   def set_position(self, x, y):
                       self.x = x
                       self.y = y
                       self._commands.append({"target": "sprite", "sprite": self.id, "action": "set_position", "x": x, "y": y})

                   def set_size(self, width, height):
                       self.width = width
                       self.height = height
                       self._commands.append({"target": "sprite", "sprite": self.id, "action": "set_size", "width": width, "height": height})

                   def set_visible(self, enabled):
                       self.visible = bool(enabled)
                       self._commands.append({"target": "sprite", "sprite": self.id, "action": "set_visible", "flag": bool(enabled)})

                   def set_opacity(self, opacity):
                       self.opacity = opacity
                       self._commands.append({"target": "sprite", "sprite": self.id, "action": "set_opacity", "value": opacity})

                   def set_texture(self, texture):
                       self.texture = texture
                       self.path = texture
                       self._commands.append({"target": "sprite", "sprite": self.id, "action": "set_texture", "texture": texture})

                   def set_layout_mode(self, layout_mode):
                       self.layout_mode = str(layout_mode or "absolute")
                       self._commands.append({"target": "sprite", "sprite": self.id, "action": "set_layout_mode", "mode": self.layout_mode})

                   def set_render_texture(self, render_texture_name):
                       self.set_texture(render_texture(render_texture_name))

                   def _resolve_rect(self):
                       actual_width = max(float(getattr(self._scene.window, "actual_width", self._scene.window.width) or 1.0), 1.0)
                       actual_height = max(float(getattr(self._scene.window, "actual_height", self._scene.window.height) or 1.0), 1.0)
                       reference_width = max(float(self._scene.window.width or 1.0), 1.0)
                       reference_height = max(float(self._scene.window.height or 1.0), 1.0)
                       if str(self.layout_mode or "absolute").lower() in ("relative", "scaled", "scale"):
                           scale_x = actual_width / reference_width
                           scale_y = actual_height / reference_height
                           return (
                               float(self.x) * scale_x,
                               float(self.y) * scale_y,
                               max(float(self.width) * scale_x, 1.0),
                               max(float(self.height) * scale_y, 1.0)
                           )
                       return (float(self.x), float(self.y), max(float(self.width), 1.0), max(float(self.height), 1.0))

                   def contains_point(self, x, y):
                       if not self.visible or not self.path:
                           return False
                       rect_x, rect_y, rect_w, rect_h = self._resolve_rect()
                       center_x = rect_x + (rect_w * 0.5)
                       center_y = rect_y + (rect_h * 0.5)
                       dx = float(x) - center_x
                       dy = float(y) - center_y
                       radians = math.radians(-float(self.rotation_degrees))
                       cos_v = math.cos(radians)
                       sin_v = math.sin(radians)
                       local_x = (dx * cos_v) - (dy * sin_v)
                       local_y = (dx * sin_v) + (dy * cos_v)
                       return abs(local_x) <= rect_w * 0.5 and abs(local_y) <= rect_h * 0.5

                   def contains_mouse(self, input):
                       return self.contains_point(input.mouse_x, input.mouse_y)

                   def contains_touch(self, input):
                       for touch in getattr(input, "touches", []):
                           if getattr(touch, "is_active", False) and self.contains_point(touch.x, touch.y):
                               return True
                       return False

                   def show(self):
                       self.set_visible(True)

                   def hide(self):
                       self.set_visible(False)

               class GuiControl:
                   def __init__(self, data, commands):
                       self.id = data.get("id", "")
                       self.name = data.get("name", "")
                       self.type = data.get("type", "")
                       self.text = data.get("text", "")
                       self.value = data.get("value", self.text)
                       self.x = data.get("x", 0)
                       self.y = data.get("y", 0)
                       self.width = data.get("width", 1)
                       self.height = data.get("height", 1)
                       self.layout_mode = data.get("layoutMode", "absolute")
                       self.visible = bool(data.get("visible", True))
                       self.checked = bool(data.get("checked", False))
                       self.progress = float(data.get("progress", 0.0))
                       self.font_size = float(data.get("fontSize", 18.0))
                       self.word_wrap = bool(data.get("wordWrap", True))
                       self.multiline = bool(data.get("multiline", False))
                       self.items = data.get("items", [])
                       self.selected_index = data.get("selectedIndex", 0)
                       self.cursor_position = int(data.get("cursorPosition", 0))
                       self.selection_start = int(data.get("selectionStart", self.cursor_position))
                       self.selection_end = int(data.get("selectionEnd", self.cursor_position))
                       self.selection_length = int(data.get("selectionLength", max(0, self.selection_end - self.selection_start)))
                       self.selected_text = str(data.get("selectedText", "") or "")
                       self.has_selection = bool(data.get("hasSelection", False))
                       self._commands = commands

                   def set_position(self, x, y):
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_position", "x": x, "y": y})

                   def set_size(self, width, height):
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_size", "width": width, "height": height})

                   def set_visible(self, enabled):
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_visible", "flag": bool(enabled)})

                   def show(self):
                       self.set_visible(True)

                   def hide(self):
                       self.set_visible(False)

                   def set_text(self, text):
                       self.text = "" if text is None else str(text)
                       self.value = self.text
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_text", "text": text})

                   def set_value(self, value):
                       self.set_text(value)

                   def set_checked(self, enabled):
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_checked", "flag": bool(enabled)})

                   def set_progress(self, value):
                       self.progress = max(0.0, min(1.0, float(value)))
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_progress", "value": self.progress})

                   def set_layout_mode(self, mode):
                       self.layout_mode = str(mode)
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_layout_mode", "mode": self.layout_mode})

                   def set_font_size(self, value):
                       self.font_size = float(value)
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_font_size", "value": self.font_size})

                   def set_word_wrap(self, enabled):
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_word_wrap", "flag": bool(enabled)})

                   def set_multiline(self, enabled):
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_multiline", "flag": bool(enabled)})

                   def set_items(self, items):
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_items", "items": list(items)})

                   def set_selected_index(self, index):
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_selected_index", "index": int(index)})

                   def replace_selection(self, text):
                       self._commands.append({"target": "gui", "control": self.id, "action": "replace_selection", "text": "" if text is None else str(text)})

               class TouchPoint:
                   def __init__(self, data):
                       self.id = int(data.get("id", 0))
                       self.x = float(data.get("x", 0.0))
                       self.y = float(data.get("y", 0.0))
                       self.delta_x = float(data.get("deltaX", 0.0))
                       self.delta_y = float(data.get("deltaY", 0.0))
                       self.phase = str(data.get("phase", "") or "").lower()
                       self.kind = str(data.get("kind", "") or "").lower()
                       self.pressure = float(data.get("pressure", 0.0))
                       self.is_active = bool(data.get("isActive", False))
                       self.is_ended = bool(data.get("isEnded", False))

               class Input:
                   def __init__(self, data, commands):
                       self._keys = set()
                       for key in data.get("keysDown", []):
                           value = str(key)
                           self._keys.add(value)
                           self._keys.add(value.lower())
                       self._mouse_buttons = set(data.get("mouseButtonsDown", []))
                       self.mouse_x = data.get("mouseX", 0)
                       self.mouse_y = data.get("mouseY", 0)
                       self.mouse_delta_x = data.get("mouseDeltaX", 0)
                       self.mouse_delta_y = data.get("mouseDeltaY", 0)
                       self.scroll_x = data.get("scrollX", 0)
                       self.scroll_y = data.get("scrollY", 0)
                       self.is_cursor_visible = bool(data.get("isCursorVisible", True))
                       self.cursor_visible = self.is_cursor_visible
                       self.is_cursor_locked = bool(data.get("isCursorLocked", False))
                       self.cursor_locked = self.is_cursor_locked
                       self.is_raw_mouse_input = bool(data.get("isRawMouseInput", False))
                       self.cursor_mode = str(data.get("cursorMode", "normal") or "normal")
                       self.alt_down = bool(data.get("altDown", False))
                       self.control_down = bool(data.get("controlDown", False))
                       self.shift_down = bool(data.get("shiftDown", False))
                       self.has_gamepad = bool(data.get("hasGamepad", False))
                       self.gamepad_name = str(data.get("gamepadName", "") or "")
                       self.gamepad_index = int(data.get("gamepadIndex", -1))
                       self.left_stick_x = float(data.get("leftStickX", 0.0))
                       self.left_stick_y = float(data.get("leftStickY", 0.0))
                       self.right_stick_x = float(data.get("rightStickX", 0.0))
                       self.right_stick_y = float(data.get("rightStickY", 0.0))
                       self.left_trigger = float(data.get("leftTrigger", 0.0))
                       self.right_trigger = float(data.get("rightTrigger", 0.0))
                       self._gamepad_buttons = set()
                       for button in data.get("gamepadButtonsDown", []):
                           value = str(button)
                           self._gamepad_buttons.add(value)
                           self._gamepad_buttons.add(value.lower())
                       self.is_touch_available = bool(data.get("isTouchAvailable", False))
                       self.has_touch = bool(data.get("hasTouch", False))
                       self.touch_count = int(data.get("touchCount", 0))
                       self.active_touch_count = int(data.get("activeTouchCount", 0))
                       self.is_touch_down = bool(data.get("isTouchDown", False))
                       self.is_touch_started = bool(data.get("isTouchStarted", False))
                       self.is_touch_ended = bool(data.get("isTouchEnded", False))
                       self.touches = [TouchPoint(item) for item in data.get("touches", [])]
                       primary_touch = data.get("primaryTouch", None)
                       self.primary_touch = TouchPoint(primary_touch) if primary_touch is not None else None
                       self.clipboard_text = str(data.get("clipboardText", "") or "")
                       self.has_clipboard_text = bool(data.get("hasClipboardText", False))
                       self._commands = commands

                   def is_key_down(self, key):
                       return str(key) in self._keys or str(key).lower() in self._keys

                   def is_mouse_button_down(self, button):
                       return str(button).lower() in self._mouse_buttons

                   def is_gamepad_button_down(self, button):
                       return str(button) in self._gamepad_buttons or str(button).lower() in self._gamepad_buttons

                   def get_touch(self, touch_id):
                       for touch in self.touches:
                           if touch.id == int(touch_id):
                               return touch
                       return None

                   def set_clipboard_text(self, text):
                       value = "" if text is None else str(text)
                       self.clipboard_text = value
                       self.has_clipboard_text = len(value) > 0
                       self._commands.append({"target": "input", "action": "set_clipboard_text", "text": value})

                   def set_cursor_visible(self, visible):
                       value = bool(visible)
                       self.is_cursor_visible = value
                       self.cursor_visible = value
                       self.is_cursor_locked = False
                       self.cursor_locked = False
                       self.is_raw_mouse_input = False
                       self.cursor_mode = "normal" if value else "hidden"
                       self._commands.append({"target": "input", "action": "set_cursor_visible", "flag": value})

                   def show_cursor(self):
                       self.set_cursor_visible(True)

                   def hide_cursor(self):
                       self.set_cursor_visible(False)

                   def set_cursor_locked(self, locked, raw_input=False):
                       value = bool(locked)
                       raw = bool(raw_input)
                       self.is_cursor_locked = value
                       self.cursor_locked = value
                       self.is_cursor_visible = False if value else True
                       self.cursor_visible = self.is_cursor_visible
                       self.is_raw_mouse_input = value and raw
                       self.cursor_mode = "raw" if value and raw else ("disabled" if value else "normal")
                       self._commands.append({"target": "input", "action": "set_cursor_locked", "flag": value, "rawInput": raw})

                   def lock_cursor(self, raw_input=False):
                       self.set_cursor_locked(True, raw_input)

                   def unlock_cursor(self):
                       self.set_cursor_locked(False)

               class Audio:
                   def __init__(self, commands):
                       self._commands = commands

                   def play(self, name):
                       self._commands.append({"target": "audio", "action": "play", "name": name})

                   def pause(self, name):
                       self._commands.append({"target": "audio", "action": "pause", "name": name})

                   def stop(self, name):
                       self._commands.append({"target": "audio", "action": "stop", "name": name})

                   def set_volume(self, name, volume):
                       self._commands.append({"target": "audio", "action": "set_volume", "name": name, "volume": volume})

                   def set_loop(self, name, loop):
                       self._commands.append({"target": "audio", "action": "set_loop", "name": name, "flag": bool(loop)})

               for raw in sys.stdin:
                   try:
                       ctx = json.loads(raw)
                       commands = []
                       entity = Entity(ctx.get("entity", {}), commands)
                       scene = Scene(ctx.get("scene", {}), commands)
                       input = Input(ctx.get("input", {}), commands)
                       audio = Audio(commands)
                       event = ctx.get("event", "")
                       if event == "start" and hasattr(module, "start"):
                           module.start(entity, scene, input, audio)
                       elif event == "update" and hasattr(module, "update"):
                           module.update(entity, scene, input, audio, ctx.get("deltaSeconds", 0.0))
                       elif event == "gui_event" and hasattr(module, "gui_event"):
                           control_id = ctx.get("controlId", "")
                           control_name = ctx.get("controlName", "")
                           gui_event_name = ctx.get("guiEventName", "")
                           if len(inspect.signature(module.gui_event).parameters) >= 7:
                               module.gui_event(entity, scene, input, audio, control_id, control_name, gui_event_name)
                           else:
                               module.gui_event(entity, scene, input, audio, control_id, gui_event_name)
                       elif event == "sprite_event" and hasattr(module, "sprite_event"):
                           sprite_id = ctx.get("spriteId", "")
                           sprite_name = ctx.get("spriteName", "")
                           sprite_event_name = ctx.get("spriteEventName", "")
                           if len(inspect.signature(module.sprite_event).parameters) >= 7:
                               module.sprite_event(entity, scene, input, audio, sprite_id, sprite_name, sprite_event_name)
                           else:
                               module.sprite_event(entity, scene, input, audio, sprite_id, sprite_event_name)
                       elif event == "tray_menu_event":
                           item_id = ctx.get("trayMenuItemId", "")
                           item_text = ctx.get("trayMenuItemText", "")
                           tray_event_name = ctx.get("trayMenuEventName", "")
                           if tray_event_name and hasattr(module, tray_event_name):
                               callback = getattr(module, tray_event_name)
                               if len(inspect.signature(callback).parameters) >= 7:
                                   callback(entity, scene, input, audio, item_id, item_text, tray_event_name)
                               else:
                                   callback(entity, scene, input, audio, item_id, tray_event_name)
                           elif hasattr(module, "tray_menu_event"):
                               if len(inspect.signature(module.tray_menu_event).parameters) >= 7:
                                   module.tray_menu_event(entity, scene, input, audio, item_id, item_text, tray_event_name)
                               else:
                                   module.tray_menu_event(entity, scene, input, audio, item_id, tray_event_name)
                       elif event in ("loading_started", "loading_progress", "loading_completed") and hasattr(module, event):
                           getattr(module, event)(entity, scene, input, audio, ctx.get("loadingProgress", 0.0), ctx.get("loadingMessage", ""))
                       elif event == "speech_completed":
                           callback = ctx.get("speechCallback", "")
                           if callback and hasattr(module, callback):
                               getattr(module, callback)(entity, scene, input, audio)
                           elif hasattr(module, "speech_completed"):
                               module.speech_completed(entity, scene, input, audio, callback)
                       elif event == "llm_event":
                           llm_event = ctx.get("llmEvent", {})
                           callback = llm_event.get("callbackName", "")
                           llm_result = None
                           if callback and hasattr(module, callback):
                               llm_result = getattr(module, callback)(entity, scene, input, audio, llm_event)
                           elif hasattr(module, "llm_event"):
                               llm_result = module.llm_event(entity, scene, input, audio, llm_event)
                           if llm_event.get("eventName", "") == "tool_execute":
                               print(FLUSH_MARKER + json.dumps(commands, ensure_ascii=False, separators=(",", ":")), flush=True)
                               commands = []
                               print(TOOL_RESULT_MARKER + json.dumps({"result": tool_result_to_text(llm_result)}, ensure_ascii=False, separators=(",", ":")), flush=True)
                               continue
                       elif event == "asr_event":
                           asr_event = ctx.get("asrEvent", {})
                           callback = asr_event.get("callbackName", "")
                           if callback and hasattr(module, callback):
                               getattr(module, callback)(entity, scene, input, audio, asr_event)
                           elif hasattr(module, "asr_event"):
                               module.asr_event(entity, scene, input, audio, asr_event)
                       elif event == "realtime_voice_event":
                           realtime_voice_event = ctx.get("realtimeVoiceEvent", {})
                           callback = realtime_voice_event.get("callbackName", "")
                           if callback and hasattr(module, callback):
                               getattr(module, callback)(entity, scene, input, audio, realtime_voice_event)
                           elif hasattr(module, "realtime_voice_event"):
                               module.realtime_voice_event(entity, scene, input, audio, realtime_voice_event)
                       print(COMMAND_MARKER + json.dumps(commands, ensure_ascii=False, separators=(",", ":")), flush=True)
                   except Exception as ex:
                       try:
                           if event == "llm_event":
                               llm_event = ctx.get("llmEvent", {})
                               if llm_event.get("eventName", "") == "tool_execute":
                                   print(FLUSH_MARKER + json.dumps(commands, ensure_ascii=False, separators=(",", ":")), flush=True)
                                   print(TOOL_RESULT_MARKER + json.dumps({
                                       "result": json.dumps({"error": str(ex)}, ensure_ascii=False, separators=(",", ":"))
                                   }, ensure_ascii=False, separators=(",", ":")), flush=True)
                                   print(str(ex), file=sys.stderr, flush=True)
                                   continue
                       except Exception:
                           pass
                       print(COMMAND_MARKER + "[]", flush=True)
                       print(str(ex), file=sys.stderr, flush=True)
               """;
    }

    private void ApplyCommands(string rawJson, RuntimeEntity currentEntity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio)
    {
        PythonCommand[] commands = JsonSerializer.Deserialize<PythonCommand[]>(rawJson, JsonOptions)
            ?? [];

        foreach (PythonCommand command in commands)
        {
            if (string.Equals(command.Target, "scene", StringComparison.OrdinalIgnoreCase))
            {
                ApplySceneCommand(command, scene);
                continue;
            }

            if (string.Equals(command.Target, "llm", StringComparison.OrdinalIgnoreCase))
            {
                ApplyLlmCommand(command, currentEntity, scene);
                continue;
            }

            if (string.Equals(command.Target, "asr", StringComparison.OrdinalIgnoreCase))
            {
                ApplyAsrCommand(command, currentEntity, scene);
                continue;
            }

            if (string.Equals(command.Target, "realtime_voice", StringComparison.OrdinalIgnoreCase))
            {
                ApplyRealtimeVoiceCommand(command, currentEntity, scene);
                continue;
            }

            if (string.Equals(command.Target, "audio", StringComparison.OrdinalIgnoreCase))
            {
                ApplyAudioCommand(command, audio);
                continue;
            }

            if (string.Equals(command.Target, "bubble", StringComparison.OrdinalIgnoreCase))
            {
                ApplyBubbleCommand(command, scene);
                continue;
            }

            if (string.Equals(command.Target, "gui", StringComparison.OrdinalIgnoreCase))
            {
                ApplyGuiCommand(command, scene);
                continue;
            }

            if (string.Equals(command.Target, "input", StringComparison.OrdinalIgnoreCase))
            {
                ApplyInputCommand(command, input);
                continue;
            }

            if (string.Equals(command.Target, "window", StringComparison.OrdinalIgnoreCase))
            {
                ApplyWindowCommand(command, scene);
                continue;
            }

            if (string.Equals(command.Target, "runtime", StringComparison.OrdinalIgnoreCase))
            {
                ApplyRuntimeCommand(command, scene);
                continue;
            }

            if (string.Equals(command.Target, "camera", StringComparison.OrdinalIgnoreCase))
            {
                ApplyCameraCommand(command, scene);
                continue;
            }

            if (string.Equals(command.Target, "debug", StringComparison.OrdinalIgnoreCase))
            {
                ApplyDebugCommand(command, scene);
                continue;
            }

            if (string.Equals(command.Target, "sprite", StringComparison.OrdinalIgnoreCase))
            {
                ApplySpriteCommand(command, scene);
                continue;
            }

            RuntimeEntity? entity = string.IsNullOrWhiteSpace(command.Entity)
                ? currentEntity
                : scene.GetEntity(command.Entity);
            if (entity is null)
            {
                continue;
            }

            ApplyEntityCommand(command, entity, currentEntity, scene, input, audio);
        }
    }

    private static void ApplyCameraCommand(PythonCommand command, RuntimeScene scene)
    {
        RuntimeCamera camera = scene.Camera;
        switch (command.Action?.ToLowerInvariant())
        {
            case "set_main_camera" when !string.IsNullOrWhiteSpace(command.Name):
                camera.SetMainCamera(command.Name!);
                break;
            case "set_camera_look_at" when !string.IsNullOrWhiteSpace(command.Name) && TryGetLookAt(command, out Vector3 position, out Vector3 target):
                camera.SetCameraLookAt(
                    command.Name!,
                    position.X,
                    position.Y,
                    position.Z,
                    target.X,
                    target.Y,
                    target.Z);
                break;
            case "bind_render_texture_camera" when !string.IsNullOrWhiteSpace(command.Name) && !string.IsNullOrWhiteSpace(command.Camera):
                camera.BindRenderTextureCamera(command.Name!, command.Camera!);
                break;
            case "set_camera_viewport" when !string.IsNullOrWhiteSpace(command.Name) && command.X.HasValue && command.Y.HasValue && command.Width.HasValue && command.Height.HasValue:
                camera.SetCameraViewport(
                    command.Name!,
                    (float)command.X.Value,
                    (float)command.Y.Value,
                    (float)command.Width.Value,
                    (float)command.Height.Value,
                    command.Mode ?? "relative");
                break;
            case "enable_camera_viewport" when !string.IsNullOrWhiteSpace(command.Name) && command.Flag.HasValue:
                camera.EnableCameraViewport(command.Name!, command.Flag.Value);
                break;
            case "set_control_mode" when !string.IsNullOrWhiteSpace(command.Mode):
            case "set_mode" when !string.IsNullOrWhiteSpace(command.Mode):
                camera.SetControlMode(command.Mode!);
                break;
            case "configure_control":
                camera.ConfigureControl(
                    ToFloat(command.Distance),
                    ToFloat(command.Height),
                    ToFloat(command.ShoulderOffset),
                    ToFloat(command.Smoothing),
                    ToFloat(command.MoveSpeed),
                    ToFloat(command.MouseSensitivity),
                    ToFloat(command.SafeRadius),
                    ToFloat(command.AutoOrbitSpeed));
                break;
            case "set_mouse_look" when command.Flag.HasValue:
                camera.SetMouseLook(command.Flag.Value, command.RequireRightMouse ?? true);
                break;
            case "set_yaw_pitch" when command.Yaw.HasValue && command.Pitch.HasValue:
                camera.SetYawPitch((float)command.Yaw.Value, (float)command.Pitch.Value);
                break;
            case "set_look_at" when TryGetLookAt(command, out Vector3 position, out Vector3 target):
                camera.SetLookAt(position, target);
                break;
            case "use_editor_orbit_mode":
            case "use_max_editor_mode":
                camera.UseEditorOrbitMode(
                    (float)(command.OrbitSensitivity ?? 0.2),
                    (float)(command.PanSensitivity ?? 1.0),
                    (float)(command.ZoomSensitivity ?? 1.0));
                break;
            case "use_custom_mode":
                camera.UseCustomMode();
                break;
            case "use_tps_mode" when !string.IsNullOrWhiteSpace(command.TargetEntity):
            case "use_third_person_mode" when !string.IsNullOrWhiteSpace(command.TargetEntity):
                camera.UseThirdPersonMode(
                    command.TargetEntity!,
                    (float)(command.Distance ?? 5.0),
                    (float)(command.Height ?? 1.5),
                    (float)(command.ShoulderOffset ?? 0.0),
                    (float)(command.Smoothing ?? 12.0));
                break;
            case "use_shoulder_mode" when !string.IsNullOrWhiteSpace(command.TargetEntity):
                camera.UseShoulderMode(
                    command.TargetEntity!,
                    (float)(command.Distance ?? 4.0),
                    (float)(command.Height ?? 1.6),
                    (float)(command.ShoulderOffset ?? 0.55),
                    (float)(command.Smoothing ?? 12.0));
                break;
            case "use_lock_on_mode" when !string.IsNullOrWhiteSpace(command.SubjectEntity) && !string.IsNullOrWhiteSpace(command.TargetEntity):
                camera.UseLockOnMode(
                    command.SubjectEntity!,
                    command.TargetEntity!,
                    (float)(command.Distance ?? 5.0),
                    (float)(command.Height ?? 1.6),
                    (float)(command.Smoothing ?? 12.0),
                    (float)(command.SafeRadius ?? 0.25),
                    (float)(command.ShoulderOffset ?? 0.0));
                break;
            case "use_fps_mode" when !string.IsNullOrWhiteSpace(command.TargetEntity):
            case "use_first_person_mode" when !string.IsNullOrWhiteSpace(command.TargetEntity):
                camera.UseFirstPersonMode(
                    command.TargetEntity!,
                    (float)(command.Height ?? 1.65),
                    (float)(command.Smoothing ?? 18.0));
                break;
            case "use_fps_control_mode" when !string.IsNullOrWhiteSpace(command.TargetEntity):
            case "use_locked_fps_mode" when !string.IsNullOrWhiteSpace(command.TargetEntity):
                camera.UseFpsControlMode(
                    command.TargetEntity!,
                    (float)(command.Height ?? 1.65),
                    (float)(command.Smoothing ?? 18.0),
                    (float)(command.MouseSensitivity ?? 0.15));
                break;
            case "use_free_fly_mode":
                camera.UseFreeFlyMode(
                    (float)(command.MoveSpeed ?? 5.0),
                    (float)(command.MouseSensitivity ?? 0.15));
                break;
            case "use_rts_mode":
                camera.UseRtsMode(
                    (float)(command.Height ?? 12.0),
                    (float)(command.Pitch ?? 55.0),
                    (float)(command.MoveSpeed ?? 8.0));
                break;
            case "use_top_down_mode":
                camera.UseTopDownMode(
                    command.TargetEntity ?? string.Empty,
                    (float)(command.Height ?? 12.0),
                    (float)(command.Smoothing ?? 12.0));
                break;
            case "use_isometric_mode":
                camera.UseIsometricMode(
                    command.TargetEntity ?? string.Empty,
                    (float)(command.Distance ?? 12.0),
                    (float)(command.Height ?? 0.0),
                    (float)(command.Smoothing ?? 12.0));
                break;
            case "use_side_scroller_mode" when !string.IsNullOrWhiteSpace(command.TargetEntity):
                camera.UseSideScrollerMode(
                    command.TargetEntity!,
                    (float)(command.Distance ?? 10.0),
                    (float)(command.Height ?? 1.5),
                    (float)(command.Smoothing ?? 12.0));
                break;
            case "use_fixed_mode" when TryGetLookAt(command, out Vector3 position, out Vector3 target):
                camera.UseFixedMode(position, target);
                break;
            case "use_cinematic_follow_mode" when !string.IsNullOrWhiteSpace(command.TargetEntity):
                camera.UseCinematicFollowMode(
                    command.TargetEntity!,
                    (float)(command.OffsetX ?? 0.0),
                    (float)(command.OffsetY ?? 1.8),
                    (float)(command.OffsetZ ?? 5.0),
                    (float)(command.Height ?? 1.5),
                    (float)(command.Smoothing ?? 8.0));
                break;
            case "use_orbital_follow_mode" when !string.IsNullOrWhiteSpace(command.TargetEntity):
                camera.UseOrbitalFollowMode(
                    command.TargetEntity!,
                    (float)(command.Distance ?? 6.0),
                    (float)(command.Height ?? 1.5),
                    (float)(command.AutoOrbitSpeed ?? 30.0),
                    (float)(command.Smoothing ?? 12.0));
                break;
            case "orbit" when command.Yaw.HasValue && command.Pitch.HasValue:
                camera.Orbit((float)command.Yaw.Value, (float)command.Pitch.Value);
                break;
            case "pan" when command.X.HasValue && command.Y.HasValue:
                camera.Pan((float)command.X.Value, (float)command.Y.Value);
                break;
            case "dolly" when command.Value.HasValue:
                camera.Dolly((float)command.Value.Value);
                break;
        }
    }

    private static void ApplyLlmCommand(PythonCommand command, RuntimeEntity callbackEntity, RuntimeScene scene)
    {
        switch (command.Action?.ToLowerInvariant())
        {
            case "cancel_request" when !string.IsNullOrWhiteSpace(command.RequestId):
                scene.Llm.CancelRequest(command.RequestId);
                break;
            case "cancel_all_requests":
                scene.Llm.CancelAllRequests();
                break;
            case "start_chat" when !string.IsNullOrWhiteSpace(command.Text):
                scene.Llm.StartChat(
                    callbackEntity,
                    command.Text,
                    command.SystemPrompt,
                    command.Model,
                    ToFloat(command.Temperature),
                    requestId: command.RequestId,
                    onDeltaCallback: command.OnDelta,
                    onCompletedCallback: command.OnCompleted,
                    onErrorCallback: command.OnError);
                break;
            case "start_chat_with_tools" when !string.IsNullOrWhiteSpace(command.Text):
                RuntimeLlmTool[] tools = (command.Tools ?? [])
                    .Where(tool => !string.IsNullOrWhiteSpace(tool.Name) && !string.IsNullOrWhiteSpace(tool.Callback))
                    .Select(tool => new RuntimeLlmScriptTool(
                        tool.Name!,
                        tool.Description ?? string.Empty,
                        tool.ParametersJsonSchema ?? tool.Parameters ?? "{\"type\":\"object\",\"properties\":{}}",
                        tool.Callback!).ToTool(callbackEntity, scene))
                    .ToArray();
                scene.Llm.StartChatWithTools(
                    callbackEntity,
                    command.Text,
                    tools,
                    command.SystemPrompt,
                    command.Model,
                    ToFloat(command.Temperature),
                    requestId: command.RequestId,
                    onDeltaCallback: command.OnDelta,
                    onCompletedCallback: command.OnCompleted,
                    onErrorCallback: command.OnError,
                    onToolCallCallback: command.OnToolCall,
                    onToolResultCallback: command.OnToolResult,
                    maxToolRounds: command.MaxToolRounds ?? 4);
                break;
        }
    }

    private static void ApplyRealtimeVoiceCommand(PythonCommand command, RuntimeEntity callbackEntity, RuntimeScene scene)
    {
        switch (command.Action?.ToLowerInvariant())
        {
            case "start_wake_word_monitoring":
                scene.RealtimeVoice.StartWakeWordMonitoring(callbackEntity, command.OnCompleted, command.OnError);
                break;
            case "stop_wake_word_monitoring":
                scene.RealtimeVoice.StopWakeWordMonitoring();
                break;
            case "start_transcription":
                scene.RealtimeVoice.StartTranscription(
                    callbackEntity,
                    requestId: command.RequestId,
                    timeoutSeconds: ToFloat(command.TimeoutSeconds),
                    onCompletedCallback: command.OnCompleted,
                    onTimeoutCallback: command.OnTimeout,
                    onErrorCallback: command.OnError);
                break;
            case "start_response" when !string.IsNullOrWhiteSpace(command.Text):
                scene.RealtimeVoice.StartResponse(
                    callbackEntity,
                    command.Text,
                    requestId: command.RequestId,
                    onDeltaCallback: command.OnDelta,
                    onCompletedCallback: command.OnCompleted,
                    onErrorCallback: command.OnError);
                break;
            case "start_voice_turn":
                scene.RealtimeVoice.StartVoiceTurn(
                    callbackEntity,
                    requestId: command.RequestId,
                    timeoutSeconds: ToFloat(command.TimeoutSeconds) ?? 30.0f,
                    onTranscriptionCompletedCallback: command.Callback,
                    onDeltaCallback: command.OnDelta,
                    onCompletedCallback: command.OnCompleted,
                    onTimeoutCallback: command.OnTimeout,
                    onErrorCallback: command.OnError);
                break;
            case "start_speak_text" when !string.IsNullOrWhiteSpace(command.Text):
                scene.RealtimeVoice.StartSpeakText(
                    callbackEntity,
                    command.Text,
                    ToFloat(command.Speed),
                    requestId: command.RequestId,
                    onCompletedCallback: command.OnCompleted,
                    onErrorCallback: command.OnError);
                break;
            case "reset_conversation":
                _ = scene.RealtimeVoice.ResetConversationAsync();
                break;
            case "cancel_request" when !string.IsNullOrWhiteSpace(command.RequestId):
                scene.RealtimeVoice.CancelRequest(command.RequestId);
                break;
            case "cancel_all_requests":
                scene.RealtimeVoice.CancelAllRequests();
                break;
        }
    }

    private static void ApplyAsrCommand(PythonCommand command, RuntimeEntity callbackEntity, RuntimeScene scene)
    {
        switch (command.Action?.ToLowerInvariant())
        {
            case "start_streaming_recognition":
                scene.Asr.StartStreamingRecognition(
                    callbackEntity,
                    requestId: command.RequestId,
                    onPartialCallback: command.OnPartial,
                    onCompletedCallback: command.OnCompleted,
                    onErrorCallback: command.OnError);
                break;
            case "stop_streaming_recognition":
                scene.Asr.StopStreamingRecognition(command.RequestId);
                break;
            case "start_wake_word_monitoring":
                scene.Asr.StartWakeWordMonitoring(
                    callbackEntity,
                    command.WakeWords ?? [],
                    requestId: command.RequestId,
                    chunkDurationSeconds: ToFloat(command.ChunkDurationSeconds),
                    extensionDurationSeconds: ToFloat(command.ExtensionDurationSeconds),
                    trailingSilencePaddingSeconds: ToFloat(command.TrailingSilencePaddingSeconds),
                    onDetectedCallback: command.OnCompleted,
                    onErrorCallback: command.OnError);
                break;
            case "stop_wake_word_monitoring":
                scene.Asr.StopWakeWordMonitoring();
                break;
        }
    }

    private static void ApplyDebugCommand(PythonCommand command, RuntimeScene scene)
    {
        switch (command.Action?.ToLowerInvariant())
        {
            case "draw_ray" when TryGetVector(command, out float x, out float y, out float z)
                && command.DirectionX.HasValue && command.DirectionY.HasValue && command.DirectionZ.HasValue:
                scene.Debug.DrawRay(
                    new Vector3(x, y, z),
                    new Vector3((float)command.DirectionX.Value, (float)command.DirectionY.Value, (float)command.DirectionZ.Value),
                    (float)(command.Length ?? 10.0),
                    GetCommandColor(command, new Vector4(1.0f, 0.2f, 0.1f, 1.0f)),
                    (float)(command.Duration ?? 0.1));
                break;
            case "draw_line" when TryGetLookAt(command, out Vector3 start, out Vector3 end):
                scene.Debug.DrawLine(
                    start,
                    end,
                    GetCommandColor(command, new Vector4(1.0f, 1.0f, 0.1f, 1.0f)),
                    (float)(command.Duration ?? 0.1));
                break;
        }
    }

    private static void ApplyWindowCommand(PythonCommand command, RuntimeScene scene)
    {
        switch (command.Action?.ToLowerInvariant())
        {
            case "set_size" when command.Width.HasValue && command.Height.HasValue:
                scene.Window.SetSize((int)command.Width.Value, (int)command.Height.Value);
                break;
            case "set_title" when command.Text is not null:
                scene.Window.SetTitle(command.Text);
                break;
            case "set_fullscreen" when command.Flag.HasValue:
                scene.Window.SetFullscreen(command.Flag.Value);
                break;
            case "set_resizable" when command.Flag.HasValue:
                scene.Window.SetResizable(command.Flag.Value);
                break;
            case "set_timing_mode" when !string.IsNullOrWhiteSpace(command.Name):
                scene.Window.SetTimingMode(command.Name);
                break;
            case "set_visible" when command.Flag.HasValue:
                scene.Window.SetVisible(command.Flag.Value);
                break;
            case "toggle_visible":
                scene.Window.ToggleVisible();
                break;
            case "exit":
            case "quit":
                scene.Window.Exit();
                break;
        }
    }

    private static void ApplyBubbleCommand(PythonCommand command, RuntimeScene scene)
    {
        RuntimeDialogueBubbleManager bubbles = scene.Bubble;
        switch (command.Action?.ToLowerInvariant())
        {
            case "clear":
                bubbles.Clear();
                return;
            case "hide_all":
                bubbles.HideAll();
                return;
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return;
        }

        string bubbleName = command.Name!;
        if (string.Equals(command.Action, "remove", StringComparison.OrdinalIgnoreCase))
        {
            bubbles.Remove(bubbleName);
            return;
        }

        RuntimeDialogueBubble bubble = bubbles.GetOrCreate(bubbleName);
        switch (command.Action?.ToLowerInvariant())
        {
            case "show":
                bubble.SetContent(
                    command.Text ?? string.Empty,
                    command.HeaderText ?? string.Empty,
                    command.FooterText ?? string.Empty);
                bubble.Show();
                break;
            case "hide":
                bubble.Hide();
                break;
            case "set_visible" when command.Flag.HasValue:
                if (command.Flag.Value)
                {
                    bubble.Show();
                }
                else
                {
                    bubble.Hide();
                }

                break;
            case "set_text":
                bubble.SetText(command.Text ?? string.Empty);
                break;
            case "set_header_text":
                bubble.SetHeaderText(command.HeaderText ?? command.Text ?? string.Empty);
                break;
            case "set_footer_text":
                bubble.SetFooterText(command.FooterText ?? command.Text ?? string.Empty);
                break;
            case "set_layout_mode" when !string.IsNullOrWhiteSpace(command.Mode):
                bubble.LayoutMode = command.Mode!;
                break;
            case "set_anchor_mode" when !string.IsNullOrWhiteSpace(command.Mode):
                bubble.AnchorMode = command.Mode!;
                break;
            case "set_screen_position" when command.X.HasValue && command.Y.HasValue:
                bubble.UseScreenSpace((float)command.X.Value, (float)command.Y.Value, command.Mode);
                break;
            case "set_screen_offset" when command.X.HasValue && command.Y.HasValue:
                bubble.SetScreenOffset((float)command.X.Value, (float)command.Y.Value);
                break;
            case "set_world_position" when command.X.HasValue && command.Y.HasValue && command.Z.HasValue:
                bubble.UseWorldSpace((float)command.X.Value, (float)command.Y.Value, (float)command.Z.Value);
                break;
            case "set_world_offset" when command.X.HasValue && command.Y.HasValue && command.Z.HasValue:
                bubble.SetWorldOffset((float)command.X.Value, (float)command.Y.Value, (float)command.Z.Value);
                break;
            case "attach_to_entity" when !string.IsNullOrWhiteSpace(command.TargetEntity):
                bubble.AttachToEntity(command.TargetEntity!, command.Flag ?? true);
                break;
            case "set_width" when command.Width.HasValue:
                bubble.Width = (float)command.Width.Value;
                break;
            case "set_padding" when command.X.HasValue && command.Y.HasValue:
                bubble.SetPadding((float)command.X.Value, (float)command.Y.Value);
                break;
            case "set_pivot" when command.X.HasValue && command.Y.HasValue:
                bubble.SetPivot((float)command.X.Value, (float)command.Y.Value);
                break;
            case "set_text_alignment" when !string.IsNullOrWhiteSpace(command.Mode):
                bubble.TextAlignment = command.Mode!;
                break;
            case "set_font_size" when command.Value.HasValue:
                bubble.FontSize = (float)command.Value.Value;
                break;
            case "set_header_font_size" when command.Value.HasValue:
                bubble.HeaderFontSize = (float)command.Value.Value;
                break;
            case "set_footer_font_size" when command.Value.HasValue:
                bubble.FooterFontSize = (float)command.Value.Value;
                break;
            case "set_background_color":
                bubble.BackgroundColor = GetCommandColor(command, bubble.BackgroundColor);
                break;
            case "set_border_color":
                bubble.BorderColor = GetCommandColor(command, bubble.BorderColor);
                break;
            case "set_text_color":
                bubble.TextColor = GetCommandColor(command, bubble.TextColor);
                break;
            case "set_header_text_color":
                bubble.HeaderTextColor = GetCommandColor(command, bubble.HeaderTextColor);
                break;
            case "set_footer_text_color":
                bubble.FooterTextColor = GetCommandColor(command, bubble.FooterTextColor);
                break;
            case "set_rounding" when command.Value.HasValue:
                bubble.Rounding = (float)command.Value.Value;
                break;
            case "set_border_thickness" when command.Value.HasValue:
                bubble.BorderThickness = (float)command.Value.Value;
                break;
            case "set_draw_order" when command.Index.HasValue:
                bubble.DrawOrder = command.Index.Value;
                break;
        }
    }

    private static void ApplyRuntimeCommand(PythonCommand command, RuntimeScene scene)
    {
        switch (command.Action?.ToLowerInvariant())
        {
            case "set_use_opencl" when command.Flag.HasValue:
                scene.Runtime.SetUseOpenCL(command.Flag.Value);
                break;
        }
    }

    private static void ApplySpriteCommand(PythonCommand command, RuntimeScene scene)
    {
        if (string.IsNullOrWhiteSpace(command.Sprite))
        {
            return;
        }

        RuntimeSpriteControl? sprite = scene.GetSprite(command.Sprite);
        if (sprite is null)
        {
            return;
        }

        switch (command.Action?.ToLowerInvariant())
        {
            case "set_position" when command.X.HasValue && command.Y.HasValue:
                sprite.SetPosition((float)command.X.Value, (float)command.Y.Value);
                break;
            case "set_size" when command.Width.HasValue && command.Height.HasValue:
                sprite.SetSize((float)command.Width.Value, (float)command.Height.Value);
                break;
            case "set_layout_mode" when !string.IsNullOrWhiteSpace(command.Mode):
                sprite.SetLayoutMode(command.Mode!);
                break;
            case "set_visible" when command.Flag.HasValue:
                sprite.Visible = command.Flag.Value;
                break;
            case "set_opacity" when command.Value.HasValue:
                sprite.Opacity = (float)command.Value.Value;
                break;
            case "set_texture" when command.Texture is not null:
                sprite.Texture = command.Texture;
                break;
            case "set_render_texture" when !string.IsNullOrWhiteSpace(command.Name):
                sprite.SetRenderTexture(command.Name!);
                break;
        }
    }

    private static void ApplyGuiCommand(PythonCommand command, RuntimeScene scene)
    {
        if (string.IsNullOrWhiteSpace(command.Control))
        {
            return;
        }

        RuntimeGuiControl? control = scene.GetGuiControl(command.Control);
        if (control is null)
        {
            return;
        }

        switch (command.Action?.ToLowerInvariant())
        {
            case "set_position" when command.X.HasValue && command.Y.HasValue:
                control.SetPosition((float)command.X.Value, (float)command.Y.Value);
                break;
            case "set_size" when command.Width.HasValue && command.Height.HasValue:
                control.SetSize((float)command.Width.Value, (float)command.Height.Value);
                break;
            case "set_visible" when command.Flag.HasValue:
                control.Visible = command.Flag.Value;
                break;
            case "set_text" when command.Text is not null:
                control.Text = command.Text;
                break;
            case "set_value" when command.Text is not null:
                control.Value = command.Text;
                break;
            case "set_checked" when command.Flag.HasValue:
                control.Checked = command.Flag.Value;
                break;
            case "set_progress" when command.Value.HasValue:
                control.Progress = (float)command.Value.Value;
                break;
            case "set_layout_mode" when !string.IsNullOrWhiteSpace(command.Mode):
                control.LayoutMode = command.Mode!;
                break;
            case "set_font_size" when command.Value.HasValue:
                control.FontSize = (float)command.Value.Value;
                break;
            case "set_word_wrap" when command.Flag.HasValue:
                control.WordWrap = command.Flag.Value;
                break;
            case "set_multiline" when command.Flag.HasValue:
                control.Multiline = command.Flag.Value;
                break;
            case "set_items" when command.Items is not null:
                control.SetItems(command.Items);
                break;
            case "set_selected_index" when command.Index.HasValue:
                control.SelectedIndex = command.Index.Value;
                break;
            case "replace_selection" when command.Text is not null:
                control.ReplaceSelection(command.Text);
                break;
        }
    }

    private static void ApplyInputCommand(PythonCommand command, RuntimeInput input)
    {
        switch (command.Action?.ToLowerInvariant())
        {
            case "set_clipboard_text" when command.Text is not null:
                input.SetClipboardText(command.Text);
                break;
            case "set_cursor_visible" when command.Flag.HasValue:
                input.SetCursorVisible(command.Flag.Value);
                break;
            case "set_cursor_locked" when command.Flag.HasValue:
                input.SetCursorLocked(command.Flag.Value, command.RawInput ?? false);
                break;
        }
    }

    private static void ApplyAudioCommand(PythonCommand command, RuntimeAudio audio)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return;
        }

        switch (command.Action?.ToLowerInvariant())
        {
            case "play":
                audio.Play(command.Name);
                break;
            case "pause":
                audio.Pause(command.Name);
                break;
            case "stop":
                audio.Stop(command.Name);
                break;
            case "set_volume" when command.Volume.HasValue:
                audio.SetVolume(command.Name, (float)command.Volume.Value);
                break;
            case "set_loop" when command.Flag.HasValue:
                audio.SetLoop(command.Name, command.Flag.Value);
                break;
        }
    }

    private static void ApplySceneCommand(PythonCommand command, RuntimeScene scene)
    {
        switch (command.Action?.ToLowerInvariant())
        {
            case "load_scene" when !string.IsNullOrWhiteSpace(command.Path):
                scene.LoadScene(command.Path);
                break;
        }
    }

    private void ApplyEntityCommand(PythonCommand command, RuntimeEntity entity, RuntimeEntity callbackEntity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio)
    {
        switch (command.Action?.ToLowerInvariant())
        {
            case "set_position" when TryGetVector(command, out float x, out float y, out float z):
                entity.SetPosition(x, y, z);
                break;
            case "translate" when TryGetVector(command, out float x, out float y, out float z):
                entity.Translate(x, y, z);
                break;
            case "set_scale" when TryGetVector(command, out float x, out float y, out float z):
                entity.SetScale(x, y, z);
                break;
            case "rotate_x" when command.Degrees.HasValue:
                entity.RotateX((float)command.Degrees.Value);
                break;
            case "rotate_y" when command.Degrees.HasValue:
                entity.RotateY((float)command.Degrees.Value);
                break;
            case "rotate_z" when command.Degrees.HasValue:
                entity.RotateZ((float)command.Degrees.Value);
                break;
            case "set_playing" when command.Flag.HasValue:
                entity.IsPlaying = command.Flag.Value;
                break;
            case "set_visible" when command.Flag.HasValue:
                entity.Visible = command.Flag.Value;
                break;
            case "set_playback_speed" when command.Value.HasValue:
                entity.PlaybackSpeed = (float)command.Value.Value;
                break;
            case "set_loop_motion" when command.Flag.HasValue:
                entity.LoopMotion = command.Flag.Value;
                break;
            case "set_reset_physics_on_motion_loop" when command.Flag.HasValue:
                entity.ResetPhysicsOnMotionLoop = command.Flag.Value;
                break;
            case "set_edge_enabled" when command.Flag.HasValue:
                entity.EnableEdge = command.Flag.Value;
                break;
            case "set_shadow_enabled" when command.Flag.HasValue:
                entity.EnableShadow = command.Flag.Value;
                break;
            case "set_water_interaction_enabled" when command.Flag.HasValue:
                entity.EnableWaterInteraction = command.Flag.Value;
                break;
            case "set_kill_on_water_contact" when command.Flag.HasValue:
                entity.KillOnWaterContact = command.Flag.Value;
                break;
            case "set_water_surface_interaction_enabled" when command.Flag.HasValue:
                entity.WaterInteractionEnabled = command.Flag.Value;
                break;
            case "set_water_interaction_radius" when command.Value.HasValue:
                entity.WaterInteractionRadius = (float)command.Value.Value;
                break;
            case "set_water_interaction_strength" when command.Value.HasValue:
                entity.WaterInteractionStrength = (float)command.Value.Value;
                break;
            case "set_particle_ripple_min_interval_seconds" when command.Value.HasValue:
                entity.ParticleRippleMinIntervalSeconds = (float)command.Value.Value;
                break;
            case "set_particle_ripple_merge_distance" when command.Value.HasValue:
                entity.ParticleRippleMergeDistance = (float)command.Value.Value;
                break;
            case "set_mirror_reflection_enabled" when command.Flag.HasValue:
                entity.MirrorReflectionEnabled = command.Flag.Value;
                break;
            case "set_plane_mirror_reflection_enabled" when command.Flag.HasValue:
                entity.PlaneMirrorReflectionEnabled = command.Flag.Value;
                break;
            case "set_plane_mirror_reflection_strength" when command.Value.HasValue:
                entity.PlaneMirrorReflectionStrength = (float)command.Value.Value;
                break;
            case "set_gerstner_waves_enabled" when command.Flag.HasValue:
                entity.GerstnerWavesEnabled = command.Flag.Value;
                break;
            case "set_gerstner_wave_count" when command.Value.HasValue:
                entity.GerstnerWaveCount = (int)Math.Round(command.Value.Value);
                break;
            case "set_gerstner_amplitude" when command.Value.HasValue:
                entity.GerstnerAmplitude = (float)command.Value.Value;
                break;
            case "set_gerstner_wavelength" when command.Value.HasValue:
                entity.GerstnerWavelength = (float)command.Value.Value;
                break;
            case "set_gerstner_speed" when command.Value.HasValue:
                entity.GerstnerSpeed = (float)command.Value.Value;
                break;
            case "set_gerstner_steepness" when command.Value.HasValue:
                entity.GerstnerSteepness = (float)command.Value.Value;
                break;
            case "set_gerstner_direction_degrees" when command.Value.HasValue:
                entity.GerstnerDirectionDegrees = (float)command.Value.Value;
                break;
            case "set_ripple_lifetime_seconds" when command.Value.HasValue:
                entity.RippleLifetimeSeconds = (float)command.Value.Value;
                break;
            case "set_ripple_wave_speed" when command.Value.HasValue:
                entity.RippleWaveSpeed = (float)command.Value.Value;
                break;
            case "set_ripple_frequency" when command.Value.HasValue:
                entity.RippleFrequency = (float)command.Value.Value;
                break;
            case "set_ripple_normal_strength" when command.Value.HasValue:
                entity.RippleNormalStrength = (float)command.Value.Value;
                break;
            case "set_draw_shadow_in_main_pass" when command.Flag.HasValue:
                entity.DrawShadowInMainPass = command.Flag.Value;
                break;
            case "apply_motion" when !string.IsNullOrWhiteSpace(command.Path):
                entity.ApplyMotion(command.Path);
                break;
            case "add_motion_layer" when !string.IsNullOrWhiteSpace(command.Path):
                entity.AddMotionLayer(command.Path, (float)(command.Weight ?? 1.0));
                break;
            case "set_motion_layers" when command.MotionLayers is not null:
                entity.SetMotionLayers(command.MotionLayers.Select(layer => new MotionLayerDefinition(
                    layer.Path ?? string.Empty,
                    (float)(layer.Weight ?? 1.0),
                    layer.ResetPhysicsOnLoop)));
                break;
            case "set_motion_layer_weight" when !string.IsNullOrWhiteSpace(command.Path) && command.Weight.HasValue:
                entity.SetMotionLayerWeight(command.Path, (float)command.Weight.Value);
                break;
            case "set_motion_layer_reset_physics_on_loop" when !string.IsNullOrWhiteSpace(command.Path) && command.Flag.HasValue:
                entity.SetMotionLayerResetPhysicsOnLoop(command.Path, command.Flag.Value);
                break;
            case "remove_motion_layer" when !string.IsNullOrWhiteSpace(command.Path):
                entity.RemoveMotionLayer(command.Path);
                break;
            case "clear_motion":
                entity.ClearMotion();
                break;
            case "play_motion":
                entity.PlayMotion();
                break;
            case "pause_motion":
                entity.PauseMotion();
                break;
            case "stop_motion":
                entity.StopMotion();
                break;
            case "reset_motion":
                entity.ResetMotion();
                break;
            case "reset_motion_physics":
                entity.ResetMotionPhysics();
                break;
            case "seek_motion_time" when command.Value.HasValue:
                entity.SeekMotionTime((float)command.Value.Value);
                break;
            case "seek_motion_frame" when command.Value.HasValue:
                entity.SeekMotionFrame((float)command.Value.Value);
                break;
            case "play_motion_layer" when !string.IsNullOrWhiteSpace(command.Path):
                entity.PlayMotionLayer(command.Path);
                break;
            case "pause_motion_layer" when !string.IsNullOrWhiteSpace(command.Path):
                entity.PauseMotionLayer(command.Path);
                break;
            case "set_motion_layer_time" when !string.IsNullOrWhiteSpace(command.Path) && command.Value.HasValue:
                entity.SetMotionLayerTime(command.Path, (float)command.Value.Value);
                break;
            case "set_motion_layer_frame" when !string.IsNullOrWhiteSpace(command.Path) && command.Value.HasValue:
                entity.SetMotionLayerFrame(command.Path, (float)command.Value.Value);
                break;
            case "set_morph_weight" when !string.IsNullOrWhiteSpace(command.Name) && command.Weight.HasValue:
                entity.SetMorphWeight(command.Name, (float)command.Weight.Value, command.Flag ?? true);
                break;
            case "set_morph_save_anim_weight" when !string.IsNullOrWhiteSpace(command.Name) && command.Weight.HasValue:
                entity.SetMorphSaveAnimWeight(command.Name, (float)command.Weight.Value);
                break;
            case "save_morph_anim_weight" when !string.IsNullOrWhiteSpace(command.Name):
                entity.SaveMorphAnimWeight(command.Name);
                break;
            case "save_anim_weight" when !string.IsNullOrWhiteSpace(command.Name):
                entity.SaveAnimWeight(command.Name);
                break;
            case "load_morph_anim_weight" when !string.IsNullOrWhiteSpace(command.Name):
                entity.LoadMorphAnimWeight(command.Name);
                break;
            case "clear_morph_anim_weight" when !string.IsNullOrWhiteSpace(command.Name):
                entity.ClearMorphAnimWeight(command.Name);
                break;
            case "clear_morph_weight_override" when !string.IsNullOrWhiteSpace(command.Name):
                entity.ClearMorphWeightOverride(command.Name);
                break;
            case "clear_morph_weight_overrides":
                entity.ClearMorphWeightOverrides();
                break;
            case "save_base_animation":
                entity.SaveBaseAnimation();
                break;
            case "load_base_animation":
                entity.LoadBaseAnimation();
                break;
            case "clear_base_animation":
                entity.ClearBaseAnimation();
                break;
            case "set_node_translate" when !string.IsNullOrWhiteSpace(command.Name) && TryGetVector(command, out float x, out float y, out float z):
                entity.SetNodeTranslate(command.Name, x, y, z, command.Flag ?? true);
                break;
            case "set_node_rotate" when !string.IsNullOrWhiteSpace(command.Name) && TryGetQuaternion(command, out Quaternion rotate):
                entity.SetNodeRotate(command.Name, rotate, command.Flag ?? true);
                break;
            case "set_node_rotate_euler" when !string.IsNullOrWhiteSpace(command.Name) && TryGetVector(command, out float x, out float y, out float z):
                entity.SetNodeRotateEuler(command.Name, x, y, z, command.Flag ?? true);
                break;
            case "set_node_scale" when !string.IsNullOrWhiteSpace(command.Name) && TryGetVector(command, out float x, out float y, out float z):
                entity.SetNodeScale(command.Name, x, y, z, command.Flag ?? true);
                break;
            case "set_node_anim_translate" when !string.IsNullOrWhiteSpace(command.Name) && TryGetVector(command, out float x, out float y, out float z):
                entity.SetNodeAnimTranslate(command.Name, x, y, z, command.Flag ?? true);
                break;
            case "set_node_anim_rotate" when !string.IsNullOrWhiteSpace(command.Name) && TryGetQuaternion(command, out Quaternion rotate):
                entity.SetNodeAnimRotate(command.Name, rotate, command.Flag ?? true);
                break;
            case "set_node_anim_rotate_euler" when !string.IsNullOrWhiteSpace(command.Name) && TryGetVector(command, out float x, out float y, out float z):
                entity.SetNodeAnimRotateEuler(command.Name, x, y, z, command.Flag ?? true);
                break;
            case "save_node_base_animation" when !string.IsNullOrWhiteSpace(command.Name):
                entity.SaveNodeBaseAnimation(command.Name);
                break;
            case "load_node_base_animation" when !string.IsNullOrWhiteSpace(command.Name):
                entity.LoadNodeBaseAnimation(command.Name);
                break;
            case "clear_node_base_animation" when !string.IsNullOrWhiteSpace(command.Name):
                entity.ClearNodeBaseAnimation(command.Name);
                break;
            case "clear_node_overrides" when !string.IsNullOrWhiteSpace(command.Name):
                entity.ClearNodeOverrides(command.Name);
                break;
            case "clear_all_node_overrides":
                entity.ClearAllNodeOverrides();
                break;
            case "set_material_texture" when command.Texture is not null && TryApplyMaterialTexture(entity, command):
                break;
            case "set_material_render_texture" when !string.IsNullOrWhiteSpace(command.Name) && TryApplyMaterialRenderTexture(entity, command):
                break;
            case "clear_material_texture_override" when TryApplyClearMaterialTextureOverride(entity, command):
                break;
            case "clear_material_texture_overrides":
                entity.ClearMaterialTextureOverrides();
                break;
            case "set_custom_shader" when !string.IsNullOrWhiteSpace(command.VertexShader) && !string.IsNullOrWhiteSpace(command.FragmentShader):
                entity.SetCustomShader(command.VertexShader, command.FragmentShader);
                break;
            case "clear_custom_shader":
                entity.ClearCustomShader();
                break;
            case "set_custom_shader_float" when !string.IsNullOrWhiteSpace(command.Name) && command.Value.HasValue:
                entity.SetCustomShaderFloat(command.Name, (float)command.Value.Value);
                break;
            case "set_custom_shader_int" when !string.IsNullOrWhiteSpace(command.Name) && command.Index.HasValue:
                entity.SetCustomShaderInt(command.Name, command.Index.Value);
                break;
            case "set_custom_shader_vector2" when !string.IsNullOrWhiteSpace(command.Name) && command.X.HasValue && command.Y.HasValue:
                entity.SetCustomShaderVector2(command.Name, (float)command.X.Value, (float)command.Y.Value);
                break;
            case "set_custom_shader_vector3" when !string.IsNullOrWhiteSpace(command.Name) && TryGetVector(command, out float x, out float y, out float z):
                entity.SetCustomShaderVector3(command.Name, x, y, z);
                break;
            case "set_custom_shader_vector4" when !string.IsNullOrWhiteSpace(command.Name) && TryGetVector4(command, out Vector4 vector):
                entity.SetCustomShaderVector4(command.Name, vector.X, vector.Y, vector.Z, vector.W);
                break;
            case "set_custom_shader_color" when !string.IsNullOrWhiteSpace(command.Name):
                Vector4 color = GetCommandColor(command, Vector4.One);
                entity.SetCustomShaderColor(command.Name, color.X, color.Y, color.Z, color.W);
                break;
            case "clear_custom_shader_uniform" when !string.IsNullOrWhiteSpace(command.Name):
                entity.ClearCustomShaderUniform(command.Name);
                break;
            case "clear_custom_shader_uniforms":
                entity.ClearCustomShaderUniforms();
                break;
            case "speak" when !string.IsNullOrWhiteSpace(command.Text):
                entity.Speak(command.Text, new RuntimeVoiceOptions
                {
                    SpeakerId = command.SpeakerId,
                    Speed = command.Speed.HasValue ? (float)command.Speed.Value : null,
                    Volume = command.Volume.HasValue ? (float)command.Volume.Value : null,
                    OnCompleted = string.IsNullOrWhiteSpace(command.Callback)
                        ? null
                        : () => entity.DispatchSpeechCallback(command.Callback)
                });
                break;
            case "stop_speaking":
                entity.StopSpeaking();
                break;
            case "bind_relation" when !string.IsNullOrWhiteSpace(command.Name):
                entity.BindRelation(
                    command.Name,
                    command.BindComponentTransform ?? true,
                    command.BindLighting ?? false);
                break;
            case "clear_relation":
                entity.ClearRelationBinding();
                break;
            case "set_capsule_collider" when command.Radius.HasValue && command.Height.HasValue && TryGetVector(command, out float x, out float y, out float z):
                entity.SetCapsuleCollider(
                    (float)command.Radius.Value,
                    (float)command.Height.Value,
                    x,
                    y,
                    z,
                    command.Axis ?? "y");
                break;
            case "add_capsule_collider" when command.Radius.HasValue && command.Height.HasValue && TryGetVector(command, out float x, out float y, out float z):
                entity.AddCapsuleCollider(
                    command.Name ?? "Capsule Collider",
                    (float)command.Radius.Value,
                    (float)command.Height.Value,
                    x,
                    y,
                    z,
                    command.Axis ?? "y",
                    (float)(command.RotationX ?? 0.0),
                    (float)(command.RotationY ?? 0.0),
                    (float)(command.RotationZ ?? 0.0));
                break;
            case "add_box_collider" when command.SizeX.HasValue && command.SizeY.HasValue && command.SizeZ.HasValue && TryGetVector(command, out float x, out float y, out float z):
                entity.AddBoxCollider(
                    command.Name ?? "Box Collider",
                    (float)command.SizeX.Value,
                    (float)command.SizeY.Value,
                    (float)command.SizeZ.Value,
                    x,
                    y,
                    z,
                    (float)(command.RotationX ?? 0.0),
                    (float)(command.RotationY ?? 0.0),
                    (float)(command.RotationZ ?? 0.0));
                break;
            case "add_mesh_collider":
                entity.AddMeshCollider(
                    command.Name ?? "Mesh Collider",
                    command.Flag ?? true,
                    (float)(command.Value ?? 55.0),
                    (float)(command.OffsetX ?? 0.0),
                    (float)(command.OffsetY ?? 0.0),
                    (float)(command.OffsetZ ?? 0.0),
                    (float)(command.SizeX ?? 1.0),
                    (float)(command.SizeY ?? 1.0),
                    (float)(command.SizeZ ?? 1.0),
                    (float)(command.RotationX ?? 0.0),
                    (float)(command.RotationY ?? 0.0),
                    (float)(command.RotationZ ?? 0.0));
                break;
            case "remove_collider" when !string.IsNullOrWhiteSpace(command.Name):
                entity.RemoveCollider(command.Name);
                break;
            case "clear_colliders":
                entity.ClearColliders();
                break;
            case "disable_collider":
                entity.DisableCollider();
                break;
        }
    }

    private static bool TryApplyMaterialTexture(RuntimeEntity entity, PythonCommand command)
    {
        if (command.Texture is null)
        {
            return false;
        }

        if (command.Index.HasValue)
        {
            return entity.SetMaterialTexture(command.Index.Value, command.Texture);
        }

        return !string.IsNullOrWhiteSpace(command.Name) && entity.SetMaterialTexture(command.Name!, command.Texture);
    }

    private static bool TryApplyMaterialRenderTexture(RuntimeEntity entity, PythonCommand command)
    {
        if (command.Index.HasValue)
        {
            return entity.SetMaterialRenderTexture(command.Index.Value, command.Name!);
        }

        return !string.IsNullOrWhiteSpace(command.Material) && entity.SetMaterialRenderTexture(command.Material!, command.Name!);
    }

    private static bool TryApplyClearMaterialTextureOverride(RuntimeEntity entity, PythonCommand command)
    {
        if (command.Index.HasValue)
        {
            entity.ClearMaterialTextureOverride(command.Index.Value);
            return true;
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return false;
        }

        int index = Array.FindIndex(entity.MaterialNames.ToArray(), name => string.Equals(name, command.Name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        entity.ClearMaterialTextureOverride(index);
        return true;
    }

    private static bool TryGetVector(PythonCommand command, out float x, out float y, out float z)
    {
        x = y = z = 0.0f;
        if (!command.X.HasValue || !command.Y.HasValue || !command.Z.HasValue)
        {
            return false;
        }

        x = (float)command.X.Value;
        y = (float)command.Y.Value;
        z = (float)command.Z.Value;
        return true;
    }

    private static bool TryGetQuaternion(PythonCommand command, out Quaternion rotation)
    {
        rotation = Quaternion.Identity;
        if (!command.X.HasValue || !command.Y.HasValue || !command.Z.HasValue || !command.W.HasValue)
        {
            return false;
        }

        rotation = new Quaternion(
            (float)command.X.Value,
            (float)command.Y.Value,
            (float)command.Z.Value,
            (float)command.W.Value);
        return true;
    }

    private static bool TryGetVector4(PythonCommand command, out Vector4 vector)
    {
        vector = default;
        if (!command.X.HasValue || !command.Y.HasValue || !command.Z.HasValue || !command.W.HasValue)
        {
            return false;
        }

        vector = new Vector4(
            (float)command.X.Value,
            (float)command.Y.Value,
            (float)command.Z.Value,
            (float)command.W.Value);
        return true;
    }

    private static bool TryGetLookAt(PythonCommand command, out Vector3 position, out Vector3 target)
    {
        position = default;
        target = default;
        if (!command.X.HasValue || !command.Y.HasValue || !command.Z.HasValue
            || !command.TargetX.HasValue || !command.TargetY.HasValue || !command.TargetZ.HasValue)
        {
            return false;
        }

        position = new Vector3((float)command.X.Value, (float)command.Y.Value, (float)command.Z.Value);
        target = new Vector3((float)command.TargetX.Value, (float)command.TargetY.Value, (float)command.TargetZ.Value);
        return true;
    }

    private static float? ToFloat(double? value)
    {
        return value.HasValue ? (float)value.Value : null;
    }

    private static Vector4 GetCommandColor(PythonCommand command, Vector4 fallback)
    {
        return command.ColorR.HasValue || command.ColorG.HasValue || command.ColorB.HasValue || command.ColorA.HasValue
            ? new Vector4(
                (float)(command.ColorR ?? fallback.X),
                (float)(command.ColorG ?? fallback.Y),
                (float)(command.ColorB ?? fallback.Z),
                (float)(command.ColorA ?? fallback.W))
            : fallback;
    }

    private sealed class PythonEvent
    {
        public string Event { get; set; } = string.Empty;

        public double DeltaSeconds { get; set; }

        public string ControlId { get; set; } = string.Empty;

        public string ControlName { get; set; } = string.Empty;

        public string GuiEventName { get; set; } = string.Empty;

        public string SpriteId { get; set; } = string.Empty;

        public string SpriteName { get; set; } = string.Empty;

        public string SpriteEventName { get; set; } = string.Empty;

        public string TrayMenuItemId { get; set; } = string.Empty;

        public string TrayMenuItemText { get; set; } = string.Empty;

        public string TrayMenuEventName { get; set; } = string.Empty;

        public float LoadingProgress { get; set; }

        public string LoadingMessage { get; set; } = string.Empty;

        public string SpeechCallback { get; set; } = string.Empty;

        public PythonLlmEvent LlmEvent { get; set; } = new();

        public PythonAsrEvent AsrEvent { get; set; } = new();

        public PythonRealtimeVoiceEvent RealtimeVoiceEvent { get; set; } = new();

        public PythonEntity Entity { get; set; } = new();

        public PythonScene Scene { get; set; } = new();

        public PythonInput Input { get; set; } = new();

        public static PythonEvent Create(
            string eventName,
            RuntimeEntity entity,
            RuntimeScene scene,
            RuntimeInput input,
            double deltaSeconds,
            string controlId,
            string controlName,
            string guiEventName,
            string spriteId,
            string spriteName,
            string spriteEventName,
            string trayMenuItemId,
            string trayMenuItemText,
            string trayMenuEventName,
            float loadingProgress,
            string loadingMessage,
            string speechCallback,
            RuntimeLlmScriptEvent? llmEvent,
            RuntimeAsrScriptEvent? asrEvent,
            RuntimeRealtimeVoiceScriptEvent? realtimeVoiceEvent)
        {
            return new PythonEvent
            {
                Event = eventName,
                DeltaSeconds = deltaSeconds,
                ControlId = controlId,
                ControlName = controlName,
                GuiEventName = guiEventName,
                SpriteId = spriteId,
                SpriteName = spriteName,
                SpriteEventName = spriteEventName,
                TrayMenuItemId = trayMenuItemId,
                TrayMenuItemText = trayMenuItemText,
                TrayMenuEventName = trayMenuEventName,
                LoadingProgress = loadingProgress,
                LoadingMessage = loadingMessage,
                SpeechCallback = speechCallback,
                LlmEvent = PythonLlmEvent.FromRuntime(llmEvent),
                AsrEvent = PythonAsrEvent.FromRuntime(asrEvent),
                RealtimeVoiceEvent = PythonRealtimeVoiceEvent.FromRuntime(realtimeVoiceEvent),
                Entity = PythonEntity.FromRuntime(entity),
                Scene = new PythonScene
                {
                    Name = scene.Name,
                    MainCamera = scene.Camera.MainCamera,
                    CameraNames = scene.Camera.CameraNames.ToArray(),
                    RenderTextureNames = scene.Camera.RenderTextureNames.ToArray(),
                    Entities = scene.Entities.Select(PythonEntity.FromRuntime).ToArray(),
                    GuiControls = scene.GuiControls.Select(PythonGuiControl.FromRuntime).ToArray(),
                    Sprites = scene.Sprites.Select(PythonSprite.FromRuntime).ToArray(),
                    Camera = PythonCamera.FromRuntime(scene.Camera),
                    Window = PythonWindow.FromRuntime(scene.Window),
                    Runtime = PythonRuntime.FromRuntime(scene.Runtime),
                    Llm = PythonLlmSettings.FromRuntime(scene.Llm),
                    Asr = PythonAsrSettings.FromRuntime(scene.Asr),
                    RealtimeVoice = PythonRealtimeVoiceSettings.FromRuntime(scene.RealtimeVoice),
                    Bubble = PythonBubbleState.FromRuntime(scene.Bubble),
                    Network = new PythonNetwork(),
                    Performance = PythonPerformance.FromRuntime(scene.Performance)
                },
                Input = PythonInput.FromRuntime(input)
            };
        }
    }

    private sealed class PythonLlmEvent
    {
        public string RequestId { get; set; } = string.Empty;

        public string EventName { get; set; } = string.Empty;

        public string Delta { get; set; } = string.Empty;

        public string AccumulatedText { get; set; } = string.Empty;

        public bool IsFinal { get; set; }

        public string Error { get; set; } = string.Empty;

        public string CallbackName { get; set; } = string.Empty;

        public PythonLlmToolCall? ToolCall { get; set; }

        public string ToolResult { get; set; } = string.Empty;

        public static PythonLlmEvent FromRuntime(RuntimeLlmScriptEvent? llmEvent)
        {
            return llmEvent is null
                ? new PythonLlmEvent()
                : new PythonLlmEvent
                {
                    RequestId = llmEvent.RequestId,
                    EventName = llmEvent.EventName,
                    Delta = llmEvent.Delta,
                    AccumulatedText = llmEvent.AccumulatedText,
                    IsFinal = llmEvent.IsFinal,
                    Error = llmEvent.Error,
                    CallbackName = llmEvent.CallbackName,
                    ToolCall = PythonLlmToolCall.FromRuntime(llmEvent.ToolCall),
                    ToolResult = llmEvent.ToolResult ?? string.Empty
                };
        }
    }

    private sealed class PythonLlmToolCall
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string ArgumentsJson { get; set; } = string.Empty;

        public static PythonLlmToolCall? FromRuntime(RuntimeLlmToolCall? toolCall)
        {
            return toolCall is null
                ? null
                : new PythonLlmToolCall
                {
                    Id = toolCall.Id,
                    Name = toolCall.Name,
                    ArgumentsJson = toolCall.ArgumentsJson
                };
        }
    }

    private sealed class PythonToolResult
    {
        public string Result { get; set; } = string.Empty;
    }

    private sealed class PythonRealtimeVoiceEvent
    {
        public string RequestId { get; set; } = string.Empty;

        public string EventName { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public string Delta { get; set; } = string.Empty;

        public string AccumulatedText { get; set; } = string.Empty;

        public bool IsFinal { get; set; }

        public string Error { get; set; } = string.Empty;

        public string CallbackName { get; set; } = string.Empty;

        public string WakeWord { get; set; } = string.Empty;

        public string RecognizedText { get; set; } = string.Empty;

        public static PythonRealtimeVoiceEvent FromRuntime(RuntimeRealtimeVoiceScriptEvent? realtimeVoiceEvent)
        {
            return realtimeVoiceEvent is null
                ? new PythonRealtimeVoiceEvent()
                : new PythonRealtimeVoiceEvent
                {
                    RequestId = realtimeVoiceEvent.RequestId,
                    EventName = realtimeVoiceEvent.EventName,
                    Text = realtimeVoiceEvent.Text,
                    Delta = realtimeVoiceEvent.Delta,
                    AccumulatedText = realtimeVoiceEvent.AccumulatedText,
                    IsFinal = realtimeVoiceEvent.IsFinal,
                    Error = realtimeVoiceEvent.Error,
                    CallbackName = realtimeVoiceEvent.CallbackName,
                    WakeWord = realtimeVoiceEvent.WakeWord,
                    RecognizedText = realtimeVoiceEvent.RecognizedText
                };
        }
    }

    private sealed class PythonAsrEvent
    {
        public string RequestId { get; set; } = string.Empty;

        public string EventName { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public bool IsFinal { get; set; }

        public string Error { get; set; } = string.Empty;

        public string CallbackName { get; set; } = string.Empty;

        public double OffsetSeconds { get; set; }

        public string WakeWord { get; set; } = string.Empty;

        public string RecognizedText { get; set; } = string.Empty;

        public static PythonAsrEvent FromRuntime(RuntimeAsrScriptEvent? asrEvent)
        {
            return asrEvent is null
                ? new PythonAsrEvent()
                : new PythonAsrEvent
                {
                    RequestId = asrEvent.RequestId,
                    EventName = asrEvent.EventName,
                    Text = asrEvent.Text,
                    IsFinal = asrEvent.IsFinal,
                    Error = asrEvent.Error,
                    CallbackName = asrEvent.CallbackName,
                    OffsetSeconds = asrEvent.OffsetSeconds,
                    WakeWord = asrEvent.WakeWord,
                    RecognizedText = asrEvent.RecognizedText
                };
        }
    }

    private sealed class PythonEntity
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public float[] Position { get; set; } = [0.0f, 0.0f, 0.0f];

        public float[] Scale { get; set; } = [1.0f, 1.0f, 1.0f];

        public float[] Rotation { get; set; } = [0.0f, 0.0f, 0.0f, 1.0f];

        public string[] MaterialNames { get; set; } = [];

        public string[] MorphNames { get; set; } = [];

        public Dictionary<string, float> MorphWeights { get; set; } = [];

        public Dictionary<string, float> MorphSaveAnimWeights { get; set; } = [];

        public string[] NodeNames { get; set; } = [];

        public Dictionary<string, PythonNodeState> Nodes { get; set; } = [];

        public PythonCollider Collider { get; set; } = new();

        public PythonCollider[] Colliders { get; set; } = [];

        public bool EnableWaterInteraction { get; set; }

        public bool KillOnWaterContact { get; set; }

        public bool WaterInteractionEnabled { get; set; }

        public float WaterInteractionRadius { get; set; }

        public float WaterInteractionStrength { get; set; }

        public float ParticleRippleMinIntervalSeconds { get; set; }

        public float ParticleRippleMergeDistance { get; set; }

        public bool MirrorReflectionEnabled { get; set; }

        public bool PlaneMirrorReflectionEnabled { get; set; }

        public float PlaneMirrorReflectionStrength { get; set; }

        public bool GerstnerWavesEnabled { get; set; }

        public int GerstnerWaveCount { get; set; }

        public float GerstnerAmplitude { get; set; }

        public float GerstnerWavelength { get; set; }

        public float GerstnerSpeed { get; set; }

        public float GerstnerSteepness { get; set; }

        public float GerstnerDirectionDegrees { get; set; }

        public float RippleLifetimeSeconds { get; set; }

        public float RippleWaveSpeed { get; set; }

        public float RippleFrequency { get; set; }

        public float RippleNormalStrength { get; set; }

        public static PythonEntity FromRuntime(RuntimeEntity entity)
        {
            PythonCollider[] colliders = entity.EffectiveColliders
                .Select(collider => PythonCollider.FromRuntime(entity, collider))
                .ToArray();
            return new PythonEntity
            {
                Id = entity.Id,
                Name = entity.Name,
                Type = entity.Type,
                Position = [entity.Position.X, entity.Position.Y, entity.Position.Z],
                Scale = [entity.Scale.X, entity.Scale.Y, entity.Scale.Z],
                Rotation = [entity.Rotation.X, entity.Rotation.Y, entity.Rotation.Z, entity.Rotation.W],
                MaterialNames = entity.MaterialNames.ToArray(),
                MorphNames = entity.MorphNames.ToArray(),
                MorphWeights = new Dictionary<string, float>(entity.MorphWeights, StringComparer.Ordinal),
                MorphSaveAnimWeights = new Dictionary<string, float>(entity.MorphSaveAnimWeights, StringComparer.Ordinal),
                NodeNames = entity.NodeNames.ToArray(),
                Nodes = entity.NodeNames
                    .Select(name => entity.TryGetNodeState(name, out PmxNodeState state)
                        ? (Name: name, State: PythonNodeState.FromState(state))
                        : (Name: name, State: null))
                    .Where(item => item.State is not null)
                    .ToDictionary(item => item.Name, item => item.State!, StringComparer.Ordinal),
                Collider = colliders.FirstOrDefault() ?? new PythonCollider(),
                Colliders = colliders,
                EnableWaterInteraction = entity.EnableWaterInteraction,
                KillOnWaterContact = entity.KillOnWaterContact,
                WaterInteractionEnabled = entity.WaterInteractionEnabled,
                WaterInteractionRadius = entity.WaterInteractionRadius,
                WaterInteractionStrength = entity.WaterInteractionStrength,
                ParticleRippleMinIntervalSeconds = entity.ParticleRippleMinIntervalSeconds,
                ParticleRippleMergeDistance = entity.ParticleRippleMergeDistance,
                MirrorReflectionEnabled = entity.MirrorReflectionEnabled,
                PlaneMirrorReflectionEnabled = entity.PlaneMirrorReflectionEnabled,
                PlaneMirrorReflectionStrength = entity.PlaneMirrorReflectionStrength,
                GerstnerWavesEnabled = entity.GerstnerWavesEnabled,
                GerstnerWaveCount = entity.GerstnerWaveCount,
                GerstnerAmplitude = entity.GerstnerAmplitude,
                GerstnerWavelength = entity.GerstnerWavelength,
                GerstnerSpeed = entity.GerstnerSpeed,
                GerstnerSteepness = entity.GerstnerSteepness,
                GerstnerDirectionDegrees = entity.GerstnerDirectionDegrees,
                RippleLifetimeSeconds = entity.RippleLifetimeSeconds,
                RippleWaveSpeed = entity.RippleWaveSpeed,
                RippleFrequency = entity.RippleFrequency,
                RippleNormalStrength = entity.RippleNormalStrength
            };
        }
    }

    private sealed class PythonNodeState
    {
        public string Name { get; set; } = string.Empty;

        public float[] Translate { get; set; } = [0.0f, 0.0f, 0.0f];

        public float[] Rotate { get; set; } = [0.0f, 0.0f, 0.0f, 1.0f];

        public float[] Scale { get; set; } = [1.0f, 1.0f, 1.0f];

        public float[] AnimTranslate { get; set; } = [0.0f, 0.0f, 0.0f];

        public float[] AnimRotate { get; set; } = [0.0f, 0.0f, 0.0f, 1.0f];

        public float[] BaseAnimTranslate { get; set; } = [0.0f, 0.0f, 0.0f];

        public float[] BaseAnimRotate { get; set; } = [0.0f, 0.0f, 0.0f, 1.0f];

        public static PythonNodeState FromState(PmxNodeState state)
        {
            return new PythonNodeState
            {
                Name = state.Name,
                Translate = [state.Translate.X, state.Translate.Y, state.Translate.Z],
                Rotate = [state.Rotate.X, state.Rotate.Y, state.Rotate.Z, state.Rotate.W],
                Scale = [state.Scale.X, state.Scale.Y, state.Scale.Z],
                AnimTranslate = [state.AnimTranslate.X, state.AnimTranslate.Y, state.AnimTranslate.Z],
                AnimRotate = [state.AnimRotate.X, state.AnimRotate.Y, state.AnimRotate.Z, state.AnimRotate.W],
                BaseAnimTranslate = [state.BaseAnimTranslate.X, state.BaseAnimTranslate.Y, state.BaseAnimTranslate.Z],
                BaseAnimRotate = [state.BaseAnimRotate.X, state.BaseAnimRotate.Y, state.BaseAnimRotate.Z, state.BaseAnimRotate.W]
            };
        }
    }

    private sealed class PythonCollider
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        public string Shape { get; set; } = "capsule";

        public string BoundBoneName { get; set; } = string.Empty;

        public float[] Center { get; set; } = [0.0f, 1.0f, 0.0f];

        public float[] Position { get; set; } = [0.0f, 1.0f, 0.0f];

        public float[] RotationDegrees { get; set; } = [0.0f, 0.0f, 0.0f];

        public float[] Size { get; set; } = [1.0f, 1.0f, 1.0f];

        public float Radius { get; set; }

        public float Height { get; set; }

        public string Axis { get; set; } = "y";

        public bool Walkable { get; set; }

        public float MaxSlopeDegrees { get; set; }

        public PythonColliderGeometry? World { get; set; }

        public static PythonCollider FromRuntime(RuntimeEntity entity, ColliderSettings collider)
        {
            PythonCollider snapshot = FromSettings(collider);
            if (collider.Enabled && !string.Equals(collider.Shape, "mesh", StringComparison.OrdinalIgnoreCase))
            {
                ColliderGeometry geometry = CollisionGeometry.CreateCollider(collider, entity.GetColliderParentWorld(collider));
                snapshot.World = PythonColliderGeometry.FromGeometry(geometry);
            }

            return snapshot;
        }

        public static PythonCollider FromSettings(ColliderSettings collider)
        {
            return new PythonCollider
            {
                Id = collider.Id,
                Name = collider.Name,
                Enabled = collider.Enabled,
                Shape = collider.Shape,
                BoundBoneName = collider.BoundBoneName,
                Center = [collider.Position.X, collider.Position.Y, collider.Position.Z],
                Position = [collider.Position.X, collider.Position.Y, collider.Position.Z],
                RotationDegrees = [collider.RotationDegrees.X, collider.RotationDegrees.Y, collider.RotationDegrees.Z],
                Size = [collider.Size.X, collider.Size.Y, collider.Size.Z],
                Radius = collider.Radius,
                Height = collider.Height,
                Axis = collider.Axis,
                Walkable = collider.Walkable,
                MaxSlopeDegrees = collider.MaxSlopeDegrees
            };
        }
    }

    private sealed class PythonColliderGeometry
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Shape { get; set; } = "capsule";

        public float[] Center { get; set; } = [0.0f, 0.0f, 0.0f];

        public float[] Start { get; set; } = [0.0f, 0.0f, 0.0f];

        public float[] End { get; set; } = [0.0f, 0.0f, 0.0f];

        public float Radius { get; set; }

        public float[] AxisX { get; set; } = [1.0f, 0.0f, 0.0f];

        public float[] AxisY { get; set; } = [0.0f, 1.0f, 0.0f];

        public float[] AxisZ { get; set; } = [0.0f, 0.0f, 1.0f];

        public float[] HalfExtents { get; set; } = [0.5f, 0.5f, 0.5f];

        public static PythonColliderGeometry FromGeometry(ColliderGeometry geometry)
        {
            return geometry.Shape == "box"
                ? new PythonColliderGeometry
                {
                    Id = geometry.Id,
                    Name = geometry.Name,
                    Shape = "box",
                    Center = ToArray(geometry.Box.Center),
                    AxisX = ToArray(geometry.Box.AxisX),
                    AxisY = ToArray(geometry.Box.AxisY),
                    AxisZ = ToArray(geometry.Box.AxisZ),
                    HalfExtents = ToArray(geometry.Box.HalfExtents)
                }
                : new PythonColliderGeometry
                {
                    Id = geometry.Id,
                    Name = geometry.Name,
                    Shape = "capsule",
                    Center = ToArray(geometry.Capsule.Center),
                    Start = ToArray(geometry.Capsule.Start),
                    End = ToArray(geometry.Capsule.End),
                    Radius = geometry.Capsule.Radius
                };
        }

        private static float[] ToArray(Vector3 value) => [value.X, value.Y, value.Z];
    }

    private sealed class PythonScene
    {
        public string Name { get; set; } = string.Empty;

        public string MainCamera { get; set; } = string.Empty;

        public string[] CameraNames { get; set; } = [];

        public string[] RenderTextureNames { get; set; } = [];

        public PythonEntity[] Entities { get; set; } = [];

        public PythonGuiControl[] GuiControls { get; set; } = [];

        public PythonSprite[] Sprites { get; set; } = [];

        public PythonCamera Camera { get; set; } = new();

        public PythonWindow Window { get; set; } = new();

        public PythonRuntime Runtime { get; set; } = new();

        public PythonLlmSettings Llm { get; set; } = new();

        public PythonAsrSettings Asr { get; set; } = new();

        public PythonRealtimeVoiceSettings RealtimeVoice { get; set; } = new();

        public PythonBubbleState Bubble { get; set; } = new();

        public PythonNetwork Network { get; set; } = new();

        public PythonPerformance Performance { get; set; } = new();
    }

    private sealed class PythonNetwork;

    private sealed class PythonBubbleState
    {
        public int Count { get; set; }

        public string[] Names { get; set; } = [];

        public string[] VisibleNames { get; set; } = [];

        public static PythonBubbleState FromRuntime(RuntimeDialogueBubbleManager bubble)
        {
            return new PythonBubbleState
            {
                Count = bubble.Count,
                Names = bubble.Names.ToArray(),
                VisibleNames = bubble.VisibleNames.ToArray()
            };
        }
    }

    private sealed class PythonRealtimeVoiceSettings
    {
        public bool Enabled { get; set; }

        public string BaseUrl { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public string Voice { get; set; } = string.Empty;

        public bool WakeWordEnabled { get; set; }

        public string[] WakeWords { get; set; } = [];

        public int? InputDeviceIndex { get; set; }

        public bool MicrophoneInputAvailable { get; set; }

        public string MicrophoneUnavailableReason { get; set; } = string.Empty;

        public static PythonRealtimeVoiceSettings FromRuntime(RuntimeRealtimeVoice realtimeVoice)
        {
            return new PythonRealtimeVoiceSettings
            {
                Enabled = realtimeVoice.Enabled,
                BaseUrl = realtimeVoice.BaseUrl,
                Model = realtimeVoice.Model,
                Voice = realtimeVoice.Voice,
                WakeWordEnabled = realtimeVoice.WakeWordEnabled,
                WakeWords = realtimeVoice.WakeWords.ToArray(),
                InputDeviceIndex = realtimeVoice.InputDeviceIndex,
                MicrophoneInputAvailable = realtimeVoice.MicrophoneInputAvailable,
                MicrophoneUnavailableReason = realtimeVoice.MicrophoneUnavailableReason
            };
        }
    }

    private sealed class PythonAsrSettings
    {
        public bool Enabled { get; set; }

        public string Provider { get; set; } = string.Empty;

        public int? InputDeviceIndex { get; set; }

        public float PartialResultIntervalSeconds { get; set; }

        public bool IsRecording { get; set; }

        public bool IsWakeWordMonitoring { get; set; }

        public bool MicrophoneInputAvailable { get; set; }

        public string MicrophoneUnavailableReason { get; set; } = string.Empty;

        public static PythonAsrSettings FromRuntime(RuntimeAsr asr)
        {
            return new PythonAsrSettings
            {
                Enabled = asr.Enabled,
                Provider = asr.Provider,
                InputDeviceIndex = asr.InputDeviceIndex,
                PartialResultIntervalSeconds = asr.PartialResultIntervalSeconds,
                IsRecording = asr.IsRecording,
                IsWakeWordMonitoring = asr.IsWakeWordMonitoring,
                MicrophoneInputAvailable = asr.MicrophoneInputAvailable,
                MicrophoneUnavailableReason = asr.MicrophoneUnavailableReason
            };
        }
    }

    private sealed class PythonRuntime
    {
        public bool UseOpenCl { get; set; }

        public bool IsUsingOpenCl { get; set; }

        public string ComputeBackend { get; set; } = "CPU";

        public static PythonRuntime FromRuntime(RuntimeProjectControl runtime)
        {
            return new PythonRuntime
            {
                UseOpenCl = runtime.UseOpenCL,
                IsUsingOpenCl = runtime.IsUsingOpenCL,
                ComputeBackend = runtime.ComputeBackend
            };
        }
    }

    private sealed class PythonPerformance
    {
        public double Fps { get; set; }

        public double RawFps { get; set; }

        public double DeltaSeconds { get; set; }

        public double TotalSeconds { get; set; }

        public long FrameCount { get; set; }

        public static PythonPerformance FromRuntime(RuntimePerformance performance)
        {
            return new PythonPerformance
            {
                Fps = performance.Fps,
                RawFps = performance.RawFps,
                DeltaSeconds = performance.DeltaSeconds,
                TotalSeconds = performance.TotalSeconds,
                FrameCount = performance.FrameCount
            };
        }
    }

    private sealed class PythonLlmSettings
    {
        public bool Enabled { get; set; }

        public string Provider { get; set; } = string.Empty;

        public string BaseUrl { get; set; } = string.Empty;

        public string ApiKey { get; set; } = string.Empty;

        public string ApiKeyEnvironmentVariable { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public string ChatCompletionsPath { get; set; } = string.Empty;

        public int TimeoutSeconds { get; set; }

        public float? DefaultTemperature { get; set; }

        public bool SkillsEnabled { get; set; }

        public bool MemoryEnabled { get; set; }

        public string SkillsDirectory { get; set; } = string.Empty;

        public string MemoryDirectory { get; set; } = string.Empty;

        public static PythonLlmSettings FromRuntime(RuntimeLlm llm)
        {
            GameProjectLlmSettings settings = llm.Settings;
            return new PythonLlmSettings
            {
                Enabled = settings.Enabled,
                Provider = settings.Provider,
                BaseUrl = settings.BaseUrl,
                ApiKey = settings.ApiKey,
                ApiKeyEnvironmentVariable = settings.ApiKeyEnvironmentVariable,
                Model = settings.Model,
                ChatCompletionsPath = settings.ChatCompletionsPath,
                TimeoutSeconds = settings.TimeoutSeconds,
                DefaultTemperature = settings.DefaultTemperature,
                SkillsEnabled = llm.SkillsEnabled,
                MemoryEnabled = llm.MemoryEnabled,
                SkillsDirectory = llm.SkillsDirectory,
                MemoryDirectory = llm.MemoryDirectory
            };
        }
    }

    private sealed class PythonCamera
    {
        public float[] Position { get; set; } = [0.0f, 0.0f, 0.0f];

        public float[] Target { get; set; } = [0.0f, 0.0f, -1.0f];

        public float[] Forward { get; set; } = [0.0f, 0.0f, -1.0f];

        public float[] Up { get; set; } = [0.0f, 1.0f, 0.0f];

        public float[] Right { get; set; } = [1.0f, 0.0f, 0.0f];

        public string MainCamera { get; set; } = string.Empty;

        public string[] CameraNames { get; set; } = [];

        public string[] RenderTextureNames { get; set; } = [];

        public string ProjectionMode { get; set; } = "perspective";

        public string ControlMode { get; set; } = "editor";

        public float Fov { get; set; }

        public float OrthographicSize { get; set; }

        public float NearClipPlane { get; set; }

        public float FarClipPlane { get; set; }

        public int Width { get; set; }

        public string Title { get; set; } = string.Empty;

        public int Height { get; set; }

        public static PythonCamera FromRuntime(RuntimeCamera camera)
        {
            return new PythonCamera
            {
                Position = [camera.Position.X, camera.Position.Y, camera.Position.Z],
                Target = [camera.Target.X, camera.Target.Y, camera.Target.Z],
                Forward = [camera.Forward.X, camera.Forward.Y, camera.Forward.Z],
                Up = [camera.Up.X, camera.Up.Y, camera.Up.Z],
                Right = [camera.Right.X, camera.Right.Y, camera.Right.Z],
                MainCamera = camera.MainCamera,
                CameraNames = camera.CameraNames.ToArray(),
                RenderTextureNames = camera.RenderTextureNames.ToArray(),
                ControlMode = camera.ControlMode,
                ProjectionMode = camera.ProjectionMode,
                Fov = camera.Fov,
                OrthographicSize = camera.OrthographicSize,
                NearClipPlane = camera.NearClipPlane,
                FarClipPlane = camera.FarClipPlane,
                Width = camera.Width,
                Height = camera.Height
            };
        }
    }

    private sealed class PythonWindow
    {
        public string Title { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }

        public int ActualWidth { get; set; }

        public int ActualHeight { get; set; }

        public bool Fullscreen { get; set; }

        public bool Resizable { get; set; }

        public string TimingMode { get; set; } = string.Empty;

        public static PythonWindow FromRuntime(RuntimeWindowControl window)
        {
            return new PythonWindow
            {
                Title = window.Title,
                Width = window.Width,
                Height = window.Height,
                ActualWidth = window.ActualWidth,
                ActualHeight = window.ActualHeight,
                Fullscreen = window.Fullscreen,
                Resizable = window.Resizable,
                TimingMode = window.TimingMode
            };
        }
    }

    private sealed class PythonSprite
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string Texture { get; set; } = string.Empty;

        public string LayoutMode { get; set; } = "absolute";

        public float X { get; set; }

        public float Y { get; set; }

        public float Width { get; set; }

        public float Height { get; set; }

        public float RotationDegrees { get; set; }

        public float Opacity { get; set; }

        public bool Visible { get; set; }

        public static PythonSprite FromRuntime(RuntimeSpriteControl sprite)
        {
            return new PythonSprite
            {
                Id = sprite.Id,
                Name = sprite.Name,
                Path = sprite.Path,
                Texture = sprite.Texture,
                LayoutMode = sprite.LayoutMode,
                X = sprite.X,
                Y = sprite.Y,
                Width = sprite.Width,
                Height = sprite.Height,
                RotationDegrees = sprite.RotationDegrees,
                Opacity = sprite.Opacity,
                Visible = sprite.Visible
            };
        }
    }

    private sealed class PythonGuiControl
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public float X { get; set; }

        public float Y { get; set; }

        public float Width { get; set; }

        public float Height { get; set; }

        public string LayoutMode { get; set; } = "absolute";

        public bool Visible { get; set; }

        public bool Checked { get; set; }

        public float Progress { get; set; }

        public float FontSize { get; set; }

        public bool WordWrap { get; set; }

        public bool Multiline { get; set; }

        public string[] Items { get; set; } = [];

        public int SelectedIndex { get; set; }

        public int CursorPosition { get; set; }

        public int SelectionStart { get; set; }

        public int SelectionEnd { get; set; }

        public int SelectionLength { get; set; }

        public string SelectedText { get; set; } = string.Empty;

        public bool HasSelection { get; set; }

        public static PythonGuiControl FromRuntime(RuntimeGuiControl control)
        {
            return new PythonGuiControl
            {
                Id = control.Id,
                Name = control.Name,
                Type = control.Type,
                Text = control.Text,
                Value = control.Value,
                X = control.X,
                Y = control.Y,
                Width = control.Width,
                Height = control.Height,
                LayoutMode = control.LayoutMode,
                Visible = control.Visible,
                Checked = control.Checked,
                Progress = control.Progress,
                FontSize = control.FontSize,
                WordWrap = control.WordWrap,
                Multiline = control.Multiline,
                Items = control.Items.ToArray(),
                SelectedIndex = control.SelectedIndex,
                CursorPosition = control.CursorPosition,
                SelectionStart = control.SelectionStart,
                SelectionEnd = control.SelectionEnd,
                SelectionLength = control.SelectionLength,
                SelectedText = control.SelectedText,
                HasSelection = control.HasSelection
            };
        }
    }

    private sealed class PythonInput
    {
        private static readonly string[] ProbedKeys =
        [
            "W", "A", "S", "D", "Q", "E", "R", "F", "Z", "X", "C", "V",
            "Space", "Enter", "Escape",
            "Tab", "Backspace", "Delete",
            "Up", "Down", "Left", "Right",
            "Number0", "Number1", "Number2", "Number3", "Number4",
            "Number5", "Number6", "Number7", "Number8", "Number9",
            "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
            "ShiftLeft", "ShiftRight", "ControlLeft", "ControlRight", "AltLeft", "AltRight"
        ];

        private static readonly (string Name, ButtonName Button)[] ProbedGamepadButtons =
        [
            ("A", ButtonName.A),
            ("B", ButtonName.B),
            ("X", ButtonName.X),
            ("Y", ButtonName.Y),
            ("LeftBumper", ButtonName.LeftBumper),
            ("RightBumper", ButtonName.RightBumper),
            ("Back", ButtonName.Back),
            ("Start", ButtonName.Start),
            ("Home", ButtonName.Home),
            ("LeftStick", ButtonName.LeftStick),
            ("RightStick", ButtonName.RightStick),
            ("DPadUp", ButtonName.DPadUp),
            ("DPadRight", ButtonName.DPadRight),
            ("DPadDown", ButtonName.DPadDown),
            ("DPadLeft", ButtonName.DPadLeft)
        ];

        public string[] KeysDown { get; set; } = [];

        public string[] MouseButtonsDown { get; set; } = [];

        public float MouseX { get; set; }

        public float MouseY { get; set; }

        public float MouseDeltaX { get; set; }

        public float MouseDeltaY { get; set; }

        public float ScrollX { get; set; }

        public float ScrollY { get; set; }

        public bool IsCursorVisible { get; set; } = true;

        public bool IsCursorLocked { get; set; }

        public bool IsRawMouseInput { get; set; }

        public string CursorMode { get; set; } = "normal";

        public bool AltDown { get; set; }

        public bool ControlDown { get; set; }

        public bool ShiftDown { get; set; }

        public bool HasGamepad { get; set; }

        public string GamepadName { get; set; } = string.Empty;

        public int GamepadIndex { get; set; } = -1;

        public float LeftStickX { get; set; }

        public float LeftStickY { get; set; }

        public float RightStickX { get; set; }

        public float RightStickY { get; set; }

        public float LeftTrigger { get; set; }

        public float RightTrigger { get; set; }

        public string[] GamepadButtonsDown { get; set; } = [];

        public bool IsTouchAvailable { get; set; }

        public bool HasTouch { get; set; }

        public int TouchCount { get; set; }

        public int ActiveTouchCount { get; set; }

        public bool IsTouchDown { get; set; }

        public bool IsTouchStarted { get; set; }

        public bool IsTouchEnded { get; set; }

        public PythonTouchPoint[] Touches { get; set; } = [];

        public PythonTouchPoint? PrimaryTouch { get; set; }

        public string ClipboardText { get; set; } = string.Empty;

        public bool HasClipboardText { get; set; }

        public static PythonInput FromRuntime(RuntimeInput input)
        {
            string clipboardText = input.ClipboardText;
            return new PythonInput
            {
                KeysDown = ProbedKeys.Where(input.IsKeyDown).ToArray(),
                MouseButtonsDown = new[] { "left", "right", "middle" }.Where(input.IsMouseButtonDown).ToArray(),
                MouseX = input.MouseX,
                MouseY = input.MouseY,
                MouseDeltaX = input.MouseDeltaX,
                MouseDeltaY = input.MouseDeltaY,
                ScrollX = input.ScrollX,
                ScrollY = input.ScrollY,
                IsCursorVisible = input.IsCursorVisible,
                IsCursorLocked = input.IsCursorLocked,
                IsRawMouseInput = input.IsRawMouseInput,
                CursorMode = input.CursorMode,
                AltDown = input.IsAltDown,
                ControlDown = input.IsControlDown,
                ShiftDown = input.IsShiftDown,
                HasGamepad = input.HasGamepad,
                GamepadName = input.GamepadName,
                GamepadIndex = input.GamepadIndex,
                LeftStickX = input.LeftStickX,
                LeftStickY = input.LeftStickY,
                RightStickX = input.RightStickX,
                RightStickY = input.RightStickY,
                LeftTrigger = input.LeftTrigger,
                RightTrigger = input.RightTrigger,
                GamepadButtonsDown = ProbedGamepadButtons
                    .Where(button => input.IsGamepadButtonDown(button.Name))
                    .Select(button => button.Name)
                    .ToArray(),
                IsTouchAvailable = input.IsTouchAvailable,
                HasTouch = input.HasTouch,
                TouchCount = input.TouchCount,
                ActiveTouchCount = input.ActiveTouchCount,
                IsTouchDown = input.IsTouchDown,
                IsTouchStarted = input.IsTouchStarted,
                IsTouchEnded = input.IsTouchEnded,
                Touches = input.Touches.Select(PythonTouchPoint.FromRuntime).ToArray(),
                PrimaryTouch = input.PrimaryTouch is { } primaryTouch
                    ? PythonTouchPoint.FromRuntime(primaryTouch)
                    : null,
                ClipboardText = clipboardText,
                HasClipboardText = clipboardText.Length > 0
            };
        }

        public sealed class PythonTouchPoint
        {
            public int Id { get; set; }

            public float X { get; set; }

            public float Y { get; set; }

            public float DeltaX { get; set; }

            public float DeltaY { get; set; }

            public string Phase { get; set; } = string.Empty;

            public string Kind { get; set; } = string.Empty;

            public float Pressure { get; set; }

            public bool IsActive { get; set; }

            public bool IsEnded { get; set; }

            public static PythonTouchPoint FromRuntime(TouchPoint touch)
            {
                return new PythonTouchPoint
                {
                    Id = touch.Id,
                    X = touch.X,
                    Y = touch.Y,
                    DeltaX = touch.DeltaX,
                    DeltaY = touch.DeltaY,
                    Phase = touch.Phase.ToString(),
                    Kind = touch.Kind.ToString(),
                    Pressure = touch.Pressure,
                    IsActive = touch.IsActive,
                    IsEnded = touch.IsEnded
                };
            }
        }
    }

    private sealed class PythonCommand
    {
        public string? Target { get; set; }

        public string? Action { get; set; }

        public string? Entity { get; set; }

        public string? Control { get; set; }

        public string? Sprite { get; set; }

        public string? Name { get; set; }

        public string? Camera { get; set; }

        public string? Material { get; set; }

        public string? Texture { get; set; }

        public string? VertexShader { get; set; }

        public string? FragmentShader { get; set; }

        public string? Path { get; set; }

        public string? Mode { get; set; }

        public string? TargetEntity { get; set; }

        public string? SubjectEntity { get; set; }

        public double? X { get; set; }

        public double? Y { get; set; }

        public double? Z { get; set; }

        public double? W { get; set; }

        public double? TargetX { get; set; }

        public double? TargetY { get; set; }

        public double? TargetZ { get; set; }

        public double? DirectionX { get; set; }

        public double? DirectionY { get; set; }

        public double? DirectionZ { get; set; }

        public double? Width { get; set; }

        public double? Height { get; set; }

        public double? Radius { get; set; }

        public double? SizeX { get; set; }

        public double? SizeY { get; set; }

        public double? SizeZ { get; set; }

        public double? Length { get; set; }

        public double? Duration { get; set; }

        public string? Axis { get; set; }

        public double? Distance { get; set; }

        public double? ShoulderOffset { get; set; }

        public double? Smoothing { get; set; }

        public double? MoveSpeed { get; set; }

        public double? MouseSensitivity { get; set; }

        public double? SafeRadius { get; set; }

        public double? AutoOrbitSpeed { get; set; }

        public double? OffsetX { get; set; }

        public double? OffsetY { get; set; }

        public double? OffsetZ { get; set; }

        public double? Yaw { get; set; }

        public double? Pitch { get; set; }

        public double? OrbitSensitivity { get; set; }

        public double? PanSensitivity { get; set; }

        public double? ZoomSensitivity { get; set; }

        public double? ColorR { get; set; }

        public double? ColorG { get; set; }

        public double? ColorB { get; set; }

        public double? ColorA { get; set; }

        public double? Value { get; set; }

        public double? Degrees { get; set; }

        public double? RotationX { get; set; }

        public double? RotationY { get; set; }

        public double? RotationZ { get; set; }

        public double? Volume { get; set; }

        public double? Weight { get; set; }

        public bool? Flag { get; set; }

        public bool? RequireRightMouse { get; set; }

        public bool? RawInput { get; set; }

        public string? Text { get; set; }

        public string? SystemPrompt { get; set; }

        public string? Model { get; set; }

        public double? Temperature { get; set; }

        public string? RequestId { get; set; }

        public string[]? WakeWords { get; set; }

        public double? TimeoutSeconds { get; set; }

        public double? ChunkDurationSeconds { get; set; }

        public double? ExtensionDurationSeconds { get; set; }

        public double? TrailingSilencePaddingSeconds { get; set; }

        public string? OnPartial { get; set; }

        public string? OnDelta { get; set; }

        public string? OnCompleted { get; set; }

        public string? OnTimeout { get; set; }

        public string? OnError { get; set; }

        public string? OnToolCall { get; set; }

        public string? OnToolResult { get; set; }

        public int? MaxToolRounds { get; set; }

        public PythonLlmToolCommand[]? Tools { get; set; }

        public string? Callback { get; set; }

        public string? HeaderText { get; set; }

        public string? FooterText { get; set; }

        public string[]? Items { get; set; }

        public int? Index { get; set; }

        public int? SpeakerId { get; set; }

        public double? Speed { get; set; }

        public bool? BindComponentTransform { get; set; }

        public bool? BindLighting { get; set; }

        public PythonMotionLayerCommand[]? MotionLayers { get; set; }
    }

    private sealed class PythonMotionLayerCommand
    {
        public string? Path { get; set; }

        public double? Weight { get; set; }

        public bool? ResetPhysicsOnLoop { get; set; }
    }

    private sealed class PythonLlmToolCommand
    {
        public string? Name { get; set; }

        public string? Description { get; set; }

        public string? ParametersJsonSchema { get; set; }

        public string? Parameters { get; set; }

        public string? Callback { get; set; }
    }
}
