# AI Workflow V3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the live LLM workflow-control failures discovered during the 2026-05-16 verification run.

**Architecture:** V3 turns workflow state from a passive read-only blackboard into an explicitly writable task contract. The first tasks add a small write tool and automatic intent hydration, then address semantic lookup failures with Chinese unit aliases. Runtime map mutation behavior should stay stable while these control-plane pieces are added.

**Tech Stack:** C# / .NET 8, xUnit, existing `ToolDefinitions`, `ToolExecutor`, `AIWorkflowState`, `AIChatService`, and `search_units` reference-loading path.

---

## Evidence Source

Primary evidence:

- `.agent/run-logs/2026-05-16-live-ai-workflow-verification.md`
- `.agent/reviews/2026-05-16-live-ai-verification-run-completion.md`

Live results:

- 2/7 scenarios fully passed.
- 4/7 scenarios partially passed.
- 1/7 scenario failed.

Important failures:

- 0/7 scenarios proactively called `get_workflow_state`.
- AI hallucinated `set_workflow_state`.
- `WorkflowState` stayed mostly empty.
- Chinese unit names were not mapped to canonical unit codes.
- Selection coordinates were ignored in local edit.

## Task Order

1. Add `set_workflow_state` write tool
2. Auto-initialize workflow goal and intent from user messages
3. Strengthen prompt around workflow state read/write protocol
4. Add Chinese unit alias codebook for `search_units`
5. Add owner/faction discovery guardrails
6. Plan selection-scoped execution constraints

## Task 1: Add `set_workflow_state` Write Tool

**Purpose:** Give the AI a legal tool for the behavior it already attempted during live verification.

**Files:**

- Modify: `src/TSMapEditor/AI/ToolDefinitions.cs`
- Modify: `src/TSMapEditor/AI/ToolExecutor.cs`
- Test: `src/TSMapEditor.Tests/AI/ToolDefinitionsTests.cs`
- Test: create `src/TSMapEditor.Tests/AI/Workflow/AIWorkflowToolTests.cs` if a pure helper is extracted

**Behavior:**

- Add tool `set_workflow_state`.
- It should update `WorkflowState.UserGoal`, `Intent`, `CurrentPhase`, `CompletedSteps`, `PendingSteps`, and `KnownRisks`.
- It must not mutate map data.
- It must be treated as query/control-plane only and should not append map state summaries.
- It must fail clearly if `WorkflowState` is unavailable.
- It should preserve existing `get_workflow_state`.

**Verification:**

Run targeted tests, full tests, and Release build with `/p:EnableMGCBItems=false`.

## Task 2: Auto-Initialize Workflow Goal And Intent

**Purpose:** Prevent empty workflow state even when the AI does not call the write tool.

**Files:**

- Create: `src/TSMapEditor/AI/Workflow/AIWorkflowIntentResolver.cs`
- Create: `src/TSMapEditor.Tests/AI/Workflow/AIWorkflowIntentResolverTests.cs`
- Modify: `src/TSMapEditor/AI/AIChatService.cs`

**Behavior:**

- Preserve the first substantial user goal instead of overwriting it with later status-check messages.
- Infer one of the seven intent strings from user text when `WorkflowState.Intent` is empty.
- Do not override an explicit intent written by `set_workflow_state`.
- Keep resolver pure and unit-testable.

## Task 3: Strengthen Workflow Protocol Prompt

**Purpose:** Make live LLM behavior align with the now-available state tools.

**Files:**

- Modify: `src/TSMapEditor/AI/AIChatService.cs`

**Behavior:**

- Tell the AI to call `set_workflow_state` before substantial multi-step generation.
- Tell the AI to call `get_workflow_state` before resuming or after several tool calls.
- Explicitly forbid inventing workflow tools not listed in the tool schema.
- Keep final response concise and avoid claiming validation not performed.

## Task 4: Chinese Unit Alias Codebook

**Purpose:** Fix live failures like "美国大兵" resolving to `GGI` instead of `GI`.

**Files:**

- Create: `src/TSMapEditor/AI/References/unit_aliases.zh.json`
- Create: `src/TSMapEditor/AI/AIUnitAliasResolver.cs`
- Create: `src/TSMapEditor.Tests/AI/AIUnitAliasResolverTests.cs`
- Modify: `src/TSMapEditor/AI/ToolExecutor.cs`

**Behavior:**

- Map common Chinese unit/building names to canonical INI codes.
- `search_units` should check aliases before fuzzy reference search.
- Alias hits should be shown clearly as high-confidence matches.
- Include at least: `美国大兵 -> GI`, `重装大兵 -> GGI`, `灰熊坦克 -> MTNK`, `犀牛坦克 -> HTNK`.

## Task 5: Owner/Faction Discovery Guardrails

**Purpose:** Avoid AI guessing house lists or showing incomplete faction lists.

**Possible files:**

- `src/TSMapEditor/AI/ToolDefinitions.cs`
- `src/TSMapEditor/AI/ToolExecutor.cs`
- `src/TSMapEditor.Tests/AI/ToolDefinitionsTests.cs`

**Behavior:**

- Either enrich `get_map_info` with real house names or add a read-only `get_houses` tool.
- Prompt should instruct AI to consult real house list before placing owned objects if uncertain.
- Do not reintroduce fallback owner matching.

## Task 6: Selection-Scoped Execution Constraints

**Purpose:** Address the failed local-edit scenario where selected coordinates were ignored.

This is larger than the first five tasks and should be designed separately before implementation.

Expected future work:

- Pass active selection into `ToolExecutor` or `PositionResolver`.
- Add selection-relative placement or absolute coordinate support.
- Enforce local edit operations to stay within selected bounds where feasible.

## Self-Review

- Spec coverage: Directly maps to live evidence: missing write tool, empty workflow state, Chinese alias failure, house-list confusion, and selection failure.
- Scope control: V3 Task 1 is small and does not change map mutation behavior.
- Test realism: First tasks use pure tests and existing build/full test checks; selection constraints are deferred because they need a separate design.
- No placeholders: Each task has concrete files and expected behavior. Later selection work is explicitly marked as separate design work rather than hidden inside V3 Task 1.
