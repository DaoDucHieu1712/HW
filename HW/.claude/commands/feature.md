---
description: Write a new feature as a vertical CQRS slice — lock the scope, work outward from the domain, test in step
argument-hint: "[feature to build]"
---

## Build status

!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Verify.ps1 -Level build`

## Existing feature tree (follow the shape that is there, do not invent a new one)

!`git ls-files HW.Application/Features --exclude-standard`

## Feature to build

$ARGUMENTS

## Task

Follow the `feature-dev` skill. **Do not skip step 1.**

1. **Lock the scope before typing a line.** Write these three lines, and **ask the user** wherever
   two readings would lead to substantially different work:
   ```
   DO:        <exactly what>
   DON'T:     <the adjacent thing we deliberately leave alone>
   DONE WHEN: <which verifier is green>
   ```
2. **Put the code in the right project.** Domain -> `HW.Domain` · the feature's CQRS + agent tools
   -> `HW.Application/Features/<Feature>/` · EF/repository/store -> `HW.Infrastructure` ·
   controller/DI -> `HW.Api` · tests -> `HW.UnitTests`.
   Only put something in `HW.Agentic` if it is right for **every** application — a test blocks it
   from referencing back up.
3. **Read an existing feature as the template** before writing (`Features/Vocabs/` is the most
   complete one). New code must read like the code around it.
4. **Work inside-out**, running `Verify.ps1 -Level build` after each layer:
   domain -> application -> infrastructure -> api -> DI.
   Going the other way (controller first) almost always leaks DTOs back into the domain.
5. **Test in step**, never all at the end: domain invariants · each domain event · the handler
   (happy + not-found + one boundary case) · each validator rule · the tool if there is one.
6. **Verify**: `Verify.ps1 -Level full`.

## Stop if

- A migration / broker schema change / any irreversible operation is needed -> **ask for approval
  first**.
- You notice you are editing a file that is not on the `DO:` line -> stop, record it under "Seen
  but not touched".

## Final report

The agreed `DO:` line · changes by layer · new tests (count + what they cover) ·
`Verify.ps1 -Level full` PASS · **seen but not touched** · **needs approval**.
