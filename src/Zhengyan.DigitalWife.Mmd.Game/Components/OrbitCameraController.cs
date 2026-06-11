using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Silk.NET.Input;

namespace Zhengyan.DigitalWife.Mmd.Game.Components;

public sealed class OrbitCameraController(OrbitCamera camera) : GameComponent
{
    private bool _firstMove = true;
    private Silk.NET.Maths.Vector2D<float> _lastMousePosition;

    public OrbitCamera Camera { get; } = camera;

    public float OrbitSensitivity { get; set; } = 0.2f;

    public float PanSensitivity { get; set; } = 1.0f;

    public float ZoomSensitivity { get; set; } = 1.0f;

    public float KeyboardPanSpeed { get; set; } = 4.0f;

    public Func<bool>? CanProcessPointerInput { get; set; }

    public Func<bool>? CanProcessKeyboardInput { get; set; }

    public override void Update(GameTime gameTime)
    {
        if (Game is null)
        {
            return;
        }

        Camera.Width = Math.Max(Game.GraphicsDevice.BackBufferSize.X, 1);
        Camera.Height = Math.Max(Game.GraphicsDevice.BackBufferSize.Y, 1);

        bool canProcessPointerInput = CanProcessPointerInput?.Invoke() ?? true;
        bool canProcessKeyboardInput = CanProcessKeyboardInput?.Invoke() ?? true;

        if (canProcessPointerInput && Game.Input.ScrollDelta.Y != 0.0f)
        {
            Camera.Dolly(Game.Input.ScrollDelta.Y * ZoomSensitivity);
        }

        CameraDragMode dragMode = canProcessPointerInput ? ResolveDragMode() : CameraDragMode.None;
        if (!canProcessPointerInput || dragMode == CameraDragMode.None)
        {
            _firstMove = true;
        }
        else
        {
            Silk.NET.Maths.Vector2D<float> current = new(Game.Input.MousePosition.X, Game.Input.MousePosition.Y);
            if (_firstMove)
            {
                _lastMousePosition = current;
                _firstMove = false;
            }
            else
            {
                float deltaX = current.X - _lastMousePosition.X;
                float deltaY = current.Y - _lastMousePosition.Y;

                switch (dragMode)
                {
                    case CameraDragMode.Orbit:
                        Camera.Orbit(deltaX * OrbitSensitivity, -deltaY * OrbitSensitivity);
                        break;
                    case CameraDragMode.Pan:
                        Camera.Pan(deltaX * PanSensitivity, deltaY * PanSensitivity);
                        break;
                    case CameraDragMode.Dolly:
                        Camera.Dolly((-deltaY * 0.05f) * ZoomSensitivity);
                        break;
                }

                _lastMousePosition = current;
            }
        }

        if (!canProcessKeyboardInput)
        {
            return;
        }

        float keyboardPan = KeyboardPanSpeed * (float)gameTime.ElapsedSeconds * 10.0f * PanSensitivity;
        float keyboardZoom = KeyboardPanSpeed * (float)gameTime.ElapsedSeconds * ZoomSensitivity;

        if (Game.Input.IsKeyDown(Key.W))
        {
            Camera.Dolly(keyboardZoom);
        }

        if (Game.Input.IsKeyDown(Key.S))
        {
            Camera.Dolly(-keyboardZoom);
        }

        if (Game.Input.IsKeyDown(Key.A))
        {
            Camera.Pan(keyboardPan, 0.0f);
        }

        if (Game.Input.IsKeyDown(Key.D))
        {
            Camera.Pan(-keyboardPan, 0.0f);
        }

        if (Game.Input.IsKeyDown(Key.Q))
        {
            Camera.Pan(0.0f, -keyboardPan);
        }

        if (Game.Input.IsKeyDown(Key.E))
        {
            Camera.Pan(0.0f, keyboardPan);
        }
    }

    private CameraDragMode ResolveDragMode()
    {
        if (Game is null)
        {
            return CameraDragMode.None;
        }

        bool altPressed = Game.Input.IsAltDown;
        if (altPressed && Game.Input.IsMouseButtonDown(MouseButton.Right))
        {
            return CameraDragMode.Dolly;
        }

        if (Game.Input.IsMouseButtonDown(MouseButton.Right))
        {
            return CameraDragMode.Orbit;
        }

        if (altPressed && (Game.Input.IsMouseButtonDown(MouseButton.Middle) || Game.Input.IsMouseButtonDown(MouseButton.Left)))
        {
            return CameraDragMode.Orbit;
        }

        if (Game.Input.IsMouseButtonDown(MouseButton.Middle))
        {
            return CameraDragMode.Pan;
        }

        return CameraDragMode.None;
    }

    private enum CameraDragMode
    {
        None,
        Orbit,
        Pan,
        Dolly
    }
}

