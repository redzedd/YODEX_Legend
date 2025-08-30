using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem; // 新增

public class NewItemDisplayUI : MonoBehaviour
{
    public Image itemImage;
    public Text itemName;
    public Text descriptionText;
    public Text effectDescriptionText;

    public float minDisplayDuration = 0.5f;

    private GameObject previousSelected;

    public void Setup(ItemData data)
    {
        itemImage.sprite = data.icon;
        itemName.text = data.itemName;
        descriptionText.text = data.description;
        effectDescriptionText.text = data.effectDescription;

        // ✅ 記錄顯示前選取的 UI（如果有）
        previousSelected = EventSystem.current.currentSelectedGameObject;

        // 清除選取，防止誤觸 UI 按鈕
        EventSystem.current.SetSelectedGameObject(null);

        // 暫停遊戲
        Time.timeScale = 0f;
        StartCoroutine(WaitAndCloseCoroutine());
    }

    IEnumerator WaitAndCloseCoroutine()
    {
        float startTime = Time.unscaledTime;

        // 等最短展示時間
        while (Time.unscaledTime - startTime < minDisplayDuration)
            yield return null;

        // 等玩家按任意鍵
        yield return new WaitUntil(() => Input.anyKeyDown);

        // ★ 重要：等到鍵盤全部放開，避免同一顆鍵下一幀被「開背包」讀到
        yield return new WaitUntil(() => !Keyboard.current.anyKey.isPressed);

        // ★ 重要：短暫封鎖開背包鍵（解決按第一次沒反應）
        if (PlayerInputHandler.Instance != null)
            PlayerInputHandler.Instance.BlockOpenInventoryFor(0.12f);

        Time.timeScale = 1f;

        if (previousSelected != null)
        {
            yield return null; // 等一幀避免 UI 焦點亂跳
            EventSystem.current.SetSelectedGameObject(previousSelected);
        }

        Destroy(gameObject);
    }
}