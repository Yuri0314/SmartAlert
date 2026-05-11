using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
using TSMapEditor.AI;
using TSMapEditor.Misc;
using TSMapEditor.UI.Controls;

namespace TSMapEditor.UI
{
    /// <summary>
    /// The AI chat panel displayed as a floating, draggable window.
    /// Provides a chat interface for natural language map editing.
    /// </summary>
    public class AIChatPanel : EditorWindow
    {
        private const int DefaultWidth = 380;
        private const int DefaultHeight = 500;
        private const int InputAreaHeight = 70;
        private const int HeaderHeight = 30;
        private const int Padding = 6;

        private readonly AIChatService chatService;

        private XNALabel lblTitle;
        private XNAListBox lbMessages;
        private EditorTextBox tbInput;
        private EditorButton btnSend;
        private EditorButton btnSettings;
        private EditorButton btnClear;
        private XNALabel lblStatus;

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
            btnSettings.Width = 40;
            btnSettings.X = Width - btnSettings.Width - Padding;
            btnSettings.Y = Padding - 2;
            btnSettings.LeftClick += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
            AddChild(btnSettings);

            // Clear button
            btnClear = new EditorButton(WindowManager);
            btnClear.Name = nameof(btnClear);
            btnClear.Text = Translator.Translate("AIChatPanel.Clear", "Clear");
            btnClear.Width = 40;
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
            AddChild(lbMessages);

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
            AddSystemMessage(Translator.Translate("AIChatPanel.Welcome3", "This window can be dragged around."));
            if (!chatService.IsConfigured)
                AddSystemMessage(Translator.Translate("AIChatPanel.PleaseConfig", "Please click Settings to configure the AI service."));
        }

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
            var item = new XNAListBoxItem();
            item.Text = Translator.Translate("AIChatPanel.You", "You") + ": " + text;
            item.TextColor = new Color(100, 180, 255);
            lbMessages.AddItem(item);
            ScrollToBottom();
        }

        private void AddAssistantMessage(string text)
        {
            // Split long messages into multiple lines
            string[] lines = text.Split('\n');
            bool first = true;
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var item = new XNAListBoxItem();
                item.Text = first ? "AI: " + line : "    " + line;
                item.TextColor = new Color(180, 255, 180);
                lbMessages.AddItem(item);
                first = false;
            }
            ScrollToBottom();
        }

        private void AddSystemMessage(string text)
        {
            var item = new XNAListBoxItem();
            item.Text = "  " + text;
            item.TextColor = new Color(180, 180, 180);
            lbMessages.AddItem(item);
            ScrollToBottom();
        }

        private void AddErrorMessage(string text)
        {
            var item = new XNAListBoxItem();
            item.Text = "✗ " + text;
            item.TextColor = new Color(255, 120, 120);
            lbMessages.AddItem(item);
            ScrollToBottom();
        }

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

        private void ScrollToBottom()
        {
            if (lbMessages.Items.Count > 0)
            {
                lbMessages.TopIndex = Math.Max(0, lbMessages.Items.Count - lbMessages.NumberOfLinesOnList);
            }
        }

        // --- Thread-safe event handlers (called from background thread) ---

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

        /// <summary>
        /// Process pending UI updates on the main thread.
        /// </summary>
        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

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
