using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
using TSMapEditor.AI;
using TSMapEditor.UI.Controls;

namespace TSMapEditor.UI.Windows
{
    /// <summary>
    /// Settings window for configuring the AI service (API endpoint, key, model).
    /// </summary>
    public class AISettingsWindow : EditorWindow
    {
        private const int WindowWidth = 450;
        private const int WindowHeight = 280;
        private const int Padding = 12;
        private const int LabelWidth = 100;

        private readonly AIChatService chatService;

        private EditorTextBox tbEndpoint;
        private EditorTextBox tbApiKey;
        private EditorTextBox tbModelName;
        private EditorButton btnTest;
        private EditorButton btnSave;
        private EditorButton btnCancel;
        private XNALabel lblTestResult;

        public AISettingsWindow(WindowManager windowManager, AIChatService chatService) : base(windowManager)
        {
            this.chatService = chatService;
        }

        public override void Initialize()
        {
            Name = nameof(AISettingsWindow);
            Width = WindowWidth;
            Height = WindowHeight;

            var config = chatService.Config ?? new AIConfig();

            int y = Padding + 10;

            // Title
            var lblTitle = new XNALabel(WindowManager);
            lblTitle.Text = "AI 服务设置";
            lblTitle.FontIndex = Constants.UIBoldFont;
            lblTitle.X = Padding;
            lblTitle.Y = y;
            AddChild(lblTitle);
            y += 30;

            // API Endpoint
            var lblEndpoint = new XNALabel(WindowManager);
            lblEndpoint.Text = "API 端点:";
            lblEndpoint.X = Padding;
            lblEndpoint.Y = y + 2;
            AddChild(lblEndpoint);

            tbEndpoint = new EditorTextBox(WindowManager);
            tbEndpoint.X = Padding + LabelWidth;
            tbEndpoint.Y = y;
            tbEndpoint.Width = Width - Padding * 2 - LabelWidth;
            tbEndpoint.Text = config.ApiEndpoint;
            AddChild(tbEndpoint);
            y += 30;

            // API Key
            var lblApiKey = new XNALabel(WindowManager);
            lblApiKey.Text = "API Key:";
            lblApiKey.X = Padding;
            lblApiKey.Y = y + 2;
            AddChild(lblApiKey);

            tbApiKey = new EditorTextBox(WindowManager);
            tbApiKey.X = Padding + LabelWidth;
            tbApiKey.Y = y;
            tbApiKey.Width = Width - Padding * 2 - LabelWidth;
            tbApiKey.Text = config.ApiKey;
            AddChild(tbApiKey);
            y += 30;

            // Model Name
            var lblModel = new XNALabel(WindowManager);
            lblModel.Text = "模型名称:";
            lblModel.X = Padding;
            lblModel.Y = y + 2;
            AddChild(lblModel);

            tbModelName = new EditorTextBox(WindowManager);
            tbModelName.X = Padding + LabelWidth;
            tbModelName.Y = y;
            tbModelName.Width = Width - Padding * 2 - LabelWidth;
            tbModelName.Text = config.ModelName;
            AddChild(tbModelName);
            y += 35;

            // Test button
            btnTest = new EditorButton(WindowManager);
            btnTest.Text = "测试连接";
            btnTest.Width = 80;
            btnTest.X = Padding;
            btnTest.Y = y;
            btnTest.LeftClick += BtnTest_LeftClick;
            AddChild(btnTest);

            lblTestResult = new XNALabel(WindowManager);
            lblTestResult.Text = "";
            lblTestResult.X = btnTest.Right + Padding;
            lblTestResult.Y = y + 4;
            AddChild(lblTestResult);
            y += 40;

            // Help text
            var lblHelp = new XNALabel(WindowManager);
            lblHelp.Text = "支持任何 OpenAI 兼容 API (百炼、MIMO、DeepSeek 等)";
            lblHelp.X = Padding;
            lblHelp.Y = y;
            lblHelp.TextColor = new Color(150, 150, 150);
            AddChild(lblHelp);
            y += 25;

            // Save / Cancel buttons
            btnSave = new EditorButton(WindowManager);
            btnSave.Text = "保存";
            btnSave.Width = 80;
            btnSave.X = Width - btnSave.Width * 2 - Padding * 2;
            btnSave.Y = Height - Constants.UIButtonHeight - Padding;
            btnSave.LeftClick += BtnSave_LeftClick;
            AddChild(btnSave);

            btnCancel = new EditorButton(WindowManager);
            btnCancel.Text = "取消";
            btnCancel.Width = 80;
            btnCancel.X = Width - btnCancel.Width - Padding;
            btnCancel.Y = Height - Constants.UIButtonHeight - Padding;
            btnCancel.LeftClick += (s, e) => Disable();
            AddChild(btnCancel);

            base.Initialize();
            CenterOnParent();
        }

        private void BtnSave_LeftClick(object sender, EventArgs e)
        {
            var config = new AIConfig
            {
                ApiEndpoint = tbEndpoint.Text?.Trim() ?? string.Empty,
                ApiKey = tbApiKey.Text?.Trim() ?? string.Empty,
                ModelName = tbModelName.Text?.Trim() ?? string.Empty
            };

            chatService.UpdateConfig(config);
            Disable();
        }

        private async void BtnTest_LeftClick(object sender, EventArgs e)
        {
            lblTestResult.Text = "测试中...";
            lblTestResult.TextColor = new Color(200, 200, 200);

            var testConfig = new AIConfig
            {
                ApiEndpoint = tbEndpoint.Text?.Trim() ?? string.Empty,
                ApiKey = tbApiKey.Text?.Trim() ?? string.Empty,
                ModelName = tbModelName.Text?.Trim() ?? string.Empty
            };

            if (!testConfig.IsConfigured)
            {
                lblTestResult.Text = "✗ 请填写所有字段";
                lblTestResult.TextColor = new Color(255, 120, 120);
                return;
            }

            try
            {
                var provider = new OpenAICompatibleProvider(testConfig);
                var messages = new System.Collections.Generic.List<ChatMessage>
                {
                    ChatMessage.User("Hello, reply with just 'OK'.")
                };
                await provider.ChatAsync("You are a test assistant. Reply with just 'OK'.", messages);

                lblTestResult.Text = "✓ 连接成功!";
                lblTestResult.TextColor = new Color(120, 255, 120);
            }
            catch (Exception ex)
            {
                lblTestResult.Text = "✗ " + ex.Message;
                lblTestResult.TextColor = new Color(255, 120, 120);
            }
        }
    }
}
