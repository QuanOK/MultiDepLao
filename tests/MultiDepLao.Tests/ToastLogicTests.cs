using System.Xml.Linq;
using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class ToastLogicTests
{
    private static readonly DateTime T0 = new(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Xml_EscapesUserText_AndParses()
    {
        var xml = ToastLogic.BuildXml("Multi DépLào", "Shop <A> & \"B\" 'C'\u0001 có tin nhắn mới");
        var texts = XDocument.Parse(xml).Descendants("text").Select(e => e.Value).ToList();
        Assert.Equal(new[] { "Multi DépLào", "Shop <A> & \"B\" 'C' có tin nhắn mới" }, texts);
        Assert.Equal("ToastGeneric", XDocument.Parse(xml).Descendants("binding").Single().Attribute("template")!.Value);
        Assert.DoesNotContain("<A>", xml);
    }

    [Fact]
    public void Suppress_OnlyWithin5Seconds()
    {
        Assert.False(ToastLogic.ShouldSuppressPopup(null, T0));
        Assert.True(ToastLogic.ShouldSuppressPopup(T0, T0.AddSeconds(4.9)));
        Assert.False(ToastLogic.ShouldSuppressPopup(T0, T0.AddSeconds(5)));
        Assert.False(ToastLogic.ShouldSuppressPopup(T0, T0.AddSeconds(-1)));
    }

    [Fact]
    public void Tag_Truncated_RegistryUpdateOnlyWhenDifferent()
    {
        Assert.Equal(64, ToastLogic.NormalizeTag(new string('a', 100)).Length);
        Assert.Equal("acc-01", ToastLogic.NormalizeTag("acc-01"));
        Assert.False(ToastLogic.NeedsRegistryUpdate("N", "p", "N", "p"));
        Assert.True(ToastLogic.NeedsRegistryUpdate(null, "p", "N", "p"));
        Assert.True(ToastLogic.NeedsRegistryUpdate("N", "q", "N", "p"));
    }
}
