namespace Centauri.Editing.Undo;

// One coarse, already-applied edit gesture (a completed gizmo drag, an entity create/delete) —
// not a live/uncommitted change. By the time a command is constructed the edit has already
// happened (a drag already moved the entity live, frame by frame, while the mouse was down; a
// click already created the entity); the command just captures enough to reverse or replay it.
// Undo()/Redo() are the only entry points — only CommandHistory calls either.
internal interface ICommand
{
    void Undo();
    void Redo();
}
