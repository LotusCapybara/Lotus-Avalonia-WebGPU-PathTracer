using System;
using System.Numerics;
using Avalonia.Input;

namespace LotusRenderer.Renderer.World;

public class CameraController
{
    private const float MIN_FOV = 20f;
    private const float MAX_FOV = 120f;
    private const float PITCH_LIMIT_FACTOR = 0.49f;
    
    public static CameraController Instance { get; private set; } = null!;
    
    private static CameraData _camera;
    
    public float yaw = 0.0f;      
    public float pitch = 0.0f;   
    
    // todo: set this in the UI as user config?
    public float MoveSpeed { get; set; } = 5f;
    public float PanSpeed { get; set; } = 2.0f;
    public float MouseSensitivity { get; set; } = 0.5f; // radians per pixel
    
    public bool MovedThisFrame { get; private set; } = false;
    
    public static CameraData GetData() => _camera;
    
    public CameraController(Vector3 position, 
        Vector3 forwardDir, Vector3 rightDir, Vector3 upDir,
        float pitch, float yaw,
        float fovDegrees, float aspectRatio)
    {
        Instance = this;
        
        Vector3 fwd = Vector3.Normalize(forwardDir);
        this.pitch = pitch;
        this.yaw = yaw;
        
        _camera = new CameraData
        {
            Position = position,
            Forward = fwd,
            Right = rightDir,
            Up = upDir,
            FrameCount = 0,
            AspectRatio = aspectRatio,
            FOV = fovDegrees
        };
        
        UpdateCameraVectors();

        InputController.OnKeyPressed += OnKeyPressed;
    }

    private void OnKeyPressed(Key key)
    {
        if (key == Key.R)
        {
            _camera.Position = Vector3.Zero;
        }
    }

    public void StartFrame()
    {
        MovedThisFrame = false;
    }

    public void Tick(float deltaTime)
    {
        StartFrame();
        TickMovement(deltaTime);
        Rotate(deltaTime);
        ChangeFov(deltaTime);
        IncrementFrame();
    }

    private void ChangeFov(float deltaTime)
    {
        if(InputController.IsMiddleMouseDown || 
           !InputController.IsRightMouseDown || 
           !InputController.IsKeyDown(Key.LeftAlt))
            return;
        
        float deltaX = InputController.MouseDelta.X;
        if(deltaX == 0)
            return;
        
        float change = deltaX *  deltaTime * 5f;
        
        _camera.FOV += change;
        _camera.FOV = Math.Clamp(_camera.FOV, MIN_FOV, MAX_FOV);
        
        MovedThisFrame = true;
    }

    private void TickMovement(float deltaTime)
    {
        Vector3 moveDir = Vector3.Zero;
        
        // WASD movement
        if (InputController.IsRightMouseDown &&
            !InputController.IsKeyDown(Key.LeftAlt) &&
            !InputController.IsMiddleMouseDown)
        {
            if (InputController.IsKeyDown(Key.W)) moveDir += _camera.Forward;
            if (InputController.IsKeyDown(Key.S)) moveDir -= _camera.Forward;
            if (InputController.IsKeyDown(Key.A)) moveDir += _camera.Right;
            if (InputController.IsKeyDown(Key.D)) moveDir -= _camera.Right;
            if (InputController.IsKeyDown(Key.Q)) moveDir -= Vector3.UnitY;
            if (InputController.IsKeyDown(Key.E)) moveDir += Vector3.UnitY;
            
            moveDir = Vector3.Normalize(moveDir) * MoveSpeed;
        }
    
        // middle click panning
        if (!InputController.IsRightMouseDown &&
            !InputController.IsKeyDown(Key.LeftAlt) &&
            InputController.IsMiddleMouseDown)
        {
            float deltaX = InputController.MouseDelta.X;
            float deltaY = InputController.MouseDelta.Y;
            
            moveDir += _camera.Up * deltaY;
            moveDir += _camera.Right * deltaX;
            moveDir = Vector3.Normalize(moveDir) * PanSpeed;
        }
        
        
        if (moveDir.LengthSquared() > 0)
        {
            
            _camera.Position += moveDir * deltaTime;
            MovedThisFrame = true;
        }
    }

    private void Rotate(float deltaTime)
    {
        if(!InputController.IsRightMouseDown)
            return;
        
        if(InputController.IsKeyDown(Key.LeftAlt))
            return;
        
        float deltaX = InputController.MouseDelta.X;
        float deltaY = InputController.MouseDelta.Y;
        
        if(deltaX == 0 && deltaY == 0)
            return;

        MovedThisFrame = true;
        
        yaw -= -deltaX * MouseSensitivity * deltaTime ;
        pitch -= deltaY * MouseSensitivity * deltaTime;
        pitch = Math.Clamp(pitch, -MathF.PI * PITCH_LIMIT_FACTOR, MathF.PI * PITCH_LIMIT_FACTOR);
        
        UpdateCameraVectors();
    }
    
    private void UpdateCameraVectors()
    {
        var cy = MathF.Cos(yaw);
        var sy = MathF.Sin(yaw);
        var cp = MathF.Cos(pitch);
        var sp = MathF.Sin(pitch);
        
        _camera.Forward = new Vector3(
            sy * cp, 
            sp,
            -cy * cp
        );
        _camera.Forward = Vector3.Normalize(_camera.Forward);
        
        _camera.Right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, _camera.Forward));
        _camera.Up = Vector3.Cross(_camera.Forward, _camera.Right);
    }
    
    public void IncrementFrame()
    {
        _camera.FrameCount++;
    }
}