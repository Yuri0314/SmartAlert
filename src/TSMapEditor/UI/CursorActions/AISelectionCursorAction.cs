using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using System;
using TSMapEditor.GameMath;
using TSMapEditor.Misc;
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

        private bool selectionActive = false;
        private int currentStartX, currentStartY, currentEndX, currentEndY;

        public override void LeftClick(Point2D cellCoords)
        {
            if (StartCellCoords == null || selectionActive)
            {
                // First click (or new click after previous selection): set starting corner
                StartCellCoords = cellCoords;
                selectionActive = false;
                return;
            }

            // Second click: complete the selection
            Point2D start = StartCellCoords.Value;
            currentStartX = Math.Min(cellCoords.X, start.X);
            currentStartY = Math.Min(cellCoords.Y, start.Y);
            currentEndX = Math.Max(cellCoords.X, start.X);
            currentEndY = Math.Max(cellCoords.Y, start.Y);

            int width = currentEndX - currentStartX + 1;
            int height = currentEndY - currentStartY + 1;

            selectionActive = true;

            // Raise the event
            SelectionCompleted?.Invoke(this, new AISelectionEventArgs(currentStartX, currentStartY, width, height));

            // Do NOT exit the action, so the preview stays on screen
        }

        public override void DrawPreview(Point2D cellCoords, Point2D cameraTopLeftPoint)
        {
            if (StartCellCoords == null)
            {
                // Before first click: just draw a label at cursor
                DrawText(cellCoords, cameraTopLeftPoint, 0, -20, Translator.Translate("AISelection.ClickStart", "AI Selection: Click to set start point"), Color.Cyan);
                return;
            }

            int startY, endY, startX, endX;

            if (selectionActive)
            {
                // Selection is complete, draw the locked rectangle
                startX = currentStartX;
                startY = currentStartY;
                endX = currentEndX;
                endY = currentEndY;
            }
            else
            {
                // After first click, before second click: draw rectangle preview from start to current cursor
                Point2D start = StartCellCoords.Value;
                startY = Math.Min(cellCoords.Y, start.Y);
                endY = Math.Max(cellCoords.Y, start.Y);
                startX = Math.Min(cellCoords.X, start.X);
                endX = Math.Max(cellCoords.X, start.X);
            }

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

            if (selectionActive)
            {
                // Draw label at the top of the selection
                Point2D centerTop = new Point2D((startX + endX) / 2, startY);
                DrawText(centerTop, cameraTopLeftPoint, 0, -20,
                    $"{Translator.Translate("AISelection.AreaSelected", "Area selected")}: ({startX},{startY}) {w}x{h} — {Translator.Translate("AISelection.TypeCommand", "type a command in chat")}", Color.Cyan);
            }
            else
            {
                DrawText(cellCoords, cameraTopLeftPoint, 0, -20,
                    $"{Translator.Translate("AISelection.Preview", "AI Selection")}: ({startX},{startY}) {w}x{h} — {Translator.Translate("AISelection.ClickConfirm", "click to confirm")}", Color.Cyan);
            }
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
