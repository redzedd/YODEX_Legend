using System.Collections.Generic;
using UnityEngine;

public class GearPuzzleManager : MonoBehaviour
{
    [System.Serializable]
    public class GearSolution
    {
        public GearPiece gear;
        [Range(0, 3)] public int correctIndex;
    }

    [Header("Puzzle Setup")]
    public List<GearSolution> gears = new List<GearSolution>();

    [Header("Reward")]
    public GameObject lockedObject;
    public GameObject unlockedObject;
    public AudioClip solveSFX;

    public bool IsSolved { get; private set; } = false;

    private void Start()
    {
        foreach (var g in gears)
        {
            if (g.gear != null) g.gear.Initialize(this);
        }

        if (lockedObject) lockedObject.SetActive(true);
        if (unlockedObject) unlockedObject.SetActive(false);
    }

    public void CheckSolution()
    {
        if (IsSolved) return;

        bool allCorrect = true;
        foreach (var g in gears)
        {
            // 只要有一個不對就失敗
            if (g.gear.currentRotationIndex != g.correctIndex)
            {
                allCorrect = false;
                break;
            }
        }

        if (allCorrect)
        {
            OnSolved();
        }
    }

    private void OnSolved()
    {
        IsSolved = true;
        Debug.Log("⚙️ 齒輪電路接通！解謎完成！");

        if (solveSFX) AudioSource.PlayClipAtPoint(solveSFX, transform.position);

        // ★ 新增：讓所有齒輪播放成功特效
        foreach (var g in gears)
        {
            if (g.gear != null) g.gear.PlaySuccessEffect();
        }

        if (lockedObject) lockedObject.SetActive(false);
        if (unlockedObject) unlockedObject.SetActive(true);
    }
}