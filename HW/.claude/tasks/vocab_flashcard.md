# Feature: Vocab Flashcard (Quizlet-style Practice)

Stateful flashcard session — fetch a shuffled deck, flip through cards (front = word, back = meaning), mark each as "Know" or "Don't Know", then submit the session in one shot to advance review stages.

---

## Flow

```
[Config screen]  →  GET /api/vocab/flashcards  →  [Flip through deck]  →  POST /api/vocab/flashcards/submit  →  [Summary]
```

---

## Step 1 — Get flashcard deck

```
GET /api/vocab/flashcards
```

**Query params**

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `count` | `number \| null` | all | Max cards to return. Omit for the full filtered set |
| `wordType` | `number \| null` | all | Filter by part of speech (0–6, see table below) |
| `reviewStage` | `number \| null` | all | Filter by stage (0–3, see table below) |
| `useDaily` | `boolean` | `false` | When `true`, only returns words due for review today (same pool as `/daily`) |

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

**ReviewStage values**

| Value | Label |
|-------|-------|
| `0` | New |
| `1` | Reviewed (day 3) |
| `2` | Reinforced (day 7) |
| `3` | Mastered (completed) |

**Example — daily deck, max 20 cards**
```bash
curl "http://localhost:5000/api/vocab/flashcards?useDaily=true&count=20"
```

**Example — only nouns, any stage**
```bash
curl "http://localhost:5000/api/vocab/flashcards?wordType=1"
```

**Response**
```json
{
  "data": [
    {
      "vocabId":     "abc123",
      "word":        "ephemeral",
      "meaning":     "lasting for a very short time",
      "example":     "The ephemeral nature of fashion.",
      "note":        null,
      "wordType":    0,
      "reviewStage": 0
    },
    {
      "vocabId":     "def456",
      "word":        "ubiquitous",
      "meaning":     "present everywhere",
      "example":     null,
      "note":        "adj",
      "wordType":    0,
      "reviewStage": 1
    }
  ]
}
```

**Rules the FE must follow**
- Deck is already shuffled by the server — render as-is, do not re-sort.
- `meaning`, `example`, `note` may all be `null` — guard against empty-back cards in the UI.
- Save `vocabId` for each card in local state — required when submitting results.
- `reviewStage: 3` means the word is already Mastered; submitting `knew: true` for it is a no-op on the server, but you may want to visually mark it as already completed.

---

## Step 2 — Submit session results

Send all card results in one request after the user finishes flipping through the deck.

```
POST /api/vocab/flashcards/submit
```

**Request**
```json
{
  "results": [
    { "vocabId": "abc123", "knew": true  },
    { "vocabId": "def456", "knew": false },
    { "vocabId": "ghi789", "knew": true  }
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `vocabId` | `string` | ID from the flashcard received in step 1 |
| `knew` | `boolean` | `true` = user knew it → server advances review stage. `false` = no change |

**Response**
```json
{
  "data": {
    "totalCards":      10,
    "knewCount":       7,
    "didntKnowCount":  3,
    "advancedVocabIds": ["abc123", "ghi789", "..."]
  }
}
```

| Field | Description |
|-------|-------------|
| `totalCards` | Total cards in the submitted session |
| `knewCount` | Cards marked `knew: true` |
| `didntKnowCount` | Cards marked `knew: false` |
| `advancedVocabIds` | IDs whose review stage was actually incremented (excludes already-Mastered ones) |

---

## FE state shape (suggested)

```ts
interface FlashCard {
  vocabId: string;
  word: string;
  meaning: string | null;
  example: string | null;
  note: string | null;
  wordType: number | null;
  reviewStage: number;
}

interface CardResult {
  vocabId: string;
  knew: boolean;
}

interface SessionResult {
  totalCards: number;
  knewCount: number;
  didntKnowCount: number;
  advancedVocabIds: string[];
}

// Local session state
interface FlashCardSession {
  deck: FlashCard[];           // received from GET /flashcards
  currentIndex: number;        // 0-based pointer into deck
  isFlipped: boolean;          // false = showing word (front), true = showing meaning (back)
  results: CardResult[];       // accumulated as user progresses
}
```

---

## Screen states

```
config  →  [Start]
         ↓
loading  (GET /flashcards)
         ↓
card-front  (show word)
         ↓ [Flip]
card-back   (show meaning + example + note)
         ↓ [Know]  or  [Don't Know]
         → next card  (repeat until deck exhausted)
         ↓
submitting  (POST /flashcards/submit)
         ↓
summary  (show knewCount / totalCards, list of didn't-know words)
         ↓
[Restart]  →  config
```

---

## Config screen options (maps to query params)

| UI control | Param sent |
|-----------|------------|
| "Use today's review words" toggle | `useDaily=true` |
| Word type dropdown | `wordType=<value>` |
| Review stage filter | `reviewStage=<value>` |
| Card count input | `count=<value>` |

All fields are optional. When `useDaily` is on, disable the `reviewStage` filter in the UI (they target different pools).

---

## Validation the FE should enforce

- `count` must be ≥ 1 if provided.
- `wordType` must be one of `0–6` if provided.
- `reviewStage` must be one of `0–3` if provided.
- Do not submit if the deck returned 0 cards — show an empty-state message instead.
- Every card in the deck must have a result (`knew: true | false`) before calling `/submit` — no skipping.

---

## Summary screen suggestions

- Show score as `knewCount / totalCards` (e.g. "7 / 10").
- List the "Don't Know" cards (filter `results` where `knew: false`, look up word from local deck) so the user can review them.
- "Study again" button → re-fetch same config.
- "Study only missed" button → re-fetch with the missed `vocabId`s (not directly supported by the API — load a fresh deck filtered to the same `wordType`/`reviewStage` and let the server shuffle).
