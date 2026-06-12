# ──────────────────────────────────────────────────────────────
# Makefile — HW project shortcuts
# Usage: make <target> [VAR=value]
# ──────────────────────────────────────────────────────────────

.PHONY: help run dry feature review migrate build \
        task-show task-new task-done task-list

# ── defaults ───────────────────────────────────────────────────
ENTITY  ?= $(error ENTITY is required, e.g. make feature ENTITY=Product)
NAME    ?= $(error NAME is required,   e.g. make migrate NAME=AddProductTable)
TITLE   ?= $(error TITLE is required,  e.g. make task-new TITLE="Add Product")

# ── help ───────────────────────────────────────────────────────
help:
	@echo ""
	@echo "  make run                     Run full workflow on current task"
	@echo "  make dry                     Preview workflow (no files written)"
	@echo "  make feature ENTITY=Product  Generate vertical slice for an entity"
	@echo "  make review                  Review changed files against project rules"
	@echo "  make migrate NAME=AddProduct Create + apply EF Core migration"
	@echo "  make build                   Build the solution"
	@echo ""
	@echo "  make task-show               Show current task"
	@echo "  make task-new TITLE='...'    Create a new task"
	@echo "  make task-done               Archive current task as done"
	@echo "  make task-list               List backlog tasks"
	@echo ""

# ── workflow ───────────────────────────────────────────────────
run:
	@./task run

dry:
	@./task run --dry

feature:
	@./task feature $(ENTITY)

review:
	@./task review

# ── build ──────────────────────────────────────────────────────
build:
	dotnet build HW.slnx

# ── migration ──────────────────────────────────────────────────
migrate:
	@./task migrate $(NAME)

# ── task management ────────────────────────────────────────────
task-show:
	@./task show

task-new:
	@./task new "$(TITLE)"

task-done:
	@./task done

task-list:
	@./task list
