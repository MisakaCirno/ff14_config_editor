namespace UIMarkerEditor;

internal static class ToastService
{
    private static Action<string>? showSuccessToast;

    public static void RegisterSuccessHandler(Action<string> handler)
    {
        showSuccessToast = handler;
    }

    public static void UnregisterSuccessHandler(Action<string> handler)
    {
        if (showSuccessToast == handler)
        {
            showSuccessToast = null;
        }
    }

    public static void ShowSuccess(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        showSuccessToast?.Invoke(message);
    }
}

public sealed class ToastNotification
{
    public ToastNotification(string message)
    {
        Message = message;
    }

    public string Message { get; }
}