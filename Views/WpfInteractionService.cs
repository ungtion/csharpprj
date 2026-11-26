using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace CSharpProject;

internal sealed class WpfInteractionService(Window owner) : IInteractionService
{
    public string? SelectSavePath(string title, string fileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = fileName,
            Filter = filter
        };

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public void SetClipboardText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException exception)
        {
            throw new InvalidOperationException($"剪贴板不可用：{exception.Message}", exception);
        }
    }
}
