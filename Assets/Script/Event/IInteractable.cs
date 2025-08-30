using UnityEngine;

public interface IInteractable
{
    int Priority { get; }
    void Interact();
    void OnFocus();   // �i��G�i�J�d��� UI ����
    void OnUnfocus(); // �i��G���}�d������� UI
}

