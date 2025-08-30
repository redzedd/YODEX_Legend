using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PickupNotificationManager : MonoBehaviour
{
    public GameObject notificationPrefab;
    public Transform notificationContainer; // 垂直排列的父物件
    public float displayDuration = 3f;
    public int maxNotifications = 3;

    private Queue<GameObject> notificationQueue = new Queue<GameObject>();
    public static PickupNotificationManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void ShowNotification(Sprite icon, string itemName, int amount)
    {
        if (notificationQueue.Count >= maxNotifications)
        {
            Destroy(notificationQueue.Dequeue());
        }

        GameObject instance = Instantiate(notificationPrefab, notificationContainer);
        instance.transform.SetAsLastSibling(); // 新的在最下方（可調）

        // 設定內容
        instance.transform.Find("Icon").GetComponent<Image>().sprite = icon;
        instance.transform.Find("ItemName").GetComponent<Text>().text = itemName;
        instance.transform.Find("Amount").GetComponent<Text>().text = $"x{amount}";

        notificationQueue.Enqueue(instance);
        StartCoroutine(HideAfterDelay(instance));
    }

    private IEnumerator HideAfterDelay(GameObject notification)
    {
        yield return new WaitForSeconds(displayDuration);

        // ❗ 安全檢查：只有 notification 還在 queue 裡才 Dequeue 它
        if (notificationQueue.Contains(notification))
        {
            notificationQueue = new Queue<GameObject>(notificationQueue); // ⭐ 重新建立 queue 以支援安全移除
            notificationQueue.Dequeue();
        }

        Destroy(notification);
    }
}
