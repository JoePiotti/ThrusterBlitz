using UnityEngine;

/// <summary>
/// Turns a pickup icon toward the headset. The quad's front faces the camera.
/// </summary>
public static class Hd2PickupIcon
{
    public static void Face(Transform icon, Transform ring)
    {
        Camera view = Camera.main;
        if (view == null)
            return;

        Face(icon, view.transform.position);
        Face(ring, view.transform.position);
    }

    static void Face(Transform quad, Vector3 view)
    {
        if (quad == null)
            return;

        Vector3 toView = view - quad.position;
        if (toView.sqrMagnitude < 0.0001f)
            return;

        // Unity's quad shows its picture along -Z. Point that side at the viewer.
        Vector3 picture = Vector3.back;
        var filter = quad.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null && filter.sharedMesh.normals != null && filter.sharedMesh.normals.Length > 0)
            picture = filter.sharedMesh.normals[0];

        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(toView.normalized, up)) > 0.98f)
            up = Vector3.forward;
        Vector3 forward = picture.z < 0f ? -toView : toView;
        quad.rotation = Quaternion.LookRotation(forward, up);
    }
}
