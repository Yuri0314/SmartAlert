# SmartAlert Agent Collaboration Protocol

## Purpose

This document defines how Codex, Antigravity, and the user collaborate on SmartAlert without making the repository noisy or letting multiple agents overwrite each other's work.

Long-lived architecture decisions belong in `docs/ai-workflow/`. Short-lived handoffs, reviews, scratch notes, and run logs belong in `.agent/`, which is ignored by git.

## Directory Layout

Tracked project documentation:

```text
docs/ai-workflow/
  global-design.md
  collaboration-protocol.md
  implementation-plan-v1.md
```

Local ignored collaboration workspace:

```text
.agent/
  handoffs/
  reviews/
  run-logs/
```

Use `.agent/` for temporary agent-to-agent communication. Do not commit files from `.agent/`.

## Roles

### Codex

Codex acts as architecture owner, planner, integrator, and final reviewer.

Responsibilities:

- Maintain the global design and implementation plan.
- Break work into small, bounded tasks.
- Review Antigravity changes against the plan.
- Verify builds/tests before claiming completion.
- Decide whether temporary findings should be promoted into tracked docs.

### Antigravity

Antigravity acts as a bounded implementation worker or external reviewer.

Responsibilities:

- Work only on the assigned task.
- Avoid broad rewrites unless the task explicitly permits them.
- Write a handoff or review note under `.agent/`.
- Report changed files, validation commands, risks, and deviations from the plan.

### User

The user acts as coordinator between agents.

Responsibilities:

- Pass Codex task instructions to Antigravity.
- Pass Antigravity handoff/review file paths back to Codex.
- Approve direction changes when agents disagree.

## Standard Handoff Flow

1. Codex writes or updates the tracked implementation plan under `docs/ai-workflow/`.
2. Codex writes a task handoff under `.agent/handoffs/` when Antigravity should act.
3. The user gives Antigravity the handoff path and instruction text from Codex.
4. Antigravity implements the bounded task.
5. Antigravity writes a completion note under `.agent/reviews/`.
6. The user gives Codex the completion note path.
7. Codex reviews the note, checks the diff, runs verification where possible, and decides next action.

## Handoff File Template

Use this structure for files in `.agent/handoffs/`:

```markdown
# Handoff: <task name>

## Context

Short summary of the global design and why this task exists.

## Task

Exact work to do.

## Allowed Files

- `path/to/file.cs`

## Do Not Touch

- `path/to/unrelated/file.cs`

## Acceptance Criteria

- Concrete expected behavior.
- Tests or build commands to run.

## Required Completion Note

Write `.agent/reviews/<task-name>-completion.md` with:

- Files changed
- Behavior changed
- Commands run and results
- Risks or deviations
```

## Completion Note Template

Use this structure for files in `.agent/reviews/`:

```markdown
# Completion: <task name>

## Files Changed

- `path/to/file.cs`

## Summary

What changed and why.

## Verification

Commands run and exact result.

## Risks

Known limitations, skipped tests, or unclear behavior.

## Deviations

Anything that differed from the handoff.
```

## Collaboration Rules

- Do not let two agents edit the same files at the same time.
- Do not assign vague tasks such as "continue optimizing AI map generation".
- Every task should have a narrow file scope and acceptance criteria.
- Temporary notes stay under `.agent/`.
- Durable design decisions move into `docs/ai-workflow/`.
- If Antigravity proposes a direction change, Codex reviews it before implementation continues.

## Current Source Of Truth

Read `docs/ai-workflow/global-design.md` before changing AI workflow, tool semantics, validation, or agent orchestration code.
