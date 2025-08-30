using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class InteractionManager : MonoBehaviour
{
    public static InteractionManager Instance { get; private set; }

    private IInteractable currentFocused;
    private List<IInteractable> interactablesInRange = new List<IInteractable>();

    private void Awake()
    {
        if (Instance != null) Destroy(gameObject);
        else Instance = this;
    }

    private void Update()
    {
        // 清除 null 物件（防止被 Destroy 後還在）
        interactablesInRange.RemoveAll(i => i == null);

        if (interactablesInRange.Count == 0)
        {
            if (currentFocused != null)
            {
                currentFocused.OnUnfocus();
                currentFocused = null;
            }
            return;
        }

        // 找出優先權最高的可互動物件
        IInteractable best = interactablesInRange.OrderBy(i => i.Priority).FirstOrDefault();

        if (best != currentFocused)
        {
            // 先關掉舊的
            currentFocused?.OnUnfocus();

            // 更新新焦點
            currentFocused = best;
            currentFocused.OnFocus();
        }
    }


    public void RegisterInteractable(IInteractable interactable)
    {
        if (!interactablesInRange.Contains(interactable))
            interactablesInRange.Add(interactable);
    }

    public void UnregisterInteractable(IInteractable interactable)
    {
        interactablesInRange.Remove(interactable);
    }

    public void TryInteract()
    {
        // 移除所有為 null 的互動物件
        interactablesInRange.RemoveAll(i => i == null);

        if (interactablesInRange.Count == 0) return;

        var highest = interactablesInRange.OrderBy(i => i.Priority).First();
        highest.Interact();
    }
}
