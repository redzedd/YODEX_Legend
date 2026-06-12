using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ExplosionDamageTrigger : MonoBehaviour
{
    [Header("Settings")]
    public LayerMask hitMask;
    public float activeDuration = 0.2f;

    [Tooltip("�O�_���L�����S�ĻP���� (�w�]�� true�A�קK�P�z���S�ĭ��|)")]
    public bool skipHitEffects = true;

    [Header("Runtime Data")]
    public float damage = 20f;
    public float poiseDamage = 40f;
    public float knockback = 5f;

    private List<GameObject> hitTargets = new List<GameObject>();
    private float timer;
#pragma warning disable CS0414
    private bool isSetup = false; // 標記是否已初始化
#pragma warning restore CS0414

    private void OnEnable()
    {
        hitTargets.Clear();
        timer = 0f;

        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= activeDuration)
        {
            var col = GetComponent<Collider>();
            if (col && col.enabled) col.enabled = false;
        }
    }

    // �� ����G�T�O MagicMissile �I�s����k�ɡA�ƭȳQ���T�л\
    public void Setup(float dmg, float poise, float knock, bool skipEffects)
    {
        damage = dmg;
        poiseDamage = poise;
        knockback = knock;
        skipHitEffects = skipEffects;
        isSetup = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & hitMask) == 0) return;

        var receiver = other.GetComponent<IHitReceiver>();
        if (receiver == null) receiver = other.GetComponentInParent<IHitReceiver>();

        GameObject targetRoot = null;
        if (receiver != null) targetRoot = (receiver as MonoBehaviour).gameObject;
        else targetRoot = other.gameObject;

        if (hitTargets.Contains(targetRoot)) return;
        hitTargets.Add(targetRoot);

        Vector3 dir = (other.transform.position - transform.position).normalized;
        dir.y = 0.2f;

        if (receiver != null)
        {
            HitContext ctx = new HitContext
            {
                damage = this.damage,
                poiseDamage = this.poiseDamage,
                knockbackForce = this.knockback,
                hitPoint = other.ClosestPoint(transform.position),
                hitNormal = -dir,
                attackDirection = dir,
                sourceProfile = null,
                // �� �o�̨ϥη��e��Ҫ� skipHitEffects�A�o�����Ӥw�g�Q Setup �ק�L
                skipHitEffects = this.skipHitEffects
            };

            receiver.OnHit(ref ctx);
        }
    }
}