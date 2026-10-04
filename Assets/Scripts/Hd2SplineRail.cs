using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Builds an Hd2GrindRail from the Unity spline on this object.
/// Knots stay editable. The beam follows the curve.
/// </summary>
[ExecuteAlways]
public class Hd2SplineRail : MonoBehaviour
{
    public Color color = Color.yellow;
    public float sampleStep = 0.4f;

    SplineContainer container;
    Hd2GrindRail rail;
    int cachedHash;

    void OnEnable()
    {
        Bake(true);
    }

    void Update()
    {
        if (Application.isPlaying)
            return;
        Bake(false);
    }

    void Bake(bool force)
    {
        if (container == null)
            container = GetComponentInChildren<SplineContainer>();
        if (container == null || container.Splines.Count == 0 || container.Splines[0].Count < 2)
            return;

        int hash = PathHash();
        if (!force && hash == cachedHash)
            return;

        if (GetComponent<MeshFilter>() == null)
            gameObject.AddComponent<MeshFilter>();
        if (GetComponent<MeshRenderer>() == null)
            gameObject.AddComponent<MeshRenderer>();
        if (GetComponent<MeshCollider>() == null)
            gameObject.AddComponent<MeshCollider>();

        if (rail == null)
            rail = GetComponent<Hd2GrindRail>();
        if (rail == null)
            rail = gameObject.AddComponent<Hd2GrindRail>();

        Spline spline = container.Splines[0];
        float length = Mathf.Max(0.5f, container.CalculateLength());
        int count = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(0.15f, sampleStep)) + 1);
        var points = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float3 world = container.EvaluatePosition(spline, t);
            points[i] = transform.InverseTransformPoint(new Vector3(world.x, world.y, world.z));
        }

        rail.points = points;
        rail.color = color;
        rail.enabled = false;
        rail.enabled = true;
        cachedHash = hash;
    }

    int PathHash()
    {
        Spline spline = container.Splines[0];
        unchecked
        {
            int hash = spline.Count;
            for (int i = 0; i < spline.Count; i++)
            {
                float3 position = spline[i].Position;
                hash = hash * 31 + position.GetHashCode();
                hash = hash * 31 + spline[i].TangentIn.GetHashCode();
                hash = hash * 31 + spline[i].TangentOut.GetHashCode();
            }

            hash = hash * 31 + transform.position.GetHashCode();
            hash = hash * 31 + transform.rotation.GetHashCode();
            return hash;
        }
    }
}
