# AI Workflow V2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Connect V1's workflow state and validation issue model to runtime quality checks without destabilizing the existing map auto-fix path.

**Architecture:** Keep `MapQualityChecker.Check()` and `QualityFix` intact at first. Add a thin conversion layer that turns current fix recommendations into `MapValidationIssue` summaries and records them in `AIWorkflowState`. Later tasks can replace or enrich the checker with policy-aware issue generation.

**Tech Stack:** C# / .NET 8, xUnit, existing `MapQualityChecker`, `QualityFix`, `AIWorkflowState`, and `MapValidationIssue`.

---

## Task Order

1. Bridge quality fixes into workflow validation issues - completed 2026-05-16
2. Clarify validation finding lifecycle in workflow state - completed 2026-05-16
3. Add policy-aware validation entry point alongside existing auto-fixes - completed 2026-05-16
4. Add intent/policy inference from workflow state - completed 2026-05-16
5. Add live/manual AI workflow verification script or checklist - completed 2026-05-16

## Task 1: Bridge QualityFix To Workflow Validation Issues

**Purpose:** Stop `MapValidationIssue` from being an orphaned model while preserving the existing `QualityFix` auto-repair path.

**Files:**

- Create: `src/TSMapEditor/AI/Validation/MapQualityIssueConverter.cs`
- Create: `src/TSMapEditor.Tests/AI/Validation/MapQualityIssueConverterTests.cs`
- Modify: `src/TSMapEditor/AI/AIChatService.cs`

**Behavior:**

- Convert each `QualityFix` into a `MapValidationIssue`.
- Write issue summaries into `AIChatService.WorkflowState.ValidationIssues` when `RunQualityChecks()` runs and finds fixes.
- Clear workflow validation issues when a map-generation quality check runs and finds no fixes.
- Preserve existing automatic fix execution.
- Do not rewrite `MapQualityChecker`.

**Verification:**

Run converter tests, full tests, and Release build with `/p:EnableMGCBItems=false`.

## Later Tasks

Later tasks should be planned one at a time after review. Do not jump directly into full validation policy rewriting.

## Task 2: Clarify Validation Finding Lifecycle

**Purpose:** Prevent the AI from treating `AIWorkflowState.ValidationIssues` as guaranteed unresolved current errors after the auto-fix loop has already run.

**Files:**

- Modify: `src/TSMapEditor/AI/Workflow/AIWorkflowState.cs`
- Modify: `src/TSMapEditor.Tests/AI/Workflow/AIWorkflowStateTests.cs`
- Modify: `src/TSMapEditor/AI/AIChatService.cs`

**Behavior:**

- Workflow state summary should label validation entries as findings from the last quality check pass.
- Summary should explain that these findings may already have been auto-fixed.
- Prompt should tell the AI to treat workflow validation entries as risk/context notes unless a fresh validation pass proves they remain unresolved.
- Do not change the `ValidationIssues` property name in this task.
- Do not change `MapQualityChecker`.

**Verification:**

Run workflow state tests, full tests, and Release build with `/p:EnableMGCBItems=false`.

## Task 3: Policy-Aware Validation Entry Point

**Purpose:** Add a non-invasive issue-returning validation entry point while keeping the existing `QualityFix` auto-fix path intact.

**Files:**

- Modify: `src/TSMapEditor/AI/MapQualityChecker.cs`
- Modify: `src/TSMapEditor/AI/Validation/MapQualityIssueConverter.cs`
- Modify: `src/TSMapEditor.Tests/AI/Validation/MapQualityIssueConverterTests.cs`

**Behavior:**

- Add `MapQualityChecker.CheckIssues(int expectedPlayers, MapValidationPolicy policy = MapValidationPolicy.Balanced)`.
- `CheckIssues` should call existing `Check(expectedPlayers)` and convert the returned fixes to `MapValidationIssue`.
- Do not change `Check(int expectedPlayers)` behavior.
- Make `MapQualityIssueConverter` apply basic policy-aware severity/intent semantics:
  - Missing spawn points stay `Error` for every policy.
  - Missing spawn ore is `Warning` for `Balanced`, `Info` and `AllowedByIntent = true` for `Creative` and `Scenario`.
  - Low terrain diversity stays `Info`.
  - `LocalOnly` should mark generated full-map quality fixes as `Intentional` or low-priority context unless they are missing spawn points.

**Verification:**

Run converter tests, full tests, and Release build with `/p:EnableMGCBItems=false`.

## Task 4: Intent/Policy Inference From Workflow State

**Purpose:** Make runtime validation issue summaries use the user's apparent map intent instead of always defaulting to `Balanced`.

**Files:**

- Create: `src/TSMapEditor/AI/Validation/MapValidationPolicyResolver.cs`
- Create: `src/TSMapEditor.Tests/AI/Validation/MapValidationPolicyResolverTests.cs`
- Modify: `src/TSMapEditor/AI/AIChatService.cs`

**Behavior:**

- Resolve `MapValidationPolicy` from `AIWorkflowState.Intent` and `AIWorkflowState.UserGoal`.
- Prefer explicit intent over user-goal text.
- Default to `Balanced`.
- Use the resolver in `AIChatService.RunQualityChecks()` when converting `QualityFix` to validation findings.
- Do not change `MapQualityChecker`.

**Verification:**

Run policy resolver tests, full tests, and Release build with `/p:EnableMGCBItems=false`.

## Task 5: Live/Manual AI Workflow Verification Checklist

**Purpose:** Define a repeatable live validation path for the AI workflow behaviors that unit tests cannot prove.

**Files:**

- Create: `docs/ai-workflow/live-ai-verification-checklist.md`

**Behavior:**

- Document prerequisites for running the editor with an AI provider.
- Define manual scenarios for balanced skirmish, tower defense/scenario, local edit, owner error recovery, infantry/vehicle separation, and validation findings.
- For each scenario, list expected tool behavior, expected user-visible result, and evidence to capture.
- Include a clear note that build/unit tests do not prove live LLM tool-use adherence.
- Do not modify source code.

**Verification:**

Run full tests and Release build if source files are untouched and the worker wants a final sanity check.

## Self-Review

- Spec coverage: Addresses the integration review's main residual risk that `MapValidationIssue` is disconnected.
- Scope control: Preserves `MapQualityChecker` and auto-fixes; does not attempt full policy validation in one step.
- Test realism: Adds pure converter tests and uses build/full tests for the `AIChatService` integration.
- No placeholders: Task 1 has concrete files, behavior, and verification.
