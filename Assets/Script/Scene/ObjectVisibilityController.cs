using System.Collections.Generic;
using UnityEngine;

public class ObjectVisibilityController : MonoBehaviour
{
    public List<GameObject> targetsToCheck;
    public List<GameObject> objectsToShow;
    public List<GameObject> objectsToHide;

    void Update()
    {
        bool allInactive = true;
        foreach (GameObject obj in targetsToCheck)
        {
            if (obj != null && obj.activeSelf)
            {
                allInactive = false;
                break;
            }
        }

        // 若全部都消失，則顯示 objectsToShow
        if (allInactive)
        {
            foreach (GameObject obj in objectsToShow)
            {
                if (obj != null && !obj.activeSelf)
                    obj.SetActive(true);
            }

            foreach (GameObject obj in objectsToHide)
            {
                if (obj != null && obj.activeSelf)
                    obj.SetActive(false);
            }
        }
    }
}
