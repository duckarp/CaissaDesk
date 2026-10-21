using CaissaDesk.Localization;
using Xunit;

namespace CaissaDesk.Tests;

public class LocalizationServiceTests
{
    [Fact]
    public void BuiltInLanguages_IncludesEnglishAndSlovak()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);
        var languages = loc.SupportedLanguages;

        Assert.Contains(languages, l => l.Code == "en");
        Assert.Contains(languages, l => l.Code == "sk");
    }

    [Fact]
    public void EnglishAndSlovak_HaveMatchingKeySets()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);

        // Retrieve private dictionary keys via reflection or test keys
        var enStrings = typeof(LocalizationService)
            .GetMethod("GetEnglishStrings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, null) as Dictionary<string, string>;

        var skStrings = typeof(LocalizationService)
            .GetMethod("GetSlovakStrings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, null) as Dictionary<string, string>;

        Assert.NotNull(enStrings);
        Assert.NotNull(skStrings);

        var missingInSlovak = enStrings.Keys.Except(skStrings.Keys).ToList();
        var missingInEnglish = skStrings.Keys.Except(enStrings.Keys).ToList();

        Assert.Empty(missingInSlovak);
        Assert.Empty(missingInEnglish);
        Assert.Equal(enStrings.Count, skStrings.Count);
    }

    [Fact]
    public async Task ConfirmationKeys_ExistInBothLanguages()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);
        string[] requiredKeys = [
            "Confirm_DeleteTournamentTitle",
            "Confirm_DeleteTournamentMsg",
            "Confirm_ArchiveTournamentTitle",
            "Confirm_ArchiveTournamentMsg",
            "Confirm_UnarchiveTournamentTitle",
            "Confirm_UnarchiveTournamentMsg",
            "Confirm_DeletePlayerTitle",
            "Confirm_DeletePlayerMsg",
            "Confirm_DeletePairingTitle",
            "Confirm_DeletePairingMsg",
            "Confirm_RegeneratePairingsTitle",
            "Confirm_RegeneratePairingsMsg",
            "Confirm_ResetRoundTitle",
            "Confirm_ResetRoundMsg",
            "Confirm_DeleteRoundTitle",
            "Confirm_DeleteRoundMsg",
            "Confirm_CannotUndo"
        ];

        foreach (var key in requiredKeys)
        {
            await loc.SetLanguageAsync("en");
            var enVal = loc[key];
            Assert.False(string.IsNullOrWhiteSpace(enVal));
            Assert.NotEqual(key, enVal);

            await loc.SetLanguageAsync("sk");
            var skVal = loc[key];
            Assert.False(string.IsNullOrWhiteSpace(skVal));
            Assert.NotEqual(key, skVal);
        }
    }

    [Fact]
    public async Task SwitchLanguage_ChangesCurrentLanguageAndNotifies()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);
        bool eventFired = false;
        loc.OnLanguageChanged += () => eventFired = true;

        Assert.Equal("en", loc.CurrentLanguage);
        Assert.Equal("Tournaments", loc["Nav_Tournaments"]);

        await loc.SetLanguageAsync("sk");

        Assert.True(eventFired);
        Assert.Equal("sk", loc.CurrentLanguage);
        Assert.Equal("Turnaje", loc["Nav_Tournaments"]);
        Assert.Equal("Štartová listina hráčov", loc["Roster_Title"]);
        Assert.Equal("Prejsť na šachovnice 3. kola", loc["Warning_GoToBoardTable", 3]);
    }

    [Fact]
    public void FormattedStrings_InterpolateProperly()
    {
        var loc = new LocalizationService(loadPersistedSettings: false);

        string enFormatted = loc["Warning_CannotPairDesc", 2, 4];
        Assert.Contains("Round 2 still has 4 match(es)", enFormatted);

        string skFormatted = loc["Warning_CannotPairDesc", 2, 4]; // fallback or english
        Assert.NotEmpty(skFormatted);
    }
}
