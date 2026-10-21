using CaissaDesk.Data;

namespace CaissaDesk.Tests;

public class NationalScoutingServiceTests
{
    [Fact]
    public void ParseSlovakSearchResultsHtml_ExtractsOliverKamenicky()
    {
        string sampleHtml = """
            <div class=" datagridphp_item parne " id="p_src_13864">
                <div style="white-space:nowrap;" id="column_nr_1" class="datagridphp_column_class datagridphp_column_class_1">13864</div>
                <div id="column_nr_2" class="datagridphp_column_class datagridphp_column_class_2"><a href="clen-13864-kamenicky-oliver">Kamenický, Oliver</a></div>
                <div id="column_nr_3" class="datagridphp_column_class datagridphp_column_class_3"><a href="index.php?str=clenovia&klubId=60702">ŠK Považské Podhradie</a></div>
                <div style="white-space:nowrap;" id="column_nr_4" class="datagridphp_column_class datagridphp_column_class_4">1060</div>
                <div style="white-space:nowrap;" id="column_nr_5" class="datagridphp_column_class datagridphp_column_class_5">549005456</div>
                <div style="white-space:nowrap;" id="column_nr_6" class="datagridphp_column_class datagridphp_column_class_6">5 &euro;</div>
            </div>
            """;

        var list = NationalScoutingService.ParseSlovakSearchResultsHtml(sampleHtml);

        Assert.Single(list);
        var player = list[0];
        Assert.Equal("13864", player.NationalId);
        Assert.Equal("Kamenický, Oliver", player.FullName);
        Assert.Equal("ŠK Považské Podhradie", player.Club);
        Assert.Equal(1060, player.NationalRating);
        Assert.Equal("549005456", player.FideId);
        Assert.Equal("SVK", player.Federation);
    }

    [Fact]
    public void ParseSlovakSearchResultsHtml_ExtractsTitleAndMultiplePlayers()
    {
        string sampleHtml = """
            <div class=" datagridphp_item parne " id="p_src_5082">
                <div style="white-space:nowrap;" id="column_nr_1" class="datagridphp_column_class datagridphp_column_class_1">5082</div>
                <div id="column_nr_2" class="datagridphp_column_class datagridphp_column_class_2"><a href="clen-5082-gazik-viktor">GM Gažík, Viktor</a></div>
                <div id="column_nr_3" class="datagridphp_column_class datagridphp_column_class_3"><a href="index.php?str=clenovia&klubId=60809">Bojnická šachová akadémia</a></div>
                <div style="white-space:nowrap;" id="column_nr_4" class="datagridphp_column_class datagridphp_column_class_4"><a href="https://ratings.fide.com/profile/14928752">2549</a></div>
                <div style="white-space:nowrap;" id="column_nr_5" class="datagridphp_column_class datagridphp_column_class_5">14928752</div>
                <div style="white-space:nowrap;" id="column_nr_6" class="datagridphp_column_class datagridphp_column_class_6">20 &euro;</div>
            </div>
            <div class=" datagridphp_item neparne " id="p_src_2285">
                <div style="white-space:nowrap;" id="column_nr_7" class="datagridphp_column_class datagridphp_column_class_1">2285</div>
                <div id="column_nr_8" class="datagridphp_column_class datagridphp_column_class_2"><a href="clen-2285-gazik-renata-irena">WIM Gažík, Renata Irena</a></div>
                <div id="column_nr_9" class="datagridphp_column_class datagridphp_column_class_3"><a href="index.php?str=clenovia&klubId=60809">Bojnická šachová akadémia</a></div>
                <div style="white-space:nowrap;" id="column_nr_10" class="datagridphp_column_class datagridphp_column_class_4"></div>
                <div style="white-space:nowrap;" id="column_nr_11" class="datagridphp_column_class datagridphp_column_class_5">1104837</div>
                <div style="white-space:nowrap;" id="column_nr_12" class="datagridphp_column_class datagridphp_column_class_6">5 &euro;</div>
            </div>
            """;

        var list = NationalScoutingService.ParseSlovakSearchResultsHtml(sampleHtml);

        Assert.Equal(2, list.Count);

        var p1 = list[0];
        Assert.Equal("5082", p1.NationalId);
        Assert.Equal("Gažík, Viktor", p1.FullName);
        Assert.Equal("Bojnická šachová akadémia", p1.Club);
        Assert.Equal(2549, p1.NationalRating);
        Assert.Equal("14928752", p1.FideId);

        var p2 = list[1];
        Assert.Equal("2285", p2.NationalId);
        Assert.Equal("Gažík, Renata Irena", p2.FullName);
        Assert.Null(p2.NationalRating); // empty in HTML
        Assert.Equal("1104837", p2.FideId);
    }

    [Fact]
    public void ParseSlovakSearchResultsHtml_DistinguishesBetweenOliverAndTomas()
    {
        string sampleHtml = """
            <div class=" datagridphp_item parne " id="p_src_13864"><div  style="white-space:nowrap;"  id="column_nr_1" class="datagridphp_column_class datagridphp_column_class_1">13864</div>
            <div  id="column_nr_2" class="datagridphp_column_class datagridphp_column_class_2"><a href="clen-13864-kamenicky-oliver">Kamenický, Oliver</a></div>
            <div  id="column_nr_3" class="datagridphp_column_class datagridphp_column_class_3"><a href="index.php?str=clenovia&klubId=60702">ŠK Považské Podhradie</a></div>
            <div  style="white-space:nowrap;"  id="column_nr_4" class="datagridphp_column_class datagridphp_column_class_4">1060</div>
            <div  style="white-space:nowrap;"  id="column_nr_5" class="datagridphp_column_class datagridphp_column_class_5">549005456</div>
            <div  style="white-space:nowrap;"  id="column_nr_6" class="datagridphp_column_class datagridphp_column_class_6">5 &euro;</div>
            </div>
            <div class=" datagridphp_item neparne " id="p_src_4232"><div  style="white-space:nowrap;"  id="column_nr_7" class="datagridphp_column_class datagridphp_column_class_1">4232</div>
            <div  id="column_nr_8" class="datagridphp_column_class datagridphp_column_class_2"><a href="clen-4232-kamenicky-tomas">Kamenický, Tomáš</a></div>
            <div  id="column_nr_9" class="datagridphp_column_class datagridphp_column_class_3"><a href="index.php?str=clenovia&klubId=60702">ŠK Považské Podhradie</a></div>
            <div  style="white-space:nowrap;"  id="column_nr_10" class="datagridphp_column_class datagridphp_column_class_4"><a href="https://ratings.fide.com/profile/14912392" target="newWin">1948</a></div>
            <div  style="white-space:nowrap;"  id="column_nr_11" class="datagridphp_column_class datagridphp_column_class_5">14912392</div>
            <div  style="white-space:nowrap;"  id="column_nr_12" class="datagridphp_column_class datagridphp_column_class_6">20 &euro;</div>
            </div>
            """;

        var list = NationalScoutingService.ParseSlovakSearchResultsHtml(sampleHtml);
        Assert.Equal(2, list.Count);

        // Match by FIDE ID
        var matchByFide = list.FirstOrDefault(p => p.FideId == "549005456");
        Assert.NotNull(matchByFide);
        Assert.Equal("13864", matchByFide.NationalId);
        Assert.Equal("Kamenický, Oliver", matchByFide.FullName);
        Assert.Equal(1060, matchByFide.NationalRating);

        // Match by First Name
        var matchByFirst = list.FirstOrDefault(p => p.FullName.Contains("Oliver"));
        Assert.NotNull(matchByFirst);
        Assert.Equal("13864", matchByFirst.NationalId);
    }
}
