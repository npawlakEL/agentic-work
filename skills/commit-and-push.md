# Skill: Commit and Push

## Trigger
Every time code or documentation is committed to the branch.

## Rules

### During Planning/Setup Phase
Commits are pushed immediately. The planner is working interactively with the user.

### During Coder ↔ Reviewer Loop (Gate 2)
- **NO PUSHING.** All commits stay local.
- Coder and Reviewer iterate locally until Reviewer signs off.
- There is exactly **ONE push** at the end of the entire cycle.
- That push only happens **after explicit user approval** (Gate 2.5).
- The agent must check in with the user, present a summary, and wait for confirmation.
- Auto-push is forbidden. The agent cannot push on its own after the coder/reviewer process.

### General Rules
- Never tell the user "everything is committed" if it hasn't been pushed to the remote (when pushing is expected).
- "Committed and push-ready" is not an acceptable status — either push it (if allowed) or state it's awaiting approval.
- If the branch doesn't exist on the remote yet, use `git push -u origin <branch>` to create it.
- After pushing, the branch should be verifiable at `https://github.com/<owner>/<repo>/tree/<branch>`.

## Steps (Post-Approval Push)
1. Verify all tests pass
2. Verify Reviewer has signed off
3. Present summary to user and ask for push approval
4. On approval: `git push` (single push of all accumulated commits)
5. Verify the push succeeded
