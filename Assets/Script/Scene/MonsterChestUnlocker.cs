using System.Collections.Generic;
using UnityEngine;

public class MonsterChestUnlocker : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("將需要被消滅的怪物拖曳到此列表")]
    public List<GameObject> enemiesToKill;

    [Header("Rewards")]
    [Tooltip("怪物全滅後要啟用的物件 (例如寶箱)")]
    public GameObject chestObject;
    [Tooltip("怪物全滅後要播放的音效")]
    public AudioClip unlockSFX;

    private bool isUnlocked = false;

    private void Start()
    {
        if (chestObject != null)
            chestObject.SetActive(false); // 初始隱藏/鎖定
    }

    private void Update()
    {
        if (isUnlocked) return;

        // 檢查列表中的怪物是否都已死亡 (變為 null 或 Inactive)
        bool allDead = true;

        // 移除已經完全 Destroy 的空引用
        enemiesToKill.RemoveAll(item => item == null);

        foreach (var enemy in enemiesToKill)
        {
            // 如果還有怪物活著 (Active)，就還沒達成條件
            if (enemy.activeInHierarchy)
            {
                allDead = false;
                break;
            }
        }

        if (allDead && enemiesToKill.Count == 0) // Count == 0 代表真的都死光被移除了
        {
            Unlock();
        }
    }

    private void Unlock()
    {
        isUnlocked = true;
        Debug.Log("🏆 怪物全滅，寶箱解鎖！");

        if (chestObject != null)
        {
            chestObject.SetActive(true);

            // 如果有特效可以在這裡生成
        }

        if (unlockSFX != null)
        {
            AudioSource.PlayClipAtPoint(unlockSFX, transform.position);
        }
    }
}