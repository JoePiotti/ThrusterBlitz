using UnityEngine;

/// <summary>
/// Fast visible projectile. Stops on the first collider that is not the shooter.
/// Hd2SpawnField is one-way: a shot from the room side passes through and can hit
/// what is beyond it. A shot from the map side stops and leaves a mark on the field.
/// </summary>
public class Hd2Shot : MonoBehaviour
{
    public Vector3 velocity;
    public float lifetime = 0.6f;
    public Transform owner;

    float age;
    Vector3 previous;
    bool hasPrevious;

    void Awake()
    {
        previous = transform.position;
        hasPrevious = true;
    }

    void Update()
    {
        if (!hasPrevious)
        {
            previous = transform.position;
            hasPrevious = true;
        }

        float dt = Time.deltaTime;
        age += dt;
        Vector3 next = transform.position + velocity * dt;
        Vector3 delta = next - previous;
        float distance = delta.magnitude;

        if (distance > 0.0001f)
        {
            var hits = Physics.RaycastAll(previous, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < hits.Length; i++)
            {
                Transform hitTransform = hits[i].transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform))
                    continue;
                if (owner != null && hitTransform.IsChildOf(owner))
                    continue;

                var field = hits[i].collider.GetComponentInParent<Hd2SpawnField>();
                if (field != null && field.ShotPasses(previous))
                    continue;

                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                RaycastHit hit = hits[bestIndex];
                var field = hit.collider.GetComponentInParent<Hd2SpawnField>();
                if (field != null)
                {
                    field.RegisterBlockedShot(hit.point, hit.normal);
                }
                else
                {
                    var target = hit.collider.GetComponentInParent<Hd2ShootTarget>();
                    if (target != null)
                        target.RegisterHit(hit.point, hit.normal);
                }

                Destroy(gameObject);
                return;
            }
        }

        transform.position = next;
        previous = next;

        if (age >= lifetime)
            Destroy(gameObject);
    }
}
