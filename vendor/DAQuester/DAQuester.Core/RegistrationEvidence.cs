namespace DAQuester.Core;

public static class RegistrationEvidence
{
    public static bool IsRefusal(string text) =>
        text.Contains("Please register your character to use this feature", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("This account must be registered", StringComparison.OrdinalIgnoreCase);

    public static bool IsFreshRefusal(CharacterSnapshot state, long afterRevision) =>
        state.Notice is { } notice && notice.Generation == state.Generation &&
        notice.Revision > afterRevision && IsRefusal(notice);

    // 1–4 and 6 are status-bar messages. Chat channels cannot establish account eligibility.
    public static bool IsRefusal(ServerNotice notice) =>
        notice.Type is 1 or 2 or 3 or 4 or 6 && IsRefusal(notice.Text);
}
