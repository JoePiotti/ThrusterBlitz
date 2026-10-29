using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Builds an Hd2GrindRail from each Unity spline on this object.
/// Knots stay editable. Each curve gets its own beam.
/// </summary>
[ExecuteAlways]
public class Hd2SplineRail : MonoBehaviour
{
    public Color color = Color.yellow;
    public float sampleStep = 0.4f;

    SplineContainer container;
    int cachedHash;

    void OnEnable()
    {
        Bake(true);
    }

    void OnValidate()
    {
        if (Application.isPlaying)
            return;
        Bake(false);
    }

    void Bake(bool force)
    {
        if (container == null)
            container = GetComponent<SplineContainer>();
        if (container == null || container.Splines.Count == 0)
            return;

        int hash = PathHash();
        if (!force && hash == cachedHash)
            return;
        cachedHash = hash;

        int built = 0;
        for (int i = 0; i < container.Splines.Count; i++)
        {
            Spline spline = container.Splines[i];
            if (spline == null || spline.Count < 2)
                continue;

            Hd2GrindRail rail = RailFor(built);
            ApplySpline(rail, spline);
            built++;
        }

        for (int extra = built; extra < 8; extra++)
        {
            Transform child = transform.Find("Rail " + (extra + 1));
            if (child != null)
                child.gameObject.SetActive(false);
        }
    }

    Hd2GrindRail RailFor(int index)
    {
        if (index == 0)
            return EnsureRail(gameObject);

        string childName = "Rail " + (index + 1);
        Transform child = transform.Find(childName);
        GameObject railObject = child != null ? child.gameObject : new GameObject(childName);
        if (child == null)
        {
            railObject.transform.SetParent(transform, false);
            railObject.transform.localPosition = Vector3.zero;
            railObject.transform.localRotation = Quaternion.identity;
            railObject.transform.localScale = Vector3.one;
        }

        railObject.SetActive(true);
        return EnsureRail(railObject);
    }

    static Hd2GrindRail EnsureRail(GameObject railObject)
    {
        if (railObject.GetComponent<MeshFilter>() == null)
            railObject.AddComponent<MeshFilter>();
        if (railObject.GetComponent<MeshRenderer>() == null)
            railObject.AddComponent<MeshRenderer>();
        if (railObject.GetComponent<MeshCollider>() == null)
            railObject.AddComponent<MeshCollider>();

        Hd2GrindRail rail = railObject.GetComponent<Hd2GrindRail>();
        if (rail == null)
            rail = railObject.AddComponent<Hd2GrindRail>();
        return rail;
    }

    void ApplySpline(Hd2GrindRail rail, Spline spline)
    {
        float length = CurveLength(spline);
        int count = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(0.15f, sampleStep)) + 1);
        var points = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float3 world = container.EvaluatePosition(spline, t);
            points[i] = rail.transform.InverseTransformPoint(new Vector3(world.x, world.y, world.z));
        }

        rail.points = points;
        rail.color = color;
        rail.enabled = false;
        rail.enabled = true;
    }

    float CurveLength(Spline spline)
    {
        float length = 0f;
        float3 previous = container.EvaluatePosition(spline, 0f);
        const int steps = 32;
        for (int i = 1; i <= steps; i++)
        {
            float3 next = container.EvaluatePosition(spline, i / (float)steps);
            length += math.distance(previous, next);
            previous = next;
        }

        return Mathf.Max(0.5f, length);
    }

    int PathHash()
    {
        unchecked
        {
            int hash = container.Splines.Count;
            for (int s = 0; s < container.Splines.Count; s++)
            {
                Spline spline = container.Splines[s];
                if (spline == null)
                    continue;
                hash = hash * 31 + spline.Count;
                for (int i = 0; i < spline.Count; i++)
                {
                    hash = hash * 31 + spline[i].Position.GetHashCode();
                    hash = hash * 31 + spline[i].TangentIn.GetHashCode();
                    hash = hash * 31 + spline[i].TangentOut.GetHashCode();
                }
            }

            hash = hash * 31 + transform.position.GetHashCode();
            hash = hash * 31 + transform.rotation.GetHashCode();
            return hash;
        }
    }
}
