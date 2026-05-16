using System.Collections.Generic;

namespace TSMapEditor.Mutations
{
    /// <summary>
    /// The Undo / Redo system.
    /// </summary>
    public class MutationManager
    {
        public List<IMutation> UndoList { get; } = new List<IMutation>();
        public List<IMutation> RedoList { get; } = new List<IMutation>();

        /// <summary>
        /// When true, Undo and Redo are blocked (e.g., during AI generation).
        /// PerformMutation still works so the AI can execute mutations normally.
        /// </summary>
        public bool IsLocked { get; set; }

        /// <summary>
        /// Performs a new mutation on the map.
        /// </summary>
        /// <param name="mutation">The mutation to perform.</param>
        public void PerformMutation(IMutation mutation)
        {
            mutation.Perform();
            RedoList.Clear();
            UndoList.Add(mutation);
        }

        public bool CanUndo() => !IsLocked && UndoList.Count > 0;

        /// <summary>
        /// Undoes the last mutation performed to the map, or the last chain of mutations
        /// if multiple mutations have the same <see cref="Mutation.EventID"/>.
        /// </summary>
        public void Undo()
        {
            if (!CanUndo())
                return;

            int lastMutationEventId = UndoList[UndoList.Count - 1].EventID;

            if (lastMutationEventId < 0)
            {
                UndoOne();
                return;
            }

            while (CanUndo() && UndoList[UndoList.Count - 1].EventID == lastMutationEventId)
            {
                UndoOne();
            }
        }

        /// <summary>
        /// Returns the latest mutation performed to the map.
        /// </summary>
        public IMutation GetLatestMutation() => UndoList.Count > 0 ? UndoList[^1] : null;

        /// <summary>
        /// Undoes the last mutation performed to the map.
        /// </summary>
        public void UndoOne()
        {
            if (IsLocked || UndoList.Count == 0)
                return;

            int lastUndoIndex = UndoList.Count - 1;
            UndoList[lastUndoIndex].Undo();
            RedoList.Add(UndoList[lastUndoIndex]);
            UndoList.RemoveAt(lastUndoIndex);
        }

        public bool CanRedo() => !IsLocked && RedoList.Count > 0;

        /// <summary>
        /// Redoes the last un-done mutation on the map.
        /// </summary>
        public void Redo()
        {
            if (!CanRedo())
                return;

            int lastRedoIndex = RedoList.Count - 1;
            RedoList[lastRedoIndex].Perform();
            UndoList.Add(RedoList[lastRedoIndex]);
            RedoList.RemoveAt(lastRedoIndex);
        }

        public void ClearUndoAndRedoLists()
        {
            UndoList.Clear();
            RedoList.Clear();
        }
    }
}

