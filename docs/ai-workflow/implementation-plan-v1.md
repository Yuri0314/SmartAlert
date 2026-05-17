# AI Workflow V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first reliable SmartAlert AI workflow foundation by removing silent ownership mistakes, separating infantry from vehicle tools, adding workflow state, and preparing policy-based validation.

**Architecture:** Keep the first wave small. Start with deterministic, testable helper logic, then wire it into `ToolExecutor`. Add workflow state as an in-memory object in `AIChatService` before attempting multi-agent orchestration or full validation.

**Tech Stack:** C# / .NET 8, xUnit, existing WAE map model, existing `ToolDefinitions` and `ToolExecutor` tool-call architecture.

---

## Current Decision On Existing Tool-Layer Patches

Keep the recent tool-layer patches for now:

- `f6318a69` spawn coordinate feedback, skip-on-water, distance warnings
- `fef0e925` river exclusion softened to warning, empty AI response fallback

They are functional improvements and should not be reverted blindly. Treat them as existing behavior to be absorbed into policy-driven validation later. Do not add more broad tool-layer patches unless a task explicitly requires it.

## Task Order

1. Owner resolution safety - completed 2026-05-16
2. Infantry tool separation - completed 2026-05-16
3. Workflow state and `get_workflow_state` - completed 2026-05-16
4. Prompt staging for intent first - completed 2026-05-16
5. Minimal validation issue model - completed 2026-05-16

## Task 1: Owner Resolution Safety

**Purpose:** Prevent invalid owner names from silently resolving to the first house, which causes units/buildings to belong to the wrong faction.

**Files:**

- Create: `src/TSMapEditor/AI/AIHouseResolver.cs`
- Create: `src/TSMapEditor.Tests/AI/AIHouseResolverTests.cs`
- Modify: `src/TSMapEditor/AI/ToolExecutor.cs`

**Behavior:**

- Empty or whitespace owner means `"Neutral"`.
- Owner matching is exact, case-insensitive.
- If no matching house exists, return `null`.
- Do not fall back to the first house.
- `ToolExecutor` should return a clear tool error when owner cannot be resolved, ideally including available house names.

**Test Cases:**

- Exact owner name resolves.
- Case-insensitive owner name resolves.
- Empty owner resolves to `Neutral` when present.
- Empty owner returns `null` when `Neutral` is missing.
- Unknown owner returns `null` and does not return the first house.
- Partial owner text does not match accidentally.

**Suggested implementation shape:**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using TSMapEditor.Models;

namespace TSMapEditor.AI
{
    internal static class AIHouseResolver
    {
        public static House ResolveOwner(IReadOnlyList<House> houses, string ownerName)
        {
            if (houses == null || houses.Count == 0)
                return null;

            string requestedOwner = string.IsNullOrWhiteSpace(ownerName) ? "Neutral" : ownerName.Trim();

            return houses.FirstOrDefault(house =>
                string.Equals(house.ININame, requestedOwner, StringComparison.OrdinalIgnoreCase));
        }

        public static string FormatAvailableOwners(IReadOnlyList<House> houses)
        {
            if (houses == null || houses.Count == 0)
                return "(none)";

            return string.Join(", ", houses.Select(house => house.ININame));
        }
    }
}
```

**Verification:**

Run:

```powershell
dotnet test src\TSMapEditor.Tests\TSMapEditor.Tests.csproj --configuration Debug --filter AIHouseResolverTests
```

Expected:

- Tests compile.
- Tests fail before production implementation if written first.
- Tests pass after implementation.

Then run:

```powershell
dotnet build src\TSMapEditor\TSMapEditor.csproj --configuration Release /p:EnableMGCBItems=false
```

Expected:

- Build exits 0.
- Existing warnings are acceptable; new errors are not.

## Task 2: Infantry Tool Separation

**Purpose:** Stop overloading `place_unit` for all unit-like objects. Vehicles and infantry should be separate tool concepts.

**Files:**

- Modify: `src/TSMapEditor/AI/ToolDefinitions.cs`
- Modify: `src/TSMapEditor/AI/ToolExecutor.cs`
- Test: add focused tests if a pure helper is extracted; otherwise document why only build verification was possible.

**Behavior:**

- Add `place_infantry`.
- Add `place_infantries` only if batch infantry placement is needed for parity.
- Keep `place_unit` temporarily for compatibility, but describe it as vehicle-only.
- Route infantry tools to `AIPlaceObjectType.Infantry`.

**Verification:**

Run:

```powershell
dotnet build src\TSMapEditor\TSMapEditor.csproj --configuration Release /p:EnableMGCBItems=false
```

Expected:

- Build exits 0.

## Task 3: Workflow State Foundation

**Purpose:** Give the AI a structured task blackboard that can be read during long map generation or editing runs.

**Files:**

- Create: `src/TSMapEditor/AI/Workflow/AIWorkflowState.cs`
- Create: `src/TSMapEditor/AI/Workflow/AIWorkflowStep.cs`
- Modify: `src/TSMapEditor/AI/AIChatService.cs`
- Modify: `src/TSMapEditor/AI/ToolDefinitions.cs`
- Modify: `src/TSMapEditor/AI/ToolExecutor.cs`
- Test: `src/TSMapEditor.Tests/AI/Workflow/AIWorkflowStateTests.cs`

**Behavior:**

- Track user goal, intent, current phase, completed steps, pending steps, known risks, active selection, and validation issues.
- Add `get_workflow_state` as a read-only tool.
- Do not persist to disk in v1.
- Do not implement full multi-agent orchestration in v1.

**Verification:**

Run workflow state unit tests, then build with MGCB disabled.

## Task 4: Intent-First Prompt Staging

**Purpose:** Encourage the agent to set intent and plan before using construction tools.

**Files:**

- Modify: `src/TSMapEditor/AI/AIChatService.cs`
- Possibly modify: `src/TSMapEditor/AI/ToolDefinitions.cs`

**Behavior:**

- Prompt should clearly distinguish `BalancedSkirmish`, `CreativeSkirmish`, `SurvivalChallenge`, `TowerDefense`, `ScenarioStory`, `BeautifyExistingMap`, and `LocalEdit`.
- Prompt should instruct AI to inspect workflow state before continuing long tasks.
- Prompt should avoid saying all spawn obstacles are always errors.

**Verification:**

Build with MGCB disabled. Manual AI validation can follow later; do not claim live AI behavior is verified without running it.

## Task 5: Minimal Validation Issue Model

**Purpose:** Prepare for policy-driven validation without rewriting all quality checks at once.

**Files:**

- Create: `src/TSMapEditor/AI/Validation/MapValidationIssue.cs`
- Create: `src/TSMapEditor/AI/Validation/MapValidationSeverity.cs`
- Create: `src/TSMapEditor/AI/Validation/MapValidationPolicy.cs`
- Modify: `src/TSMapEditor/AI/MapQualityChecker.cs` only if a small, clear integration point is available.
- Test: `src/TSMapEditor.Tests/AI/Validation/MapValidationIssueTests.cs`

**Behavior:**

- Severity values: `Error`, `Warning`, `Info`, `Intentional`.
- Policy values should support at least `Balanced`, `Creative`, `Scenario`, and `LocalOnly`.
- Do not rewrite all quality checks in this task.

**Verification:**

Run validation model tests, then build with MGCB disabled.

## Self-Review

- Spec coverage: Covers first-wave items from `docs/ai-workflow/global-design.md`: owner safety, infantry separation, workflow state, intent staging, validation model.
- Scope control: Defers complete multi-agent orchestration, full pathfinding, full balance analysis, and persistent workflow logs.
- Test realism: Task 1 starts with pure logic tests because WAE `Map` fixture construction may be expensive. Later tasks can expand test coverage once seams exist.
- No placeholders: Each task names files, expected behavior, and verification commands.
