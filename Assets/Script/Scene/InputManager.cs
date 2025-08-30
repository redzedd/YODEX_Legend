using UnityEngine;

public class InputManager : MonoBehaviour
{
    [Tooltip("當場上有這個 Tag 的物件存在時，關閉玩家輸入")]
    public string targetTag = "ItemCard";

    private bool isInputDisabled = false;

    void Update()
    {
        bool tagExists = GameObject.FindWithTag(targetTag) != null;

        if (tagExists && !isInputDisabled)
        {
            // 顯示卡片：全面關閉玩家 & UI 操作
            PlayerInputHandler.Instance.DisablePlayerInput();
            PlayerInputHandler.Instance.DisableUIMapInput();
            isInputDisabled = true;
        }
        else if (!tagExists && isInputDisabled)
        {
            // 卡片關閉：只打開玩家操作；UIMap 交由「真的需要 UI」的地方（例如背包）自己開
            PlayerInputHandler.Instance.EnablePlayerInput();
            PlayerInputHandler.Instance.DisableUIMapInput(); // ← 關鍵：保持關閉
            isInputDisabled = false;
        }
    }
}
