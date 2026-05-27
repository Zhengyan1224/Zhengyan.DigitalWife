using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal sealed class PythonScriptInstance : IScriptInstance
{
    private const string CommandMarker = "__DW_COMMANDS__";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

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
        float loadingProgress = 0.0f,
        string loadingMessage = "",
        string speechCallback = "",
        RuntimeLlmScriptEvent? llmEvent = null)
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException($"Python process exited with code {_process.ExitCode}.");
        }

        PythonEvent payload = PythonEvent.Create(eventName, entity, scene, input, deltaSeconds, controlId, controlName, guiEventName, loadingProgress, loadingMessage, speechCallback, llmEvent);
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

            if (line.StartsWith("__DW_FLUSH__", StringComparison.Ordinal))
            {
                ApplyCommands(line["__DW_FLUSH__".Length..], entity, scene, input, audio);
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
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(BuildWorkerSource());
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
               import statistics
               import sys
               import time
               import urllib.error
               import urllib.request

               COMMAND_MARKER = "__DW_COMMANDS__"
               FLUSH_MARKER = "__DW_FLUSH__"
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

               def emit_commands(commands):
                   print(FLUSH_MARKER + json.dumps(commands, ensure_ascii=False, separators=(",", ":")), flush=True)

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

                   def set_draw_shadow_in_main_pass(self, enabled):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "set_draw_shadow_in_main_pass", "flag": bool(enabled)})

                   def apply_motion(self, path):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "apply_motion", "path": path})

                   def add_motion_layer(self, path, weight=1.0):
                       self._commands.append({"target": "entity", "entity": self.id, "action": "add_motion_layer", "path": path, "weight": weight})

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
                       if shape == "box":
                           result.append(make_box(entity, collider))
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
                       self.performance = Performance(data.get("performance", {}))
                       self.fps = self.performance.fps
                       self.raw_fps = self.performance.raw_fps
                       self.delta_seconds = self.performance.delta_seconds
                       self.frame_count = self.performance.frame_count
                       self.window = Window(data.get("window", {}), commands)
                       self.camera = Camera(data.get("camera", {}), commands)
                       self.debug = Debug(commands)
                       self.save = SaveStore()
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
                               return Sprite(item, self._commands)
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

               class Sprite:
                   def __init__(self, data, commands):
                       self.id = data.get("id", "")
                       self.name = data.get("name", "")
                       self.path = data.get("path", "")
                       self.texture = data.get("texture", self.path)
                       self.x = data.get("x", 0)
                       self.y = data.get("y", 0)
                       self.width = data.get("width", 1)
                       self.height = data.get("height", 1)
                       self.opacity = data.get("opacity", 1)
                       self.visible = bool(data.get("visible", True))
                       self._commands = commands

                   def set_position(self, x, y):
                       self._commands.append({"target": "sprite", "sprite": self.id, "action": "set_position", "x": x, "y": y})

                   def set_size(self, width, height):
                       self._commands.append({"target": "sprite", "sprite": self.id, "action": "set_size", "width": width, "height": height})

                   def set_visible(self, enabled):
                       self._commands.append({"target": "sprite", "sprite": self.id, "action": "set_visible", "flag": bool(enabled)})

                   def set_opacity(self, opacity):
                       self._commands.append({"target": "sprite", "sprite": self.id, "action": "set_opacity", "value": opacity})

                   def set_texture(self, texture):
                       self._commands.append({"target": "sprite", "sprite": self.id, "action": "set_texture", "texture": texture})

                   def set_render_texture(self, render_texture_name):
                       self.set_texture(render_texture(render_texture_name))

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

               class Input:
                   def __init__(self, data):
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
                       self.alt_down = bool(data.get("altDown", False))
                       self.control_down = bool(data.get("controlDown", False))
                       self.shift_down = bool(data.get("shiftDown", False))

                   def is_key_down(self, key):
                       return str(key) in self._keys or str(key).lower() in self._keys

                   def is_mouse_button_down(self, button):
                       return str(button).lower() in self._mouse_buttons

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

               for raw in sys.stdin:
                   try:
                       ctx = json.loads(raw)
                       commands = []
                       entity = Entity(ctx.get("entity", {}), commands)
                       scene = Scene(ctx.get("scene", {}), commands)
                       input = Input(ctx.get("input", {}))
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
                           if callback and hasattr(module, callback):
                               getattr(module, callback)(entity, scene, input, audio, llm_event)
                           elif hasattr(module, "llm_event"):
                               module.llm_event(entity, scene, input, audio, llm_event)
                       print(COMMAND_MARKER + json.dumps(commands, ensure_ascii=False, separators=(",", ":")), flush=True)
                   except Exception as ex:
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

            if (string.Equals(command.Target, "audio", StringComparison.OrdinalIgnoreCase))
            {
                ApplyAudioCommand(command, audio);
                continue;
            }

            if (string.Equals(command.Target, "gui", StringComparison.OrdinalIgnoreCase))
            {
                ApplyGuiCommand(command, scene);
                continue;
            }

            if (string.Equals(command.Target, "window", StringComparison.OrdinalIgnoreCase))
            {
                ApplyWindowCommand(command, scene);
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
        if (!string.Equals(command.Action, "start_chat", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(command.Text))
        {
            return;
        }

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
            case "set_draw_shadow_in_main_pass" when command.Flag.HasValue:
                entity.DrawShadowInMainPass = command.Flag.Value;
                break;
            case "apply_motion" when !string.IsNullOrWhiteSpace(command.Path):
                entity.ApplyMotion(command.Path);
                break;
            case "add_motion_layer" when !string.IsNullOrWhiteSpace(command.Path):
                entity.AddMotionLayer(command.Path, (float)(command.Weight ?? 1.0));
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

        public float LoadingProgress { get; set; }

        public string LoadingMessage { get; set; } = string.Empty;

        public string SpeechCallback { get; set; } = string.Empty;

        public PythonLlmEvent LlmEvent { get; set; } = new();

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
            float loadingProgress,
            string loadingMessage,
            string speechCallback,
            RuntimeLlmScriptEvent? llmEvent)
        {
            return new PythonEvent
            {
                Event = eventName,
                DeltaSeconds = deltaSeconds,
                ControlId = controlId,
                ControlName = controlName,
                GuiEventName = guiEventName,
                LoadingProgress = loadingProgress,
                LoadingMessage = loadingMessage,
                SpeechCallback = speechCallback,
                LlmEvent = PythonLlmEvent.FromRuntime(llmEvent),
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
                    Llm = PythonLlmSettings.FromRuntime(scene.Llm),
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
                    CallbackName = llmEvent.CallbackName
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

        public static PythonEntity FromRuntime(RuntimeEntity entity)
        {
            PythonCollider[] colliders = entity.EffectiveColliders.Select(PythonCollider.FromSettings).ToArray();
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
                Colliders = colliders
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

        public float[] Center { get; set; } = [0.0f, 1.0f, 0.0f];

        public float[] Position { get; set; } = [0.0f, 1.0f, 0.0f];

        public float[] RotationDegrees { get; set; } = [0.0f, 0.0f, 0.0f];

        public float[] Size { get; set; } = [1.0f, 1.0f, 1.0f];

        public float Radius { get; set; }

        public float Height { get; set; }

        public string Axis { get; set; } = "y";

        public static PythonCollider FromSettings(ColliderSettings collider)
        {
            return new PythonCollider
            {
                Id = collider.Id,
                Name = collider.Name,
                Enabled = collider.Enabled,
                Shape = collider.Shape,
                Center = [collider.Position.X, collider.Position.Y, collider.Position.Z],
                Position = [collider.Position.X, collider.Position.Y, collider.Position.Z],
                RotationDegrees = [collider.RotationDegrees.X, collider.RotationDegrees.Y, collider.RotationDegrees.Z],
                Size = [collider.Size.X, collider.Size.Y, collider.Size.Z],
                Radius = collider.Radius,
                Height = collider.Height,
                Axis = collider.Axis
            };
        }
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

        public PythonLlmSettings Llm { get; set; } = new();

        public PythonPerformance Performance { get; set; } = new();
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
                DefaultTemperature = settings.DefaultTemperature
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

        public float X { get; set; }

        public float Y { get; set; }

        public float Width { get; set; }

        public float Height { get; set; }

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
                X = sprite.X,
                Y = sprite.Y,
                Width = sprite.Width,
                Height = sprite.Height,
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
                SelectedIndex = control.SelectedIndex
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

        public string[] KeysDown { get; set; } = [];

        public string[] MouseButtonsDown { get; set; } = [];

        public float MouseX { get; set; }

        public float MouseY { get; set; }

        public float MouseDeltaX { get; set; }

        public float MouseDeltaY { get; set; }

        public float ScrollX { get; set; }

        public float ScrollY { get; set; }

        public bool AltDown { get; set; }

        public bool ControlDown { get; set; }

        public bool ShiftDown { get; set; }

        public static PythonInput FromRuntime(RuntimeInput input)
        {
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
                AltDown = input.IsAltDown,
                ControlDown = input.IsControlDown,
                ShiftDown = input.IsShiftDown
            };
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

        public string? Text { get; set; }

        public string? SystemPrompt { get; set; }

        public string? Model { get; set; }

        public double? Temperature { get; set; }

        public string? RequestId { get; set; }

        public string? OnDelta { get; set; }

        public string? OnCompleted { get; set; }

        public string? OnError { get; set; }

        public string? Callback { get; set; }

        public string[]? Items { get; set; }

        public int? Index { get; set; }

        public int? SpeakerId { get; set; }

        public double? Speed { get; set; }

        public bool? BindComponentTransform { get; set; }

        public bool? BindLighting { get; set; }
    }
}
