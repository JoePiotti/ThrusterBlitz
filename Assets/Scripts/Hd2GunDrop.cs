using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// On death the weapon leaves the hand and the hand stays with the arm. A grip weapon carries
/// the visible fist (its "Hand" child and the HandSkin mesh); that fist moves onto the
/// ragdoll's hand bone so it falls with the arm, and the gun drops on its own as a loose
/// physics object. Before this the gun and fist stayed together where the hand had been, or
/// snagged, while the body fell, and the fist's wrist stretched from the arm to the gun.
/// </summary>
public static class Hd2GunDrop
{
    /// <summary>Seconds a dropped gun stays on the floor.</summary>
    public const float Lifetime = 8f;

    static readonly List<Transform> moved = new List<Transform>();

    /// <summary>
    /// Drops a held gun. Its fist goes onto armBone (when there is one). Returns the objects
    /// moved onto the bone, so a body that is reused (a bot) can remove them at respawn.
    /// The corpse's colliders are ignored so the gun can't catch on its owner.
    /// </summary>
    public static List<Transform> Drop(Transform gun, Transform armBone, Transform corpse)
    {
        var result = new List<Transform>();
        if (gun == null)
            return result;

        Silence(gun.gameObject);
        if (armBone != null)
            MoveFist(gun, armBone, result);

        Rigidbody boneBody = armBone != null ? armBone.GetComponentInParent<Rigidbody>() : null;
        gun.SetParent(null, true);

        var body = gun.gameObject.AddComponent<Rigidbody>();
        body.mass = 1.5f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        if (boneBody != null)
        {
            body.linearVelocity = boneBody.linearVelocity;
            body.angularVelocity = boneBody.angularVelocity;
        }

        BoxCollider box = FitBox(gun);
        if (corpse != null)
        {
            Collider[] own = corpse.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < own.Length; i++)
            {
                if (own[i] != null)
                    Physics.IgnoreCollision(box, own[i], true);
            }
        }

        Object.Destroy(gun.gameObject, Lifetime);
        return result;
    }

    static void MoveFist(Transform gun, Transform armBone, List<Transform> result)
    {
        moved.Clear();
        Transform fist = Find(gun, "Hand");
        if (fist != null)
            moved.Add(fist);

        // The fist mesh may sit beside its bones rather than under them; it goes too.
        SkinnedMeshRenderer[] skins = gun.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skins.Length; i++)
        {
            SkinnedMeshRenderer skin = skins[i];
            if (skin == null || skin.name != "HandSkin")
                continue;
            if (fist == null || !skin.transform.IsChildOf(fist))
                moved.Add(skin.transform);
        }

        for (int i = 0; i < moved.Count; i++)
        {
            moved[i].SetParent(armBone, true);
            SkinnedMeshRenderer[] fistSkins = moved[i].GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int s = 0; s < fistSkins.Length; s++)
                fistSkins[s].updateWhenOffscreen = true;
            result.Add(moved[i]);
        }
    }

    // A box around the gun's meshes, in the gun's own space.
    static BoxCollider FitBox(Transform gun)
    {
        Bounds local = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;
        MeshFilter[] meshes = gun.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshes.Length; i++)
        {
            Mesh mesh = meshes[i].sharedMesh;
            if (mesh == null)
                continue;
            Bounds b = mesh.bounds;
            Vector3 min = b.min;
            Vector3 max = b.max;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = new Vector3((c & 1) == 0 ? min.x : max.x, (c & 2) == 0 ? min.y : max.y, (c & 4) == 0 ? min.z : max.z);
                Vector3 point = gun.InverseTransformPoint(meshes[i].transform.TransformPoint(corner));
                if (!any)
                {
                    local = new Bounds(point, Vector3.zero);
                    any = true;
                }
                else
                    local.Encapsulate(point);
            }
        }

        var box = gun.gameObject.AddComponent<BoxCollider>();
        if (any)
        {
            box.center = local.center;
            Vector3 size = local.size;
            float least = 0.04f / Mathf.Max(0.0001f, Mathf.Abs(gun.lossyScale.x));
            box.size = new Vector3(Mathf.Max(size.x, least), Mathf.Max(size.y, least), Mathf.Max(size.z, least));
        }

        return box;
    }

    // A dropped gun does nothing: no scripts, beams, effects or old colliders.
    static void Silence(GameObject gun)
    {
        Behaviour[] behaviours = gun.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
            behaviours[i].enabled = false;

        LineRenderer[] lines = gun.GetComponentsInChildren<LineRenderer>(true);
        for (int i = 0; i < lines.Length; i++)
            lines[i].enabled = false;

        ParticleSystem[] particles = gun.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Collider[] colliders = gun.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;
    }

    static Transform Find(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = Find(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
