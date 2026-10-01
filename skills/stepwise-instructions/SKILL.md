---
name: stepwise-instructions
description: Guide a user through operational tasks one actionable step at a time, with a recommended action and branching based on each result. Use when the user asks to be walked through a procedure, troubleshoot interactively, or proceed step by step in chat.
---

# Stepwise Instructions

Use this skill to guide the user through a task interactively. Keep the next action clear and small, and wait for its result before giving a dependent action.

## Workflow

1. **Understand the goal and current state.** Use conversation context first. Inspect available code, configuration, documentation, and tool output for answers before asking the user. Do not ask for details already present.
2. **Choose the next safe action.** Prefer a read-only inspection that resolves an important unknown. Separate inspection from edits, restarts, access changes, deletion, or other state-changing operations. Explain the effect before guiding a consequential change.
3. **Give one step.** Provide exactly one command, action, or decision for the user to do next. Make it copyable and specific to their current environment. Include what result to report and, when useful, what common outcomes mean.
4. **Recommend a path.** When the user must choose, state the recommended choice and briefly explain why. Ask only that one decision at a time.
5. **Wait and branch.** Stop after the step. Use the user's result to choose the next instruction; do not assume success or send a dependent sequence in advance. If the output changes the diagnosis, revise the path.
6. **Handle interruptions.** Answer a status question or relevant side question briefly, then resume from the last unresolved step unless the user changes or cancels the goal.
7. **Close the loop.** Once the procedure is complete, state the verified outcome and any remaining user action in a short summary.

## Message shape

Use a compact format such as:

> **Step 1:** Run `command` in the named shell. Report the `Active:` line only. If it says `active`, we’ll inspect the next setting.
>
> **Recommended action:** Use option A because it matches the current evidence.

Use only the parts that fit the current step. Do not pad simple instructions with repeated explanations.

## Safety and clarity

- Never ask the user to paste passwords, private keys, access tokens, connection strings, or other secrets. Tell them to enter credentials only in the trusted prompt or secret store; request redacted status/output instead.
- Avoid commands whose output may expose secrets. If sensitive output is unavoidable, give a safe filtering or redaction instruction before asking for results.
- Identify whether the step is read-only or changes state when that distinction matters. For a change, state what it changes and the intended effect before providing the command.
- Do not imply a change has succeeded until the user reports a result or available evidence verifies it.
- Keep each message focused on the immediate next step. Do not list future dependent steps as a checklist.
- If the user's requested action is outside their stated authorization or requires an unavailable capability, explain the specific blocker and give the nearest safe next step.

## Review checklist

- [ ] There is only one actionable next step or one decision in this message.
- [ ] The command/action fits the user's known environment and current state.
- [ ] Any recommendation is explicit and brief.
- [ ] The requested result is safe to share and tells the user what to report.
- [ ] Dependent instructions wait until the user returns the result.
