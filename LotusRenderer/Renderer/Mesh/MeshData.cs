using LotusRenderer.Renderer.Types;

public class MeshData
{
    public PackedVertex[] Vertices;
    public uint[] Triangles; // Vertices length * 3
    public uint matId;
    public int LogicalIndex;
    public int OrderId;
}