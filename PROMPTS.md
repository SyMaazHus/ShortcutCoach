# Dev Log — AI-Assisted Development

How this project was built with Claude Code: the prompts that worked, the ones
that didn't, and what I changed. Each entry = one task.

**Approach:** spec-driven development. SPEC.md defines scope and acceptance
criteria, CLAUDE.md gives the agent persistent context and rules, every task
starts with a plan I review before any code is written, and every change is
verified by build + tests before commit.

---

## Template

### [Date] — [Milestone] — [Task]
**Prompt:**
> (what I asked)

**What happened:** (did the plan/code work first time? what was wrong?)

**What I changed / follow-up prompt:**
> (how I corrected or refined it)

**Lesson:** (what I'd prompt differently next time)

**Commit:** `abc1234`

---

## Entries

### 2026-10-28 — Setup — Project context
**Prompt:** Wrote SPEC.md (scope, architecture, acceptance criteria) and
CLAUDE.md (stack, workflow rules, known Win32 gotchas) before writing any code.

**Why:** giving the agent the known failure modes up front (GC'd hook delegate,
slow hook callbacks, DPI scaling) should prevent the most common bugs instead
of debugging them later.

**Lesson:** _fill in after M1 — did the gotchas section actually prevent them?_
