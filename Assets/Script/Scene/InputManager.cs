using UnityEngine;

/// <summary>
/// [已棄用] 舊版字卡顯示時控制玩家/UI 輸入的補丁。
///
/// 失效原因:每幀以「場上有沒有 tag=ItemCard 物件」為唯一判斷依據,
/// 無條件 DisableUIMapInput / EnablePlayerInput, 會破壞「字卡疊在烹飪/背包 UI 上」
/// 這類字卡進場前已是 UI 模態的情境 — 字卡消失後它會誤把 PlayerInput 打開、
/// 把 UIMap 關掉,造成背景 UI 看起來開著但操作全失效。
///
/// 改由 NewItemDisplayUI.Setup 進場時快照輸入狀態, 離場時還原到該快照
/// (見 NewItemDisplayUI 的 _prevTimeScale / _prevPlayerInputEnabled 機制)。
///
/// 保留空殼以避免場景中既有 GameObject 出現 missing script 警告;
/// 場景驗證完畢可移除掛載此元件的 GameObject 並刪除此檔案。
/// </summary>
public class InputManager : MonoBehaviour
{
    [HideInInspector] public string targetTag = "ItemCard";
}
