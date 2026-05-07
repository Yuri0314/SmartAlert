using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using System;
using TSMapEditor.GameMath;
using TSMapEditor.Models;

namespace TSMapEditor.UI.CursorActions
{
    /// <summary>
    /// A cursor action that allows the user to select a rectangular area on the map
    /// for AI-assisted operations. Two left-clicks define the rectangle corners.
    /// The selection is passed to AIChatService as context for subsequent AI commands.
    /// </summary>
    public class AISelectionCursorAction : CursorAction
    {
        public AISelectionCursorAction(ICursorActionTarget cursorActionTarget) : base(cursorActionTarget)
        {
        }

        public override string GetName() => "AI Selection";

        public override bool DrawCellCursor => true;

        /// <summary>
        /// The first corner of the selection rectangle (set on first click).
        /// </summary>
        public Point2D? StartCellCoords { get; set; } = null;

        /// <summary>
        /// Raised when the user completes a selection (two clicks).
        /// Passes the selection rectangle (X, Y, Width, Height).
        /// </summary>
        public event EventHandler<AISelectionEventArgs> SelectionCompleted;

        public override void OnActionEnter()
        {
            StartCellCoords = null;
        }

        public override void OnActionExit()
        {
            StartCellCoords = null;
        }

        public override void LeftClick(Point2D cellCoords)
        {
            if (StartCellCoords == null)
            {
                // First click: set starting corner
                StartCellCoords = cellCoords;
                return;
            }

            // Second click: complete the selection
            Point2D start = StartCellCoords.Value;
            int startX = Math.Min(cellCoords.X, start.X);
            int startY = Math.Min(cellCoords.Y, start.Y);
            int endX = Math.Max(cellCoords.X, start.X);
            int endY = Math.Max(cellCoords.Y, start.Y);

            int width = endX - startX + 1;
            int height = endY - startY + 1;

            // Raise the event
            SelectionCompleted?.Invoke(this, new AISelectionEventArgs(startX, startY, width, height));

            // Reset and exit the action
            StartCellCoords = null;
            ExitAction();
        }

        public override void DrawPreview(Point2D cellCoords, Point2D cameraTopLeftPoint)
        {
            if (StartCellCoords == null)
            {
                // Before first click: just draw a label at cursor
                DrawText(cellCoords, cameraTopLeftPoint, 0, -20, "AI 选区: 点击设定起点", Color.Cyan);
                return;
            }

            // After first click: draw rectangle preview from start to current cursor
            Point2D start = StartCellCoords.Value;
            int startY = Math.Min(cellCoords.Y, start.Y);
            int endY = Math.Max(cellCoords.Y, start.Y);
            int startX = Math.Min(cellCoords.X, start.X);
            int endX = Math.Max(cellCoords.X, start.X);

            Func<Point2D, Map, Point2D> func = Is2DMode
                ? CellMath.CellTopLeftPointFromCellCoords
                : CellMath.CellTopLeftPointFromCellCoords_3D;

            // Calculate the four corners of the diamond shape
            Point2D topPoint = func(new Point2D(startX, startY), CursorActionTarget.Map) - cameraTopLeftPoint + new Point2D(Constants.CellSizeX / 2, 0);
            Point2D bottomPoint = func(new Point2D(endX, endY), CursorActionTarget.Map) - cameraTopLeftPoint + new Point2D(Constants.CellSizeX / 2, Constants.CellSizeY);
            Point2D leftPoint = func(new Point2D(startX, endY), CursorActionTarget.Map) - cameraTopLeftPoint + new Point2D(0, Constants.CellSizeY / 2);
            Point2D rightPoint = func(new Point2D(endX, startY), CursorActionTarget.Map) - cameraTopLeftPoint + new Point2D(Constants.CellSizeX, Constants.CellSizeY / 2);

            topPoint = topPoint.ScaleBy(CursorActionTarget.Camera.ZoomLevel);
            bottomPoint = bottomPoint.ScaleBy(CursorActionTarget.Camera.ZoomLevel);
            leftPoint = leftPoint.ScaleBy(CursorActionTarget.Camera.ZoomLevel);
            rightPoint = rightPoint.ScaleBy(CursorActionTarget.Camera.ZoomLevel);

            // Draw the selection rectangle with cyan color
            Color lineColor = Color.Cyan;
            int thickness = 2;
            Renderer.DrawLine(topPoint.ToXNAVector(), leftPoint.ToXNAVector(), lineColor, thickness);
            Renderer.DrawLine(topPoint.ToXNAVector(), rightPoint.ToXNAVector(), lineColor, thickness);
            Renderer.DrawLine(leftPoint.ToXNAVector(), bottomPoint.ToXNAVector(), lineColor, thickness);
            Renderer.DrawLine(rightPoint.ToXNAVector(), bottomPoint.ToXNAVector(), lineColor, thickness);

            // Draw size label
            int w = endX - startX + 1;
            int h = endY - startY + 1;
            DrawText(cellCoords, cameraTopLeftPoint, 0, -20,
                $"AI 选区: ({startX},{startY}) {w}x{h} — 点击确认", Color.Cyan);
        }
    }

    /// <summary>
    /// Event args for AI selection completion.
    /// </summary>
    public class AISelectionEventArgs : EventArgs
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public AISelectionEventArgs(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}
