using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Copies a spline into the grind rail on this object. Rail points are feet positions.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-50)]
public class Hd2SplineRail : MonoBehaviour
{
    const float SampleStep = 0.5f;

    void Awake()
    {
        Apply();
    }

    public void Apply()
    {
        var container = GetComponent<SplineContainer>();
        var rail = GetComponent<Hd2GrindRail>();
        if (container == null || rail == null)
            return;

        float length = container.CalculateLength();
        int count = Mathf.Max(2, Mathf.CeilToInt(length / SampleStep) + 1);
        var sampled = new Vector3[count];
        int last = count - 1;
        for (int i = 0; i < count; i++)
        {
            float t = last == 0 ? 0f : i / (float)last;
            Vector3 world = container.EvaluatePosition(t);
            sampled[i] = transform.InverseTransformPoint(world);
        }

        rail.points = sampled;
        rail.enabled = false;
        rail.enabled = true;
    }
}
