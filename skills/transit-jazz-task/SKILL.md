---
name: transit-jazz-task
description: Create and manage tasks on the Transit Jazz GitHub Project board (Project #4). Automatically adds tasks with Todo status.
metadata:
  pattern: tool-wrapper
---

# Transit Jazz Task Manager

## Purpose

Specialized skill for creating and managing tasks on the Transit Jazz GitHub Project board. Automatically sets tasks to "Todo" status upon creation.

## Project Configuration

- **Project Number:** 4
- **Project ID:** PVT_kwHOAgReBs4BWmMc
- **Owner:** henryfaulkner
- **Board URL:** https://github.com/users/henryfaulkner/projects/4/views/2
- **Status Field ID:** PVTSSF_lAHOAgReBs4BWmMczhR5Bxg
- **Todo Option ID:** f75ad846

## Workflows

### Workflow: Create Task

Creates a new draft issue in the Transit Jazz project with "Todo" status using the standard ticket format.

**Standard Ticket Format**:

```markdown
## Description
<Brief description of the task or feature>

## Details
<Detailed information, context, implementation notes>

## Acceptance Criteria
- [ ] <Criterion 1>
- [ ] <Criterion 2>
- [ ] <Criterion 3>
```

**Commands**:

```bash
# Create draft item with standard format
gh project item-create 4 --owner "henryfaulkner" --title "<TITLE>" --body "<BODY_WITH_STANDARD_FORMAT>" --format json

# Set status to Todo
gh project item-edit --id "<ITEM_ID>" --project-id "PVT_kwHOAgReBs4BWmMc" --field-id "PVTSSF_lAHOAgReBs4BWmMczhR5Bxg" --single-select-option-id "f75ad846" --format json
```

**Example**:

```bash
gh project item-create 4 --owner "henryfaulkner" --title "Implement jazz engine" --body "## Description
Implement the core jazz engine for Transit Jazz.

## Details
The jazz engine will handle real-time audio processing and MIDI input.

## Acceptance Criteria
- [ ] Engine initializes correctly
- [ ] MIDI input is processed
- [ ] Audio output is generated" --format json
```

### Workflow: List Tasks

Lists all items in the Transit Jazz project.

**Command**:

```bash
gh project item-list 4 --owner "henryfaulkner" --format json
```

### Workflow: Update Task Status

Updates the status of an existing task.

**Command**:

```bash
gh project item-edit --id "<ITEM_ID>" --project-id "PVT_kwHOAgReBs4BWmMc" --field-id "PVTSSF_lAHOAgReBs4BWmMczhR5Bxg" --single-select-option-id "<OPTION_ID>" --format json
```

Status Option IDs:
- Todo: f75ad846
- In Progress: 47fc9ee4
- Done: 98236657

## Safety & Verification

- **Human-in-the-Loop**: Present all state-changing commands (create, edit) and their parameters to the user before execution.
- **Verification**: Verify Item IDs before editing operations.

## Output Handling

Always prefer `--format json` for structured data. The internal **Item ID** (e.g., `PVTI_...`) is critical for `item-edit` operations.
