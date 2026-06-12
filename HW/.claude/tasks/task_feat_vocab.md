# Feature: Vocab (Word Learning)

CRUD for vocabulary words + daily mission review based on spaced repetition (day 3 / 7 / 14).

---

## Endpoints

### Get list (paginated + search)
```bash
curl "http://localhost:5000/api/vocab?search=hello&pageIndex=1&pageSize=10"
```

### Get daily mission (words due for review today)
```bash
curl "http://localhost:5000/api/vocab/daily"
```

### Get by ID
```bash
curl "http://localhost:5000/api/vocab/{id}"
```

### Create
```bash
curl -X POST "http://localhost:5000/api/vocab" \
  -H "Content-Type: application/json" \
  -d '{ "word": "ephemeral", "meaning": "lasting for a very short time", "example": "The ephemeral nature of fashion.", "note": "adj" }'
```

### Update (partial — send only changed fields)
```bash
curl -X PUT "http://localhost:5000/api/vocab/{id}" \
  -H "Content-Type: application/json" \
  -d '{ "meaning": "updated meaning" }'
```

### Delete (soft delete)
```bash
curl -X DELETE "http://localhost:5000/api/vocab/{id}"
```

### Mark reviewed (advances review stage)
```bash
curl -X POST "http://localhost:5000/api/vocab/{id}/review"
```

---

## Review stages

| Stage | Meaning | Next review |
|-------|---------|-------------|
| 0 | New | day 3 from creation |
| 1 | Reviewed day 3 | day 7 from creation |
| 2 | Reviewed day 7 | day 14 from creation |
| 3 | Completed | none (`nextReviewAt = null`) |

`isCompleted = true` when stage = 3. Daily mission returns words where `nextReviewAt ≤ now` and not completed.
