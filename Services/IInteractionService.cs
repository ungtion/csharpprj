namespace CSharpProject;

internal interface IInteractionService
{
    string? SelectSavePath(string title, string fileName, string filter);
    void SetClipboardText(string text);
}
