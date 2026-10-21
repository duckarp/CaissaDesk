namespace CaissaDesk.Localization;

public interface ILocalizationService
{
    string CurrentLanguage { get; }
    IReadOnlyList<LanguageInfo> SupportedLanguages { get; }
    string this[string key] { get; }
    string this[string key, params object[] args] { get; }
    Task SetLanguageAsync(string languageCode);
    void RegisterLanguage(LanguageInfo info, IReadOnlyDictionary<string, string> strings);
    event Action? OnLanguageChanged;
}
