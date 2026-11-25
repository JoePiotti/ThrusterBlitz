using UnityEngine;

/// <summary>
/// Shared pieces for the pickup looks (Hd2FloatRings): glowing lines and
/// soft see-through shapes, built at run time over the saved pad and sphere.
/// </summary>
public static class Hd2PickupLook
{
    static Material lineMaterial;

    public static LineRenderer Line(Transform parent, string name, Color color, float width, int points, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = loop;
        line.widthMultiplier = width;
        line.positionCount = points;
        if (lineMaterial == null)
            lineMaterial = new Material(Shader.Find("Sprites/Default"));
        line.sharedMaterial = lineMaterial;
        line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCapVertices = 2;
        return line;
    }

    /// <summary>Points of a regular polygon (a circle with many sides) in the XZ plane.</summary>
    public static void Polygon(LineRenderer line, int sides, float radius, float filled = 1f)
    {
        filled = Mathf.Clamp01(filled);
        int count = filled >= 1f ? sides : Mathf.Max(2, Mathf.CeilToInt(sides * filled) + 1);
        line.loop = filled >= 1f;
        line.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            float t = Mathf.Min(i / (float)sides, filled);
            float angle = t * Mathf.PI * 2f + Mathf.PI * 0.5f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    public static void Tint(LineRenderer line, Color color, float alpha)
    {
        if (line == null)
            return;
        Color c = new Color(color.r, color.g, color.b, alpha);
        line.startColor = line.endColor = c;
    }

    /// <summary>A see-through unlit primitive (beam, glow disc) with no collider.</summary>
    public static Material Ghost(GameObject target, Color color)
    {
        var collider = target.GetComponent<Collider>();
        if (collider != null)
            Object.Destroy(collider);
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 2f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = 3000;
        SetColor(material, color);
        var renderer = target.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return material;
    }

    public static void SetColor(Material material, Color color)
    {
        if (material == null)
            return;
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
    }

    /// <summary>Hides the saved pad, fades the saved sphere shell and the old refill ring.</summary>
    public static void QuietOldLook(Transform pickup, float shellAlpha)
    {
        Transform pad = pickup.Find("Pad");
        if (pad != null)
        {
            var renderer = pad.GetComponent<Renderer>();
            if (renderer != null)
                renderer.enabled = false;
        }

        Transform sphere = pickup.Find("Sphere");
        if (sphere != null)
        {
            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material shell = renderer.material;
                Color c = shell.HasProperty("_BaseColor") ? shell.GetColor("_BaseColor") : shell.color;
                c.a = shellAlpha;
                SetColor(shell, c);
                renderer.enabled = shellAlpha > 0.001f;
            }
        }
    }

    public static void HideOldRing(Transform pickup)
    {
        Transform ring = pickup.Find("Ring");
        if (ring == null)
            return;
        var line = ring.GetComponent<LineRenderer>();
        if (line != null && line.enabled)
            line.enabled = false;
    }
}
