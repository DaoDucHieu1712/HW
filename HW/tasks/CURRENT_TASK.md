# Current Task

<!--
  INSTRUCTIONS:
  Fill in this file to define a task for the agent workflow.
  Run /run-workflow to execute the full pipeline.
  Run individual agents: /agent-read, /agent-analyze, /agent-code, /agent-test, /agent-report, /agent-apply
-->

## Task ID
TASK-001

## Title
<!-- Short, descriptive title -->
Add Product Feature

## Type
<!-- feature | bugfix | refactor | chore -->
feature

## Priority
<!-- high | medium | low -->
medium

## Description
<!--
  Describe what needs to be done. Be specific.
  Include: what functionality to add/change, expected behavior, any edge cases.
-->
Add a Product entity with CRUD operations. Products have a name, description, price (decimal), and stock quantity (int). Products should be pageable and searchable by name. Only authenticated users can create/update/delete products.

## Acceptance Criteria
<!--
  List concrete, testable conditions that must be true when the task is done.
-->
- [ ] Product entity exists with: Name (required, min 3 chars), Description (optional), Price (> 0), Stock (>= 0)
- [ ] GET /api/product supports paging and name search
- [ ] GET /api/product/{id} returns a product by ID
- [ ] POST /api/product creates a new product (auth required)
- [ ] PUT /api/product/{id} updates a product (auth required)
- [ ] DELETE /api/product/{id} soft-deletes a product (auth required)
- [ ] FluentValidation validates create/update requests
- [ ] Soft delete is used (IsDelete = true), not hard delete

## Affected Areas
<!--
  Which layers/files are likely to change?
-->
- HW.Domain/Entities/
- HW.Application/Dtos/
- HW.Application/Services/
- HW.Application/Validators/
- HW.Api/Controllers/
- HW.Infrastructure/ApplicationDbContext.cs
- HW.Infrastructure/DI/

## Notes
<!-- Any extra context, constraints, or decisions already made -->
Follow the existing Blog feature as a reference implementation.
