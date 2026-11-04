/**
 * The owner's undo history for one open canvas, as server versions. Undo is a selective undo of the batch at that
 * version (a new batch); redo is the undo of that undo. A fresh edit clears the redo stack.
 */
export interface UndoState {
  undo: number[];
  redo: number[];
}

export const emptyUndo: UndoState = { undo: [], redo: [] };
const MAX = 50;

export function recordEdit(state: UndoState, version: number): UndoState {
  return { undo: [...state.undo, version].slice(-MAX), redo: [] };
}

/** The version to undo now, and the state once the server confirms it (with the undo batch's own version). */
export function nextUndo(state: UndoState): number | undefined {
  return state.undo.at(-1);
}

export function afterUndo(state: UndoState, undoBatchVersion: number): UndoState {
  return { undo: state.undo.slice(0, -1), redo: [...state.redo, undoBatchVersion] };
}

export function nextRedo(state: UndoState): number | undefined {
  return state.redo.at(-1);
}

export function afterRedo(state: UndoState, redoBatchVersion: number): UndoState {
  return { undo: [...state.undo, redoBatchVersion].slice(-MAX), redo: state.redo.slice(0, -1) };
}

/** Undo refused (later changes touched the same items): drop that entry so the next Ctrl+Z tries the one before. */
export function dropUndo(state: UndoState): UndoState {
  return { ...state, undo: state.undo.slice(0, -1) };
}

export function dropRedo(state: UndoState): UndoState {
  return { ...state, redo: state.redo.slice(0, -1) };
}
