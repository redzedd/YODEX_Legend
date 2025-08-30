using UnityEngine;

public class InputManager : MonoBehaviour
{
    [Tooltip("����W���o�� Tag ������s�b�ɡA�������a��J")]
    public string targetTag = "ItemCard";

    private bool isInputDisabled = false;

    void Update()
    {
        bool tagExists = GameObject.FindWithTag(targetTag) != null;

        if (tagExists && !isInputDisabled)
        {
            // ��ܥd���G�����������a & UI �ާ@
            PlayerInputHandler.Instance.DisablePlayerInput();
            PlayerInputHandler.Instance.DisableUIMapInput();
            isInputDisabled = true;
        }
        else if (!tagExists && isInputDisabled)
        {
            // �d�������G�u���}���a�ާ@�FUIMap ��ѡu�u���ݭn UI�v���a��]�Ҧp�I�]�^�ۤv�}
            PlayerInputHandler.Instance.EnablePlayerInput();
            PlayerInputHandler.Instance.DisableUIMapInput(); // �� ����G�O������
            isInputDisabled = false;
        }
    }
}
