using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A headshot kill pops the robot's head off. The robot is one skinned mesh, so the head
/// is cut out of it once per mesh: the pieces skinned to the Head and eye bones become a
/// loose head mesh, and the rest a headless body mesh on the same skeleton. On a headshot
/// death the body swaps to the headless mesh, a spark puff fires at the neck, and the
/// head flies off as its own physics object and is removed after a few seconds.
/// Restore puts the head back when a bot comes back to life.
/// </summary>
public class Hd2HeadPop : MonoBehaviour
{
    const float HeadLifeSeconds = 6f;
    const float LaunchSpeed = 6f;
    const float LaunchUp = 3.5f;
    const float LaunchSpin = 14f;
    static readonly string[] HeadBones = { "Head", "Left_Eye", "Right_Eye" };

    class Cut
    {
        public Mesh headless;
        public Mesh head;     // in the Head bone's bind space
        public int headBone;  // index into the renderer's bones
    }

    static readonly Dictionary<Mesh, Cut> cuts = new Dictionary<Mesh, Cut>();

    SkinnedMeshRenderer skin;
    Mesh original;

    /// <summary>Pops the head off the skinned robot under root. Safe to call on any body.</summary>
    public static void Pop(Transform root, Hd2Health health)
    {
        if (root == null)
            return;
        SkinnedMeshRenderer target = HeadSkin(root, out int headIndex);
        if (target == null)
            return;
        Cut cut = CutFor(target.sharedMesh, target.bones, headIndex);
        if (cut == null || cut.head == null)
            return;

        Transform headBone = target.bones[cut.headBone];
        var marker = root.GetComponent<Hd2HeadPop>();
        if (marker == null)
            marker = root.gameObject.AddComponent<Hd2HeadPop>();
        if (marker.skin == null)
        {
            marker.skin = target;
            marker.original = target.sharedMesh;
        }

        target.sharedMesh = cut.headless;
        Launch(root, target, headBone, cut.head, health);
        Hd2Explosion.Shock(headBone.position, new Color(1f, 0.55f, 0.15f));
    }

    /// <summary>Cuts the head out of this robot's mesh ahead of time, so the first headshot doesn't hitch.</summary>
    public static void Prepare(Transform root)
    {
        if (root == null)
            return;
        SkinnedMeshRenderer target = HeadSkin(root, out int headIndex);
        if (target != null)
            CutFor(target.sharedMesh, target.bones, headIndex);
    }

    /// <summary>Puts the head back (a bot coming back to life).</summary>
    public static void Restore(Transform root)
    {
        if (root == null)
            return;
        var marker = root.GetComponent<Hd2HeadPop>();
        if (marker == null || marker.skin == null || marker.original == null)
            return;
        marker.skin.sharedMesh = marker.original;
        marker.skin = null;
        marker.original = null;
    }

    static SkinnedMeshRenderer HeadSkin(Transform root, out int headIndex)
    {
        headIndex = -1;
        var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int s = 0; s < skins.Length; s++)
        {
            Transform[] bones = skins[s].bones;
            if (skins[s].sharedMesh == null || bones == null)
                continue;
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] != null && bones[b].name == "Head")
                {
                    headIndex = b;
                    return skins[s];
                }
            }
        }

        return null;
    }

    static Cut CutFor(Mesh mesh, Transform[] bones, int headIndex)
    {
        if (mesh == null)
            return null;
        if (cuts.TryGetValue(mesh, out Cut known))
            return known;

        Cut cut = Build(mesh, bones, headIndex);
        cuts[mesh] = cut;
        return cut;
    }

    // Sorts the mesh's separate pieces into head and body. A piece belongs to the head
    // when most of its vertices follow the Head or eye bones; the few neck pieces that
    // bend between head and neck stay on the body.
    static Cut Build(Mesh mesh, Transform[] bones, int headIndex)
    {
        if (!mesh.isReadable)
            return null;

        var isHeadBone = new bool[bones.Length];
        for (int b = 0; b < bones.Length; b++)
            isHeadBone[b] = bones[b] != null && System.Array.IndexOf(HeadBones, bones[b].name) >= 0;

        BoneWeight[] weights = mesh.boneWeights;
        Vector3[] vertices = mesh.vertices;
        int count = vertices.Length;
        if (weights == null || weights.Length != count)
            return null;

        var headVertex = new bool[count];
        for (int v = 0; v < count; v++)
        {
            BoneWeight w = weights[v];
            int top = w.boneIndex0;
            float best = w.weight0;
            if (w.weight1 > best) { best = w.weight1; top = w.boneIndex1; }
            if (w.weight2 > best) { best = w.weight2; top = w.boneIndex2; }
            if (w.weight3 > best) { top = w.boneIndex3; }
            headVertex[v] = top >= 0 && top < isHeadBone.Length && isHeadBone[top];
        }

        // Pieces: vertices joined by triangles.
        var parent = new int[count];
        for (int v = 0; v < count; v++)
            parent[v] = v;
        int Find(int a)
        {
            while (parent[a] != a)
            {
                parent[a] = parent[parent[a]];
                a = parent[a];
            }
            return a;
        }

        int subMeshes = mesh.subMeshCount;
        var triangles = new int[subMeshes][];
        for (int s = 0; s < subMeshes; s++)
        {
            triangles[s] = mesh.GetTriangles(s);
            int[] t = triangles[s];
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = Find(t[i]);
                int b = Find(t[i + 1]);
                int c = Find(t[i + 2]);
                parent[b] = a;
                parent[Find(c)] = Find(a);
            }
        }

        var size = new Dictionary<int, int>();
        var headCount = new Dictionary<int, int>();
        for (int v = 0; v < count; v++)
        {
            int piece = Find(v);
            size.TryGetValue(piece, out int n);
            size[piece] = n + 1;
            if (headVertex[v])
            {
                headCount.TryGetValue(piece, out int h);
                headCount[piece] = h + 1;
            }
        }

        var pieceIsHead = new Dictionary<int, bool>();
        foreach (var pair in size)
        {
            headCount.TryGetValue(pair.Key, out int h);
            pieceIsHead[pair.Key] = h >= pair.Value * 0.6f;
        }

        Matrix4x4 bind = mesh.bindposes[headIndex];
        Vector3[] normals = mesh.normals;
        Vector2[] uvs = mesh.uv;
        var headVertices = new List<Vector3>();
        var headNormals = new List<Vector3>();
        var headUvs = new List<Vector2>();
        var remap = new Dictionary<int, int>();
        var headTriangles = new List<int>[subMeshes];
        var bodyTriangles = new List<int>[subMeshes];
        int headTotal = 0;
        for (int s = 0; s < subMeshes; s++)
        {
            headTriangles[s] = new List<int>();
            bodyTriangles[s] = new List<int>();
            int[] t = triangles[s];
            for (int i = 0; i < t.Length; i += 3)
            {
                bool head = pieceIsHead[Find(t[i])];
                if (!head)
                {
                    bodyTriangles[s].Add(t[i]);
                    bodyTriangles[s].Add(t[i + 1]);
                    bodyTriangles[s].Add(t[i + 2]);
                    continue;
                }

                for (int k = 0; k < 3; k++)
                {
                    int v = t[i + k];
                    if (!remap.TryGetValue(v, out int mapped))
                    {
                        mapped = headVertices.Count;
                        remap[v] = mapped;
                        headVertices.Add(bind.MultiplyPoint3x4(vertices[v]));
                        headNormals.Add(normals != null && normals.Length == count ? bind.MultiplyVector(normals[v]).normalized : Vector3.up);
                        headUvs.Add(uvs != null && uvs.Length == count ? uvs[v] : Vector2.zero);
                    }

                    headTriangles[s].Add(mapped);
                }

                headTotal++;
            }
        }

        if (headTotal == 0)
            return new Cut();

        var headless = Object.Instantiate(mesh);
        headless.name = mesh.name + " (headless)";
        for (int s = 0; s < subMeshes; s++)
            headless.SetTriangles(bodyTriangles[s], s, false);

        var headMesh = new Mesh { name = mesh.name + " (head)" };
        headMesh.SetVertices(headVertices);
        headMesh.SetNormals(headNormals);
        headMesh.SetUVs(0, headUvs);
        headMesh.subMeshCount = subMeshes;
        for (int s = 0; s < subMeshes; s++)
            headMesh.SetTriangles(headTriangles[s], s);
        headMesh.RecalculateBounds();

        return new Cut { headless = headless, head = headMesh, headBone = headIndex };
    }

    static void Launch(Transform root, SkinnedMeshRenderer skin, Transform headBone, Mesh head, Hd2Health health)
    {
        var flying = new GameObject("PoppedHead");
        flying.transform.SetPositionAndRotation(headBone.position, headBone.rotation);
        flying.transform.localScale = headBone.lossyScale;
        flying.AddComponent<MeshFilter>().sharedMesh = head;
        var renderer = flying.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = skin.sharedMaterials;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var collider = flying.AddComponent<SphereCollider>();
        Bounds box = head.bounds;
        collider.center = box.center;
        collider.radius = Mathf.Max(box.extents.x, Mathf.Max(box.extents.y, box.extents.z)) * 0.8f;

        // Keep it from snagging on the body it came off.
        var own = root.GetComponentsInChildren<Collider>();
        for (int i = 0; i < own.Length; i++)
        {
            if (own[i] != null && own[i].enabled)
                Physics.IgnoreCollision(collider, own[i], true);
        }

        var rigidbody = flying.AddComponent<Rigidbody>();
        rigidbody.mass = 2f;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        Vector3 away = Vector3.up;
        if (health != null)
        {
            Vector3 shot = health.LastHitPoint - health.LastShotOrigin;
            shot.y = 0f;
            if (shot.sqrMagnitude > 0.0001f)
                away = shot.normalized;
        }

        rigidbody.linearVelocity = away * LaunchSpeed + Vector3.up * LaunchUp;
        rigidbody.angularVelocity = Random.onUnitSphere * LaunchSpin;
        Destroy(flying, HeadLifeSeconds);
    }
}
