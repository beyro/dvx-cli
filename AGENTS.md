## Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## Test-Driven Development

**Red, green, refactor — never write implementation code first.**

1. **Red** — write a failing test that captures the desired behaviour. Run it and confirm it fails for the right reason.
2. **Green** — write the minimum code needed to make that test pass. Run the test and confirm it passes.
3. **Refactor** — clean up while keeping all tests green. Re-run the full test suite after every step.

- No production code without a failing test that demands it.
- If a test passes on the first run, verify it actually exercises the new behaviour — it may be a false green.
- Run the full suite (`dotnet test`) before declaring any task done.

## Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

## Let the code speak for itself
- Don't write comments (other than XmlDocs) to explain your reasoning when the code is already clear
- Only write comments when necessary to clarify non-obvious decisions

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

## Keep the Regression Test Cases Up to Date

**Every code change must be reflected in `RegressionTests.md`.**

After making any change — new feature, behaviour change, bug fix, or removed functionality — update `RegressionTests.md` before considering the task done:

- Add a new case (following the existing `<AREA>-NN` ID and table format) for any new or changed user-visible behaviour.
- Amend the relevant existing case when behaviour changes.
- Remove or mark obsolete any case that no longer applies.
- Keep the Contents list in sync when a whole section is added or removed.

If a change genuinely affects no manual test case, say so explicitly instead of silently skipping this step.
