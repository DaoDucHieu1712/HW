# Update: Vocab — WordType Classification + Filtered Search

Added `wordType` field to classify vocabulary words by part of speech. Search and exam generation now support filtering by type.

---

## WordType values

| Value | Label |
|-------|-------|
| `0` | Adjective |
| `1` | Noun |
| `2` | Adverb |
| `3` | Verb |
| `4` | Conjunction |
| `5` | Preposition |
| `6` | Collocation |

`wordType` is optional everywhere — existing words without a type return `null`.

---

## Changed endpoints

### Create — now accepts `wordType`
```bash
curl -X POST "http://localhost:5000/api/vocab" \
  -H "Content-Type: application/json" \
  -d '{ "word": "ephemeral", "meaning": "lasting for a very short time", "example": "The ephemeral nature of fashion.", "note": "optional note", "wordType": 0 }'
```

### Update (partial) — now accepts `wordType`
```bash
curl -X PUT "http://localhost:5000/api/vocab/{id}" \
  -H "Content-Type: application/json" \
  -d '{ "wordType": 3 }'
```

### Get list — now filterable by `wordType`
```bash
curl "http://localhost:5000/api/vocab?wordType=1&search=hello&pageIndex=1&pageSize=10"
```

Filters stack: `search` narrows by word/meaning text, `wordType` narrows by part of speech. Both are optional.

---

## Response shape (updated)

`wordType` is now included in every read response.

```json
{
  "id": "abc123",
  "word": "ephemeral",
  "meaning": "lasting for a very short time",
  "example": "The ephemeral nature of fashion.",
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
```

`wordType` is `null` for words that were created before this update.

---

## Unchanged endpoints

These endpoints are unchanged — no new fields required.

```bash
GET  /api/vocab/daily      # daily mission (unchanged)
GET  /api/vocab/{id}       # get by id (response now includes wordType)
DELETE /api/vocab/{id}     # soft delete (unchanged)
POST /api/vocab/{id}/review  # mark reviewed (unchanged)
```
# Feature: Vocab Test (Practice Exam)

Stateless exam mode — generate random questions from the vocab list, display them to the user, then grade all answers in one shot. Nothing is stored to DB.

---

## Flow

```
POST /api/vocab/exam/generate  →  show questions  →  user answers  →  POST /api/vocab/exam/grade  →  show result
```

---

## Step 1 — Generate questions

```
POST /api/vocab/exam/generate
```

**Request**
```json
{
  "questionCount": 10,
  "wordType": 1,
  "from": "2026-01-01T00:00:00Z",
  "to":   "2026-06-17T23:59:59Z"
}
```

All filter fields are optional and stackable.

| Field | Type | Description |
|-------|------|-------------|
| `questionCount` | `number` | **Required.** How many questions to generate |
| `wordType` | `number \| null` | Filter pool to one word type (see table below). Omit for all types |
| `from` | `ISO 8601 \| null` | Only include words noted on or after this date |
| `to` | `ISO 8601 \| null` | Only include words noted on or before this date |

**WordType values**

| Value | Label |
|-------|-------|
| `0` | Adjective |
| `1` | Noun |
| `2` | Adverb |
| `3` | Verb |
| `4` | Conjunction |
| `5` | Preposition |
| `6` | Collocation |

**Response**
```json
{
  "data": [
    {
      "questionId": "3f2a...",
      "vocabId":    "vocab-id-1",
      "type":       0,
      "word":       "ephemeral",
      "displayedMeaning": null,
      "options": ["long-lasting", "lasting a short time", "recurring", "abstract"]
    },
    {
      "questionId": "9c1b...",
      "vocabId":    "vocab-id-2",
      "type":       1,
      "word":       "ubiquitous",
      "displayedMeaning": "found everywhere",
      "options": null
    },
    {
      "questionId": "a4d7...",
      "vocabId":    "vocab-id-3",
      "type":       2,
      "word":       "serene",
      "displayedMeaning": null,
      "options": null
    }
  ]
}
```

**Question types**

| `type` | Name | What to show | What user inputs |
|--------|------|-------------|-----------------|
| `0` | MultipleChoice | Word + `options` (4 choices) | One of the option strings |
| `1` | TrueFalse | Word + `displayedMeaning` — "Is this correct?" | `"true"` or `"false"` |
| `2` | Written | Word only | Type the meaning |

**Rules the FE must follow**
- Save the full question list in local state after receiving it — you need `questionId`, `vocabId`, `type`, and `displayedMeaning` when submitting.
- Never show `options` for TrueFalse, never show `displayedMeaning` for MultipleChoice or Written.
- `options` is already shuffled by the server — render as-is.
- Correct answer is never sent to the client.

---

## Step 2 — Grade answers

Send all answers in one request after the user finishes.

```
POST /api/vocab/exam/grade
```

**Request**
```json
{
  "answers": [
    {
      "questionId":       "3f2a...",
      "vocabId":          "vocab-id-1",
      "type":             0,
      "displayedMeaning": null,
      "answer":           "lasting a short time"
    },
    {
      "questionId":       "9c1b...",
      "vocabId":          "vocab-id-2",
      "type":             1,
      "displayedMeaning": "found everywhere",
      "answer":           "true"
    },
    {
      "questionId":       "a4d7...",
      "vocabId":          "vocab-id-3",
      "type":             2,
      "displayedMeaning": null,
      "answer":           "calm and peaceful"
    }
  ]
}
```

**Grading rules**

| Type | Graded as |
|------|-----------|
| MultipleChoice | `answer` == vocab meaning (case-insensitive, trimmed) |
| Written | `answer` == vocab meaning (case-insensitive, trimmed) |
| TrueFalse | check if `displayedMeaning` == vocab meaning → derive expected `"true"`/`"false"` → compare to `answer` |

> FE must echo back `displayedMeaning` exactly as received for TrueFalse questions — the server uses it to re-derive the correct answer.

**Response**
```json
{
  "data": {
    "totalQuestions": 10,
    "correctCount":   7,
    "score":          70.0,
    "results": [
      {
        "questionId":      "3f2a...",
        "word":            "ephemeral",
        "correctAnswer":   "lasting a short time",
        "submittedAnswer": "lasting a short time",
        "isCorrect":       true
      },
      {
        "questionId":      "9c1b...",
        "word":            "ubiquitous",
        "correctAnswer":   "true",
        "submittedAnswer": "false",
        "isCorrect":       false
      }
    ]
  }
}
```

---

## FE state shape (suggested)

```ts
type QuestionType = 0 | 1 | 2;

interface ExamQuestion {
  questionId: string;
  vocabId: string;
  type: QuestionType;
  word: string;
  displayedMeaning: string | null;  // TrueFalse only
  options: string[] | null;          // MultipleChoice only
}

interface UserAnswer {
  questionId: string;
  vocabId: string;
  type: QuestionType;
  displayedMeaning: string | null;
  answer: string;                    // "" until user answers
}

interface ExamResult {
  totalQuestions: number;
  correctCount: number;
  score: number;
  results: AnswerResult[];
}

interface AnswerResult {
  questionId: string;
  word: string;
  correctAnswer: string;
  submittedAnswer: string;
  isCorrect: boolean;
}
```

---

## Screen states

```
idle  →  [Start Exam]
       ↓
loading (generate)
       ↓
in-progress  (render questions, collect answers)
       ↓
submitting (grade)
       ↓
result  (show score + per-question breakdown)
       ↓
[Restart]  →  idle
```

---

## Validation the FE should enforce before submitting

- `questionCount` must be ≥ 1.
- `wordType` must be one of `0–6` if provided.
- `from` must be before `to` if both are provided.
- All questions must have a non-empty `answer` before calling `/grade`.
- For TrueFalse, answer must be exactly `"true"` or `"false"` (lowercase).
