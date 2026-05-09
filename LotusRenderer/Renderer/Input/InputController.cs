using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia.Input;
using LotusRenderer;

public class InputController
{
    private static bool _isRightMouseDown = false;
    private static Vector2 _mousePos;
    
    private static readonly HashSet<Key> _pressedKeys = new();
    
    public static Vector2 LastMousePosition { get; private set; }
    public static Vector2 MouseDelta { get; private set; }
    public static bool IsRightMouseDown => _isRightMouseDown;
    public static bool IsMiddleMouseDown { get; private set; } = false;

    public static Action<Key> OnKeyPressed;

    public InputController()
    {
        var window = MainWindow.Get();
        var imgViewport = window.imgViewport;
        
        imgViewport.PointerPressed += OnPointerPressed;
        imgViewport.PointerReleased += OnPointerReleased;
        imgViewport.PointerMoved += OnPointerMoved;

        window.KeyDown += OnKeyDown;
        window.KeyUp += OnKeyUp;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        _pressedKeys.Remove(e.Key);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        _pressedKeys.Add(e.Key);
        OnKeyPressed?.Invoke(e.Key);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(null);
        _mousePos = new Vector2((float)point.Position.X, (float)point.Position.Y);
    }

    public static void Tick()
    {
        MouseDelta = _mousePos - LastMousePosition;
        LastMousePosition = _mousePos;
    }
    
    public static bool IsKeyDown(Key key) => _pressedKeys.Contains(key);
    
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isRightMouseDown && e.Properties.IsRightButtonPressed)
        {
            _isRightMouseDown = true;
            var point = e.GetCurrentPoint(null);
            MouseDelta = new Vector2();
            _mousePos = new Vector2((float)point.Position.X, (float)point.Position.Y);
            LastMousePosition = _mousePos;
        }
        
        if (!IsMiddleMouseDown && e.Properties.IsMiddleButtonPressed)
        {
            IsMiddleMouseDown = true;
            var point = e.GetCurrentPoint(null);
            MouseDelta = new Vector2();
            _mousePos = new Vector2((float)point.Position.X, (float)point.Position.Y);
            LastMousePosition = _mousePos;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isRightMouseDown && e.InitialPressMouseButton == MouseButton.Right)
        {
            _isRightMouseDown = false;
        }

        if (IsMiddleMouseDown && e.InitialPressMouseButton == MouseButton.Middle)
        {
            IsMiddleMouseDown = false;
        }
    }
}