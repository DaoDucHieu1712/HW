import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { MemorySaver } from "@langchain/langgraph";
import { loadConfig } from "../src/config.js";
import { buildGraph } from "../src/graph.js";
import { Workspace } from "../src/tools/workspace.js";

function scratchWorkspace(): Workspace {
  const root = mkdtempSync(join(tmpdir(), "loop-ws-"));
  mkdirSync(join(root, "HW.Domain", "Entities"), { recursive: true });
  mkdirSync(join(root, "bin"), { recursive: true });
  writeFileSync(join(root, "HW.Domain", "Entities", "Vocab.cs"), "public class Vocab\n{\n}\n");
  writeFileSync(join(root, "HW.Domain", "Notes.md"), "notes\n");
  writeFileSync(join(root, "bin", "Secret.cs"), "should never be listed\n");
  return new Workspace({ root, excludeDirs: ["bin", "obj", ".git"] });
}

// The containment check is the security property of the whole system: every path reaching the
// workspace was written by a language model. These are the shapes a confused model actually emits.
test("workspace refuses paths that escape the root", () => {
  const workspace = scratchWorkspace();

  for (const escape of [
    "../outside.cs",
    "../../etc/passwd",
    "HW.Domain/../../outside.cs",
    "HW.Domain/../../../Windows/System32/drivers/etc/hosts",
  ]) {
    assert.throws(() => workspace.resolvePath(escape), /outside the workspace root/, `should refuse ${escape}`);
  }
});

test("workspace refuses paths inside excluded directories", () => {
  const workspace = scratchWorkspace();
  assert.throws(() => workspace.resolvePath("bin/Secret.cs"), /excluded directory/);
});

test("workspace allows ordinary relative paths", () => {
  const workspace = scratchWorkspace();
  assert.ok(workspace.resolvePath("HW.Domain/Entities/Vocab.cs").endsWith("Vocab.cs"));
  assert.equal(workspace.exists("HW.Domain/Entities/Vocab.cs"), true);
  assert.equal(workspace.exists("HW.Domain/Entities/Missing.cs"), false);
});

test("list skips excluded directories and honours globs", async () => {
  const workspace = scratchWorkspace();

  const all = await workspace.list(undefined, 100);
  assert.equal(all.some((file) => file.path.startsWith("bin/")), false, "bin/ must not be listed");

  const csharp = await workspace.list("**/*.cs", 100);
  assert.deepEqual(
    csharp.map((file) => file.path),
    ["HW.Domain/Entities/Vocab.cs"],
  );

  const scoped = await workspace.list("HW.Domain/Entities/*.cs", 100);
  assert.equal(scoped.length, 1);

  // A single star must not cross a directory separator.
  const shallow = await workspace.list("HW.Domain/*.cs", 100);
  assert.equal(shallow.length, 0);
});

test("read prefixes 1-based line numbers and windows correctly", async () => {
  const workspace = scratchWorkspace();

  const whole = await workspace.read("HW.Domain/Entities/Vocab.cs");
  assert.match(whole, /^HW\.Domain\/Entities\/Vocab\.cs \(lines 1-/);
  assert.match(whole, /1\tpublic class Vocab/);

  const window = await workspace.read("HW.Domain/Entities/Vocab.cs", 2, 2);
  assert.match(window, /lines 2-2 of/);
  assert.match(window, /2\t\{/);
});

test("search finds substrings and respects the glob", async () => {
  const workspace = scratchWorkspace();

  const hits = await workspace.search("class Vocab", undefined, false, 10);
  assert.equal(hits.length, 1);
  assert.equal(hits[0].line, 1);

  const scoped = await workspace.search("class Vocab", "**/*.md", false, 10);
  assert.equal(scoped.length, 0);
});

// The graph's shape is the design. If an edge is dropped, this is where it shows up rather than
// twenty minutes into a paid run.
test("graph compiles with every node and gate wired", async () => {
  const config = loadConfig();
  const graph = buildGraph(config, new MemorySaver());
  const drawn = await graph.getGraphAsync();
  const nodes = new Set(Object.keys(drawn.nodes));

  for (const expected of [
    "prepare",
    "scout",
    "make_plan",
    "plan_gate",
    "code",
    "verify_build",
    "verify_tests",
    "repair",
    "review_change",
    "propose",
    "patch_gate",
    "exhausted",
  ]) {
    assert.ok(nodes.has(expected), `node '${expected}' is missing from the graph`);
  }

  const edges = drawn.edges.map((edge) => `${edge.source}->${edge.target}`);

  // The cycle that makes this a loop rather than a pipeline: a repair always re-enters through a
  // real build, never straight back to review.
  assert.ok(edges.includes("repair->verify_build"), "repair must route back through build");
  assert.ok(edges.includes("code->verify_build"), "code must be verified by build");
  assert.ok(edges.includes("propose->patch_gate"), "nothing may reach apply without the patch gate");
  assert.ok(
    edges.some((edge) => edge === "verify_build->repair"),
    "a failing build must be able to reach repair",
  );
  assert.ok(
    edges.some((edge) => edge === "verify_tests->repair"),
    "a failing test must be able to reach repair",
  );
  assert.ok(
    edges.some((edge) => edge === "plan_gate->make_plan"),
    "the plan gate must be able to send a plan back for revision",
  );
});
