<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at specs/053-worker-observability/plan.md
<!-- SPECKIT END -->

## Git commits

NEVER run `git commit` (or any command that creates a commit, e.g. via a
speckit `after_*`/`before_*` auto-commit hook) on behalf of the user. Leave
changes staged/unstaged for the user to review and commit themselves, even
when a workflow step (like the speckit git extension's optional auto-commit
hooks) would normally offer to commit. This applies regardless of which
skill or workflow is running.
