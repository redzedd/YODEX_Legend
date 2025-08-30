using System;
using System.Collections;
using UnityEngine;

public class PlayerMoveState : PlayerState
{
    public PlayerMoveState(PlayerController player, StateMachine stateMachine) : base(player, stateMachine) { }

    private float smoothMovement = 0f; // ⭐ 加這個：平滑動畫速度
    private float smoothSpeed = 12.8f; // ⭐ 平滑係數（可以調整）
    private bool isBraking = false; // 是否正在急煞
    private float currentSpeedValue = 0f;
    private float animSmoothSpeed = 12f; // 平滑速度值的Lerp用
    private float animSpeedValue = 0f; // 專門給動畫使用的速度
    private float animSmoothTime = 8f; // 推進動畫速度的 Lerp 強度，可微調
    private bool runExhaustedLatch = false; // 體力歸零後，必須放開跑步鍵再按才能跑


    public override void Enter()
    {
        smoothMovement = 0f;
        animSpeedValue = 0f; // ⭐ 保證動畫從靜止開始推進
        isBraking = false; // ⭐ 進入狀態時清除急煞狀態
    }

    public override void Update()
    {
        // ⭐ Dodge 優先於攻擊與移動速度計算（你也可以調整優先權）
        if (PlayerInputHandler.Instance.DodgeTriggered)
        {
            // 先檢查體力，足夠才切進 DodgeState
            if (PlayerStamina.Instance.HasStamina(15))
            {
                stateMachine.ChangeState(player.dodgeState);
                return;
            }
            else
            {
                // 體力不足 → 什麼都不做（不切狀態、不播動畫），完全沒反應
                // 可選：播放一個 UI 提示或音效，但程式邏輯上不要切狀態
            }
        }
        Vector2 moveInput = player.MoveInput;
        Vector3 move = new Vector3(moveInput.x, 0, moveInput.y);
        float targetMoveAmount = moveInput.magnitude;
        float startupBoost = (smoothMovement < 0.05f && targetMoveAmount > 0.1f) ? smoothSpeed * 3f : smoothSpeed;
        smoothMovement = Mathf.Lerp(smoothMovement, targetMoveAmount, startupBoost * Time.deltaTime);

        if (smoothMovement > 0.01f)
        {
            move = Quaternion.Euler(0, player.mainCamera.transform.eulerAngles.y, 0) * move;
            move = move.normalized;

            if (player.CanRotate && move.sqrMagnitude > 0.0001f) // ⭐ move有明確方向再旋轉
            {
                Quaternion targetRotation = Quaternion.LookRotation(move);
                player.transform.rotation = Quaternion.Lerp(player.transform.rotation, targetRotation, player.rotationSpeed * Time.deltaTime * 1.5f);
            }

            float targetSpeedValue;

            // 只要玩家「沒有按住」跑步鍵，就解除 Latch，並清空連續消耗殘量
            if (!player.RunTriggered)
            {
                runExhaustedLatch = false;
                PlayerStamina.Instance.CancelContinuousConsume(); // 避免殘量造成之後瞬間多扣
            }

            if (player.RunTriggered && !runExhaustedLatch)
            {
                // 先確認至少還有 1 點體力，且本幀連續消耗後仍可維持
                if (PlayerStamina.Instance.HasStamina(1) &&
                    PlayerStamina.Instance.ConsumeOverTime(PlayerStamina.Instance.RunStaminaCostPerSecond))
                {
                    // ▶ 跑步
                    targetSpeedValue = Mathf.Lerp(0f, 12.8f, smoothMovement);
                    player.Animator.SetBool("isRun", true);
                }
                else
                {
                    // ❌ 這一幀已經跑到見底（或不足以維持）→ 啟用 Latch，直到玩家放開跑步鍵
                    runExhaustedLatch = true;

                    // 回到走路
                    targetSpeedValue = Mathf.Lerp(0f, 6.4f, smoothMovement * 10f);
                    player.Animator.SetBool("isRun", false);
                }
            }
            else
            {
                // 沒按跑步鍵 或 被 Latch 鎖住 → 走路
                targetSpeedValue = Mathf.Lerp(0f, 6.4f, smoothMovement * 10f);
                player.Animator.SetBool("isRun", false);
            }

            bool shouldBrake = currentSpeedValue > 6.4f && smoothMovement < 0.2f;

            // 急煞觸發時
            if (shouldBrake && !isBraking)
            {
                //Debug.Log("🔥 Brake Triggered!");
                player.Animator.SetTrigger("Brake");
                isBraking = true;
                player.StartCoroutine(ResetBrakeFlag(0.4f)); // ⭐ 新增這行，避免一幀就被重設
            }
            else
            {
                // ⭐ 平滑推進動畫速度（非急煞狀態才平滑）
                currentSpeedValue = Mathf.Lerp(currentSpeedValue, targetSpeedValue, animSmoothSpeed * Time.deltaTime);
            }

            animSpeedValue = Mathf.Lerp(animSpeedValue, currentSpeedValue, animSmoothTime * Time.deltaTime * 5);
            player.Animator.SetFloat("VSpeed", animSpeedValue);
            player.Animator.SetBool("isRun", player.RunTriggered); // ⭐ 新增參數控制
        }
        else
        {
            stateMachine.ChangeState(player.idleState);
        }

        if (player.AttackTriggered)
        {
            stateMachine.ChangeState(player.attackState);
        }
    }

    private IEnumerator ResetBrakeFlag(float delay)
    {
        yield return new WaitForSeconds(delay);
        isBraking = false;
    }
}