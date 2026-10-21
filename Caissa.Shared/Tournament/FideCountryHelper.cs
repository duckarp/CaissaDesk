using System.Text.RegularExpressions;

namespace Caissa.Shared.Models;

public static class FideCountryHelper
{
    private static readonly Dictionary<string, string> CountryNameToCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Slovakia"] = "SVK",
        ["Slovak Republic"] = "SVK",
        ["Czech Republic"] = "CZE",
        ["Czechia"] = "CZE",
        ["Austria"] = "AUT",
        ["Poland"] = "POL",
        ["Hungary"] = "HUN",
        ["Germany"] = "GER",
        ["Ukraine"] = "UKR",
        ["France"] = "FRA",
        ["United States"] = "USA",
        ["United States of America"] = "USA",
        ["USA"] = "USA",
        ["England"] = "ENG",
        ["Scotland"] = "SCO",
        ["Wales"] = "WLS",
        ["Ireland"] = "IRL",
        ["Northern Ireland"] = "IRL",
        ["Spain"] = "ESP",
        ["Italy"] = "ITA",
        ["Switzerland"] = "SUI",
        ["Netherlands"] = "NED",
        ["Belgium"] = "BEL",
        ["Norway"] = "NOR",
        ["Sweden"] = "SWE",
        ["Finland"] = "FIN",
        ["Denmark"] = "DEN",
        ["Croatia"] = "CRO",
        ["Slovenia"] = "SLO",
        ["Serbia"] = "SRB",
        ["Romania"] = "ROU",
        ["Bulgaria"] = "BUL",
        ["Greece"] = "GRE",
        ["Turkey"] = "TUR",
        ["Turkiye"] = "TUR",
        ["Türkiye"] = "TUR",
        ["Russia"] = "RUS",
        ["Russian Federation"] = "RUS",
        ["Armenia"] = "ARM",
        ["Azerbaijan"] = "AZE",
        ["Georgia"] = "GEO",
        ["Kazakhstan"] = "KAZ",
        ["Uzbekistan"] = "UZB",
        ["Israel"] = "ISR",
        ["Canada"] = "CAN",
        ["Brazil"] = "BRA",
        ["Argentina"] = "ARG",
        ["Australia"] = "AUS",
        ["New Zealand"] = "NZL",
        ["China"] = "CHN",
        ["India"] = "IND",
        ["Japan"] = "JPN",
        ["South Korea"] = "KOR",
        ["Korea"] = "KOR",
        ["North Korea"] = "PRK",
        ["Belarus"] = "BLR",
        ["Moldova"] = "MDA",
        ["Lithuania"] = "LTU",
        ["Latvia"] = "LAT",
        ["Estonia"] = "EST",
        ["Iceland"] = "ISL",
        ["Portugal"] = "POR",
        ["Cyprus"] = "CYP",
        ["Malta"] = "MLT",
        ["Luxembourg"] = "LUX",
        ["Monaco"] = "MON",
        ["Liechtenstein"] = "LIE",
        ["Andorra"] = "AND",
        ["San Marino"] = "SMR",
        ["Bosnia and Herzegovina"] = "BIH",
        ["Bosnia & Herzegovina"] = "BIH",
        ["North Macedonia"] = "MKD",
        ["Macedonia"] = "MKD",
        ["Montenegro"] = "MNE",
        ["Albania"] = "ALB",
        ["Kosovo"] = "KOS",
        ["Egypt"] = "EGY",
        ["Morocco"] = "MAR",
        ["South Africa"] = "RSA",
        ["Nigeria"] = "NGR",
        ["Algeria"] = "ALG",
        ["Tunisia"] = "TUN",
        ["Mexico"] = "MEX",
        ["Cuba"] = "CUB",
        ["Colombia"] = "COL",
        ["Chile"] = "CHI",
        ["Peru"] = "PER",
        ["Venezuela"] = "VEN",
        ["Philippines"] = "PHI",
        ["Indonesia"] = "INA",
        ["Vietnam"] = "VIE",
        ["Singapore"] = "SGP",
        ["Malaysia"] = "MAS",
        ["Thailand"] = "THA",
        ["Iran"] = "IRI",
        ["Iraq"] = "IRQ",
        ["Saudi Arabia"] = "KSA",
        ["United Arab Emirates"] = "UAE",
        ["UAE"] = "UAE",
        ["Qatar"] = "QAT",
        ["Kuwait"] = "KUW",
        ["Jordan"] = "JOR",
        ["Lebanon"] = "LBN",
        ["Syria"] = "SYR",
        ["Mongolia"] = "MGL",
        ["Chinese Taipei"] = "TPE",
        ["Taiwan"] = "TPE",
        ["Hong Kong"] = "HKG",
        ["Bangladesh"] = "BAN",
        ["Sri Lanka"] = "SRI",
        ["Nepal"] = "NEP",
        ["Pakistan"] = "PAK",
        ["Faroe Islands"] = "FAI",
        ["Guernsey"] = "GGY",
        ["Jersey"] = "JCI",
        ["Isle of Man"] = "IOM",
        ["Gibraltar"] = "GIB",
        ["FIDE"] = "FID"
    };

    private static readonly Dictionary<string, string> Iso2ToCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sk"] = "SVK",
        ["cz"] = "CZE",
        ["at"] = "AUT",
        ["pl"] = "POL",
        ["hu"] = "HUN",
        ["de"] = "GER",
        ["ua"] = "UKR",
        ["fr"] = "FRA",
        ["us"] = "USA",
        ["gb"] = "ENG",
        ["es"] = "ESP",
        ["it"] = "ITA",
        ["ch"] = "SUI",
        ["nl"] = "NED",
        ["be"] = "BEL",
        ["no"] = "NOR",
        ["se"] = "SWE",
        ["fi"] = "FIN",
        ["dk"] = "DEN",
        ["hr"] = "CRO",
        ["si"] = "SLO",
        ["rs"] = "SRB",
        ["ro"] = "ROU",
        ["bg"] = "BUL",
        ["gr"] = "GRE",
        ["tr"] = "TUR",
        ["ru"] = "RUS",
        ["am"] = "ARM",
        ["az"] = "AZE",
        ["ge"] = "GEO",
        ["kz"] = "KAZ",
        ["uz"] = "UZB",
        ["il"] = "ISR",
        ["ca"] = "CAN",
        ["br"] = "BRA",
        ["ar"] = "ARG",
        ["au"] = "AUS",
        ["nz"] = "NZL",
        ["cn"] = "CHN",
        ["in"] = "IND",
        ["jp"] = "JPN",
        ["kr"] = "KOR",
        ["by"] = "BLR",
        ["md"] = "MDA",
        ["lt"] = "LTU",
        ["lv"] = "LAT",
        ["ee"] = "EST",
        ["is"] = "ISL",
        ["pt"] = "POR",
        ["cy"] = "CYP",
        ["mt"] = "MLT",
        ["lu"] = "LUX",
        ["mc"] = "MON",
        ["li"] = "LIE",
        ["ad"] = "AND",
        ["sm"] = "SMR",
        ["ba"] = "BIH",
        ["mk"] = "MKD",
        ["me"] = "MNE",
        ["al"] = "ALB",
        ["xk"] = "KOS",
        ["eg"] = "EGY",
        ["ma"] = "MAR",
        ["za"] = "RSA",
        ["ng"] = "NGR",
        ["dz"] = "ALG",
        ["tn"] = "TUN",
        ["mx"] = "MEX",
        ["cu"] = "CUB",
        ["co"] = "COL",
        ["cl"] = "CHI",
        ["pe"] = "PER",
        ["ve"] = "VEN",
        ["ph"] = "PHI",
        ["id"] = "INA",
        ["vn"] = "VIE",
        ["sg"] = "SGP",
        ["my"] = "MAS",
        ["th"] = "THA",
        ["ir"] = "IRI",
        ["iq"] = "IRQ",
        ["sa"] = "KSA",
        ["ae"] = "UAE",
        ["qa"] = "QAT",
        ["kw"] = "KUW",
        ["jo"] = "JOR",
        ["lb"] = "LBN",
        ["sy"] = "SYR",
        ["mn"] = "MGL",
        ["tw"] = "TPE",
        ["hk"] = "HKG",
        ["bd"] = "BAN",
        ["lk"] = "SRI",
        ["np"] = "NEP",
        ["pk"] = "PAK",
        ["fo"] = "FAI",
        ["gg"] = "GGY",
        ["je"] = "JCI",
        ["im"] = "IOM",
        ["gi"] = "GIB"
    };

    public static string? ToFideCode(string? countryOrFed)
    {
        if (string.IsNullOrWhiteSpace(countryOrFed)) return null;
        string trimmed = countryOrFed.Trim();

        // 1. If 3 letters and already uppercase or standard
        if (trimmed.Length == 3 && trimmed.All(char.IsLetter))
        {
            return trimmed.ToUpperInvariant();
        }

        // 2. Direct name match
        if (CountryNameToCode.TryGetValue(trimmed, out var codeFromName))
        {
            return codeFromName;
        }

        // 3. Remove diacritics and try again
        string unaccented = Player.RemoveDiacritics(trimmed);
        if (CountryNameToCode.TryGetValue(unaccented, out var codeFromUnaccented))
        {
            return codeFromUnaccented;
        }

        // 4. ISO-2 match
        if (trimmed.Length == 2 && Iso2ToCode.TryGetValue(trimmed, out var codeFromIso2))
        {
            return codeFromIso2;
        }

        return null;
    }

    public static string? TryExtractFromFlagUrl(string? flagUrl)
    {
        if (string.IsNullOrWhiteSpace(flagUrl)) return null;

        // Pattern 1: /svg/SVK.svg
        var match3 = Regex.Match(flagUrl, @"/svg/([A-Za-z]{3})\.svg", RegexOptions.IgnoreCase);
        if (match3.Success)
        {
            return match3.Groups[1].Value.ToUpperInvariant();
        }

        // Pattern 2: /images/flags/sk.svg or /flags/sk.svg
        var match2 = Regex.Match(flagUrl, @"/flags/([a-z]{2})\.svg", RegexOptions.IgnoreCase);
        if (match2.Success)
        {
            string iso2 = match2.Groups[1].Value.ToLowerInvariant();
            if (Iso2ToCode.TryGetValue(iso2, out var code))
            {
                return code;
            }
        }

        return null;
    }
}
