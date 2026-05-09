using System.Numerics;
using LotusRenderer.Renderer.Types;

public static class MaterialFactoryHelper
{
    public static Material CreateMatte(Vector3 color, float roughness = 0.8f)
    {
        return CreatePbr(color, 0.0f, roughness);
    }
    
    public static Material CreatePlastic(Vector3 color, float roughness = 0.1f)
    {
        var mat = CreatePbr(color, 0.0f, roughness);
        mat.IOR = 1.45f;
        return mat;
    }
    
    public static Material CreateMetal(Vector3 color, float roughness = 0.2f)
    {
        return CreatePbr(color, 1.0f, roughness);
    }
    
    public static Material CreateChrome()
    {
        return CreateMetal(new Vector3(0.9f, 0.9f, 0.9f), 0.0f);
    }

    public static Material CreateGold(float roughness = 0.1f)
    {
        return CreateMetal(new Vector3(1.00f, 0.782f, 0.344f), roughness);
    }
    
    public static Material CreateCopper(float roughness = 0.2f)
    {
        return CreateMetal(new Vector3(0.95f, 0.64f, 0.54f), roughness);
    }
    
    public static Material CreateClearGlass(Vector3 color, float ior = 1.5f, float roughness = 0.0f)
    {
        var mat = CreatePbr(color, 0.0f, roughness, 1.0f);
        mat.IOR = ior;
        return mat;
    }
    
    public static Material CreateFrostedGlass()
    {
        return CreateClearGlass(new Vector3(1f), 1.5f, 0.4f);
    }
    
    public static Material CreateEmitter(Vector3 color, float strength)
    {
        var mat = CreatePbr(Vector3.Zero, 0.0f, 1.0f);
        mat.EmissionColor = color;
        mat.EmissionStrength = strength;
        return mat;
    }

    public static Material CreateCarFloor()
    {
        var mat = CreatePbr(new Vector3(0.1f), 0.0f, 0.9f);
        mat.EmissionColor = Vector3.Zero;
        mat.EmissionStrength = 0f;
        return mat;
    }
    
    public static Material CreateCarPaint(Vector3 baseColor, float metalness = 0.5f)
    {
        var mat = CreatePbr(baseColor, metalness, 0.99f);
        
        mat.CoatWeight = 1.0f;
        mat.CoatRoughness = 0.0f; 
        mat.CoatIOR = 1.5f;       
        
        return mat;
    }
    
    public static Material CreateLayeredCoatRough(Vector3 baseColor, float coatRoughness)
    {
        var mat = CreatePbr(baseColor, 0f, 1f); 
        
        mat.CoatWeight = 1.0f;
        mat.CoatRoughness = coatRoughness;
        mat.CoatIOR = 1.5f;      
        
        return mat;
    }
    
    public static Material CreateMarble()
    {
        var mat = CreatePbr(new Vector3(0.9f), 0.0f, 0.1f, 0.0f); 
        
        mat.TransmissionWeight = 1.0f; 
        mat.IOR = 1.5f;
        
        mat.SSWeight = 1.0f;          
        mat.SSRadius = new Vector3(0.85f, 0.85f, 0.85f); 
        mat.SSScale = 0.5f;           
        mat.SSAnisotropy = 0.0f;     
        return mat;
    }

    public static Material CreateJade()
    {
        var mat = CreatePbr(new Vector3(0.1f, 0.8f, 0.3f), 0.0f, 0.2f, 0.0f);
        mat.TransmissionWeight = 1.0f;
        mat.IOR = 1.6f; 
        
        mat.SSWeight = 1.0f;
        mat.SSRadius = new Vector3(0.2f, 0.9f, 0.4f); 
        mat.SSScale = 1.7f;         
        mat.SSAnisotropy = 0.0f;
        return mat;
    }

    public static Material CreateSkin()
    {
        var mat = CreatePbr(new Vector3(0.98f, 0.72f, 0.65f), 0.0f, 0.4f, 0.0f);
        mat.TransmissionWeight = 0.0f; 
        mat.IOR = 1.4f;
        
        mat.SSWeight = 1.0f;
        mat.SSRadius = new Vector3(1.0f, 0.2f, 0.1f); 
        mat.SSScale = 0.3f;          
        mat.SSAnisotropy = 0.7f;    
        return mat;
    }
    
    public static Material CreateMilk()
    {
        var mat = CreatePbr(new Vector3(0.95f), 0.0f, 0.3f, 0.0f);
        mat.TransmissionWeight = 1.0f;
        mat.IOR = 1.35f;
        
        mat.SSWeight = 1.0f;
        mat.SSRadius = new Vector3(1.0f, 0.95f, 0.8f);
        mat.SSScale = 0.1f;           
        mat.SSAnisotropy = 0.6f;      
        return mat;
    }
    
    public static Material CreateGummyRed()
    {
        var mat = CreatePbr(new Vector3(0.9f, 0.1f, 0.1f), 0.0f, 0.5f, 0.0f);
        mat.TransmissionWeight = 1.0f;
        mat.IOR = 1.45f; 
        
        mat.SSWeight = 1.0f;
        mat.SSRadius = new Vector3(1.0f, 0.2f, 0.2f); 
        mat.SSScale = 0.05f;           
        mat.SSAnisotropy = 0.8f;    
        return mat;
    }
    
    public static Material CreatePbr(Vector3 albedo, float metallic, float roughness, float transmission = 0.0f)
    {
        return new Material
        {
            BaseColor = new Vector4(albedo, 1.0f), 
            
            Metallic = metallic,
            Roughness = roughness,
            IOR = 1.5f,            
            TransmissionWeight = transmission,
            
            SSWeight = 0.0f,
            SSRadius = Vector3.Zero,
            SSScale = 0.0f,
            SSAnisotropy = 0.0f,
            
            SpecularTint = Vector3.One,
            Anisotropy = 0.0f,
            
            CoatWeight = 0.0f,
            CoatRoughness = 0.0f,
            CoatIOR = 1.5f,
            
            CoatTint = Vector3.One,
            SheenWeight = 0.0f,
            
            SheenRoughness = 0.0f,
            SheenTint = Vector3.One,
            EmissionStrength = 0.0f,
            
            EmissionColor = Vector3.Zero,
            
            textIdBaseColor = -1,
            textIdRoughness = -1,
            textIdMetallic = -1,
            textIdEmission = -1,
            textIdNormal = -1,
            textIdTransmission = -1,
        };
    }
}