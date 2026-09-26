using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One continuous grind path. Points are local-space feet positions.
/// An empty points array builds a horseshoe: open ends at endFeetHeight,
/// far crest at crestFeetHeight. The mesh is the solid the thrust arc hits.
/// </summary>
[ExecuteAlways]
public class Hd2GrindRail : MonoBehaviour
{
    [Tooltip("Local-space feet path. Two or more points are used as the rail. Leave empty to build the horseshoe below.")]
    public Vector3[] points;

    [Header("Horseshoe")]
    [Tooltip("Distance between the two open ends, in meters.")]
    public float openWidth = 10f;
    [Tooltip("Length of each straight leg before the far curve, in meters.")]
    public float legLength = 5f;
    [Tooltip("Feet height at both open ends. Matches the old ground rail so feet meet the beam.")]
    public float endFeetHeight = 0.21f;
    [Tooltip("Feet height at the far crest, about 1 m above the floor.")]
    public float crestFeetHeight = 1f;
    [Tooltip("Local XZ midpoint of the open end. Y is ignored.")]
    public Vector3 openCenter = new Vector3(0f, 0f, 2f);

    [Tooltip("A landing this close to either open end rides toward the other end.")]
    public float endEntry = 1.5f;

    public float length { get; private set; }

    const float BeamWidth = 0.36f;
    const float BeamThickness = 0.18f;
    const float SampleStep = 0.35f;

    Vector3[] path;
    float[] cumulative;
    Mesh beamMesh;
    MeshFilter beamFilter;
    MeshCollider beamCollider;
    int shapeHash;

    void OnEnable()
    {
        beamFilter = GetComponent<MeshFilter>();
        beamCollider = GetComponent<MeshCollider>();
        Rebuild();
    }

    void OnDestroy()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= RebuildIfAlive;
#endif
        DestroyMesh();
    }

    void Update()
    {
        if (beamFilter != null && beamFilter.sharedMesh == null)
            Rebuild();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall -= RebuildIfAlive;
        UnityEditor.EditorApplication.delayCall += RebuildIfAlive;
    }

    void RebuildIfAlive()
    {
        if (this == null)
            return;
        Rebuild();
    }
#endif

    public Vector3 PointAlong(float distance)
    {
        if (path == null || path.Length == 0)
            return transform.position;
        if (path.Length == 1)
            return transform.TransformPoint(path[0]);

        distance = Mathf.Clamp(distance, 0f, length);
        int segment = SegmentIndex(distance);
        float start = cumulative[segment];
        float span = cumulative[segment + 1] - start;
        float t = span > 0.0001f ? (distance - start) / span : 0f;
        return transform.TransformPoint(Vector3.Lerp(path[segment], path[segment + 1], t));
    }

    public float DistanceAlong(Vector3 worldPosition)
    {
        if (path == null || path.Length < 2)
            return 0f;

        Vector3 local = transform.InverseTransformPoint(worldPosition);
        float bestSqr = float.MaxValue;
        float bestAlong = 0f;
        for (int i = 0; i < path.Length - 1; i++)
        {
            Vector3 a = path[i];
            Vector3 b = path[i + 1];
            Vector3 ab = b - a;
            float abSqr = ab.sqrMagnitude;
            if (abSqr < 0.0000001f)
                continue;

            float t = Mathf.Clamp01(Vector3.Dot(local - a, ab) / abSqr);
            Vector3 closest = a + ab * t;
            float sqr = (local - closest).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            bestAlong = cumulative[i] + Mathf.Sqrt(abSqr) * t;
        }

        return bestAlong;
    }

    public Vector3 PlanarTangent(float distance)
    {
        Vector3 tangent = Tangent(distance);
        tangent.y = 0f;
        if (tangent.sqrMagnitude < 0.0001f)
            return Vector3.forward;
        return tangent.normalized;
    }

    /// <summary>
    /// +1 follows the polyline toward the last point. -1 follows it toward the first.
    /// Within endEntry of either open end, the ride always heads for the other end.
    /// </summary>
    public float EntrySign(Vector3 playerFeet, Vector3 landing)
    {
        float dist = DistanceAlong(landing);
        float span = Mathf.Max(0.01f, length);
        float entry = Mathf.Clamp(endEntry, 0.05f, span * 0.49f);
        if (dist <= entry)
            return 1f;
        if (dist >= span - entry)
            return -1f;

        Vector3 travel = landing - playerFeet;
        travel.y = 0f;
        float along = Vector3.Dot(travel, PlanarTangent(dist));
        if (Mathf.Abs(along) >= 0.2f)
            return Mathf.Sign(along);
        return (span - dist) >= dist ? 1f : -1f;
    }

    Vector3 Tangent(float distance)
    {
        if (path == null || path.Length < 2)
            return transform.forward;

        int segment = SegmentIndex(Mathf.Clamp(distance, 0f, length));
        Vector3 ab = path[segment + 1] - path[segment];
        if (ab.sqrMagnitude < 0.0000001f)
            return transform.forward;
        return transform.TransformDirection(ab).normalized;
    }

    int SegmentIndex(float distance)
    {
        int last = path.Length - 2;
        for (int i = last; i >= 0; i--)
        {
            if (distance >= cumulative[i])
                return i;
        }

        return 0;
    }

    void Rebuild()
    {
        int hash = HashShape();
        if (beamMesh != null && hash == shapeHash && beamFilter != null && beamFilter.sharedMesh == beamMesh)
            return;

        BuildPath();
        Mesh mesh = BuildBeamMesh();
        if (mesh == null)
            return;

        mesh.hideFlags = HideFlags.DontSave;
        mesh.name = "HorseshoeRail";
        if (beamFilter != null)
            beamFilter.sharedMesh = mesh;
        if (beamCollider != null)
        {
            beamCollider.sharedMesh = null;
            beamCollider.sharedMesh = mesh;
        }

        DestroyMesh();
        beamMesh = mesh;
        shapeHash = hash;
    }

    int HashShape()
    {
        unchecked
        {
            int hash = 17;
            if (points != null && points.Length >= 2)
            {
                hash = hash * 31 + points.Length;
                for (int i = 0; i < points.Length; i++)
                    hash = hash * 31 + points[i].GetHashCode();
                return hash;
            }

            hash = hash * 31 + openWidth.GetHashCode();
            hash = hash * 31 + legLength.GetHashCode();
            hash = hash * 31 + endFeetHeight.GetHashCode();
            hash = hash * 31 + crestFeetHeight.GetHashCode();
            hash = hash * 31 + openCenter.GetHashCode();
            return hash;
        }
    }

    void BuildPath()
    {
        Vector3[] source = points != null && points.Length >= 2 ? points : BuildHorseshoe();
        var cleaned = new List<Vector3>(source.Length);
        cleaned.Add(source[0]);
        for (int i = 1; i < source.Length; i++)
        {
            if ((source[i] - cleaned[cleaned.Count - 1]).sqrMagnitude > 0.000001f)
                cleaned.Add(source[i]);
        }

        path = cleaned.ToArray();
        cumulative = new float[path.Length];
        length = 0f;
        for (int i = 1; i < path.Length; i++)
        {
            length += Vector3.Distance(path[i - 1], path[i]);
            cumulative[i] = length;
        }
    }

    Vector3[] BuildHorseshoe()
    {
        float half = Mathf.Max(0.5f, openWidth * 0.5f);
        float leg = Mathf.Max(0f, legLength);
        float radius = half;
        float arc = Mathf.PI * radius;
        float total = leg + arc + leg;
        int count = Mathf.Max(3, Mathf.CeilToInt(total / SampleStep) + 1);
        if ((count & 1) == 0)
            count++;

        var sampled = new Vector3[count];
        int last = count - 1;
        for (int i = 0; i < count; i++)
        {
            float s = total * (i / (float)last);
            sampled[i] = SampleHorseshoe(s, total, half, leg, radius, arc);
        }

        return sampled;
    }

    Vector3 SampleHorseshoe(float s, float total, float half, float leg, float radius, float arc)
    {
        float x;
        float z;
        if (s <= leg)
        {
            x = openCenter.x - half;
            z = openCenter.z + s;
        }
        else if (s >= leg + arc)
        {
            float t = s - leg - arc;
            x = openCenter.x + half;
            z = openCenter.z + leg - t;
        }
        else
        {
            float theta = Mathf.PI - (s - leg) / radius;
            x = openCenter.x + Mathf.Cos(theta) * radius;
            z = openCenter.z + leg + Mathf.Sin(theta) * radius;
        }

        float u = total > 0.0001f ? Mathf.Clamp01(s / total) : 0f;
        float hump = 0.5f - 0.5f * Mathf.Cos(u * Mathf.PI * 2f);
        float y = Mathf.Lerp(endFeetHeight, crestFeetHeight, hump);
        return new Vector3(x, y, z);
    }

    Mesh BuildBeamMesh()
    {
        if (path == null || path.Length < 2)
            return null;

        int rings = path.Length;
        var verts = new List<Vector3>(rings * 4);
        var tris = new List<int>((rings - 1) * 24 + 12);
        float halfW = BeamWidth * 0.5f;
        Vector3 firstUp = Vector3.up;

        for (int i = 0; i < rings; i++)
        {
            Vector3 forward;
            if (i < rings - 1)
                forward = path[i + 1] - path[i];
            else
                forward = path[i] - path[i - 1];
            if (forward.sqrMagnitude < 0.0000001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0000001f)
                right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right).normalized;
            if (i == 0)
                firstUp = up;

            Vector3 p = path[i];
            verts.Add(p + right * halfW);
            verts.Add(p + right * halfW - up * BeamThickness);
            verts.Add(p - right * halfW - up * BeamThickness);
            verts.Add(p - right * halfW);
        }

        for (int i = 0; i < rings - 1; i++)
        {
            int a = i * 4;
            int b = (i + 1) * 4;
            AddTri(tris, a + 0, b + 3, b + 0);
            AddTri(tris, a + 0, a + 3, b + 3);
            AddTri(tris, a + 0, b + 1, a + 1);
            AddTri(tris, a + 0, b + 0, b + 1);
            AddTri(tris, a + 1, b + 1, b + 2);
            AddTri(tris, a + 1, b + 2, a + 2);
            AddTri(tris, a + 3, b + 2, b + 3);
            AddTri(tris, a + 3, a + 2, b + 2);
        }

        int end = (rings - 1) * 4;
        AddTri(tris, 0, 1, 2);
        AddTri(tris, 0, 2, 3);
        AddTri(tris, end + 0, end + 2, end + 1);
        AddTri(tris, end + 0, end + 3, end + 2);

        OrientOutward(verts, tris, firstUp);

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AddTri(List<int> tris, int a, int b, int c)
    {
        tris.Add(a);
        tris.Add(b);
        tris.Add(c);
    }

    static void OrientOutward(List<Vector3> verts, List<int> tris, Vector3 outward)
    {
        if (tris.Count < 3)
            return;

        Vector3 normal = Vector3.Cross(
            verts[tris[1]] - verts[tris[0]],
            verts[tris[2]] - verts[tris[0]]);
        if (Vector3.Dot(normal, outward) >= 0f)
            return;

        for (int i = 0; i < tris.Count; i += 3)
        {
            int swap = tris[i + 1];
            tris[i + 1] = tris[i + 2];
            tris[i + 2] = swap;
        }
    }

    void DestroyMesh()
    {
        if (beamMesh == null)
            return;

        if (Application.isPlaying)
            Destroy(beamMesh);
        else
            DestroyImmediate(beamMesh);
        beamMesh = null;
    }
}
