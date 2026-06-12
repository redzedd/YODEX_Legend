# YODEX_Legend — Project Instructions

> **Language Rule (HARD REQUIREMENT):** All user-facing output, code comments, XML docs, `[Tooltip]` strings, and `Debug.Log` messages MUST be written in **Traditional Chinese (繁體中文)**. This file itself is in English; everything you produce for the user/codebase is in Traditional Chinese.

---

## 0. Audience — Game Designer, Not Programmer

The user is a **game designer**, not a software engineer. This shapes both how you explain work and how you design the things you build.

### How to Explain Your Work

- **Code logic: brief and intuitive.** After writing or modifying code, summarize *what changed* in 1–2 plain sentences. Avoid programming jargon (dependency injection, generics, async semantics, lambdas, state machines, etc.) unless the user asks.
- **Unity steps: detailed and concrete.** Spend the words on the Unity Editor side — exactly which GameObject to create, which Component to add, which Inspector field to fill, which asset to drag where. Use numbered step-by-step lists.
  - Good: 「1. 在 Hierarchy 建立空 GameObject，命名為 `AttackHitbox` / 2. 加上 `BoxCollider`，勾選 `Is Trigger` / 3. 將剛建立的 `EnemyAttackData.asset` 拖到 Inspector 的 `Attack Data` 欄位」
  - Bad: 「我建立了一個資料驅動的攻擊判定系統，使用 ScriptableObject 解耦設定與邏輯」
- **No need to justify architecture in detail.** If asked why, give a one-sentence reason in designer-friendly terms (「這樣你之後改數值不用動程式碼」), not an essay on SOLID.

### How to Design What You Build

Every system you add MUST be tunable by the designer in the Unity Editor without touching code.

- **Inspector-first.** Prefer `[SerializeField]` private fields over hardcoded values. Numbers, timings, ranges, prefab references — all exposed in the Inspector.
- **`[Tooltip("繁體中文說明")]` on every serialized field.** The Tooltip is the designer's documentation; if a field has no Tooltip, the designer can't tune it safely.
- **ScriptableObject for tunable config.** Stats, attack data, ability data, item data → SO assets, not magic numbers in code. The designer should be able to duplicate an SO asset, tweak values, and ship a new variant without recompiling.
- **Keep setup interfaces simple — favor flexibility over cleverness.**
  - Bad: a MonoBehaviour that needs 12 dragged references + a custom setup script + Editor scripting knowledge to work.
  - Good: a MonoBehaviour with 3–4 clearly named Inspector slots, sensible defaults, and `[Header("...")]` groupings.
  - When a system genuinely needs many references, bundle them into a single SO config asset and expose only that one asset slot.
- **`[Header]`, `[Space]` liberally.** Group related fields visually.
- **不要使用 `[Range(min, max)]` 拉桿。** 設計師偏好直接填寫數值取得完全自由度，拉桿邊界會限制嘗試極端值的可能。需要傳達合理範圍時，在 `[Tooltip]` 內用「建議 0.1~2 秒」「推薦 30~50」這類文字推薦，**不要**用 Range 屬性強制邊界。
- **Sensible defaults.** A freshly-added component should work (or at least not error) without the designer wiring every field. Default to safe values.

### Operating Mode

- If the user asks for a feature, your reply should be: **brief code summary → detailed Unity setup steps → any risks/follow-ups**. Not: long architectural rationale → code dump.
- If a setup ends up requiring more than ~5 steps, that's a smell — re-think whether the system can be auto-wired or bundled into a prefab/SO.

---

## 1. Project Overview

|Item|Value|
|---|---|
|Game Type|Open-world 3rd-person action-adventure|
|References|*The Legend of Zelda: BOTW/TOTK*, *Elden Ring*|
|Core Loops|Combat, Exploration, Puzzle-solving|
|Target Platform|PC (Windows)|
|Unity|**6000.0.x LTS**|
|Render Pipeline|**URP**|
|Runtime|.NET Standard 2.1, C# 9.0|
|IDE|Cursor|
|Script Root|`Assets/Script/` (preserve this layout)|

---

## 2. Installed Plugins (use these — do NOT reinvent)

|Plugin|Version|Replaces / Role|
|---|---|---|
|Animancer Pro|8.2.3|Unity Animator Controller|
|A* Pathfinding Project|5.4.4|Unity NavMesh|
|DOTween Pro|1.0.410|Manual `Mathf.Lerp` / coroutine tweens|
|Cinemachine|3.1.6|Direct Camera transform control|
|Kinematic Character Controller (KCC)|3.4.4|Rigidbody / CharacterController movement|
|Behavior Designer (Opsive)|Asset Store (installed under `Assets/Behavior Designer/`)|Enemy AI logic|
|Behavior Designer Movement Pack (Opsive)|Asset Store (installed under `Assets/Behavior Designer Movement/`)|AI movement actions — note: NavMesh-based; this project replaces them with A\* via `AstarMovement`|
|Input System|1.16.0|Old Input Manager|

**Not installed — do not suggest:** FEEL/MMFeedbacks, UniTask, VContainer/Zenject, Odin.

### Plugin Usage Rules

- **Animancer:** Use `AnimancerComponent.Play(ClipTransition)`. Do not create AnimatorControllers.
- **A\*:** Control `AIPath` / `RichAI`. Never reference `NavMeshAgent`. Use `Seeker.StartPath()` for custom paths.
- **DOTween:** Use extension methods (`transform.DOMove`, `image.DOFade`). Always chain `.SetLink(gameObject)` OR kill in `OnDestroy()` to prevent leaks. Never write manual lerp coroutines.
- **Cinemachine 3.x:** Manipulate `CinemachineCamera.Priority` or input axes — never write to `Camera.main.transform`. Use `CinemachineImpulseSource` for shake.
- **KCC:** Implement `ICharacterController` on motors. Never mix with Rigidbody physics on the same actor. Movement logic goes in `UpdateVelocity` / `UpdateRotation` callbacks.
- **Behavior Designer (Opsive):** Custom actions inherit from `BehaviorDesigner.Runtime.Tasks.Action`, conditionals from `Conditional`; methods return `TaskStatus` and use lifecycle hooks (`OnAwake` / `OnStart` / `OnUpdate` / `OnEnd`). Shared variables (`SharedFloat`, `SharedBool`, `SharedGameObject`, …) are the sharing mechanism — do not reach out with `GameObject.Find`. Decorate with `[TaskCategory("Enemy")]` / `[TaskDescription("…")]` for editor grouping. **Do NOT** confuse with Unity's own `Unity.Behavior` package (Trees for Everyone) — it is not installed here.
- **Input System:** Use generated C# class from `.inputactions` asset. Do not poll `Input.GetKey`.

### Async Model

UniTask is **not** installed. Use:

1. `System.Threading.Tasks.Task` + `CancellationToken` for async work.
2. Unity Coroutines for frame-based sequencing.
3. DOTween `Sequence` / `.AsyncWaitForCompletion()` for tween chaining.

Do not introduce UniTask without asking.

---

## 3. Code Style

### Formatting

- **Allman braces** (opening brace on new line).
- One top-level type per file; filename matches type name.
- No blank lines inside a function body.
- Always declare explicit types. `var` allowed only when RHS is `new T(...)`.

### Naming

|Element|Convention|Example|
|---|---|---|
|Class / Struct / Enum|PascalCase|`PlayerController`|
|Method|PascalCase, verb-first|`ExecuteAttack`, `IsGrounded`|
|Property|PascalCase|`CurrentHealth`|
|Private field|`_camelCase`|`_playerHealth`|
|Local variable / parameter|camelCase|`targetPosition`|
|Constant|UPPER_SNAKE_CASE|`MAX_STAMINA`|
|Interface|`I` + PascalCase|`IDamageable`|
|Boolean|verb prefix|`isDead`, `hasKey`, `canDash`|

Allowed short locals: `rb` (Rigidbody), `tf` (Transform), `go` (GameObject). Fields still use full words (`_rigidbody`, `_transform`).

### Fields & Serialization

- Prefer `[SerializeField] private` over `public` for Inspector data.
- Every serialized field needing designer input gets a `[Tooltip("繁體中文說明")]`.
- Use `readonly` for anything not mutated after construction.
- Encapsulate related primitives into `struct` or `ScriptableObject` — do not pass 5+ loose floats.

### Functions

- Single responsibility; target under ~30 lines.
- Guard clauses / early return over nested `if`.
- Expression-bodied `=>` for one-liners and trivial getters.
- Max 3 parameters; beyond that, pass a context struct.

### Class Member Order

1. Serialized fields
2. Private fields
3. Properties
4. Unity lifecycle (`Awake` → `OnDestroy`)
5. Public methods
6. Private methods

---

## 4. Architecture

No rigid global pattern — pick the fit per system, but respect these separations:

- **Data:** `ScriptableObject` for designer-tunable config; plain `struct`/`record`-like classes for runtime state.
- **Logic:** Pure C# classes where possible (testable, no `MonoBehaviour` dependency).
- **View:** `MonoBehaviour` wires Unity lifecycle to logic classes.

### Dependency Rules

- **Forbidden in hot paths:** `GameObject.Find`, `FindObjectOfType`, `GetComponent` inside `Update/FixedUpdate/LateUpdate`.
- **Cache every `GetComponent` in `Awake`**.
- No DI framework installed — use constructor injection for pure classes, `Initialize(...)` methods or `[SerializeField]` references for MonoBehaviours.
- Prefer composition over inheritance. Define an `interface` when two systems need to talk.

### Folder Layout (keep as-is)

```text
Assets/Script/
    GAS/                 # Gameplay ability system (existing)
    GAS/Targeting/LockOn # Lock-on system (existing)
    ...
```

New systems get a new sibling folder with a focused namespace.

---

## 5. Performance Rules (PC target, but still hold the line)

- **Zero allocations in `Update`/`FixedUpdate`/`LateUpdate`.** No `new`, no LINQ, no boxing, no string concatenation.
- Use `CompareTag("X")` — never `gameObject.tag == "X"`.
- Distance checks: compare `sqrMagnitude` against a squared threshold.
- Physics / Rigidbody-adjacent work goes in `FixedUpdate`.
- Pool bullets, VFX, damage numbers, and any frequently spawned prefab.
- Pre-hash animator params & shader props with `Animator.StringToHash` / `Shader.PropertyToID` into `static readonly int`.
- For KCC-driven actors, do movement math inside `ICharacterController` callbacks, not `Update`.

---

## 6. Logging & Error Handling

- Wrap `Debug.Log` behind a `LogManager` helper marked `[Conditional("DEVELOPMENT_BUILD")]` so release builds strip logs.
- `try/catch` is only for I/O, network, JSON parsing, and addressables loads — never for gameplay flow control.
- Messages passed to `Debug.LogWarning` / `Debug.LogError` are in Traditional Chinese.

---

## 7. Comments & Documentation

- Default to **no comments**. Names should explain *what*.
- Write a comment only when the *why* is non-obvious: invariants, workarounds, perf hacks, plugin quirks.
- When you do comment: Traditional Chinese, one short line preferred. XML `<summary>` for public APIs touched by other systems.
- Never add "此函式由 X 呼叫" / "Added for ticket #123" — those belong in PR descriptions.

---

## 8. Working Protocol for the AI

When given a task:

1. **Plan first (in Traditional Chinese).** Briefly describe the approach, which plugins apply, and which existing scripts will change.
2. **Check plugin triggers** — if the task touches animation/movement/camera/tween/AI/input, the matching plugin from §2 is the default tool.
3. **Confirm before large refactors.** For single-file edits or additive features, proceed directly.
4. **Finish the job.** No `// TODO`, no stubs, no "you can implement this later."
5. **Include all `using` directives** (`using Animancer;`, `using DG.Tweening;`, `using KinematicCharacterController;`, `using BehaviorDesigner.Runtime;`, `using BehaviorDesigner.Runtime.Tasks;`, `using Pathfinding;`, `using UnityEngine.InputSystem;`).
6. **If unsure, say so.** Do not fabricate API signatures — plugin versions above are authoritative; verify against installed packages when in doubt.

### Response Style

- Traditional Chinese, concise, **designer-friendly** — see §0.
- Code blocks: show the diff, then 1–2 plain sentences on *what* changed. No deep dives into implementation patterns unless the user asks.
- Unity setup: a numbered list with concrete GameObject 名稱、Component 名稱、Inspector 欄位、資產拖放位置. Designer should be able to follow without re-reading the code.
- Flag risks (perf, plugin version mismatch, platform, Inspector setup pitfalls) explicitly rather than burying them.
