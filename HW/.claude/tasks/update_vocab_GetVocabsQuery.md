# Update: Vocab List — Date Range Filter

Added `fromDate` and `toDate` query params to `GET /api/vocab`. Both filter by `notedAt` (the date the word was added).

---

## Endpoint

```
GET /api/vocab
```

### All query params (all optional)

| Param | Type | Description |
|-------|------|-------------|
| `search` | `string` | Filter by word or meaning (case-insensitive substring) |
| `wordType` | `number` | Filter by word type (0–6, see WordType table) |
| `fromDate` | `ISO 8601` | Only words noted on or after this date |
| `toDate` | `ISO 8601` | Only words noted on or before this date |
| `pageIndex` | `number` | Page number (1-based) |
| `pageSize` | `number` | Items per page |

All filters stack. Results are always ordered by `createdAt` descending.

---

## Examples

### Words added this month
```
GET /api/vocab?fromDate=2026-06-01T00:00:00Z&toDate=2026-06-30T23:59:59Z&pageIndex=1&pageSize=20
```

### Nouns added this week
```
GET /api/vocab?wordType=1&fromDate=2026-06-16T00:00:00Z&pageIndex=1&pageSize=20
```

### Full filter combined
```
GET /api/vocab?search=run&wordType=3&fromDate=2026-01-01T00:00:00Z&toDate=2026-06-18T23:59:59Z&pageIndex=1&pageSize=10
```

---

## Response (unchanged shape)

```json
{
  "data": {
    "items": [
      {
        "id": "abc123",
        "word": "ephemeral",
        "meaning": "lasting for a very short time",
        "example": null,
        "note": null,
        "wordType": 0,
        "notedAt": "2026-06-17T07:00:00Z",
        "reviewStage": 0,
        "nextReviewAt": "2026-06-20T07:00:00Z",
        "lastReviewedAt": null,
        "isCompleted": false,
        "createdAt": "2026-06-17T07:00:00Z",
        "createdBy": null,
        "updatedAt": null,
        "updatedBy": null
      }
    ],
    "pageIndex": 1,
    "pageSize": 20,
    "totalCount": 42,
    "totalPages": 3,
    "hasNextPage": true,
    "hasPreviousPage": false
  }
}
```

---

## FE notes

- Send dates as UTC ISO 8601 strings (`2026-06-01T00:00:00Z`).
- To filter a full day, set `fromDate` to start of day (`T00:00:00Z`) and `toDate` to end of day (`T23:59:59Z`).
- `fromDate` and `toDate` are independent — either one can be used alone.
