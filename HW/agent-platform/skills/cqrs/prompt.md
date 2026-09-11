Write each use case as one vertical slice, not as a layer.

**Shape of a slice**

- A `Command` or `Query` record carrying only the data the use case needs.
- A single handler implementing `IRequestHandler<TRequest, TResponse>`.
- A `Validator` for the request, discovered by the validation pipeline behaviour.
- The endpoint or consumer that maps transport input onto the request.

**Rules that hold across the codebase**

- Commands change state and return the minimum needed to confirm it (an id, a
  version). Queries never change state and never call into the domain's
  behaviour methods.
- Handlers orchestrate; they do not contain business rules. Rules live on the
  aggregate, so they hold no matter which slice reaches them.
- A handler depends on abstractions the application layer owns, never on a
  concrete infrastructure type.
- Cross-cutting concerns -- validation, logging, transactions, retries -- belong
  in pipeline behaviours, added once and applied to every request.
- One transaction per command, opened by the transaction behaviour, not by the
  handler.

**When reviewing a slice, check**

- Does the request carry exactly what the use case needs, and nothing more?
- Would a business rule in this handler be bypassed if another slice reached the
  same aggregate? If so, it is in the wrong place.
- Are failures modelled as results the caller must handle, or as exceptions that
  will surface as a 500?
