# Vocab Content Field — Frontend Task

## Context

The `Vocab` entity has been refactored: the separate structured fields
(`Meaning`, `Example`, `Note`, `WordType`) have been replaced by a single
`Content` field (`longtext`) that stores rich HTML from a Text Editor.

**Word** remains the primary key/display name (plain text).  
**Content** carries all dictionary-like detail: definition, example, word type,
notes — formatted freely by the user.

---

## BE API changes (already deployed)

### Vocab response shape
```jsonc
{
  "id": "...",
  "word": "ephemeral",
  "content": "<p><strong>[adj]</strong> Lasting for a very short time.</p><p><em>Example:</em> The beauty of cherry blossoms is ephemeral.</p>",
  "notedAt": "...",
  "reviewStage": 0,
  "nextReviewAt": "...",
  "lastReviewedAt": null,
  "isCompleted": false,
  "createdAt": "...",
  "createdBy": "...",
  "updatedAt": null,
  "updatedBy": null
}
```

### Create / Update payload
```jsonc
// POST /api/vocab
// PUT  /api/vocab/:id
{ "word": "ephemeral", "content": "<p>...</p>" }
```

### FlashCard shape
```jsonc
{ "vocabId": "...", "word": "...", "content": "<p>...</p>", "reviewStage": 0 }
```

### Paging filter (GET /api/vocab)
Removed: `wordType`  
Kept: `search` (matches word only), `fromDate`, `toDate`, `pageIndex`, `pageSize`

### Exam (POST /api/vocab/exam/generate)
Removed: `wordType`  
Kept: `questionCount`, `from`, `to`

Exam question DTO field renamed: `displayedMeaning` → `displayedContent`  
Exam answer DTO field renamed: `displayedMeaning` → `displayedContent`

---

## FE Tasks

### 1. Vocab list page

- Remove columns / filter chips for `Meaning`, `Example`, `Note`, `WordType`.
- Update paging API call — drop `wordType` param.
- In the list row / card, render `content` as HTML (use `dangerouslySetInnerHTML`
  or a safe HTML renderer). Truncate long content with a "Show more" toggle or
  tooltip.

### 2. Vocab create / edit form

Replace the separate input fields with a **rich text editor** for `Content`.

Recommended editors (pick one):
- **Tiptap** (React, headless, extensible) — suggested
- **Quill** via `react-quill`
- **TipTap Starter Kit** covers bold, italic, lists, headings — enough for
  dictionary-style entries

**Suggested Content structure the editor should encourage (not enforced by BE):**

```
[noun / verb / adj / adv / phrase]
Definition: ...
Example: ...
Notes: ...
Synonyms: ...
```

The editor is free-form; the above is just a starter template the FE can
pre-fill as placeholder text.

**Form fields:**
```
Word      — plain text input (required, max 200)
Content   — rich text editor (optional)
```

**API call on submit:**
```ts
await api.post('/vocab', { word, content: editor.getHTML() })
await api.put(`/vocab/${id}`, { word, content: editor.getHTML() })
```

### 3. Vocab detail page

Replace individual field display with the rich text renderer:
```tsx
<div dangerouslySetInnerHTML={{ __html: vocab.content ?? '' }} />
// or use a safe renderer like dompurify + dangerouslySetInnerHTML
```

Install DOMPurify for XSS protection if rendering raw HTML:
```sh
npm install dompurify @types/dompurify
```
```tsx
import DOMPurify from 'dompurify'
<div dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(vocab.content ?? '') }} />
```

### 4. Flashcard component

- Front of card: `word`
- Back of card: render `content` as HTML (sanitized)
- Remove any WordType badge/filter pill

### 5. Flashcard filter UI

Remove the `WordType` dropdown filter. The remaining filters are:
- `Count` (number of cards)
- `ReviewStage` (0 New / 1 Reviewed / 2 Reinforced)
- `UseDaily` (toggle)

### 6. Exam page

- Update generate exam payload — remove `wordType` field.
- Rename all references to `displayedMeaning` → `displayedContent` in
  exam question and answer DTOs.
- For TrueFalse questions: render `displayedContent` as HTML.
- For MultipleChoice options: render each option's HTML snippet as a choice.
  Consider stripping tags for short display (use DOMParser or a strip-tags
  helper) so options appear as plain text in the radio list.

### 7. Shared type updates (TypeScript)

Update `VocabResponseDto`, `FlashCardDto`, `VocabExamQuestionDto`,
`ExamAnswerDto` types to reflect the new shape (see BE changes above).

---

## Migration note (BE — one-time)

The running API process must be stopped before running:
```sh
dotnet ef migrations add add_vocab_content_remove_fields \
  --project HW.Infrastructure \
  --startup-project HW.Api

dotnet ef database update \
  --project HW.Infrastructure \
  --startup-project HW.Api
```

This drops `Meaning`, `Example`, `Note`, `WordType` columns and adds `Content longtext`.

---

## Done criteria

- [ ] Vocab list renders content as HTML, WordType filter removed
- [ ] Create/edit form uses a rich text editor for Content
- [ ] Detail page renders Content HTML safely
- [ ] Flashcard back shows Content HTML; WordType filter removed
- [ ] Exam correctly uses `displayedContent` field name
- [ ] TypeScript types updated across all vocab-related API calls
