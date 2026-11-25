using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The main menu's room: a small spaceship cabin built in code around the player, who stands
/// at the origin facing +Z. Windows run along both side walls onto the space skybox; a
/// console stands in front with the menu screen above it. Textures and materials are in
/// Resources/MainMenu (made by Tools/shootout_look/gen_ship.py and build_ship.py).
/// Every surface gets UVs in metres, so the textures tile at their real size.
/// </summary>
public static class Hd2ShipRoom
{
    public const float HalfWidth = 2.4f;
    public const float Back = -2.0f;
    public const float Front = 3.0f;
    public const float Height = 2.6f;
    const float Wall = 0.12f;
    const float WindowLow = 0.85f;
    const float WindowHigh = 2.15f;
    const float WindowBack = -1.1f;
    const float WindowFront = 2.1f;

    /// <summary>Centre of the menu screen, its size, and its tilt (degrees, top leaning away).</summary>
    public static readonly Vector3 ScreenCenter = new Vector3(0f, 1.42f, 1.5f);
    public const float ScreenWidth = 1.6f;
    public const float ScreenHeight = 0.95f;
    public const float ScreenTilt = 10f;

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    public static Transform Build(Transform parent)
    {
        var root = new GameObject("ShipRoom").transform;
        root.SetParent(parent, false);

        // Shell
        Box(root, "Floor", new Vector3(0f, -0.05f, (Back + Front) * 0.5f), new Vector3(HalfWidth * 2f + Wall * 2f, 0.1f, Front - Back + Wall * 2f), "ShipFloor", 1f);
        Box(root, "Ceiling", new Vector3(0f, Height + 0.05f, (Back + Front) * 0.5f), new Vector3(HalfWidth * 2f + Wall * 2f, 0.1f, Front - Back + Wall * 2f), "ShipCeiling", 1.5f);
        Box(root, "BackWall", new Vector3(0f, Height * 0.5f, Back - Wall * 0.5f), new Vector3(HalfWidth * 2f, Height, Wall), "ShipWall", 2f);
        Box(root, "FrontWall", new Vector3(0f, Height * 0.5f, Front + Wall * 0.5f), new Vector3(HalfWidth * 2f, Height, Wall), "ShipWall", 2f);
        for (int s = -1; s <= 1; s += 2)
            SideWall(root, s);

        // Edge light strips where the walls meet the floor and ceiling
        for (int s = -1; s <= 1; s += 2)
        {
            float x = s * (HalfWidth - 0.02f);
            Strip(root, new Vector3(x, 0.03f, (Back + Front) * 0.5f), new Vector3(0.04f, 0.06f, Front - Back), "ShipTrimCyan");
            Strip(root, new Vector3(x, Height - 0.04f, (Back + Front) * 0.5f), new Vector3(0.04f, 0.08f, Front - Back), "ShipTrimViolet");
        }

        Strip(root, new Vector3(0f, 0.03f, Back + 0.02f), new Vector3(HalfWidth * 2f, 0.06f, 0.04f), "ShipTrimCyan");
        Strip(root, new Vector3(0f, 0.03f, Front - 0.02f), new Vector3(HalfWidth * 2f, 0.06f, 0.04f), "ShipTrimCyan");

        // Door outline on the back wall
        Strip(root, new Vector3(-0.55f, 1.05f, Back + 0.02f), new Vector3(0.05f, 2.1f, 0.04f), "ShipTrimViolet");
        Strip(root, new Vector3(0.55f, 1.05f, Back + 0.02f), new Vector3(0.05f, 2.1f, 0.04f), "ShipTrimViolet");
        Strip(root, new Vector3(0f, 2.1f, Back + 0.02f), new Vector3(1.15f, 0.05f, 0.04f), "ShipTrimViolet");

        // Console and the screen bezel
        Console(root);

        // Lights: soft fill from above, a cool accent at the console
        Light(root, "CabinLight", new Vector3(0f, Height - 0.3f, 0.4f), new Color(0.92f, 0.95f, 1f), 1.3f, 6.5f);
        Light(root, "ConsoleGlow", new Vector3(0f, 1.0f, 1.0f), new Color(0.35f, 0.8f, 1f), 0.6f, 2.5f);
        return root;
    }

    static void SideWall(Transform root, int side)
    {
        float x = side * (HalfWidth + Wall * 0.5f);
        float length = Front - Back;
        Box(root, "WallLow", new Vector3(x, WindowLow * 0.5f, (Back + Front) * 0.5f), new Vector3(Wall, WindowLow, length), "ShipWall", 2f);
        Box(root, "WallHigh", new Vector3(x, (WindowHigh + Height) * 0.5f, (Back + Front) * 0.5f), new Vector3(Wall, Height - WindowHigh, length), "ShipWall", 2f);
        float h = WindowHigh - WindowLow;
        float y = (WindowLow + WindowHigh) * 0.5f;
        Box(root, "WallBack", new Vector3(x, y, (Back + WindowBack) * 0.5f), new Vector3(Wall, h, WindowBack - Back), "ShipWall", 2f);
        Box(root, "WallFront", new Vector3(x, y, (WindowFront + Front) * 0.5f), new Vector3(Wall, h, Front - WindowFront), "ShipWall", 2f);

        // Window frame and mullions
        float fx = side * (HalfWidth - 0.01f);
        float span = WindowFront - WindowBack;
        float mid = (WindowBack + WindowFront) * 0.5f;
        Strip(root, new Vector3(fx, WindowLow, mid), new Vector3(0.1f, 0.08f, span + 0.08f), "ShipTrimCyan");
        Strip(root, new Vector3(fx, WindowHigh, mid), new Vector3(0.1f, 0.08f, span + 0.08f), "ShipTrimCyan");
        Strip(root, new Vector3(fx, y, WindowBack), new Vector3(0.1f, h, 0.08f), "ShipTrimCyan");
        Strip(root, new Vector3(fx, y, WindowFront), new Vector3(0.1f, h, 0.08f), "ShipTrimCyan");
        for (int k = 1; k < 3; k++)
            Strip(root, new Vector3(x, y, WindowBack + span * k / 3f), new Vector3(Wall * 0.6f, h, 0.05f), "ShipWall");
    }

    static void Console(Transform root)
    {
        // Pedestal and a desk top tilted toward the player
        Box(root, "ConsoleBase", new Vector3(0f, 0.42f, 1.25f), new Vector3(1.5f, 0.84f, 0.5f), "ShipWall", 2f);
        var desk = Box(root, "ConsoleDesk", new Vector3(0f, 0.9f, 1.12f), new Vector3(1.9f, 0.08f, 0.6f), "ShipConsole", 1f);
        desk.localRotation = Quaternion.Euler(-14f, 0f, 0f);
        Strip(root, new Vector3(0f, 0.86f, 0.83f), new Vector3(1.9f, 0.04f, 0.04f), "ShipTrimCyan");

        // Screen bezel behind the menu canvas
        Quaternion tilt = Quaternion.Euler(-ScreenTilt, 0f, 0f);
        var bezel = Box(root, "ScreenBezel", ScreenCenter + tilt * new Vector3(0f, 0f, 0.035f), new Vector3(ScreenWidth + 0.08f, ScreenHeight + 0.08f, 0.05f), "ShipTrimViolet", 1f);
        bezel.localRotation = tilt;
        var stand = Box(root, "ScreenStand", new Vector3(0f, 0.97f, 1.55f), new Vector3(0.3f, 0.12f, 0.12f), "ShipWall", 1f);
        stand.localRotation = Quaternion.identity;
    }

    static void Strip(Transform root, Vector3 center, Vector3 size, string material)
    {
        Box(root, "Strip", center, size, material, 1f, true);
    }

    static void Light(Transform root, string name, Vector3 position, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = position;
        var light = go.AddComponent<UnityEngine.Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
    }

    static Material Load(string name)
    {
        if (materials.TryGetValue(name, out Material m) && m != null)
            return m;
        m = Resources.Load<Material>("MainMenu/" + name);
        materials[name] = m;
        return m;
    }

    /// <summary>
    /// A box with UVs in metres (tile = metres per texture repeat). A strip maps u along its
    /// longest side and v across each face, for the LED trim textures.
    /// </summary>
    static Transform Box(Transform root, string name, Vector3 center, Vector3 size, string material, float tile, bool strip = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = center;
        var filter = go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Load(material);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        filter.sharedMesh = BoxMesh(size, tile, strip);
        return go.transform;
    }

    static Mesh BoxMesh(Vector3 size, float tile, bool strip)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        Vector3 h = size * 0.5f;
        Vector3[] faceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        int longAxis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        foreach (Vector3 n in faceNormals)
        {
            // Two axes across the face: a (right on the face) and b (up on the face).
            Vector3 a = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.Cross(Vector3.up, n).normalized * -1f;
            Vector3 b = Mathf.Abs(n.y) > 0.5f ? Vector3.Cross(n, a) : Vector3.up;
            if (Mathf.Abs(n.y) > 0.5f && n.y < 0f)
                b = -b;
            float sa = Mathf.Abs(Vector3.Dot(size, a));
            float sb = Mathf.Abs(Vector3.Dot(size, b));
            int start = verts.Count;
            for (int i = 0; i < 4; i++)
            {
                float fa = (i == 1 || i == 2) ? 1f : 0f;
                float fb = i >= 2 ? 1f : 0f;
                Vector3 p = Vector3.Scale(n, h) + a * (fa - 0.5f) * sa + b * (fb - 0.5f) * sb;
                verts.Add(p);
                normals.Add(n);
                if (strip)
                {
                    // u along the strip's long side in metres, v across this face (0..1).
                    Vector3 along = longAxis == 0 ? Vector3.right : longAxis == 1 ? Vector3.up : Vector3.forward;
                    bool aAlong = Mathf.Abs(Vector3.Dot(a, along)) > 0.5f;
                    float u = aAlong ? fa * sa : fb * sb;
                    float v = aAlong ? fb : fa;
                    uvs.Add(new Vector2(u / tile, v));
                }
                else
                    uvs.Add(new Vector2(fa * sa / tile, fb * sb / tile));
            }

            // Wind so the face points along n.
            Vector3 cross = Vector3.Cross(verts[start + 1] - verts[start], verts[start + 2] - verts[start]);
            // Unity's front faces: Cross(v1 - v0, v2 - v0) points along the normal.
            if (Vector3.Dot(cross, n) > 0f)
            {
                tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
                tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
            }
            else
            {
                tris.Add(start); tris.Add(start + 2); tris.Add(start + 1);
                tris.Add(start); tris.Add(start + 3); tris.Add(start + 2);
            }
        }

        var mesh = new Mesh { name = "Box" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }
}
