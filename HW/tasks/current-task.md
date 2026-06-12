---
id: TASK-001
type: feature
priority: medium
status: pending
created: 2026-03-25
---

# Add Product Feature

Products have: Name (required, min 3 chars), Description (optional), Price (decimal > 0), Stock quantity (int >= 0). Pageable and searchable by name.

## Criteria
- [ ] Product entity: Name (required, min 3), Description (optional), Price (decimal > 0), Stock (int >= 0)
- [ ] GET /api/product — paging + name search (public)
- [ ] GET /api/product/{id} — single (public)
- [ ] POST /api/product — create (auth required)
- [ ] PUT /api/product/{id} — partial update (auth required)
- [ ] DELETE /api/product/{id} — soft delete (auth required)
- [ ] FluentValidation on create and update DTOs
- [ ] EF Core migration for Products table

## Notes
Follow Blog feature as reference implementation.
