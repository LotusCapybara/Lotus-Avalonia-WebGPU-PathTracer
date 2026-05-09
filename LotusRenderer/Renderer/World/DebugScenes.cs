using System.Collections.Generic;
using System.Numerics;
using LotusRenderer.Renderer.Types;
using LotusRenderer.Renderer.World;

public static class DebugScenes
{
    public static void OneTempSphere(Scene scene)
    {
        List<Material> materials = new List<Material>();

        materials.Add(MaterialFactoryHelper.CreateMarble());
        materials.Add(MaterialFactoryHelper.CreateEmitter(new Vector3(1, 1, 1), 5f));

        scene.Materials = materials.ToArray();

        scene.Spheres = 
        [
            new() {Position = new Vector3(0.0f, 0, 10.0f), Radius = 1.5f, materialId = 0},
        ];

        scene.Boxes =
        [
            new SdfBox
            {
                Center = new Vector3(0, 10f, 0),
                HalfExtents = new Vector3(10, 0.2f, 10),
                materialId = 1
            }
        ];

        scene.Boxes = [];

    }
        
    public static void ThreeSphere(Scene scene)
    {
        scene.Spheres = 
        [
            new() {Position = new Vector3(0.0f, 0, 10.0f), Radius = 0.5f},
            new() {Position = new Vector3(2.0f, -0.5f, 10.0f), Radius = 0.5f},
            new() {Position = new Vector3(0.0f, -2f, 10.0f), Radius = 1.0f}
        ];

        scene.Boxes = [];
    }
    
    public static void OneFloor(Scene scene)
    {
        scene.Spheres = [];

        scene.Boxes =
        [
            new SdfBox{ Center = new Vector3(0, -3f, 0), HalfExtents = new Vector3(4.2f, 0.2f, 2.3f)} ,
        ];
    }
    
    public static void OneBox(Scene scene)
    {
        scene.Spheres = [];

        scene.Boxes =
        [
            new SdfBox{ Center = new Vector3(0, 0, 0), HalfExtents = new Vector3(1f, 1f, 1f)} ,
        ];
    }
    
    public static void GetCornelBoxes(Scene scene)
    {
        // materials
        scene.Materials =
        [
            MaterialFactoryHelper.CreateMatte(new Vector3(0.8f, 0.8f, 0.8f)), // 0: White Wall
            MaterialFactoryHelper.CreateMatte(new Vector3(0.8f, 0.1f, 0.1f)), // 1: Red Wall
            MaterialFactoryHelper.CreateMatte(new Vector3(0.1f, 0.8f, 0.1f)), // 2: Green Wall
            MaterialFactoryHelper.CreateEmitter(new Vector3(1.0f, 0.9f, 0.7f), 20.0f), // 3: Ceiling Light
            MaterialFactoryHelper.CreateChrome(), // 4: Chrome Sphere
            MaterialFactoryHelper.CreateClearGlass(new Vector3(1), 1.5f), // 5: Glass Sphere
            MaterialFactoryHelper.CreateGold(0.2f), // 6: Rough Gold
            MaterialFactoryHelper.CreatePlastic(new Vector3(1, 1, 0), 0.1f), // 7: plastic
            MaterialFactoryHelper.CreatePlastic(new Vector3(1, 0, 1), 0.2f), // 8: plastic
            MaterialFactoryHelper.CreatePlastic(new Vector3(0, 1, 1), 0.2f) // 9: plastic
        ];
        
        // --- SETTINGS ---
        float roomSize = 10.0f;       // 4 meters wide/deep
        float roomHeight = 5.0f;     // 4 meters high
        float wallThick = 0.025f;      // Thickness of walls
    
        // Helper to calc offsets
        float halfW = roomSize / 2.0f;
        float centerY = roomHeight / 2.0f;
    
        // The "radius" (half-size) of the wall for the collider logic
        // We add wallThick to push the center OUTWARDS so the inner face is at exactly 'halfW'
        float wallOffset = halfW + (wallThick / 1.0f); 

        scene.Boxes =
        [
            // floor
            new SdfBox
            {
                Center = new Vector3(0, -wallThick/2, 3), 
                HalfExtents = new Vector3(halfW, wallThick, halfW),
                materialId = 0
            },
            // ceiling
            new SdfBox
            {
                Center = new Vector3(0, roomHeight + wallThick/2, 3), 
                HalfExtents = new Vector3(halfW, wallThick, halfW),
                materialId = 0
            },
            // back wall
            new SdfBox
            {
                Center = new Vector3(0, centerY, wallOffset), 
                HalfExtents = new Vector3(halfW, roomHeight / 2, wallThick),
                materialId = 0
            },
            // left wall
            new SdfBox
            {
                Center = new Vector3(-halfW, centerY, 3), 
                HalfExtents = new Vector3(wallThick, roomHeight / 2, halfW),
                materialId = 1
            },
            // right wall
            new SdfBox
            {
                Center = new Vector3(halfW, centerY, 3),
                HalfExtents = new Vector3(wallThick, roomHeight / 2, halfW),
                materialId = 2
            },
            
            // light in ceiling
            new SdfBox
            {
                Center = new Vector3(0, roomHeight - wallThick, 3), 
                HalfExtents = new Vector3(3, wallThick * 4, 1f),
                materialId = 3
            },
            
            
            // just 2 boxes
            // new SdfBox
            // {
            //     Center = new Vector3(1.0f, 1.5f, 3), 
            //     HalfExtents = new Vector3(1.2f, 1.5f, 1.2f),
            //     materialId = 4
            // },
            // new SdfBox
            // {
            //     Center = new Vector3(-1.0f, 0.5f, 3), 
            //     HalfExtents = new Vector3(1.2f, 0.5f, 1.2f),
            //     materialId = 6
            // },
        ];

        scene.Spheres =
        [
            new SdfSphere
            {
                Position = new Vector3(-2.0f, 1.2f + 0.5f, 1f), 
                Radius = 0.75f,
                materialId = 4
            },
            
            new SdfSphere
            {
                Position = new Vector3(0.0f, 3.0f, 1.2f), 
                Radius = 0.9f,
                materialId = 6
            },
            
            new SdfSphere
            {
                Position = new Vector3(1.5f, 1.2f + 0.5f, 1.5f), 
                Radius = 0.75f,
                materialId = 7
            },
            
            new SdfSphere
            {
                Position = new Vector3(3.5f, 2, 1.5f), 
                Radius = 0.75f,
                materialId = 8
            },
            
            new SdfSphere
            {
                Position = new Vector3(2.5f, 0.75f, 0f), 
                Radius = 0.75f,
                materialId = 9
            },
        ];
    }
    
    public static void ManySpheres(Scene scene)
    {
        // materials
        List<Material> materials = new List<Material>();

        materials.Add(MaterialFactoryHelper.CreateMatte(new Vector3(0.8f, 0.8f, 0.8f)));
        materials.Add(MaterialFactoryHelper.CreateEmitter(new Vector3(1, 1, 1), 5f));

        
        for (int i = 0; i < 5; i++)
        {
            materials.Add(MaterialFactoryHelper.CreateMatte(new Vector3(0.8f, 0.8f, 0.8f), 1f - 0.2f * i));
        }
        

        for (int i = 0; i < 5; i++)
        {
            materials.Add(MaterialFactoryHelper.CreatePlastic(new Vector3(0, 0, 1), 1f - 0.2f * i));
        }
        
        for (int i = 0; i < 5; i++)
        {
            materials.Add(MaterialFactoryHelper.CreateMetal(new Vector3(0, 0, 1), 1f - 0.2f * i));
        }
        
        for (int i = 0; i < 5; i++)
        {
            materials.Add(MaterialFactoryHelper.CreateLayeredCoatRough(new Vector3(0, 0, 1), 0.8f - 0.2f * i));
        }
        
        for (int i = 0; i < 5; i++)
        {
            materials.Add(MaterialFactoryHelper.CreateClearGlass(new Vector3(1f), 1.0f + 0.2f * i, 0f));
        }
        
        for (int i = 0; i < 5; i++)
        {
            materials.Add(MaterialFactoryHelper.CreateClearGlass(new Vector3(1f), 1.5f, 1f - 0.2f * i));
        }
        
        for (int i = 0; i < 5; i++)
        {
            materials.Add(MaterialFactoryHelper.CreateClearGlass(new Vector3(0, 0, 1), 1.0f + 0.2f * i, 0f));
        }
        
        for (int i = 0; i < 5; i++)
        {
            materials.Add(MaterialFactoryHelper.CreateClearGlass(new Vector3(0, 0, 1), 1.5f, 1f - 0.2f * i));
        }
        
        // 5 SSS materials
        materials.Add(MaterialFactoryHelper.CreateMarble());
        materials.Add(MaterialFactoryHelper.CreateJade());
        materials.Add(MaterialFactoryHelper.CreateSkin());
        materials.Add(MaterialFactoryHelper.CreateMilk());
        materials.Add(MaterialFactoryHelper.CreateGummyRed());

        scene.Materials = materials.ToArray();
        
        scene.Boxes =
        [
            // floor
            new SdfBox
            {
                Center = new Vector3(0, -0.2f, 0), 
                HalfExtents = new Vector3(5, 0.2f, 13),
                materialId = 0
            },
            
            // 3 ceiling lights
            // new SdfBox
            // {
            //     Center = new Vector3(0, 5f, -8), 
            //     HalfExtents = new Vector3(5, 0.2f, 1),
            //     materialId = 1
            // },
            // new SdfBox
            // {
            //     Center = new Vector3(0, 5f, 0), 
            //     HalfExtents = new Vector3(5, 0.2f, 1),
            //     materialId = 1
            // },
            // new SdfBox
            // {
            //     Center = new Vector3(0, 5f, 8), 
            //     HalfExtents = new Vector3(5, 0.2f, 1),
            //     materialId = 1
            // },
        ];

        List<SdfSphere> spheres = new List<SdfSphere>();

        float margin = 2f;
        float startX = - margin * 2.5f + 0.5f;

        // spheres matte
        for (int i = 0; i < 5; i++)
        {
            spheres.Add( new SdfSphere
            {
                Position = new Vector3(startX + i * margin + 0.5f, 0.5f, 4f), 
                Radius = 0.5f,
                materialId = i + 2
            });
        }
        
        // spheres plastic
        for (int i = 0; i < 5; i++)
        {
            spheres.Add( new SdfSphere
            {
                Position = new Vector3(startX + i * margin + 0.5f, 0.5f, 2f), 
                Radius = 0.5f,
                materialId = i + 7
            });
        }
        
        // spheres metal
        for (int i = 0; i < 5; i++)
        {
            spheres.Add( new SdfSphere
            {
                Position = new Vector3(startX + i * margin + 0.5f, 0.5f, 0f), 
                Radius = 0.5f,
                materialId = i + 12
            });
        }
        
        // spheres car paint
        for (int i = 0; i < 5; i++)
        {
            spheres.Add( new SdfSphere
            {
                Position = new Vector3(startX + i * margin + 0.5f, 0.5f, -2f), 
                Radius = 0.5f,
                materialId = i + 17
            });
        }

        // Clear Glass 1
        for (int i = 0; i < 5; i++)
        {
            spheres.Add( new SdfSphere
            {
                Position = new Vector3(startX + i * margin + 0.5f, 0.5f, -4f), 
                Radius = 0.5f,
                materialId = i + 22
            });
        }
        // Clear Glass 2
        for (int i = 0; i < 5; i++)
        {
            spheres.Add( new SdfSphere
            {
                Position = new Vector3(startX + i * margin + 0.5f, 0.5f, -6f), 
                Radius = 0.5f,
                materialId = i + 27
            });
        }
        //  Clear Glass 3
        for (int i = 0; i < 5; i++)
        {
            spheres.Add( new SdfSphere
            {
                Position = new Vector3(startX + i * margin + 0.5f, 0.5f, -8f), 
                Radius = 0.5f,
                materialId = i + 32
            });
        }
        // Clear Glass 4
        for (int i = 0; i < 5; i++)
        {
            spheres.Add( new SdfSphere
            {
                Position = new Vector3(startX + i * margin + 0.5f, 0.5f, -10f), 
                Radius = 0.5f,
                materialId = i + 37
            });
        }
        
        for (int i = 0; i < 5; i++)
        {
            // marble, jade, skin, milk, gummy red
            spheres.Add( new SdfSphere
            {
                Position = new Vector3(startX + i * margin + 0.5f, 0.5f, -12f), 
                Radius = 0.5f,
                materialId = i + 42
            });
        }
        

        scene.Spheres = spheres.ToArray();
    }
    
    public static void FloorAndTopLights(Scene scene)
    {
        // materials
        List<Material> materials = new List<Material>();

        materials.Add(MaterialFactoryHelper.CreateCarFloor());
        materials.Add(MaterialFactoryHelper.CreateEmitter(new Vector3(1, 1, 1), 5f));
        scene.Materials = materials.ToArray();
        
        scene.Boxes =
        [
            // floor
            new SdfBox
            {
                Center = new Vector3(0, -0.65f, 0), 
                HalfExtents = new Vector3(5, 0.2f, 13),
                materialId = 0
            },
            
            // // 3 ceiling lights
            // new SdfBox
            // {
            //     Center = new Vector3(0, 10f, -8), 
            //     HalfExtents = new Vector3(5, 0.2f, 1),
            //     materialId = 1
            // },
            // new SdfBox
            // {
            //     Center = new Vector3(0, 10f, 0), 
            //     HalfExtents = new Vector3(5, 0.2f, 1),
            //     materialId = 1
            // },
            // new SdfBox
            // {
            //     Center = new Vector3(0, 10f, 8), 
            //     HalfExtents = new Vector3(5, 0.2f, 1),
            //     materialId = 1
            // },
        ];
    }
}