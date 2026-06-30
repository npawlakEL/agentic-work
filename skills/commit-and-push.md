# Skill: Commit and Push

## Trigger
Every time code or documentation is committed to the branch.

## Rule
**Commits must be pushed to remote immediately after committing.** A local-only commit is not "done." The work is not complete until it exists on the remote branch and is accessible via GitHub.

## Steps
1. `git add` the relevant files
2. `git commit` with a descriptive message
3. **`git push`** to the remote — do NOT skip this step
4. Verify the push succeeded (check output for errors)

## Notes
- Never tell the user "everything is committed" if it hasn't been pushed to the remote.
- "Committed and push-ready" is not an acceptable status — push it.
- If the branch doesn't exist on the remote yet, use `git push -u origin <branch>` to create it.
- After pushing, the branch should be verifiable at `https://github.com/<owner>/<repo>/tree/<branch>`.
