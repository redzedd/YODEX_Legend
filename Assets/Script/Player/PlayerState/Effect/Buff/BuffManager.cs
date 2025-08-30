using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// ���� Buff �޲z�G
/// - ���� Buff �H buffId �ߤ@�A����(���Ťj)�л\�C��
/// - �A��(Regeneration) �אּ�y���ӳ̤j�ͩR�ʤ���z�^��]�ƭȨӦ� BuffDefinition �� tier.percentPerTick / duration / tickInterval�^
/// - �A�Ͷ}�l�G�����b���a���W AddComponent<Regeneration>() �� ConfigureByPercent(...) �� Begin()
/// - �A�͵���/�Q�����G�۰ʲ����޲z��P UI�]�q�\ Regeneration ���ƥ� + �ƴ��p�ɾ��^
/// - UI�G�Y���W�� BuffBarUI�A�|�۰ʷs�W/��s/�����ϥܡA�è� buffId �Ƨ�
/// - �A�i�H�⥻���������A�]�i����F�Y�S��A�I�s GetOrCreate() �|�۰ʫإߨ� DontDestroyOnLoad
/// </summary>
public class BuffManager : MonoBehaviour
{
    public static BuffManager Instance { get; private set; }

    // ---- ��K�b����a��O�ҥi�Ρ]�Y�A�w�⥦��i�����A�]���v�T�^----
    public static BuffManager GetOrCreate()
    {
        if (Instance) return Instance;

        var existing = FindObjectOfType<BuffManager>();
        if (existing)
        {
            Instance = existing;
            return Instance;
        }

        var go = new GameObject("[BuffManager]");
        Instance = go.AddComponent<BuffManager>();
        DontDestroyOnLoad(go);
        return Instance;
    }

    private class ActiveBuff
    {
        public BuffDefinition def;
        public int level;
        public Coroutine fallbackTimer;  // �ƴ��۰ʲM��
    }

    // �H buffId �� key�]�C�� Buff �P�ɶȤ@�ӵ��ťͮġ^
    private readonly Dictionary<int, ActiveBuff> _active = new();

    // ====== �ͩR�g�� ======
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ====== ��~ API ======

    /// <summary>�M�Τ@�� Buff�]�P buffId�G�����л\�C���F�C���L�k�л\�����^</summary>
    public void Apply(BuffDefinition def, int level)
    {
        if (!def) return;
        level = Mathf.Clamp(level, 1, 3);

        // �����л\�C���F�p�w�s�b�B�s���Ÿ��C �� ����
        if (_active.TryGetValue(def.buffId, out var cur))
        {
            if (level < cur.level) return; // �C�����л\
            InternalRemove(def.buffId, visualOnly: false); // �����b�����ª��]�ĪG+UI+�p�ɾ��^
        }

        // ��ڥ[�W�ĪG
        AddEffect(def, level);

        // �O�����ҥΤ�
        var entry = new ActiveBuff { def = def, level = level };
        _active[def.buffId] = entry;

        // UI�G���/��s�]�� BuffBarUI �~�ʧ@�^
        if (BuffBarUI.Instance) BuffBarUI.Instance.ShowOrUpdate(def, level);

        // �ƴ��M�z�]�H�w�q���`����ɶ����۰ʲ����F�ƥ�|���Ĳ�o�M�z�^
        var tier = def.GetTier(level);
        if (tier != null && tier.duration > 0f)
            entry.fallbackTimer = StartCoroutine(RemoveAfter(def.buffId, tier.duration));
    }

    /// <summary>��ʲ����Y�� buffId �� Buff</summary>
    public void RemoveById(int buffId)
    {
        InternalRemove(buffId, visualOnly: false);
    }

    // ====== �����G�[/��/�j�w �� ======

    private void InternalRemove(int buffId, bool visualOnly)
    {
        if (_active.TryGetValue(buffId, out var cur))
        {
            if (cur.fallbackTimer != null) StopCoroutine(cur.fallbackTimer);

            if (!visualOnly)
                RemoveEffect(cur.def); // �u���~���D�ʲ������л\�ɤ~�u���u�����ĪG�v

            _active.Remove(buffId);
            if (BuffBarUI.Instance) BuffBarUI.Instance.RemoveById(buffId);
        }
    }

    private IEnumerator RemoveAfter(int buffId, float seconds)
    {
        // �y�[ 0.05s buffer�A�קK�P�̫�@���P�V�v�A
        yield return new WaitForSeconds(seconds + 0.05f);
        InternalRemove(buffId, visualOnly: false);
    }

    private void AddEffect(BuffDefinition def, int level)
    {
        var tier = def.GetTier(level);
        switch (def.specialEffect)
        {
            case SpecialEffectType.Regeneration:
                {
                    // �X�X �ন�u�ʤ���A�͡v�X�X
                    // �����a�ؼ�
                    var player = PlayerHealth.Instance ? PlayerHealth.Instance.gameObject : null;
                    if (player == null) return;

                    // �w���_���G��������{�s�A�͡]�קK���ơ^
                    // �Y�A���� StatusEffectManager�A�O�d�o����O�I�F
                    // �S���]�S���Y�A�U���ٷ|�ˬd���b���a���W�� Regeneration �ð���C
                    if (StatusEffectManager.Instance != null)
                        StatusEffectManager.Instance.CancelAllRegeneration();

                    // ���M�����W�{���� Regeneration�]�Y�٦���d�^
                    var existed = player.GetComponent<Regeneration>();
                    if (existed != null) existed.ForceStop();

                    float perTickPercent = Mathf.Max(0f, tier.percentPerTick);
                    float duration = Mathf.Max(0f, tier.duration);
                    float interval = Mathf.Max(0.01f, tier.tickInterval);

                    // ���W�ʤ��� Regeneration
                    var regen = player.AddComponent<Regeneration>();
                    regen.ConfigureByPercent(perTickPercent, duration, interval);
                    regen.Begin();

                    // �� �j�w�ƥ�]���O�I���@�^�G����/���� �� �۰ʲM�z�޲z��P UI
                    regen.OnEffectEnd += () => OnRegenFinishedOrCanceled(def.buffId);
                    regen.OnEffectCanceled += () => OnRegenFinishedOrCanceled(def.buffId);

                    break;
                }

                // TODO: ��L�S��ĪG�i�b���X�R�]AttackBoost�BFireResistance...�^
        }
    }

    private void RemoveEffect(BuffDefinition def)
    {
        switch (def.specialEffect)
        {
            case SpecialEffectType.Regeneration:
                {
                    // �u���I�s�A�� StatusEffectManager�]�Y�s�b�^
                    if (StatusEffectManager.Instance != null)
                        StatusEffectManager.Instance.CancelAllRegeneration();

                    // �A�O�I�G�⪱�a���W�ݯd�� Regeneration �j������
                    var player = PlayerHealth.Instance ? PlayerHealth.Instance.gameObject : null;
                    if (player != null)
                    {
                        var regen = player.GetComponent<Regeneration>();
                        if (regen != null) regen.ForceStop();
                    }
                    break;
                }

                // TODO: ��L�S��ĪG���M��
        }
    }

    private void OnRegenFinishedOrCanceled(int buffId)
    {
        // �ƥ�w�O�ҮĪG����/�Q�����F�A�o�̡u�u���O�b�P UI �����v�A���n�A Cancel �@��
        InternalRemove(buffId, visualOnly: true);
    }
}
