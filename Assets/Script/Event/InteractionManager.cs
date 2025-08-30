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
        // �M�� null ����]����Q Destroy ���٦b�^
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

        // ��X�u���v�̰����i���ʪ���
        IInteractable best = interactablesInRange.OrderBy(i => i.Priority).FirstOrDefault();

        if (best != currentFocused)
        {
            // �������ª�
            currentFocused?.OnUnfocus();

            // ��s�s�J�I
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
        // �����Ҧ��� null �����ʪ���
        interactablesInRange.RemoveAll(i => i == null);

        if (interactablesInRange.Count == 0) return;

        var highest = interactablesInRange.OrderBy(i => i.Priority).First();
        highest.Interact();
    }
}
