using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;

/// <summary>
/// Tools > HD2 > Apply Night League Look.
/// Skins the Shootout arena face by face. Every ProBuilder face gets a material for what it
/// is and where it sits, with UVs laid out for that material:
///   court      - painted court lines and team zones over the whole floor
///   Mid walls  - league banner on the long faces, padded ends
///   Low 45s    - TB badge on the long faces
///   half walls - RED / BLUE boards, so you know which half you're in
///   ramps      - tread up the slopes with hazard edges, an amber LED trim down each slab
///                side, amber lips on the walkways, framed support panels (team coloured
///                on the faces that look at centre court)
///   side decks - grating with amber lips
///   bases      - team deck, team lips, downlit undersides, team-lit columns
///   frame      - court-facing column faces get a light strip, the rest stay plain steel;
///                ring beams carry floodlights; base roofs get a downlit ceiling and an LED
///                ribbon board along the front
/// The outer arena walls, shields, pickups and everything not matched by RoleOf keep their
/// look. Undo works. Run it again after editing the level, then save the scene.
/// Textures and materials: Assets/Materials/ShootoutLook (made by Tools/shootout_look/gen.py).
/// </summary>
static class Hd2ShootoutLook
{
    const string Folder = "Assets/Materials/ShootoutLook/";

    enum Map
    {
        Keep,
        Fit,     // the whole (coplanar) wall shows the whole texture
        Bands,   // u repeats in whole tiles along the wall, v spans it bottom to top
        Metres,  // repeats in whole tiles both ways
        Across,  // slopes: u spans the width, v repeats up the slope
        Deck,    // base decks: v = 0 on the edge facing the arena
        Column,  // u spans the face width, v repeats in whole tiles up it
        Slab     // thin slanted strips: u runs along the long edge, v spans the thickness
    }

    struct Pick
    {
        public string material;
        public Map map;
        public float tileU;
        public float tileV;

        public Pick(string material, Map map, float tileU = 1f, float tileV = 1f)
        {
            this.material = material;
            this.map = map;
            this.tileU = tileU;
            this.tileV = tileV;
        }
    }

    struct FaceInfo
    {
        public Vector3 normal;
        public float area;
        public float minY;
        public float height;
        public float width;   // horizontal extent along the face (vertical faces)
        public float fill;    // area / (width * height): 1 for rectangles, small for slanted strips
        public float facing;  // how much the face looks toward centre court (-1..1, horizontal)
        public Vector3 center;
        public Vector3 longEdge;
    }

    struct Context
    {
        public string role;
        public string team;   // Red / Blue for objects that belong to a side, else null
        public float floorY;
        public Vector3 arenaCenter;
    }

    static readonly Pick KeepFace = new Pick(null, Map.Keep);

    [MenuItem("Tools/HD2/Apply Night League Look (open scene)")]
    static void Apply()
    {
        var meshes = Object.FindObjectsByType<ProBuilderMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        float floorY = 0f;
        Vector3 arenaCenter = Vector3.zero;
        foreach (var pb in meshes)
        {
            if (pb.name == "arena")
            {
                var b = pb.GetComponent<Renderer>().bounds;
                floorY = b.min.y;
                arenaCenter = new Vector3(b.center.x, floorY, b.center.z);
            }
        }

        var materials = new Dictionary<string, Material>();
        int styled = 0;
        var missing = new HashSet<string>();
        foreach (var pb in meshes)
        {
            string role = RoleOf(pb.transform);
            if (role == null || EditorUtility.IsPersistent(pb))
                continue;
            var renderer = pb.GetComponent<MeshRenderer>();
            if (renderer == null)
                continue;

            var ctx = new Context
            {
                role = role,
                team = TeamOf(pb.transform, renderer.bounds.center, arenaCenter, role),
                floorY = floorY,
                arenaCenter = arenaCenter
            };
            Undo.RegisterCompleteObjectUndo(new Object[] { pb, renderer }, "Night League look");
            Style(pb, renderer, ctx, materials, missing);
            EditorUtility.SetDirty(pb);
            EditorUtility.SetDirty(renderer);
            EditorSceneManager.MarkSceneDirty(pb.gameObject.scene);
            styled++;
        }

        if (missing.Count > 0)
            Debug.LogWarning("Night League look: missing materials in " + Folder + ": " + string.Join(", ", missing));
        Debug.Log("Night League look: styled " + styled + " objects. Save the scene to keep it.");
    }

    // ---------------------------------------------------------------- roles
    static string RoleOf(Transform t)
    {
        string n = t.name;
        if (n == "arena")
            return "court";
        if (n.StartsWith("Mid "))
            return "mid";
        if (n.StartsWith("Low 45"))
            return "low45";
        if (n.StartsWith("half wall support"))
            return "halfwall";
        if (n == "Cube Center")
            return "low45";
        if (n.StartsWith("Ramp "))
            return "ramp";
        if (n.StartsWith("platform high"))
            return "high";
        if (n.StartsWith("side supports"))
            return "frame";
        string team = ParentTeam(t);
        if (n.StartsWith("base platform"))
            return team == null ? null : "deck";
        if (n.StartsWith("corner support") || n.StartsWith("base support"))
            return team == null ? "frame" : "pillar";
        return null;
    }

    static string ParentTeam(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            if (p.name.Contains("Red"))
                return "Red";
            if (p.name.Contains("Blue"))
                return "Blue";
        }

        return null;
    }

    static string TeamOf(Transform t, Vector3 center, Vector3 arenaCenter, string role)
    {
        string team = ParentTeam(t);
        if (team != null)
            return team;
        if (role == "ramp")
        {
            if (t.name.EndsWith(" R"))
                return "Red";
            if (t.name.EndsWith(" B"))
                return "Blue";
        }

        if (role == "halfwall")
            return center.z >= arenaCenter.z ? "Red" : "Blue";
        return null;
    }

    // Which team's end a point is in (red is +z in this arena).
    static string EndOf(Vector3 p, Vector3 arenaCenter)
    {
        return p.z >= arenaCenter.z ? "Red" : "Blue";
    }

    static Pick Choose(Context ctx, FaceInfo f)
    {
        float ny = f.normal.y;
        bool up = ny > 0.3f;
        bool flatUp = ny > 0.97f;
        bool down = ny < -0.3f;
        bool vertical = !up && !down;
        float above = f.minY - ctx.floorY;
        if (f.area < 0.0001f)
            return new Pick("SteelPlain", Map.Metres, 1f, 1f);

        switch (ctx.role)
        {
            case "court":
                return up ? new Pick("Court", Map.Fit) : KeepFace;

            case "mid":
                if (vertical)
                    return f.width >= 3.5f && f.height >= 2.5f ? new Pick("BarrierLogo", Map.Fit) : new Pick("BarrierSide", Map.Bands, 2f);
                return up ? new Pick("BarrierTop", Map.Metres, 1f, 1f) : new Pick("SteelPlain", Map.Metres, 1f, 1f);

            case "low45":
                if (vertical)
                    return f.width >= 2.5f ? new Pick("BarrierBadge", Map.Fit) : new Pick("BarrierSide", Map.Bands, 2f);
                return up ? new Pick("BarrierTop", Map.Metres, 1f, 1f) : new Pick("SteelPlain", Map.Metres, 1f, 1f);

            case "halfwall":
                if (vertical)
                    return f.width >= 2f ? new Pick("BarrierTeam" + ctx.team, Map.Fit) : new Pick("BarrierSide", Map.Bands, 2f);
                return up ? new Pick("BarrierTop", Map.Metres, 1f, 1f) : new Pick("SteelPlain", Map.Metres, 1f, 1f);

            case "ramp":
                if (flatUp)
                    return new Pick("WalkDeck", Map.Metres, 1f, 1f);
                if (up)
                    return new Pick("Tread", Map.Across, 1f, 1f);
                if (down)
                    return new Pick("SteelPlain", Map.Metres, 2f, 2f);
                if (f.height <= 0.8f)
                    return new Pick("EdgeAmber", Map.Bands, 1f);
                if (f.fill < 0.5f)
                    return new Pick("TrimAmber", Map.Slab, 2f);
                return f.facing > 0.5f && ctx.team != null
                    ? new Pick("SteelPanel" + ctx.team, Map.Bands, 3.2f)
                    : new Pick("SteelPanel", Map.Bands, 3.2f);

            case "high":
                if (up)
                    return new Pick("Grate", Map.Metres, 1f, 1f);
                if (down)
                    return new Pick("SteelPlain", Map.Metres, 1f, 1f);
                return f.height <= 0.8f ? new Pick("EdgeAmber", Map.Bands, 1f) : new Pick("SteelPanel", Map.Bands, 3.2f);

            case "deck":
                if (up)
                    return new Pick("BaseDeck" + ctx.team, Map.Deck);
                if (down)
                    return f.area > 100f ? new Pick("Canopy", Map.Metres, 3f, 3f) : new Pick("SteelPlain", Map.Metres, 2f, 2f);
                return f.height <= 0.8f ? new Pick("Edge" + ctx.team, Map.Bands, 1f) : new Pick("SteelPanel", Map.Bands, 3.2f);

            case "pillar":
                if (!vertical)
                    return new Pick("SteelPlain", Map.Metres, 1f, 1f);
                if (f.width < 0.6f)
                    return new Pick("SteelPlain", Map.Metres, 1f, 1f);
                return f.facing > 0.3f ? new Pick("Column" + ctx.team, Map.Column, 1f, 2f) : new Pick("ColumnPlain", Map.Column, 1f, 2f);

            case "frame":
                if (vertical)
                {
                    if (above >= 8.5f && f.height >= 2f)
                        return new Pick("StadiumBeam", Map.Bands, 3f);
                    if (f.height <= 0.6f && f.width >= 20f && Mathf.Abs(f.normal.z) > 0.9f && above >= 11f)
                        return new Pick("Ribbon" + EndOf(f.center, ctx.arenaCenter), Map.Bands, 10f);
                    if (f.height <= 0.6f || f.width < 0.6f)
                        return new Pick("SteelPlain", Map.Metres, 1f, 1f);
                    if (f.fill < 0.6f)
                        return new Pick("SteelFascia", Map.Metres, 2f, 2f);
                    return f.facing > 0.5f ? new Pick("ColumnWhite", Map.Column, 1f, 2f) : new Pick("ColumnPlain", Map.Column, 1f, 2f);
                }

                if (down && f.area > 100f)
                    return new Pick("Canopy", Map.Metres, 3f, 3f);
                return new Pick("SteelPlain", Map.Metres, 1f, 1f);
        }

        return KeepFace;
    }

    // ---------------------------------------------------------------- styling
    static void Style(ProBuilderMesh pb, MeshRenderer renderer, Context ctx,
        Dictionary<string, Material> cache, HashSet<string> missing)
    {
        Transform tr = pb.transform;
        IList<Vector3> local = pb.positions;
        var world = new Vector3[local.Count];
        for (int i = 0; i < local.Count; i++)
            world[i] = tr.TransformPoint(local[i]);

        var uvs = new Vector2[local.Count];
        IList<Vector2> old = pb.textures;
        if (old != null && old.Count == uvs.Length)
            old.CopyTo(uvs, 0);

        Material[] oldMaterials = renderer.sharedMaterials;
        var used = new List<Material>();
        IList<Face> faces = pb.faces;
        var infos = new FaceInfo[faces.Count];
        var picks = new Pick[faces.Count];
        var mats = new Material[faces.Count];
        for (int i = 0; i < faces.Count; i++)
        {
            infos[i] = Measure(faces[i], world, ctx.arenaCenter);
            picks[i] = Choose(ctx, infos[i]);
            if (picks[i].map != Map.Keep)
            {
                mats[i] = Load(picks[i].material, cache, missing);
                if (mats[i] == null)
                    picks[i] = KeepFace;
            }

            if (picks[i].map == Map.Keep && oldMaterials.Length > 0)
                mats[i] = oldMaterials[Mathf.Clamp(faces[i].submeshIndex, 0, oldMaterials.Length - 1)];
        }

        // Faces that share a plane and a material and touch each other are laid out as one
        // wall, so a graphic or a run of tiles carries across them (the half walls are two
        // faces per side). Separate faces on the same plane (a row of columns) stay separate.
        var ext = new Vector4[faces.Count];
        var keys = new string[faces.Count];
        for (int i = 0; i < faces.Count; i++)
        {
            if (picks[i].map == Map.Keep)
                continue;
            Axes(infos[i], picks[i], ctx.arenaCenter, out Vector3 a, out Vector3 b);
            Vector3 n = infos[i].normal;
            keys[i] = string.Format("{0}|{1}|{2:F1},{3:F1},{4:F1}|{5:F1}", picks[i].material, picks[i].map,
                n.x, n.y, n.z, Vector3.Dot(n, infos[i].center));
            var e = new Vector4(float.MaxValue, float.MinValue, float.MaxValue, float.MinValue);
            foreach (int v in faces[i].distinctIndexes)
            {
                float pa = Vector3.Dot(world[v], a), pbv = Vector3.Dot(world[v], b);
                e.x = Mathf.Min(e.x, pa);
                e.y = Mathf.Max(e.y, pa);
                e.z = Mathf.Min(e.z, pbv);
                e.w = Mathf.Max(e.w, pbv);
            }

            ext[i] = e;
        }

        var parent = new int[faces.Count];
        for (int i = 0; i < faces.Count; i++)
            parent[i] = i;
        const float touch = 0.05f;
        for (int i = 0; i < faces.Count; i++)
        {
            if (keys[i] == null)
                continue;
            for (int j = i + 1; j < faces.Count; j++)
            {
                if (keys[j] != keys[i])
                    continue;
                bool overlapA = ext[i].x <= ext[j].y + touch && ext[j].x <= ext[i].y + touch;
                bool overlapB = ext[i].z <= ext[j].w + touch && ext[j].z <= ext[i].w + touch;
                if (overlapA && overlapB)
                    parent[Find(parent, i)] = Find(parent, j);
            }
        }

        var groupExt = new Dictionary<int, Vector4>();
        for (int i = 0; i < faces.Count; i++)
        {
            if (keys[i] == null)
                continue;
            int root = Find(parent, i);
            groupExt[root] = groupExt.TryGetValue(root, out Vector4 g)
                ? new Vector4(Mathf.Min(g.x, ext[i].x), Mathf.Max(g.y, ext[i].y), Mathf.Min(g.z, ext[i].z), Mathf.Max(g.w, ext[i].w))
                : ext[i];
        }

        for (int i = 0; i < faces.Count; i++)
        {
            Face face = faces[i];
            if (picks[i].map != Map.Keep)
            {
                Lay(face, world, uvs, infos[i], picks[i], ctx.arenaCenter, groupExt[Find(parent, i)]);
                face.manualUV = true;
            }

            int index = used.IndexOf(mats[i]);
            if (index < 0)
            {
                used.Add(mats[i]);
                index = used.Count - 1;
            }

            face.submeshIndex = index;
        }

        renderer.sharedMaterials = used.ToArray();
        pb.textures = uvs;
        pb.ToMesh();
        pb.Refresh();
    }

    static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    static Material Load(string name, Dictionary<string, Material> cache, HashSet<string> missing)
    {
        if (cache.TryGetValue(name, out Material m))
            return m;
        m = AssetDatabase.LoadAssetAtPath<Material>(Folder + "NL_" + name + ".mat");
        if (m == null)
            missing.Add(name);
        cache[name] = m;
        return m;
    }

    static FaceInfo Measure(Face face, Vector3[] world, Vector3 arenaCenter)
    {
        var info = new FaceInfo();
        Vector3 sum = Vector3.zero;
        float area = 0f;
        var tris = face.indexes;
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            Vector3 a = world[tris[i]], b = world[tris[i + 1]], c = world[tris[i + 2]];
            Vector3 cross = Vector3.Cross(b - a, c - a);
            sum += cross;
            area += cross.magnitude * 0.5f;
        }

        info.normal = sum.sqrMagnitude > 1e-10f ? sum.normalized : Vector3.up;
        info.area = area;
        float minY = float.MaxValue, maxY = float.MinValue;
        Vector3 center = Vector3.zero;
        var verts = face.distinctIndexes;
        foreach (int i in verts)
        {
            minY = Mathf.Min(minY, world[i].y);
            maxY = Mathf.Max(maxY, world[i].y);
            center += world[i];
        }

        info.center = verts.Count > 0 ? center / verts.Count : Vector3.zero;
        info.minY = minY;
        info.height = maxY - minY;
        Vector3 t = Tangent(info.normal);
        float minU = float.MaxValue, maxU = float.MinValue;
        foreach (int i in verts)
        {
            float u = Vector3.Dot(world[i], t);
            minU = Mathf.Min(minU, u);
            maxU = Mathf.Max(maxU, u);
        }

        info.width = maxU - minU;
        info.fill = info.width * info.height > 1e-4f ? area / (info.width * info.height) : 1f;

        Vector3 toCenter = arenaCenter - info.center;
        toCenter.y = 0f;
        Vector3 flatN = new Vector3(info.normal.x, 0f, info.normal.z);
        info.facing = toCenter.sqrMagnitude > 1e-4f && flatN.sqrMagnitude > 1e-4f
            ? Vector3.Dot(flatN.normalized, toCenter.normalized)
            : 0f;

        // Longest edge (for slanted strips like the ramp slab sides).
        float best = 0f;
        info.longEdge = t;
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                Vector3 e = world[tris[i + (k + 1) % 3]] - world[tris[i + k]];
                if (e.sqrMagnitude > best)
                {
                    best = e.sqrMagnitude;
                    info.longEdge = e.normalized;
                }
            }
        }

        return info;
    }

    // Horizontal direction that runs to the right when you look straight at the face.
    static Vector3 Tangent(Vector3 normal)
    {
        Vector3 t = Vector3.Cross(normal, Vector3.up);
        if (t.sqrMagnitude < 1e-6f)
            return Vector3.right;
        return t.normalized;
    }

    // Two axes on the face (in metres): a runs "right", b runs "up" the texture.
    static void Axes(FaceInfo info, Pick pick, Vector3 arenaCenter, out Vector3 a, out Vector3 b)
    {
        Vector3 n = info.normal;
        bool vertical = Mathf.Abs(n.y) <= 0.3f;
        if (pick.map == Map.Across)
        {
            Vector3 down = new Vector3(n.x, 0f, n.z);
            down = down.sqrMagnitude > 1e-6f ? down.normalized : Vector3.forward;
            a = Vector3.Cross(Vector3.up, -down).normalized;
            b = Vector3.Cross(n, a).normalized;
            if (Vector3.Dot(b, Vector3.up) < 0f)
                b = -b;
        }
        else if (pick.map == Map.Deck)
        {
            Vector3 away = info.center - arenaCenter;
            away.y = 0f;
            b = Mathf.Abs(away.x) > Mathf.Abs(away.z)
                ? new Vector3(Mathf.Sign(away.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(away.z));
            a = Vector3.Cross(Vector3.up, b).normalized;
        }
        else if (pick.map == Map.Slab)
        {
            a = Vector3.ProjectOnPlane(info.longEdge, n).normalized;
            if (Vector3.Dot(a, Tangent(n)) < 0f)
                a = -a;
            b = Vector3.Cross(n, a).normalized;
            if (Vector3.Dot(b, Vector3.up) < 0f)
                b = -b;
        }
        else if (vertical)
        {
            a = Tangent(n);
            b = Vector3.up;
        }
        else
        {
            // Floors, tops and ceilings: world X right, world Z up (seen from above, north up).
            a = Vector3.right;
            b = Vector3.forward;
            if (n.y < 0f)
                a = -a;
        }
    }

    static void Lay(Face face, Vector3[] world, Vector2[] uvs, FaceInfo info, Pick pick, Vector3 arenaCenter, Vector4 ext)
    {
        Axes(info, pick, arenaCenter, out Vector3 a, out Vector3 b);
        float minA = ext.x, minB = ext.z;
        float spanA = Mathf.Max(1e-4f, ext.y - ext.x);
        float spanB = Mathf.Max(1e-4f, ext.w - ext.z);
        float tilesA = Mathf.Max(1f, Mathf.Round(spanA / pick.tileU));
        float tilesB = Mathf.Max(1f, Mathf.Round(spanB / pick.tileV));
        foreach (int i in face.distinctIndexes)
        {
            float fa = (Vector3.Dot(world[i], a) - minA) / spanA;
            float fb = (Vector3.Dot(world[i], b) - minB) / spanB;
            Vector2 uv;
            switch (pick.map)
            {
                case Map.Fit:
                case Map.Deck:
                    uv = new Vector2(fa, fb);
                    break;
                case Map.Bands:
                case Map.Slab:
                    uv = new Vector2(fa * tilesA, fb);
                    break;
                case Map.Across:
                case Map.Column:
                    uv = new Vector2(fa, fb * tilesB);
                    break;
                default:
                    uv = new Vector2(fa * tilesA, fb * tilesB);
                    break;
            }

            uvs[i] = uv;
        }
    }
}
