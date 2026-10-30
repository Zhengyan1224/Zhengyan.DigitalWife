using System.Numerics;

namespace Zhengyan.DigitalWife.Mmd.Game.Input;

/// <summary>Recognizes a mouse click or a single-finger tap from one frame of raw input.</summary>
public sealed class PointerClickGesture
{
    private readonly Dictionary<int, Contact> _contacts = [];
    private Contact? _mouse;
    private bool _mouseWasDown;
    private bool _multiTouch;

    public double MaxTapSeconds { get; set; } = 0.3;

    public bool Update(bool mouseDown, Vector2 mousePosition,
        IReadOnlyList<TouchPoint> touches, IReadOnlyList<TouchPoint> transitions,
        double deltaSeconds, out Vector2 screenPoint,
        float movementThreshold = 12.0f, Func<Vector2, bool>? isBlocked = null)
    {
        screenPoint = default;
        bool clicked = false;
        double elapsed = double.IsFinite(deltaSeconds) ? Math.Max(deltaSeconds, 0) : 0;
        float threshold = float.IsFinite(movementThreshold) ? Math.Max(movementThreshold, 0) : 12;
        float thresholdSquared = threshold * threshold;
        foreach (Contact contact in _contacts.Values) contact.Seconds += elapsed;

        // Ordered transitions preserve taps and multi-touch that finish between frames.
        foreach (TouchPoint touch in transitions)
        {
            Vector2 position = new(touch.X, touch.Y);
            if (touch.Phase == TouchPhase.Started)
            {
                if (_contacts.Count == 0) _multiTouch = false;
                _contacts[touch.Id] = new(position, isBlocked?.Invoke(position) == true);
                if (_contacts.Count > 1) _multiTouch = true;
            }
            else if (touch.IsEnded && _contacts.Remove(touch.Id, out Contact? contact))
            {
                contact.Observe(position, thresholdSquared, isBlocked);
                if (touch.Phase == TouchPhase.Ended && !_multiTouch && !contact.Blocked
                    && contact.Seconds <= MaxTapSeconds)
                {
                    clicked = true;
                    screenPoint = position;
                }
            }
        }

        foreach (TouchPoint touch in touches)
        {
            if (_contacts.TryGetValue(touch.Id, out Contact? contact))
                contact.Observe(new(touch.X, touch.Y), thresholdSquared, isBlocked);
        }
        // Focus loss or a missing native release must not leave a stale candidate.
        foreach (int id in _contacts.Keys.Where(id => !touches.Any(t => t.Id == id && t.IsActive)).ToArray())
            _contacts.Remove(id);

        bool touchFrame = touches.Count > 0 || transitions.Count > 0 || _contacts.Count > 0;
        if (touchFrame) _mouse = null;
        if (mouseDown && !_mouseWasDown && !touchFrame)
            _mouse = new(mousePosition, isBlocked?.Invoke(mousePosition) == true);
        _mouse?.Observe(mousePosition, thresholdSquared, isBlocked);
        if (!mouseDown && _mouseWasDown && _mouse is { Blocked: false } && !touchFrame && !clicked)
        {
            clicked = true;
            screenPoint = mousePosition;
        }
        if (!mouseDown) _mouse = null;
        _mouseWasDown = mouseDown;
        return clicked;
    }

    public void Reset()
    {
        _contacts.Clear();
        _mouse = null;
        _mouseWasDown = false;
        _multiTouch = false;
    }

    private sealed class Contact(Vector2 start, bool blocked)
    {
        public double Seconds;
        public bool Blocked = blocked;
        public void Observe(Vector2 position, float thresholdSquared, Func<Vector2, bool>? isBlocked)
            => Blocked |= !float.IsFinite(position.X) || !float.IsFinite(position.Y)
                || Vector2.DistanceSquared(start, position) > thresholdSquared
                || isBlocked?.Invoke(position) == true;
    }
}
