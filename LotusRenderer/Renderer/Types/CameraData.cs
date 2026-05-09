using System.Numerics;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct CameraData          // 64 bytes
{
    public Vector3 Position;      // 12 
    public float FOV;             // 4
    public Vector3 Forward;       // 12
    public uint FrameCount;       // 4
    public Vector3 Right;         // 12
    public float AspectRatio;     // 4
    public Vector3 Up;            // 12
    public float _pad1;           // 4
    
    public static CameraData Create(Vector3 position, Vector3 lookAt, float fovDegrees, float aspectRatio)
    {
        var forward = Vector3.Normalize(lookAt - position);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        
        return new CameraData
        {
            Position = position,
            Forward = forward,
            Right = right,
            Up = up,
            FOV = fovDegrees,
            AspectRatio = aspectRatio,
            FrameCount = 0
        };
    }
}