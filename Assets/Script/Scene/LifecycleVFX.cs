using UnityEngine;
using System.Collections;

public class LifecycleVFX : MonoBehaviour
{
    public GameObject appearVfxPrefab;
    public GameObject disappearVfxPrefab;

    [Header("瞷┑筐")]
    public int delayFrames = 3;     // ┑筐碭碫冀ㄒ 3 碫

    private bool _disappearSpawned = false;

    void OnEnable()
    {
        _disappearSpawned = false;
        if (appearVfxPrefab != null)
            StartCoroutine(SpawnAppearDelayed());
    }

    void OnDisable()
    {
        if (!_disappearSpawned)
        {
            TrySpawn(disappearVfxPrefab);
            _disappearSpawned = true;
        }
    }

    void OnDestroy()
    {
        if (!_disappearSpawned)
        {
            TrySpawn(disappearVfxPrefab);
            _disappearSpawned = true;
        }
    }

    IEnumerator SpawnAppearDelayed()
    {
        for (int i = 0; i < delayFrames; i++)
            yield return null; // 单﹚碫计

        TrySpawn(appearVfxPrefab);
    }

    void TrySpawn(GameObject prefab)
    {
        if (!prefab) return;

        GameObject vfx = Instantiate(prefab, transform.position, Quaternion.identity);

        // 沽刚ъ采╰参关㏑
        var ps = vfx.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            Destroy(vfx, ps.main.duration + ps.main.startLifetime.constantMax);
            return;
        }

        // 狦⊿Τ采╰参碞倒㏕﹚丁ㄒ 3 
        Destroy(vfx, 3f);
    }
}
