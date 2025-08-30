using UnityEngine;
using System.Collections;

public class TestHealthDebug : MonoBehaviour
{
    public int totalAmount = 10;   // �`�^�_
    public int perTick = 2;        // �C��
    public float tickInterval = 1; // �C�����j(��)
    private Coroutine regenRoutine;

    void Update()
    {
        // �� K �� ���� 10
        if (Input.GetKeyDown(KeyCode.K))
        {
            if (PlayerHealth.Instance != null)
                PlayerHealth.Instance.TakeDamage(10);
        }

        // �� H �� �����^�� 10
        if (Input.GetKeyDown(KeyCode.H))
        {
            if (PlayerHealth.Instance != null)
                PlayerHealth.Instance.HealDirect(10);
        }

        // �� J �� �}�l�@�ӫ���^�� buff�]�C��+2�A����5��^
        if (Input.GetKeyDown(KeyCode.J))
        {
            StatusEffectManager.GetOrCreate().ApplyRegeneration(totalAmount, perTick, tickInterval);
            Debug.Log($"[TestRegenLauncher] �ҰʦA�͡Gtotal={totalAmount}, perTick={perTick}, interval={tickInterval}s");
        }
    }
}
