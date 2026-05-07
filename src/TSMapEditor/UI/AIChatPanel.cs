using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
using TSMapEditor.AI;
using TSMapEditor.UI.Controls;

namespace TSMapEditor.UI
{
    /// <summary>
    /// The AI chat panel displayed on the right side of the editor.
    /// Provides a chat interface for natural language map editing.
    /// </summary>
    public class AIChatPanel : EditorPanel
    {
        private const int PanelWidth = 350;
        private const int InputAreaHeight = 70;
        private const int HeaderHeight = 30;
        private const int Padding = 6;

        private readonly AIChatService chatService;
        private readonly WindowManager windowManager;

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
        private bool? pendingBusyState;
        private readonly object pendingLock = new object();

        public AIChatPanel(WindowManager windowManager, AIChatService chatService) : base(windowManager)
        {
            this.windowManager = windowManager;
            this.chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));

            chatService.MessageReceived += OnMessageReceived;
            chatService.ErrorOccurred += OnErrorOccurred;
            chatService.BusyStateChanged += OnBusyStateChanged;
        }

        /// <summary>
        /// Event raised when the user clicks the settings button.
        /// </summary>
        public event EventHandler SettingsRequested;

        public override void Initialize()
        {
            Name = nameof(AIChatPanel);
            Width = PanelWidth;

            // Header label
            lblTitle = new XNALabel(WindowManager);
            lblTitle.Name = nameof(lblTitle);
            lblTitle.Text = "AI 助手";
            lblTitle.FontIndex = Constants.UIBoldFont;
            lblTitle.X = Padding;
            lblTitle.Y = Padding;
            AddChild(lblTitle);

            // Settings button (top right)
            btnSettings = new EditorButton(WindowManager);
            btnSettings.Name = nameof(btnSettings);
            btnSettings.Text = "⚙";
            btnSettings.Width = 30;
            btnSettings.X = Width - btnSettings.Width - Padding;
            btnSettings.Y = Padding - 2;
            btnSettings.LeftClick += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
            AddChild(btnSettings);

            // Clear button
            btnClear = new EditorButton(WindowManager);
            btnClear.Name = nameof(btnClear);
            btnClear.Text = "清空";
            btnClear.Width = 40;
            btnClear.X = btnSettings.X - btnClear.Width - Padding;
            btnClear.Y = Padding - 2;
            btnClear.LeftClick += (s, e) =>
            {
                chatService.ClearHistory();
                lbMessages.Clear();
                AddSystemMessage("对话已清空。");
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
            lblStatus.Text = chatService.IsConfigured ? "就绪" : "未配置 - 请点击 ⚙ 设置";
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
            btnSend.Text = "发送";
            btnSend.Width = 60;
            btnSend.X = tbInput.Right + Padding;
            btnSend.Y = tbInput.Y - 1;
            btnSend.LeftClick += (s, e) => SendMessage();
            AddChild(btnSend);

            base.Initialize();

            // Welcome message
            AddSystemMessage("欢迎使用 SmartAlert AI 助手！");
            AddSystemMessage("输入自然语言指令来编辑地图。");
            if (!chatService.IsConfigured)
                AddSystemMessage("请先点击 ⚙ 配置 AI 服务。");
        }

        /// <summary>
        /// Refreshes the layout when the panel is resized.
        /// </summary>
        public void RefreshLayout()
        {
            if (lbMessages == null)
                return;

            lbMessages.Height = Height - HeaderHeight - InputAreaHeight - Padding * 2;
            lblStatus.Y = lbMessages.Bottom + 2;
            lblStatus.ClientRectangle = new Rectangle(Padding, lbMessages.Bottom + 2, Width - Padding * 2, 16);
            tbInput.Y = Height - Constants.UITextBoxHeight - Padding;
            btnSend.Y = tbInput.Y - 1;
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
            item.Text = "你: " + text;
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

        /// <summary>
        /// Process pending UI updates on the main thread.
        /// </summary>
        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            lock (pendingLock)
            {
                if (pendingMessage != null)
                {
                    AddAssistantMessage(pendingMessage);
                    pendingMessage = null;
                }

                if (pendingError != null)
                {
                    AddErrorMessage(pendingError);
                    pendingError = null;
                }

                if (pendingBusyState.HasValue)
                {
                    bool busy = pendingBusyState.Value;
                    lblStatus.Text = busy ? "AI 思考中..." : (chatService.IsConfigured ? "就绪" : "未配置");
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

            base.Kill();
        }
    }
}
