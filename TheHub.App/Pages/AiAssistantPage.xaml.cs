using System;
using System.Windows;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Windows.Input;
using TheHub.Core.AI;

namespace TheHub.App.Pages;

public partial class AiAssistantPage : WpfUserControl
{
    private AIAssistantModule? _aiAssistantModule;

    public AiAssistantPage()
    {
        InitializeComponent();
    }

    public void Initialize(AIAssistantModule aiAssistantModule)
    {
        _aiAssistantModule = aiAssistantModule;
    }

    private async void OnAiExecuteClick(object sender, RoutedEventArgs e)
    {
        if (_aiAssistantModule == null) return;
        string prompt = AiInputTextBox.Text.Trim();
        if (string.IsNullOrEmpty(prompt)) return;

        AiOutputTextBlock.Text = "⏳ Processing prompt through security sandbox...";
        var result = await _aiAssistantModule.ProcessPromptAsync(prompt);
        if (result.Success)
        {
            AiOutputTextBlock.Text = $"✅ {result.Message}";
        }
        else
        {
            AiOutputTextBlock.Text = $"⛔ {result.Message}";
        }
    }

    private void OnAiInputKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAiExecuteClick(sender, e);
        }
    }
}
