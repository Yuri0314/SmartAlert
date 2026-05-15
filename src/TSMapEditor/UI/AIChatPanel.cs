using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
using System.Collections.Generic;
using System.Text;
using TSMapEditor.AI;
using TSMapEditor.Misc;
using TSMapEditor.UI.Controls;

namespace TSMapEditor.UI
{
    /// <summary>
    /// The AI chat panel displayed as a floating, draggable, resizable window.
    /// Provides a chat interface for natural language map editing.
    /// </summary>
    public class AIChatPanel : EditorWindow
    {
        private const int DefaultWidth = 420;
        private const int DefaultHeight = 520;
        private const int MinWidth = 300;
        private const int MinHeight = 250;
        private const int InputAreaHeight = 70;
        private const int HeaderHeight = 30;
        private const int Padding = 6;
        private const int ResizeHandleSize = 12;

        private readonly AIChatService chatService;

        private XNALabel lblTitle;
        private XNAListBox lbMessages;
        private EditorTextBox tbInput;
        private EditorButton btnSend;
        private EditorButton btnSettings;
        private EditorButton btnClear;
        private XNALabel lblStatus;
        private EditorContextMenu messageContextMenu;

        // Resize state
        private bool isResizing;
        private Point resizeStartPoint;
        private Point resizeStartSize;

        // Track pending UI updates from background thread
        private string pendingMessage;
        private string pendingError;
        private string pendingProgress;
        private bool? pendingBusyState;
        private readonly object pendingLock = new object();

        public AIChatPanel(WindowManager windowManager, AIChatService chatService) : base(windowManager)
        {
            this.chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));

            chatService.MessageReceived += OnMessageReceived;
            chatService.ErrorOccurred += OnErrorOccurred;
            chatService.BusyStateChanged += OnBusyStateChanged;
            chatService.ToolProgressUpdate += OnToolProgress;
        }

        /// <summary>
        /// Event raised when the user clicks the settings button.
        /// </summary>
        public event EventHandler SettingsRequested;

        public override void Initialize()
        {
            Name = nameof(AIChatPanel);
            Width = DefaultWidth;
            Height = DefaultHeight;
            CenterByDefault = false; // Don't auto-center, let user position it

            // Header label
            lblTitle = new XNALabel(WindowManager);
            lblTitle.Name = nameof(lblTitle);
            lblTitle.Text = Translator.Translate("AIChatPanel.Title", "SmartAlert AI Assistant");
            lblTitle.FontIndex = Constants.UIBoldFont;
            lblTitle.X = Padding;
            lblTitle.Y = Padding;
            AddChild(lblTitle);

            // Settings button (top right)
            btnSettings = new EditorButton(WindowManager);
            btnSettings.Name = nameof(btnSettings);
            btnSettings.Text = Translator.Translate("AIChatPanel.Settings", "Settings");
            btnSettings.Width = 50;
            btnSettings.X = Width - btnSettings.Width - Padding;
            btnSettings.Y = Padding - 2;
            btnSettings.LeftClick += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
            AddChild(btnSettings);

            // Clear button
            btnClear = new EditorButton(WindowManager);
            btnClear.Name = nameof(btnClear);
            btnClear.Text = Translator.Translate("AIChatPanel.Clear", "Clear");
            btnClear.Width = 50;
            btnClear.X = btnSettings.X - btnClear.Width - Padding;
            btnClear.Y = Padding - 2;
            btnClear.LeftClick += (s, e) =>
            {
                chatService.ClearHistory();
                lbMessages.Clear();
                AddSystemMessage(Translator.Translate("AIChatPanel.ChatCleared", "Chat cleared."));
            };
            AddChild(btnClear);

            // Messages list
            lbMessages = new XNAListBox(WindowManager);
            lbMessages.Name = nameof(lbMessages);
            lbMessages.X = Padding;
            lbMessages.Y = HeaderHeight + Padding;
            lbMessages.Width = Width - Padding * 2;
            lbMessages.Height = Height - HeaderHeight - InputAreaHeight - Padding * 2;
            lbMessages.FontIndex = Constants.UIDefaultFont;
            lbMessages.LineHeight = 18;
            lbMessages.AllowRightClickUnselect = false;
            lbMessages.RightClick += LbMessages_RightClick;
            AddChild(lbMessages);

            // Right-click context menu for messages
            messageContextMenu = new EditorContextMenu(WindowManager);
            messageContextMenu.Name = nameof(messageContextMenu);
            messageContextMenu.Width = 180;
            messageContextMenu.AddItem(
                Translator.Translate("AIChatPanel.CopySelected", "Copy Selected"),
                CopySelectedMessage,
                () => lbMessages.SelectedItem != null);
            messageContextMenu.AddItem(
                Translator.Translate("AIChatPanel.CopyAll", "Copy All"),
                CopyAllMessages,
                () => lbMessages.Items.Count > 0);
            AddChild(messageContextMenu);

            // Status label
            lblStatus = new XNALabel(WindowManager);
            lblStatus.Name = nameof(lblStatus);
            lblStatus.Text = chatService.IsConfigured ? Translator.Translate("AIChatPanel.StatusReady", "Ready | Ctrl+Shift+A to toggle") : Translator.Translate("AIChatPanel.StatusNotConfigured", "Not configured - click Settings");
            lblStatus.X = Padding;
            lblStatus.Y = lbMessages.Bottom + 2;
            lblStatus.ClientRectangle = new Rectangle(Padding, lbMessages.Bottom + 2, Width - Padding * 2, 16);
            AddChild(lblStatus);

            // Input text box
            tbInput = new EditorTextBox(WindowManager);
            tbInput.Name = nameof(tbInput);
            tbInput.X = Padding;
            tbInput.Y = Height - Constants.UITextBoxHeight - Padding;
            tbInput.Width = Width - 60 - Padding * 3;
            tbInput.EnterPressed += (s, e) => SendMessage();
            AddChild(tbInput);

            // Send button
            btnSend = new EditorButton(WindowManager);
            btnSend.Name = nameof(btnSend);
            btnSend.Text = Translator.Translate("AIChatPanel.Send", "Send");
            btnSend.Width = 60;
            btnSend.X = tbInput.Right + Padding;
            btnSend.Y = tbInput.Y - 1;
            btnSend.LeftClick += (s, e) => SendMessage();
            AddChild(btnSend);

            base.Initialize();

            // Welcome message
            AddSystemMessage(Translator.Translate("AIChatPanel.Welcome1", "Welcome to SmartAlert AI Assistant!"));
            AddSystemMessage(Translator.Translate("AIChatPanel.Welcome2", "Type natural language commands to edit the map."));
            AddSystemMessage(Translator.Translate("AIChatPanel.Welcome3", "Drag bottom-right corner to resize. Right-click to copy."));
            if (!chatService.IsConfigured)
                AddSystemMessage(Translator.Translate("AIChatPanel.PleaseConfig", "Please click Settings to configure the AI service."));
        }

        #region Resize Logic

        /// <summary>
        /// Returns true if the cursor is within the resize handle area (bottom-right corner).
        /// </summary>
        private bool IsCursorInResizeHandle()
        {
            var cursor = GetCursorPoint();
            return cursor.X >= Width - ResizeHandleSize && cursor.X <= Width &&
                   cursor.Y >= Height - ResizeHandleSize && cursor.Y <= Height;
        }

        public override void OnMouseLeftDown(InputEventArgs inputEventArgs)
        {
            if (IsCursorInResizeHandle())
            {
                isResizing = true;
                resizeStartPoint = GetCursorPoint();
                resizeStartSize = new Point(Width, Height);
                inputEventArgs.Handled = true;
                return;
            }

            base.OnMouseLeftDown(inputEventArgs);
        }

        /// <summary>
        /// Repositions all child controls when the window size changes.
        /// </summary>
        private void RepositionChildren()
        {
            btnSettings.X = Width - btnSettings.Width - Padding;
            btnClear.X = btnSettings.X - btnClear.Width - Padding;

            lbMessages.Width = Width - Padding * 2;
            lbMessages.Height = Height - HeaderHeight - InputAreaHeight - Padding * 2;

            lblStatus.Y = lbMessages.Bottom + 2;
            lblStatus.ClientRectangle = new Rectangle(Padding, lbMessages.Bottom + 2, Width - Padding * 2, 16);

            tbInput.Y = Height - Constants.UITextBoxHeight - Padding;
            tbInput.Width = Width - 60 - Padding * 3;

            btnSend.X = tbInput.Right + Padding;
            btnSend.Y = tbInput.Y - 1;
        }

        #endregion

        #region Right-click Copy

        private void LbMessages_RightClick(object sender, EventArgs e)
        {
            lbMessages.SelectedIndex = lbMessages.HoveredIndex;
            messageContextMenu.Open(GetCursorPoint());
        }

        private void CopySelectedMessage()
        {
            if (lbMessages.SelectedItem == null)
                return;

            try
            {
                string text = lbMessages.SelectedItem.Text?.Trim();
                if (!string.IsNullOrEmpty(text))
                    System.Windows.Forms.Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                Rampastring.Tools.Logger.Log($"AIChatPanel: Failed to copy to clipboard: {ex.Message}");
            }
        }

        private void CopyAllMessages()
        {
            if (lbMessages.Items.Count == 0)
                return;

            try
            {
                var sb = new StringBuilder();
                foreach (var item in lbMessages.Items)
                {
                    sb.AppendLine(item.Text);
                }
                string text = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(text))
                    System.Windows.Forms.Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                Rampastring.Tools.Logger.Log($"AIChatPanel: Failed to copy all to clipboard: {ex.Message}");
            }
        }

        #endregion

        #region Text Wrapping

        /// <summary>
        /// Wraps a long text string into multiple lines that fit within the given pixel width.
        /// Uses a conservative character width estimate (8px for ASCII, 14px for CJK).
        /// </summary>
        private List<string> WrapText(string text, int maxWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text) || maxWidth <= 0)
            {
                lines.Add(text ?? string.Empty);
                return lines;
            }

            // Account for scrollbar and padding
            int availableWidth = maxWidth - 24;
            if (availableWidth <= 50) availableWidth = 50;

            foreach (string rawLine in text.Split('\n'))
            {
                if (string.IsNullOrEmpty(rawLine))
                    continue;

                string remaining = rawLine;
                while (remaining.Length > 0)
                {
                    int fitChars = EstimateCharsToFit(remaining, availableWidth);
                    if (fitChars >= remaining.Length)
                    {
                        lines.Add(remaining);
                        break;
                    }

                    // Try to break at a space or punctuation
                    int breakAt = fitChars;
                    for (int i = fitChars - 1; i >= fitChars / 2; i--)
                    {
                        char c = remaining[i];
                        if (c == ' ' || c == '，' || c == '。' || c == '、' || c == '；' || c == '：')
                        {
                            breakAt = i + 1;
                            break;
                        }
                    }

                    lines.Add(remaining.Substring(0, breakAt));
                    remaining = remaining.Substring(breakAt).TrimStart();
                }
            }

            if (lines.Count == 0)
                lines.Add(string.Empty);

            return lines;
        }

        /// <summary>
        /// Estimates how many characters fit within the given pixel width.
        /// CJK characters are approximately 14px wide, ASCII characters ~7px.
        /// </summary>
        private int EstimateCharsToFit(string text, int pixelWidth)
        {
            int totalWidth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int charWidth = IsWideChar(text[i]) ? 14 : 7;
                if (totalWidth + charWidth > pixelWidth)
                    return Math.Max(1, i);
                totalWidth += charWidth;
            }
            return text.Length;
        }

        private static bool IsWideChar(char c)
        {
            // CJK Unified Ideographs, CJK Symbols, Fullwidth Forms, etc.
            return (c >= 0x2E80 && c <= 0x9FFF) ||
                   (c >= 0xF900 && c <= 0xFAFF) ||
                   (c >= 0xFE30 && c <= 0xFE4F) ||
                   (c >= 0xFF00 && c <= 0xFFEF);
        }

        #endregion

        #region Message Display

        private void SendMessage()
        {
            string text = tbInput.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return;

            // Add user message to display
            AddUserMessage(text);
            tbInput.Text = string.Empty;

            // Send to AI service
            chatService.SendMessage(text);
        }

        private void AddUserMessage(string text)
        {
            string prefix = Translator.Translate("AIChatPanel.You", "You") + ": ";
            var wrappedLines = WrapText(prefix + text, lbMessages.Width);
            bool first = true;
            foreach (string line in wrappedLines)
            {
                var item = new XNAListBoxItem();
                item.Text = first ? line : "    " + line;
                item.TextColor = new Color(100, 180, 255);
                lbMessages.AddItem(item);
                first = false;
            }
            ScrollToBottom();
        }

        private void AddAssistantMessage(string text)
        {
            // Split by newlines first, then wrap each line
            string[] rawLines = text.Split('\n');
            bool firstLine = true;
            foreach (string rawLine in rawLines)
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                string prefix = firstLine ? "AI: " : "    ";
                var wrappedLines = WrapText(prefix + rawLine, lbMessages.Width);
                bool firstWrapped = true;
                foreach (string wl in wrappedLines)
                {
                    var item = new XNAListBoxItem();
                    item.Text = firstWrapped ? wl : "    " + wl;
                    item.TextColor = new Color(180, 255, 180);
                    lbMessages.AddItem(item);
                    firstWrapped = false;
                }
                firstLine = false;
            }
            ScrollToBottom();
        }

        private void AddSystemMessage(string text)
        {
            var wrappedLines = WrapText("  " + text, lbMessages.Width);
            foreach (string line in wrappedLines)
            {
                var item = new XNAListBoxItem();
                item.Text = line;
                item.TextColor = new Color(180, 180, 180);
                lbMessages.AddItem(item);
            }
            ScrollToBottom();
        }

        private void AddErrorMessage(string text)
        {
            var wrappedLines = WrapText("✗ " + text, lbMessages.Width);
            foreach (string line in wrappedLines)
            {
                var item = new XNAListBoxItem();
                item.Text = line;
                item.TextColor = new Color(255, 120, 120);
                lbMessages.AddItem(item);
            }
            ScrollToBottom();
        }

        #endregion

        #region Selection Info

        /// <summary>
        /// Shows selection info in the chat panel when AI selection is completed.
        /// </summary>
        public void ShowSelectionInfo(int x, int y, int width, int height)
        {
            var item = new XNAListBoxItem();
            item.Text = $"◆ {Translator.Translate("AIChatPanel.SelectedArea", "Selected area")}: ({x},{y}) {width}×{height}";
            item.TextColor = new Color(0, 220, 220); // Cyan
            lbMessages.AddItem(item);

            var hintItem = new XNAListBoxItem();
            hintItem.Text = "  " + Translator.Translate("AIChatPanel.SelectionHint", "Type a command to operate on the selection (e.g. \"fill with ore\")");
            hintItem.TextColor = new Color(150, 150, 150);
            lbMessages.AddItem(hintItem);

            ScrollToBottom();
        }

        /// <summary>
        /// Clears the selection indicator in the chat panel.
        /// </summary>
        public void ClearSelectionInfo()
        {
            var item = new XNAListBoxItem();
            item.Text = "◇ " + Translator.Translate("AIChatPanel.SelectionCleared", "Selection cleared");
            item.TextColor = new Color(120, 120, 120);
            lbMessages.AddItem(item);
            ScrollToBottom();
        }

        #endregion

        private void ScrollToBottom()
        {
            if (lbMessages.Items.Count > 0)
            {
                lbMessages.TopIndex = Math.Max(0, lbMessages.Items.Count - lbMessages.NumberOfLinesOnList);
            }
        }

        #region Thread-safe Event Handlers

        private void OnMessageReceived(object sender, string message)
        {
            lock (pendingLock)
            {
                pendingMessage = message;
            }
        }

        private void OnErrorOccurred(object sender, string error)
        {
            lock (pendingLock)
            {
                pendingError = error;
            }
        }

        private void OnBusyStateChanged(object sender, bool isBusy)
        {
            lock (pendingLock)
            {
                pendingBusyState = isBusy;
            }
        }

        private void OnToolProgress(object sender, string progress)
        {
            lock (pendingLock)
            {
                pendingProgress = progress;
            }
        }

        #endregion

        /// <summary>
        /// Process pending UI updates on the main thread, plus handle resize dragging.
        /// </summary>
        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            // Handle resize dragging
            if (isResizing)
            {
                if (Cursor.LeftDown)
                {
                    var current = GetCursorPoint();
                    int newWidth = resizeStartSize.X + (current.X - resizeStartPoint.X);
                    int newHeight = resizeStartSize.Y + (current.Y - resizeStartPoint.Y);

                    Width = Math.Max(MinWidth, newWidth);
                    Height = Math.Max(MinHeight, newHeight);
                    RepositionChildren();
                }
                else
                {
                    isResizing = false;
                }
            }

            // Process pending map operations on the main thread
            // This is critical: mutations must run on the game thread for rendering to refresh
            chatService.ProcessPendingOperations();

            lock (pendingLock)
            {
                if (pendingMessage != null)
                {
                    AddAssistantMessage(pendingMessage);
                    pendingMessage = null;
                }

                if (pendingProgress != null)
                {
                    AddSystemMessage(pendingProgress);
                    pendingProgress = null;
                }

                if (pendingError != null)
                {
                    AddErrorMessage(pendingError);
                    pendingError = null;
                }

                if (pendingBusyState.HasValue)
                {
                    bool busy = pendingBusyState.Value;
                    lblStatus.Text = busy ? Translator.Translate("AIChatPanel.Thinking", "AI thinking...") : (chatService.IsConfigured ? Translator.Translate("AIChatPanel.Ready", "Ready") : Translator.Translate("AIChatPanel.NotConfigured", "Not configured"));
                    btnSend.AllowClick = !busy;
                    pendingBusyState = null;
                }
            }
        }

        /// <summary>
        /// Draw the resize handle indicator at the bottom-right corner.
        /// </summary>
        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);

            // Draw resize grip dots at bottom-right corner
            var gripColor = new Color(160, 160, 160, 200);
            int bx = Width - 6;
            int by = Height - 6;
            // 3x3 dot pattern
            for (int row = 0; row < 3; row++)
            {
                for (int col = row; col < 3; col++)
                {
                    FillRectangle(new Rectangle(bx - col * 4, by - row * 4, 2, 2), gripColor);
                }
            }
        }

        public override void Kill()
        {
            chatService.MessageReceived -= OnMessageReceived;
            chatService.ErrorOccurred -= OnErrorOccurred;
            chatService.BusyStateChanged -= OnBusyStateChanged;
            chatService.ToolProgressUpdate -= OnToolProgress;

            base.Kill();
        }
    }
}
