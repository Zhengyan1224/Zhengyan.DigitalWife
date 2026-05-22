using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

namespace Zhengyan.DigitalWife.Samples.GamePlayer;

internal sealed class PythonScriptInstance : IScriptInstance
{
    private const string CommandMarker = "__DW_COMMANDS__";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _scriptPath;
    private readonly Process _process;

    public PythonScriptInstance(string scriptPath)
    {
        _scriptPath = scriptPath;
        _process = StartWorker(scriptPath);
    }

    public void Start(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio)
    {
        SendEvent("start", entity, scene, input, audio, 0.0);
    }

    public void Update(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, double deltaSeconds)
    {
        SendEvent("update", entity, scene, input, audio, deltaSeconds);
    }

    public void GuiEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string controlId, string eventName)
    {
        SendEvent("gui_event", entity, scene, input, audio, 0.0, controlId, eventName);
    }

    public void LoadingEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string eventName, float progress, string message)
    {
        SendEvent(eventName, entity, scene, input, audio, 0.0, controlId: string.Empty, guiEventName: string.Empty, loadingProgress: progress, loadingMessage: message);
    }

    public void SpeechEvent(RuntimeEntity entity, RuntimeScene scene, RuntimeInput input, RuntimeAudio audio, string callbackName)
    {
        SpeechCompleted(entity, scene, input, audio, callbackName);
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
        string guiEventName = "",
        float loadingProgress = 0.0f,
        string loadingMessage = "",
        string speechCallback = "")
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException($"Python process exited with code {_process.ExitCode}.");
        }

        PythonEvent payload = PythonEvent.Create(eventName, entity, scene, input, deltaSeconds, controlId, guiEventName, loadingProgress, loadingMessage, speechCallback);
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

            Console.WriteLine(line);
        }
    }

    private static Process StartWorker(string scriptPath)
    {
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
               import json
               import sys

               COMMAND_MARKER = "__DW_COMMANDS__"
               script_path = sys.argv[1]
               spec = importlib.util.spec_from_file_location("game_script", script_path)
               module = importlib.util.module_from_spec(spec)
               spec.loader.exec_module(module)

               class Entity:
                   def __init__(self, data, commands):
                       self.id = data.get("id", "")
                       self.name = data.get("name", "")
                       self.type = data.get("type", "")
                       self.position = data.get("position", [0, 0, 0])
                       self.scale = data.get("scale", [1, 1, 1])
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

               class Camera:
                   def __init__(self, data, commands):
                       self.position = data.get("position", [0, 0, 0])
                       self.target = data.get("target", [0, 0, -1])
                       self.forward = data.get("forward", [0, 0, -1])
                       self.up = data.get("up", [0, 1, 0])
                       self.right = data.get("right", [1, 0, 0])
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
                       self._entities = data.get("entities", [])
                       self._gui_controls = data.get("guiControls", [])
                       self._sprites = data.get("sprites", [])
                       self.window = Window(data.get("window", {}), commands)
                       self.camera = Camera(data.get("camera", {}), commands)
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
                       self.x = data.get("x", 0)
                       self.y = data.get("y", 0)
                       self.width = data.get("width", 1)
                       self.height = data.get("height", 1)
                       self.visible = bool(data.get("visible", True))
                       self.checked = bool(data.get("checked", False))
                       self.word_wrap = bool(data.get("wordWrap", True))
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
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_text", "text": text})

                   def set_checked(self, enabled):
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_checked", "flag": bool(enabled)})

                   def set_word_wrap(self, enabled):
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_word_wrap", "flag": bool(enabled)})

                   def set_items(self, items):
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_items", "items": list(items)})

                   def set_selected_index(self, index):
                       self._commands.append({"target": "gui", "control": self.id, "action": "set_selected_index", "index": int(index)})

               class Input:
                   def __init__(self, data):
                       self._keys = set(data.get("keysDown", []))
                       self._mouse_buttons = set(data.get("mouseButtonsDown", []))
                       self.mouse_x = data.get("mouseX", 0)
                       self.mouse_y = data.get("mouseY", 0)
                       self.mouse_delta_x = data.get("mouseDeltaX", 0)
                       self.mouse_delta_y = data.get("mouseDeltaY", 0)
                       self.scroll_x = data.get("scrollX", 0)
                       self.scroll_y = data.get("scrollY", 0)
                       self.alt_down = bool(data.get("altDown", False))
                       self.control_down = bool(data.get("controlDown", False))

                   def is_key_down(self, key):
                       return key in self._keys

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
                           module.gui_event(entity, scene, input, audio, ctx.get("controlId", ""), ctx.get("guiEventName", ""))
                       elif event in ("loading_started", "loading_progress", "loading_completed") and hasattr(module, event):
                           getattr(module, event)(entity, scene, input, audio, ctx.get("loadingProgress", 0.0), ctx.get("loadingMessage", ""))
                       elif event == "speech_completed":
                           callback = ctx.get("speechCallback", "")
                           if callback and hasattr(module, callback):
                               getattr(module, callback)(entity, scene, input, audio)
                           elif hasattr(module, "speech_completed"):
                               module.speech_completed(entity, scene, input, audio, callback)
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
            case "set_checked" when command.Flag.HasValue:
                control.Checked = command.Flag.Value;
                break;
            case "set_word_wrap" when command.Flag.HasValue:
                control.WordWrap = command.Flag.Value;
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
        if (string.Equals(command.Action, "load_scene", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(command.Path))
        {
            scene.LoadScene(command.Path);
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
        }
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

    private sealed class PythonEvent
    {
        public string Event { get; set; } = string.Empty;

        public double DeltaSeconds { get; set; }

        public string ControlId { get; set; } = string.Empty;

        public string GuiEventName { get; set; } = string.Empty;

        public float LoadingProgress { get; set; }

        public string LoadingMessage { get; set; } = string.Empty;

        public string SpeechCallback { get; set; } = string.Empty;

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
            string guiEventName,
            float loadingProgress,
            string loadingMessage,
            string speechCallback)
        {
            return new PythonEvent
            {
                Event = eventName,
                DeltaSeconds = deltaSeconds,
                ControlId = controlId,
                GuiEventName = guiEventName,
                LoadingProgress = loadingProgress,
                LoadingMessage = loadingMessage,
                SpeechCallback = speechCallback,
                Entity = PythonEntity.FromRuntime(entity),
                Scene = new PythonScene
                {
                    Name = scene.Name,
                    Entities = scene.Entities.Select(PythonEntity.FromRuntime).ToArray(),
                    GuiControls = scene.GuiControls.Select(PythonGuiControl.FromRuntime).ToArray(),
                    Sprites = scene.Sprites.Select(PythonSprite.FromRuntime).ToArray(),
                    Camera = PythonCamera.FromRuntime(scene.Camera),
                    Window = PythonWindow.FromRuntime(scene.Window)
                },
                Input = PythonInput.FromRuntime(input)
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

        public static PythonEntity FromRuntime(RuntimeEntity entity)
        {
            return new PythonEntity
            {
                Id = entity.Id,
                Name = entity.Name,
                Type = entity.Type,
                Position = [entity.Position.X, entity.Position.Y, entity.Position.Z],
                Scale = [entity.Scale.X, entity.Scale.Y, entity.Scale.Z]
            };
        }
    }

    private sealed class PythonScene
    {
        public string Name { get; set; } = string.Empty;

        public PythonEntity[] Entities { get; set; } = [];

        public PythonGuiControl[] GuiControls { get; set; } = [];

        public PythonSprite[] Sprites { get; set; } = [];

        public PythonCamera Camera { get; set; } = new();

        public PythonWindow Window { get; set; } = new();
    }

    private sealed class PythonCamera
    {
        public float[] Position { get; set; } = [0.0f, 0.0f, 0.0f];

        public float[] Target { get; set; } = [0.0f, 0.0f, -1.0f];

        public float[] Forward { get; set; } = [0.0f, 0.0f, -1.0f];

        public float[] Up { get; set; } = [0.0f, 1.0f, 0.0f];

        public float[] Right { get; set; } = [1.0f, 0.0f, 0.0f];

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

        public float X { get; set; }

        public float Y { get; set; }

        public float Width { get; set; }

        public float Height { get; set; }

        public bool Visible { get; set; }

        public bool Checked { get; set; }

        public bool WordWrap { get; set; }

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
                X = control.X,
                Y = control.Y,
                Width = control.Width,
                Height = control.Height,
                Visible = control.Visible,
                Checked = control.Checked,
                WordWrap = control.WordWrap,
                Items = control.Items.ToArray(),
                SelectedIndex = control.SelectedIndex
            };
        }
    }

    private sealed class PythonInput
    {
        private static readonly string[] ProbedKeys =
        [
            "W", "A", "S", "D", "Q", "E",
            "Space", "Enter", "Escape",
            "Up", "Down", "Left", "Right"
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
                ControlDown = input.IsControlDown
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

        public string? Path { get; set; }

        public string? Mode { get; set; }

        public string? TargetEntity { get; set; }

        public string? SubjectEntity { get; set; }

        public double? X { get; set; }

        public double? Y { get; set; }

        public double? Z { get; set; }

        public double? TargetX { get; set; }

        public double? TargetY { get; set; }

        public double? TargetZ { get; set; }

        public double? Width { get; set; }

        public double? Height { get; set; }

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

        public double? Value { get; set; }

        public double? Degrees { get; set; }

        public double? Volume { get; set; }

        public double? Weight { get; set; }

        public bool? Flag { get; set; }

        public bool? RequireRightMouse { get; set; }

        public string? Text { get; set; }

        public string? Callback { get; set; }

        public string[]? Items { get; set; }

        public int? Index { get; set; }

        public int? SpeakerId { get; set; }

        public double? Speed { get; set; }

        public bool? BindComponentTransform { get; set; }

        public bool? BindLighting { get; set; }
    }
}
