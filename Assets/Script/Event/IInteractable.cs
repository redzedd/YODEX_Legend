using UnityEngine;

public interface IInteractable
{
    int Priority { get; }
    void Interact();
    void OnFocus();   // 可選：進入範圍時 UI 提示
    void OnUnfocus(); // 可選：離開範圍時關閉 UI
}

