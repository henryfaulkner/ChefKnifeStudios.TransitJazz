# Cross-Agent Skill Synchronization Design

**Status:** Design only  
**Date:** 2026-08-29  
**Scope:** Repository-owned custom skills. Speckit skills and third-party/vendor skill
packages are intentionally outside this design.

## Decision

This is highly feasible. Use the Agent Skills `SKILL.md` format as the canonical,
portable source format, keep each custom skill under the repository's `skills/`
directory, and generate the three host-specific installations with one small,
dependency-free PowerShell script.

The script is a renderer, not a recursive copy command. It materializes a portable
skill plus an optional, narrow agent overlay into these generated destinations:

| Host | Generated destination | Why it is needed |
|---|---|---|
| Claude Code | `.claude/skills/<skill>/` | Claude's project skill location |
| Codex | `.agents/skills/<skill>/` | Codex's repository skill location |
| OpenCode | `.opencode/skills/<skill>/` | OpenCode's highest-precedence project skill location |

Only `skills/` and the sync script are source-controlled. All three output locations
are generated and ignored by Git. The script must never read or change agent settings,
credentials, plugins, commands, or any directory outside these three skill roots.

## Feasibility findings

All three hosts support a directory containing `SKILL.md` plus optional supporting
files, and all load the body only when the skill is selected. This makes a common
source viable.

* Claude Code uses `.claude/skills/<name>/SKILL.md`, follows symlinked skill
  directories, and supports the open Agent Skills format plus Claude-only extensions.
  [Claude Code skills documentation](https://code.claude.com/docs/en/slash-commands)
* Codex discovers repository skills in `.agents/skills` from the working directory
  through the repository root, and also supports symlinked skill folders.
  [OpenAI's Codex skills documentation](https://learn.chatgpt.com/docs/build-skills)
* OpenCode discovers `.opencode/skills`, `.claude/skills`, and `.agents/skills`.
  Its project `.opencode/skills` source wins over the compatibility locations, so a
  purpose-built OpenCode rendering has unambiguous precedence.
  [OpenCode skills documentation](https://opencode.ai/v2/docs/skills/)

The existing local layout supports this conclusion:

* `add-transit-city`, `discover-transit-city`, and `mj-gtfs` differ between the
  current Claude and Codex copies only because they name a particular agent or use a
  hard-coded agent-specific skill path.
* The other populated ordinary skills in both locations are already the same.
* OpenCode currently has independent, different versions of `grill-me` and
  `ponytail`, demonstrating why a blind directory copy is insufficient.
* `skills/grafana` is already a canonical-style source but is not materialized in
  the other host locations.
* `util-testing/SKILL.md` is empty and therefore is not a valid portable skill. It
  must be completed or explicitly omitted before it enters the catalog.
* `interface-design` contains a nested third-party package and its own Git metadata;
  it is not a normal repository-authored skill. It stays independently installed or
  becomes an explicitly managed vendor dependency later.

## Source layout

```text
skills/
├── _skill-sync/
│   └── catalog.json                 # exact, intentional set of managed skills
├── add-transit-city/
│   ├── SKILL.md                     # portable canonical source
│   ├── references/
│   └── .skill-sync/                 # optional overlays; never copied as an asset
│       └── claude.json
├── grafana/
│   ├── SKILL.md
│   └── .skill-sync/
│       └── codex/assets/agents/openai.yaml
│                                      # target-only Codex/ChatGPT metadata, if needed
└── ponytail/
    └── SKILL.md

tools/
└── sync-skills.ps1
```

`catalog.json` is an allow-list, rather than auto-discovering every directory. This
prevents helper folders, unfinished skills, Speckit, and vendor packages from becoming
active merely because they happen to be present. A representative entry is:

```json
{
  "schemaVersion": 1,
  "skills": {
    "ponytail": { "targets": ["claude", "codex", "opencode"] },
    "grafana": { "targets": ["claude", "codex", "opencode"] }
  }
}
```

The catalog initially contains only the selected repository-authored, non-Speckit
skills. It deliberately does not infer ownership from an existing target directory.

## Portable-source contract

Each canonical source is a valid, useful standalone Agent Skill. The generator does
not make an incomplete pseudo-format that only works after rendering.

1. The directory name and `name` are the same lowercase kebab-case identifier
   (`^[a-z0-9]+(-[a-z0-9]+)*$`), unique across the catalog.
2. `SKILL.md` begins with YAML frontmatter containing a concise one-line `name` and
   `description`. Canonical frontmatter is limited to the portable fields:
   `name`, `description`, `license`, `compatibility`, `metadata`, and
   `allowed-tools`.
3. Supporting files are referenced by paths relative to the skill directory. A skill
   refers to another managed skill by name, not through `.claude/skills`,
   `.agents/skills`, or `.opencode/skills`.
4. Canonical prose is host-neutral: say “the repository instructions” rather than
   naming `CLAUDE.md` or `AGENTS.md`; say “automated mode may block package install”
   rather than naming a particular agent.
5. Canonical content must not use Claude-only dynamic shell injection, Claude argument
   substitutions, or host-specific tool names. Those features are valid only in a
   declared overlay.

These rules remove the current three path/name differences at their source, rather
than encoding needless replacements in the generator.

## Agent overlays

Most skills should have no overlay. An overlay exists only when the same workflow
requires different host semantics, not merely different wording.

An optional file such as `skills/<name>/.skill-sync/claude.json` may:

* add approved host-only frontmatter, for example Claude's `argument-hint` or
  `disable-model-invocation`;
* copy an explicitly listed target-only asset, for example Codex's optional
  `agents/openai.yaml`.

It may **not** contain a second full `SKILL.md` or a target-specific body rewrite.
Reword the canonical skill when prose can be portable; add a dedicated skill only when
the workflow is genuinely different. This keeps the synchronizer intentionally small.

Claude-only features such as `context: fork`, `allowed-tools`, dynamic `!` shell
content, and argument substitution remain in a Claude overlay. OpenCode-only
`metadata.opencode/*` values remain in an OpenCode overlay. Codex presentation and
tool-dependency metadata remain in Codex-only `agents/openai.yaml`. No target receives
another host's executable behavior accidentally.

## Sync script behavior

`tools/sync-skills.ps1` uses Windows PowerShell/PowerShell 7 built-ins only. It has no
package-manager, network, Git, or agent-CLI dependency.

```powershell
# Show intended changes and validate sources; no writes.
powershell -NoProfile -File tools/sync-skills.ps1 -Mode Check

# Render changed managed skills into the three target locations.
powershell -NoProfile -File tools/sync-skills.ps1 -Mode Sync

# Optional foreground watcher for active multi-agent work.
powershell -NoProfile -File tools/sync-skills.ps1 -Mode Watch
```

To repair or adopt only one host, pass `-Targets claude`, `-Targets codex`, or
`-Targets opencode`. This is useful when a running agent has an exclusive lock on its
current skill file; close or restart that agent, then sync that target explicitly.

`Watch` is convenience only: it uses a foreground `FileSystemWatcher` and debounces
changes to `skills/`. It does not install a service, scheduled task, shell profile
change, or Git hook. The normal `Sync` command is the predictable, one-command path
after a skill edit.

For every catalogued skill and target, the script will:

1. validate the portable-source contract and that all referenced source assets exist;
2. render the canonical files, excluding `.skill-sync`, then apply only that target's
   declared overlay and target-only assets;
3. replace only changed generated skill directories, then record the managed skill
   names in a generated manifest; and
4. report changed, unchanged, skipped, and failed skills per target.

It will refuse to overwrite a target directory that is not recorded in its generated
manifest. The first migration requires `-Adopt`, which backs up changed pre-existing
directories before they are replaced. Once a target is adopted, it is generated output:
edit `skills/`, never the generated copy. The next sync intentionally replaces a
managed directory so removed supporting files cannot linger.

The script renders every selected skill to a temporary directory and verifies that each
changed destination can be opened for write before it changes any output. If an agent
is holding one of its skills, sync fails without changing a different target.

## Source-control and migration plan

1. Inventory the existing non-Speckit skills and decide which are repository-authored.
   Do not automatically select a winner where the current OpenCode version has
   different behavior; reconcile it intentionally into the portable source.
2. Move the selected sources into `skills/`, rewriting the three hard-coded
   agent/path references as host-neutral prose. Complete or exclude `util-testing`.
3. Add the catalog and any genuinely required overlays. Render each target into a
   disposable migration backup first, review the files, then adopt them.
4. Ignore generated target skill directories and their manifests in `.gitignore`.
   Keep only `skills/`, `tools/sync-skills.ps1`, and this documentation under source
   control. Existing agent settings and non-skill files keep their current ownership.
5. Remove only the now-generated duplicate skill directories after the adoption check.
   Do not touch `speckit-*`, `interface-design`, OpenCode commands, plugins, or agent
   configuration.

This results in one reviewed source per custom skill, with generated installation
copies deliberately excluded from commits. It eliminates source-control duplication
without relying on fragile repository symlinks or requiring all agents to interpret
one another's extensions.

## Verification and operating expectations

`-Mode Check` is the required pre-use check. It must fail for an unknown catalog entry,
missing `SKILL.md`, invalid name, missing/empty description, forbidden host-specific
construct in a canonical body, missing target asset, or an unmanaged destination that
would be overwritten.

After the first render and after adding a new skill, perform this one-time host smoke
test:

| Host | Acceptance check |
|---|---|
| Claude Code | The skill appears in `/skills`; both explicit invocation and a matching prompt load the expected instructions. |
| Codex | The skill appears in `/skills` or can be invoked with `$name`; restart only if the change is not detected automatically. |
| OpenCode | The skill has the expected path-derived ID, is advertised when it has a description, and its `skill` permission allows loading. |

Claude watches existing skill directories for `SKILL.md` updates. Codex normally
detects changes but may require a restart. OpenCode has its own permission and
precedence model, so the explicit `.opencode/skills` rendering is the authoritative
one for OpenCode. The sync script does not and cannot force a running agent to reload
its skill registry.

## Alternatives rejected

**Three plain copies.** It is simple initially but recreates the existing drift and
does not explain which copy is authoritative.

**Directory symlinks as the primary solution.** Claude and Codex document symlink
support, and OpenCode could be pointed at a common source, but this cannot safely
express host-only behavior. Windows symlink permissions and checkout behavior also
make it less reliable for this repository. It remains an option for a future,
strictly-portable skill set, not the baseline.

**A plugin per agent.** Plugins are appropriate for public distribution or bundled
connectors, but add manifest, installation, versioning, and lifecycle work without
solving the immediate local drift problem.

**A background service or automatic Git hook.** Both create unnecessary state and
failure modes. A foreground watch mode plus an explicit sync command is sufficient,
and neither path interferes with the user's commit workflow.

## Out of scope

* Installing, publishing, or updating external skills.
* Migrating Speckit skills or OpenCode command files.
* Syncing skills to global user directories or cloud-hosted Claude skills.
* Changing agent model, MCP, permission, secret, plugin, or Git configuration.
* Creating a Git commit.
