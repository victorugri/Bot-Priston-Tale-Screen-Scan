using BotPriston.Platform.Native;

namespace BotPriston.Platform.Window;

public static class ProcessElevation
{
    public static bool CurrentIsElevated => Environment.IsPrivilegedProcess;

    /// <summary>
    /// True/false if the process' elevation could be read; null if access was denied — which,
    /// from a non-elevated process, usually means the target IS elevated.
    /// </summary>
    public static bool? IsElevated(int processId)
    {
        IntPtr process = AdvApi.OpenProcess(AdvApi.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!AdvApi.OpenProcessToken(process, AdvApi.TOKEN_QUERY, out var token)) return null;
            try
            {
                return AdvApi.GetTokenInformation(token, AdvApi.TokenElevation, out int elevated, sizeof(int), out _)
                    ? elevated != 0
                    : null;
            }
            finally
            {
                AdvApi.CloseHandle(token);
            }
        }
        finally
        {
            AdvApi.CloseHandle(process);
        }
    }

    /// <summary>
    /// Windows silently drops input sent from a normal process to an elevated one (UIPI).
    /// Returns a warning message when that is (probably) the case, else null.
    /// </summary>
    public static string? InputBlockedWarning(int gameProcessId)
    {
        if (CurrentIsElevated) return null;
        return IsElevated(gameProcessId) switch
        {
            true => "The game runs as administrator but the bot doesn't: Windows will silently drop the bot's keys and clicks. Run the bot as administrator.",
            null => "Could not read the game's privileges; if it runs as administrator, the bot must too or its input will be ignored.",
            false => null,
        };
    }
}
