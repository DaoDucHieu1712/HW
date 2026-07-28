# TakeNote — Frontend Integration Guide

> Base URL: `http://localhost:<port>/api`  
> All responses are wrapped in `ApiResponse<T>`.

---

## TypeScript Types

```ts
// --- Envelope ---
interface ApiResponse<T> {
  success: boolean;
  statusCode: number;
  message: string;
  errors?: string[];
  data: T;
  timestamp: string; // ISO 8601
}

// --- Pagination ---
interface PagedResult<T> {
  items: T[];
  pageIndex: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

// --- Folder ---
interface FolderResponseDto {
  id: string;
  name: string;
  parentId: string | null;   // null = root folder
  icon: string | null;        // e.g. emoji "📁"
  createdAt: string | null;   // ISO 8601
  updatedAt: string | null;
  children: FolderResponseDto[]; // recursive; empty array on GetById
}

interface CreateFolderRequestDto {
  name: string;               // required, max 200
  parentId?: string | null;   // null or omit = root
  icon?: string | null;
}

interface UpdateFolderRequestDto {
  name?: string | null;       // only sent fields are updated
  icon?: string | null;
}

interface MoveFolderRequestDto {
  newParentId: string | null; // null = move to root
}

// --- Note ---
interface NoteResponseDto {
  id: string;
  title: string | null;
  content: string | null;   // JSON-encoded block array (see Block Format)
  folderId: string | null;  // null = root-level note
  createdAt: string | null;
  updatedAt: string | null;
}

interface CreateNoteRequestDto {
  title?: string | null;    // max 500
  content?: string | null;  // JSON block array as string
  folderId?: string | null; // null or omit = root
}

interface UpdateNoteRequestDto {
  title?: string | null;    // only sent fields are updated
  content?: string | null;
}

interface MoveNoteRequestDto {
  folderId: string | null;  // null = move to root
}

// --- Trash ---
interface TrashedFolderDto {
  id: string;
  name: string;
  parentId: string | null;
  updatedAt: string | null;
}

interface TrashedNoteDto {
  id: string;
  title: string | null;
  folderId: string | null;
  updatedAt: string | null;
}

interface TrashedItemsResponseDto {
  folders: TrashedFolderDto[];
  notes: TrashedNoteDto[];
}
```

---

## Block Content Format

`content` is stored as a **serialized JSON string**. The backend is agnostic to its structure — parsing and rendering is entirely the frontend's responsibility.

Recommended schema:

```ts
type BlockType =
  | 'heading1' | 'heading2' | 'heading3'
  | 'paragraph'
  | 'bulletList' | 'numberedList' | 'listItem'
  | 'todo'
  | 'image'
  | 'divider'
  | 'code';

interface Block {
  id: string;         // uuid, stable across edits
  type: BlockType;
  content?: string;   // text content for text blocks
  checked?: boolean;  // for todo blocks
  url?: string;       // for image blocks
  language?: string;  // for code blocks
}

// Example note content:
const blocks: Block[] = [
  { id: "a1b2", type: "heading1", content: "Meeting Notes" },
  { id: "c3d4", type: "paragraph", content: "Discussed Q3 roadmap..." },
  { id: "e5f6", type: "todo", content: "Follow up with design team", checked: false },
  { id: "g7h8", type: "image", url: "https://..." },
];

// When saving:
const requestBody: UpdateNoteRequestDto = {
  content: JSON.stringify(blocks),
};

// When reading:
const blocks: Block[] = JSON.parse(note.content ?? '[]');
```

---

## Folder API

### GET `/api/folder` — Get full folder tree

Returns a nested tree from the root. Use this to render the sidebar.

**Response:** `ApiResponse<FolderResponseDto[]>`

```json
{
  "success": true,
  "statusCode": 200,
  "data": [
    {
      "id": "abc123",
      "name": "Work",
      "parentId": null,
      "icon": "💼",
      "createdAt": "2026-06-19T10:00:00Z",
      "updatedAt": "2026-06-19T10:00:00Z",
      "children": [
        {
          "id": "def456",
          "name": "Projects",
          "parentId": "abc123",
          "icon": null,
          "createdAt": "2026-06-19T11:00:00Z",
          "updatedAt": "2026-06-19T11:00:00Z",
          "children": []
        }
      ]
    }
  ]
}
```

---

### GET `/api/folder/{id}` — Get single folder

Returns the folder without `children` populated (empty array).

**Response:** `ApiResponse<FolderResponseDto>`

---

### POST `/api/folder` — Create folder

**Request body:** `CreateFolderRequestDto`

```json
{ "name": "Personal", "parentId": null, "icon": "🏠" }
```

**Response:** `204 No Content`

---

### PUT `/api/folder/{id}` — Rename / change icon

Only the fields you send will be updated (partial update). Send `null` to explicitly clear a field.

**Request body:** `UpdateFolderRequestDto`

```json
{ "name": "Personal Notes", "icon": "📓" }
```

**Response:** `204 No Content`

---

### DELETE `/api/folder/{id}` — Move folder to Trash

**Cascade:** the folder, all sub-folders (any depth), and all notes inside them are moved to Trash.

**Response:** `204 No Content`

---

### PATCH `/api/folder/{id}/move` — Move folder

Changes the folder's parent. To move to root, send `newParentId: null`.

**Request body:** `MoveFolderRequestDto`

```json
{ "newParentId": "abc123" }
```

**Response:** `204 No Content`

---

### POST `/api/folder/{id}/restore` — Restore folder from Trash

Restores only the folder itself (not its contents — restore notes/sub-folders individually).

**Edge case:** if the original parent folder is still in Trash, the folder is silently re-parented to root (`parentId = null`). Show a toast like _"Restored to root because its parent folder is still in Trash."_

**Response:** `204 No Content`

---

## Note API

### GET `/api/note` — List notes (paged)

Omit `folderId` to list root-level notes.

**Query params:**

| Param       | Type   | Default | Description                        |
|-------------|--------|---------|------------------------------------|
| `folderId`  | string | —       | Filter by folder; omit for root    |
| `search`    | string | —       | Searches by title (case-insensitive)|
| `pageIndex` | int    | 1       |                                    |
| `pageSize`  | int    | 20      |                                    |

**Response:** `ApiResponse<PagedResult<NoteResponseDto>>`

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "id": "note001",
        "title": "Q3 Roadmap",
        "content": "[{\"id\":\"a1\",\"type\":\"paragraph\",\"content\":\"...\"}]",
        "folderId": "def456",
        "createdAt": "2026-06-19T12:00:00Z",
        "updatedAt": "2026-06-19T12:00:00Z"
      }
    ],
    "pageIndex": 1,
    "pageSize": 20,
    "totalCount": 1,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPreviousPage": false
  }
}
```

---

### GET `/api/note/trash` — Get all Trash items

Returns all soft-deleted folders and notes.

**Response:** `ApiResponse<TrashedItemsResponseDto>`

```json
{
  "success": true,
  "statusCode": 200,
  "data": {
    "folders": [
      { "id": "f1", "name": "Old Project", "parentId": null, "updatedAt": "2026-06-18T09:00:00Z" }
    ],
    "notes": [
      { "id": "n1", "title": "Draft", "folderId": null, "updatedAt": "2026-06-17T14:00:00Z" }
    ]
  }
}
```

---

### GET `/api/note/{id}` — Get single note

**Response:** `ApiResponse<NoteResponseDto>`

---

### POST `/api/note` — Create note

**Request body:** `CreateNoteRequestDto`

```json
{ "title": "Untitled", "content": null, "folderId": "def456" }
```

**Response:** `204 No Content`

> Tip: immediately call `GET /api/note?folderId=...` after creation to get the new note's id, or optimistically insert with a temporary id.

---

### PUT `/api/note/{id}` — Update note content / title

Only sent fields are updated. To auto-save while typing, debounce this call (e.g. 500 ms after last keystroke).

**Request body:** `UpdateNoteRequestDto`

```json
{
  "title": "Q3 Roadmap",
  "content": "[{\"id\":\"a1\",\"type\":\"heading1\",\"content\":\"Q3 Roadmap\"}]"
}
```

**Response:** `204 No Content`

---

### DELETE `/api/note/{id}` — Move note to Trash

**Response:** `204 No Content`

---

### PATCH `/api/note/{id}/move` — Move note to another folder

Send `folderId: null` to move to root.

**Request body:** `MoveNoteRequestDto`

```json
{ "folderId": "abc123" }
```

**Response:** `204 No Content`

---

### POST `/api/note/{id}/restore` — Restore note from Trash

**Edge case:** if the note's original folder is still in Trash, the note is silently moved to root (`folderId = null`). Show a toast like _"Restored to root because its folder is still in Trash."_

**Response:** `204 No Content`

---

## Error Handling

All errors follow the same envelope:

```json
{
  "success": false,
  "statusCode": 404,
  "message": "Folder with id 'abc' was not found.",
  "errors": null,
  "data": null
}
```

Validation errors use status `422` and include an `errors` array:

```json
{
  "success": false,
  "statusCode": 422,
  "message": "Validation failed",
  "errors": ["Name must not be empty.", "Name must not exceed 200 characters."]
}
```

| Status | Meaning                                          |
|--------|--------------------------------------------------|
| 204    | Mutation succeeded, no body                      |
| 400    | Bad request / domain rule violation              |
| 404    | Folder or Note not found                         |
| 422    | Validation error (field-level)                   |
| 500    | Unexpected server error                          |

---

## Key Business Rules

| Rule | Detail |
|------|--------|
| Tree depth | Unlimited nesting; tree is always returned fully hydrated by `GET /api/folder` |
| Root items | Notes/folders with `folderId = null` / `parentId = null` are root-level |
| Partial update | `PUT` endpoints only update non-null fields; send only what changed |
| Cascade Trash | `DELETE /api/folder/{id}` moves the folder + all descendants + all their notes to Trash |
| Restore — parent in Trash | Restored item is re-parented to root; UI should notify the user |
| Trash scope | `GET /api/note/trash` returns **all** soft-deleted folders and notes regardless of depth |
| Content format | Backend stores `content` as-is (opaque string); block parsing is 100% frontend |
| Auto-save | Debounce `PUT /api/note/{id}` — no separate "save" step required |

---

## Suggested UI Flows

### Create a note in a folder
1. `POST /api/note` with `{ folderId, title: "Untitled" }`
2. Navigate to the new note (call `GET /api/note?folderId=...` to get the ID, or use a server-returned Location header if available)
3. User types → debounced `PUT /api/note/{id}` with updated `content`

### Rename a folder
1. User edits the name inline
2. On blur / Enter: `PUT /api/folder/{id}` with `{ name: "New Name" }`

### Drag-and-drop reorder / nesting
- Move note into folder: `PATCH /api/note/{id}/move` with `{ folderId: targetId }`
- Move folder under another: `PATCH /api/folder/{id}/move` with `{ newParentId: targetId }`
- Move to root: send `folderId: null` / `newParentId: null`

### Delete folder with confirmation
1. Show confirm dialog: _"Move 'Work' and all its contents to Trash?"_
2. On confirm: `DELETE /api/folder/{id}`
3. Refresh sidebar tree via `GET /api/folder`

### Trash view
1. `GET /api/note/trash` → display folders and notes grouped
2. Restore item: `POST /api/folder/{id}/restore` or `POST /api/note/{id}/restore`
3. On success, re-fetch the tree and trash list

---

## Recommended Fetch Wrapper (TypeScript)

```ts
const BASE = '/api';

async function api<T>(
  method: string,
  path: string,
  body?: unknown
): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: body != null ? JSON.stringify(body) : undefined,
  });

  if (res.status === 204) return undefined as T;

  const json: ApiResponse<T> = await res.json();
  if (!json.success) throw new Error(json.message);
  return json.data as T;
}

// Usage examples:
const tree   = await api<FolderResponseDto[]>('GET', '/folder');
const notes  = await api<PagedResult<NoteResponseDto>>('GET', '/note?folderId=abc&pageIndex=1&pageSize=20');
const trash  = await api<TrashedItemsResponseDto>('GET', '/note/trash');

await api('POST',  '/folder',          { name: 'Work', parentId: null });
await api('PUT',   `/folder/${id}`,    { name: 'New Name' });
await api('DELETE',`/folder/${id}`);
await api('PATCH', `/folder/${id}/move`, { newParentId: 'xyz' });
await api('POST',  `/folder/${id}/restore`);

await api('POST',  '/note',             { title: 'Untitled', folderId: 'abc' });
await api('PUT',   `/note/${id}`,       { title: 'Q3', content: JSON.stringify(blocks) });
await api('DELETE',`/note/${id}`);
await api('PATCH', `/note/${id}/move`,  { folderId: 'xyz' });
await api('POST',  `/note/${id}/restore`);
```


-------------------------------
SRS : 
# Software Requirements Specification (SRS)

## TakeNote — Web-Based Note-Taking Application with Tree-Structured Folders

**Version:** 1.0
**Date:** June 19, 2026
**Status:** Draft

---

## 1. Introduction

### 1.1 Purpose

This document specifies the functional and non-functional requirements for **TakeNote**, a web-based, single-user note-taking application inspired by Notion. TakeNote's defining characteristic is a **hierarchical tree-structured folder system** that allows users to organize notes into nested folders and sub-folders of unlimited depth, alongside a rich block-based editor.

This SRS is intended for product managers, designers, frontend/backend engineers, and QA engineers involved in the design, development, and testing of TakeNote.

### 1.2 Scope

TakeNote is a **web application** (browser-based, no desktop or mobile native clients in this version) that enables a single user to:

- Create, organize, and manage notes within a tree-structured folder hierarchy (unlimited nesting).
- Write and format rich content using a block-based editor (text, headings, lists, checkboxes, code, images, tables, embeds).
- Search, tag, and filter notes.
- Move, duplicate, and reorganize notes/folders via drag-and-drop within the tree.
- Export and import notes.
- Access the application from any modern web browser with data persisted to a backend server/database.

**Out of scope for v1.0:**
- Real-time multi-user collaboration (co-editing, live cursors, comments between users).
- Native desktop or mobile applications.
- Team/workspace permission management (multi-user roles).
- Public page publishing (e.g., "share to web" Notion-style public pages).

### 1.3 Intended Audience

| Audience | Use of this document |
|---|---|
| Product Manager | Validate scope and prioritize features |
| UI/UX Designer | Derive wireframes and interaction flows |
| Frontend Engineers | Implement UI, tree component, editor |
| Backend Engineers | Implement API, data model, persistence |
| QA Engineers | Derive test cases and acceptance criteria |

### 1.4 Definitions, Acronyms, and Abbreviations

| Term | Definition |
|---|---|
| SRS | Software Requirements Specification |
| Node | A single entry in the tree structure — either a Folder or a Note |
| Folder Node | A tree node that can contain child nodes (folders or notes) |
| Note Node | A tree node (leaf or container) that holds editable content |
| Root | The top-level invisible container of the tree, owned by the user |
| Block | A discrete content unit in the editor (paragraph, heading, list item, image, etc.) |
| Drag-and-Drop (DnD) | User interaction for moving nodes within the tree by dragging |
| Trash | Soft-delete holding area for removed nodes |
| Breadcrumb | UI element showing the path from root to the currently open note |

### 1.5 References

- Notion Help Center — Page hierarchy and sidebar behavior (conceptual reference only; no proprietary material reproduced)
- RFC 2119 — Key words for use in RFCs to Indicate Requirement Levels (MUST/SHOULD/MAY)

### 1.6 Document Conventions

This SRS uses **MUST**, **SHOULD**, and **MAY** per RFC 2119 conventions to indicate requirement priority. Functional requirements are uniquely numbered as `FR-<Module>-<Number>` for traceability.

---

## 2. Overall Description

### 2.1 Product Perspective

TakeNote is a new, standalone, single-user web product. It is not an add-on or extension to an existing system. It follows a client-server architecture:

- **Frontend:** Single-Page Application (SPA) running in the browser.
- **Backend:** REST/GraphQL API server handling authentication, business logic, and persistence.
- **Database:** Persistent storage for the tree structure, note content, and metadata.

### 2.2 Product Functions (Summary)

1. User authentication (sign up, log in, log out, password reset).
2. Tree-structured folder/note hierarchy management (create, rename, move, delete, restore).
3. Rich block-based note editor with autosave.
4. Drag-and-drop reorganization of the tree.
5. Full-text search across notes and folder names.
6. Tagging and filtering of notes.
7. Trash (soft delete) and permanent delete.
8. Import/export of notes (Markdown, HTML, PDF).
9. Favorites/pinned shortcuts.
10. Basic note sharing via a read-only link (no live collaboration).

### 2.3 User Characteristics

TakeNote targets individual knowledge workers, students, and personal-productivity users who are comfortable with standard web applications. No specialized technical knowledge is required. The product assumes a single account per user; no team/role concepts exist in this version.

### 2.4 Operating Environment

- **Client:** Latest two stable versions of Chrome, Firefox, Safari, and Edge. Minimum supported screen width: 1024px for full sidebar+editor layout; responsive degradation down to 768px (sidebar collapses to overlay).
- **Server:** Cloud-hosted backend (platform-agnostic; containerized deployment assumed).
- **Connectivity:** Application requires an active internet connection. Offline editing is **not** required in v1.0 (see Section 2.6, Constraints).

### 2.5 Design and Implementation Constraints

- The folder/note hierarchy MUST be modeled as a tree data structure (each node has exactly one parent, except the root).
- The system MUST prevent circular references (a folder cannot become a descendant of itself).
- Maximum nesting depth: configurable, default limit of **20 levels**, to protect UI rendering and prevent abuse.
- The editor MUST persist content automatically (autosave) without requiring an explicit "Save" action.
- This version assumes single-user data isolation (no shared workspaces).

### 2.6 Assumptions and Dependencies

- Users have a stable internet connection while using the app (no offline-first requirement in v1.0).
- A relational or document database capable of efficient adjacency-list or nested-set tree queries is available.
- Browser supports modern JavaScript (ES2020+), localStorage is **not** used for persistent note data (server is source of truth) but MAY be used for transient UI state (e.g., last expanded folders, draft buffering before sync).

---

## 3. System Features and Functional Requirements

### 3.1 Module: Authentication & Account

| ID | Requirement |
|---|---|
| FR-AUTH-01 | The system MUST allow a new user to register using email and password. |
| FR-AUTH-02 | The system MUST allow a registered user to log in and log out. |
| FR-AUTH-03 | The system MUST allow a user to reset a forgotten password via email verification. |
| FR-AUTH-04 | The system MUST enforce password strength rules (minimum 8 characters, at least one number). |
| FR-AUTH-05 | The system SHOULD support OAuth login (Google) as an alternative to email/password. |
| FR-AUTH-06 | The system MUST keep a user's session active via secure token (e.g., JWT) with expiry and refresh handling. |

### 3.2 Module: Tree-Structured Folder & Note Hierarchy (Core Feature)

This is the defining module of TakeNote. The hierarchy is modeled as a **tree**: a single root per user, where every node (folder or note) has exactly one parent and zero or more children.

#### 3.2.1 Tree Data Model Requirements

| ID | Requirement |
|---|---|
| FR-TREE-01 | The system MUST represent each user's workspace as a single rooted tree. |
| FR-TREE-02 | Each node in the tree MUST be one of two types: **Folder** (container, may hold child folders and notes) or **Note** (content document; MAY also contain child notes/folders, mirroring Notion's nested-page behavior). |
| FR-TREE-03 | Each node MUST store: unique ID, parent ID (nullable only for root), node type, title, ordering index among siblings, created timestamp, updated timestamp, and soft-delete flag. |
| FR-TREE-04 | The system MUST prevent a node from being assigned as a descendant of itself (no cycles). |
| FR-TREE-05 | The system MUST enforce a configurable maximum tree depth (default: 20 levels) and reject operations that would exceed it, with a clear error message. |
| FR-TREE-06 | Sibling nodes under the same parent MUST maintain a stable, user-controllable display order. |
| FR-TREE-07 | The system MUST support efficient retrieval of: (a) direct children of a node, (b) the full ancestor path (breadcrumb) of a node, (c) the full subtree rooted at a node. |

#### 3.2.2 Tree UI / Sidebar Requirements

| ID | Requirement |
|---|---|
| FR-TREE-08 | The system MUST display the tree in a persistent left sidebar, with expand/collapse toggles (▸/▾) for any node containing children. |
| FR-TREE-09 | The system MUST visually indent child nodes to reflect their depth in the hierarchy. |
| FR-TREE-10 | The system MUST persist each user's expand/collapse state across sessions. |
| FR-TREE-11 | The system MUST allow inline creation of a new Folder or Note as a child of any selected node, via a "+" control or right-click context menu. |
| FR-TREE-12 | The system MUST allow inline renaming of any node directly in the tree (double-click or context menu → Rename). |
| FR-TREE-13 | The system MUST allow deleting a node (and its entire subtree) via context menu, moving it to Trash (soft delete). |
| FR-TREE-14 | The system MUST display a confirmation dialog before deleting a node that contains children, stating the number of descendant items affected. |
| FR-TREE-15 | The system MUST show a context menu (right-click) on each node offering: New Sub-page, Rename, Duplicate, Move To, Delete, Copy Link, Add to Favorites. |
| FR-TREE-16 | The system SHOULD show node icons (folder icon vs. document icon) and MAY allow custom emoji icons per node, consistent with Notion-style UX. |

#### 3.2.3 Drag-and-Drop Reorganization

| ID | Requirement |
|---|---|
| FR-TREE-17 | The system MUST allow users to drag a node and drop it onto another node to reparent it (make it a child of the drop target). |
| FR-TREE-18 | The system MUST allow users to drag a node between two siblings to reorder it without changing its parent. |
| FR-TREE-19 | The system MUST provide a visual drop indicator (line or highlighted target) during drag operations, distinguishing "reorder" vs. "nest inside" drop zones. |
| FR-TREE-20 | The system MUST reject (and visually indicate rejection of) a drop operation that would create a cycle or exceed max depth. |
| FR-TREE-21 | The system MUST update the moved node's parent, ordering index, and all affected siblings' ordering atomically (single transaction). |
| FR-TREE-22 | The system SHOULD auto-scroll the sidebar when dragging a node near the top/bottom edge of the visible tree area. |
| FR-TREE-23 | As an alternative to drag-and-drop, the system MUST provide a "Move To" dialog with a searchable tree picker, for accessibility and precision moves. |

#### 3.2.4 Move, Duplicate, Trash

| ID | Requirement |
|---|---|
| FR-TREE-24 | The system MUST support duplicating a node; duplication MUST deep-copy the entire subtree (all descendant folders/notes) with new unique IDs. |
| FR-TREE-25 | The system MUST move deleted nodes (and subtrees) into a Trash view rather than permanently deleting them immediately. |
| FR-TREE-26 | The system MUST allow restoring a node from Trash back to its original parent location, or to a new location if the original parent no longer exists. |
| FR-TREE-27 | The system MUST allow permanent deletion of a node from Trash, with an explicit "this cannot be undone" confirmation. |
| FR-TREE-28 | The system SHOULD automatically purge Trash items older than 30 days (configurable). |

### 3.3 Module: Note Editor

| ID | Requirement |
|---|---|
| FR-EDIT-01 | The system MUST provide a block-based rich text editor supporting: paragraphs, headings (H1–H3), bulleted lists, numbered lists, to-do/checkbox lists, blockquotes, code blocks (with syntax highlighting), dividers, and images. |
| FR-EDIT-02 | The system MUST support basic inline formatting: bold, italic, underline, strikethrough, inline code, and hyperlinks. |
| FR-EDIT-03 | The system MUST support a slash ("/") command menu to insert any block type at the cursor position. |
| FR-EDIT-04 | The system MUST support drag-handle reordering of blocks within a single note. |
| FR-EDIT-05 | The system MUST autosave note content continuously (debounced, e.g., every 1–2 seconds of inactivity) without requiring manual save. |
| FR-EDIT-06 | The system MUST display a save-status indicator ("Saving…" / "Saved"). |
| FR-EDIT-07 | The system SHOULD support embedding a table block with editable rows/columns. |
| FR-EDIT-08 | The system SHOULD support nesting a note as a sub-page link inside another note's content (Notion-style "page mention"), which also creates a corresponding tree relationship. |
| FR-EDIT-09 | The system MUST support image upload (drag-drop or file picker) into the note body. |
| FR-EDIT-10 | The system MAY support basic version history (last N autosave snapshots) for recovery. |

### 3.4 Module: Navigation, Search & Discovery

| ID | Requirement |
|---|---|
| FR-NAV-01 | The system MUST provide a breadcrumb trail at the top of an open note, showing the full ancestor path from root to the current note, with each segment clickable for navigation. |
| FR-NAV-02 | The system MUST provide a global search (keyboard shortcut, e.g., Ctrl/Cmd+K) that searches note titles and body content. |
| FR-NAV-03 | Search results MUST display the matching node's tree path (breadcrumb) alongside the match snippet. |
| FR-NAV-04 | The system MUST allow filtering search results by node type (Folder/Note) and by tag. |
| FR-NAV-05 | The system MUST support a "Favorites"/"Pinned" section in the sidebar, independent of tree position, listing user-starred nodes for quick access. |
| FR-NAV-06 | The system SHOULD maintain a "Recently Viewed" list of the last N opened notes. |

### 3.5 Module: Tagging & Metadata

| ID | Requirement |
|---|---|
| FR-TAG-01 | The system MUST allow users to add one or more free-text tags to any note. |
| FR-TAG-02 | The system MUST allow filtering/browsing notes by tag from a dedicated tags view. |
| FR-TAG-03 | The system SHOULD auto-suggest existing tags when typing a new tag (autocomplete). |

### 3.6 Module: Import / Export

| ID | Requirement |
|---|---|
| FR-IO-01 | The system MUST allow exporting a single note as Markdown (.md). |
| FR-IO-02 | The system MUST allow exporting a single note as PDF. |
| FR-IO-03 | The system SHOULD allow exporting an entire folder subtree as a zipped folder structure of Markdown files, preserving the tree hierarchy as nested directories. |
| FR-IO-04 | The system SHOULD allow importing Markdown files (single file or zipped folder) and reconstructing the corresponding tree structure. |

### 3.7 Module: Sharing (Non-Collaborative)

| ID | Requirement |
|---|---|
| FR-SHARE-01 | The system MUST allow generating a read-only, unguessable share link for a single note. |
| FR-SHARE-02 | The system MUST allow revoking a previously generated share link. |
| FR-SHARE-03 | Shared read-only views MUST NOT expose the rest of the user's tree structure — only the shared note's content (and its nested sub-pages, if the user opts to include them). |

---

## 4. External Interface Requirements

### 4.1 User Interface

- **Layout:** Two/three-pane layout — (1) collapsible tree sidebar, (2) main editor pane, (3) optional right-side metadata panel (tags, last edited, etc.).
- **Tree Sidebar:** Persistent, scrollable, with indent-based hierarchy, expand/collapse carets, hover-revealed action icons (add child, more options).
- **Keyboard Shortcuts:** MUST support at minimum: global search (Ctrl/Cmd+K), new note (Ctrl/Cmd+N), bold/italic/underline standard shortcuts.
- **Responsive Behavior:** Sidebar MUST collapse into an overlay/drawer below 768px viewport width.

### 4.2 API Interface

- The backend MUST expose a documented REST or GraphQL API for: authentication, CRUD on nodes (folders/notes), tree move/reorder operations, content read/write, search, tags, trash, and sharing.
- All tree-mutating endpoints (move, delete, reparent) MUST be atomic and return the updated subtree state.

### 4.3 Hardware Interface

Not applicable (standard web application; no special hardware interfaces required).

### 4.4 Communications Interface

- All client-server communication MUST occur over HTTPS (TLS 1.2+).
- The system SHOULD use WebSocket or polling for autosave-status sync across multiple open tabs of the same user (so the same note isn't shown stale in another tab) — note: this is single-tab-consistency only, not multi-user real-time collaboration.

---

## 5. Non-Functional Requirements

### 5.1 Performance

| ID | Requirement |
|---|---|
| NFR-PERF-01 | The tree sidebar MUST render and remain responsive for workspaces containing up to 10,000 nodes. |
| NFR-PERF-02 | Expanding/collapsing a folder node MUST complete in under 200ms for up to 500 direct children. |
| NFR-PERF-03 | Note content MUST load (open from sidebar click) within 500ms under normal network conditions (p95). |
| NFR-PERF-04 | Drag-and-drop reorder/reparent operations MUST commit to the backend and reflect in the UI within 1 second (p95). |
| NFR-PERF-05 | Global search MUST return results within 1 second for workspaces up to 10,000 notes. |

### 5.2 Scalability

- The system SHOULD use an efficient tree storage strategy (e.g., adjacency list with materialized path, or nested set model) to avoid O(n) full-tree scans on common operations like "fetch breadcrumb" or "fetch children."
- The database schema MUST support horizontal scaling of read operations (e.g., via indexing on parent_id and materialized path columns).

### 5.3 Reliability & Availability

- NFR-REL-01: The system SHOULD target 99.5% uptime for the backend service.
- NFR-REL-02: Autosave MUST NOT lose more than the last 2 seconds of unsynced edits in the event of a network interruption; the client MUST queue and retry failed autosave requests.
- NFR-REL-03: Soft-deleted (Trashed) data MUST be recoverable for at least 30 days before permanent purge.

### 5.4 Security

- NFR-SEC-01: All passwords MUST be hashed (e.g., bcrypt/argon2) — never stored in plaintext.
- NFR-SEC-02: All API endpoints MUST enforce that a user can only access/mutate nodes within their own tree (strict ownership checks on every request).
- NFR-SEC-03: Share links MUST use cryptographically random, non-sequential tokens.
- NFR-SEC-04: The system MUST sanitize all rich-text/HTML content to prevent XSS injection via note bodies.

### 5.5 Usability

- NFR-USE-01: A first-time user MUST be able to create a folder, create a note inside it, and write content without consulting documentation (discoverable UI).
- NFR-USE-02: All destructive actions (delete, permanent delete) MUST require explicit confirmation.
- NFR-USE-03: The system SHOULD provide undo (Ctrl/Cmd+Z) for the most recent tree operation (move, delete, rename) within the active session.

### 5.6 Maintainability

- NFR-MAINT-01: The tree manipulation logic (cycle detection, depth limits, reordering) SHOULD be isolated in a well-tested core module/service, decoupled from UI rendering, to allow safe future extension (e.g., adding multi-user permissions later).

### 5.7 Accessibility

- NFR-ACC-01: The tree sidebar MUST be fully keyboard-navigable (arrow keys to move between nodes, Enter to open, Space to toggle expand/collapse).
- NFR-ACC-02: All interactive elements MUST have appropriate ARIA roles/labels for screen reader compatibility (tree, treeitem, group roles per WAI-ARIA Tree View Pattern).

---

## 6. Data Model (High-Level)

### 6.1 Node Entity

| Field | Type | Description |
|---|---|---|
| id | UUID | Unique identifier |
| user_id | UUID | Owner reference |
| parent_id | UUID (nullable) | Parent node reference; null only for root |
| type | Enum(folder, note) | Node type |
| title | String | Display title |
| icon | String (nullable) | Emoji or icon identifier |
| order_index | Integer | Sibling ordering position |
| content | JSON/Document | Block-based content (notes only) |
| tags | Array[String] | Associated tags |
| is_favorite | Boolean | Favorite/pinned flag |
| is_deleted | Boolean | Soft-delete flag (Trash) |
| deleted_at | Timestamp (nullable) | Time moved to Trash |
| created_at | Timestamp | Creation time |
| updated_at | Timestamp | Last modification time |

### 6.2 Key Tree Invariants

1. Exactly one root node per user (or an implicit virtual root with `parent_id = null` representing top-level items).
2. `parent_id` of any node MUST reference an existing, non-deleted node (or null for root-level items) at write time.
3. No node may be its own ancestor (cycle prevention enforced at the API layer before commit).
4. `order_index` MUST be unique among siblings under the same `parent_id`.

---

## 7. Acceptance Criteria Summary (Sample — Core Tree Feature)

| Scenario | Expected Result |
|---|---|
| User creates a new folder at root, then a sub-folder inside it, then a note inside the sub-folder | Sidebar shows 3-level nested hierarchy with correct indentation; breadcrumb on the note reads Root path correctly |
| User drags a note from Folder A and drops it onto Folder B | Note becomes a child of Folder B; Folder A no longer lists it; operation persists after page refresh |
| User attempts to drag a parent folder into its own child folder | Operation is rejected; UI shows a "not allowed" indicator; no change persisted |
| User deletes a folder containing 5 notes | Confirmation dialog states "This will move 1 folder and 5 notes to Trash"; upon confirmation, all 6 items move to Trash and disappear from the main tree |
| User restores a note from Trash whose original parent folder was also deleted | Note is restored to root level (or nearest surviving ancestor) with a notice explaining the relocation |

---

## 8. Future Considerations (Out of Scope for v1.0)

- Real-time multi-user collaborative editing.
- Workspace-level sharing with role-based permissions (Owner/Editor/Viewer).
- Public web publishing of notes.
- Native mobile and desktop applications.
- Offline-first editing with conflict resolution/sync.
- AI-assisted writing features (summarization, autocomplete).

---

*End of Document*