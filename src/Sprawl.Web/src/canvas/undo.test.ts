import { describe, expect, it } from "vitest";
import { afterRedo, afterUndo, dropUndo, emptyUndo, nextRedo, nextUndo, recordEdit } from "./undo";

describe("undo history", () => {
  it("undoes the latest edit, redoes by undoing the undo, and a new edit clears redo", () => {
    let state = recordEdit(recordEdit(emptyUndo, 4), 6);
    expect(nextUndo(state)).toBe(6);
    state = afterUndo(state, 7); // the server committed the undo of v6 as v7
    expect(nextUndo(state)).toBe(4);
    expect(nextRedo(state)).toBe(7);
    state = afterRedo(state, 8); // redo = undo of v7, committed as v8
    expect(state).toEqual({ undo: [4, 8], redo: [] });
    state = afterUndo(state, 9);
    state = recordEdit(state, 10);
    expect(state.redo).toEqual([]);
  });

  it("skips an entry the server refused to undo", () => {
    expect(nextUndo(dropUndo(recordEdit(recordEdit(emptyUndo, 2), 3)))).toBe(2);
  });
});
