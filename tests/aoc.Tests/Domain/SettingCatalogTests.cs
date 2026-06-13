using aoc.Domain;
using FluentAssertions;
using Xunit;

namespace aoc.Tests.Domain;

public class SettingCatalogTests
{
    [Fact]
    public void All_ContainsAllExpectedSettings()
    {
        var allKeys = SettingCatalog.All.Keys.OrderBy(k => k).ToArray();

        allKeys.Should().BeEquivalentTo(new[]
        {
            "color-gamut",
            "gamma", "hdr",
            "low-blue",
            "overdrive",
        });
    }

    [Fact]
    public void All_HasCorrectTotalCount()
    {
        SettingCatalog.All.Should().HaveCount(5);
    }

    [Fact]
    public void TryGet_IsCaseSensitive()
    {
        // TryGet expects already-normalized (lowercased) keys;
        // case normalization is the caller's responsibility.
        var ok = SettingCatalog.TryGet("hdr", out var def);
        ok.Should().BeTrue();
        def!.Name.Should().Be("hdr");

        var notOk = SettingCatalog.TryGet("HdR", out _);
        notOk.Should().BeFalse();
    }

    [Fact]
    public void TryGet_UnknownKey_ReturnsFalse()
    {
        var ok = SettingCatalog.TryGet("no-such-setting", out var def);
        ok.Should().BeFalse();
        def.Should().BeNull();
    }

    [Fact]
    public void TryGet_NullKey_ThrowsArgumentNullException()
    {
        var act = () => SettingCatalog.TryGet(null!, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Category: 图像 ──

    public static TheoryData<string, string, string> ImageSettings => new()
    {
        { "gamma",       "Gamma 值", "图像" },
        { "low-blue",     "低蓝光模式", "图像" },
        { "overdrive",    "响应速度 OverDrive", "图像" },
    };

    [Theory]
    [MemberData(nameof(ImageSettings))]
    public void ImageSettings_HaveCorrectAttributes(string key, string expectedDesc, string expectedCategory)
    {
        var ok = SettingCatalog.TryGet(key, out var def);
        ok.Should().BeTrue();
        def!.Description.Should().Be(expectedDesc);
        def.Category.Should().Be(expectedCategory);
        def.Name.Should().Be(key);
    }

    [Fact]
    public void Overdrive_HasExpectedEnumMap()
    {
        var ok = SettingCatalog.TryGet("overdrive", out var def);
        ok.Should().BeTrue();
        def!.EnumMap.Should().NotBeNull();
        def.EnumMap.Should().HaveCount(5);
        def.EnumMap["off"].Should().Be("0");
        def.EnumMap["weak"].Should().Be("1");
        def.EnumMap["medium"].Should().Be("2");
        def.EnumMap["strong"].Should().Be("3");
        def.EnumMap["strongest"].Should().Be("4");
    }

    [Fact]
    public void Gamma_HasExpectedEnumMap()
    {
        var ok = SettingCatalog.TryGet("gamma", out var def);
        ok.Should().BeTrue();
        def!.EnumMap.Should().NotBeNull();
        def.EnumMap.Should().HaveCount(3);
        def.EnumMap["1"].Should().Be("0");
        def.EnumMap["2"].Should().Be("1");
        def.EnumMap["3"].Should().Be("2");
    }

    [Fact]
    public void LowBlue_HasExpectedEnumMap()
    {
        var ok = SettingCatalog.TryGet("low-blue", out var def);
        ok.Should().BeTrue();
        def!.EnumMap.Should().NotBeNull();
        def.EnumMap.Should().HaveCount(5);
        def.EnumMap["关闭"].Should().Be("0");
        def.EnumMap["多媒体"].Should().Be("1");
        def.EnumMap["网络"].Should().Be("2");
        def.EnumMap["办公室"].Should().Be("3");
        def.EnumMap["阅读"].Should().Be("4");
        def.Getter.Should().Be("LowBlueModel");
    }

    // ── Category: 色彩 ──

    public static TheoryData<string, string> ColorSettings => new()
    {
        { "color-gamut",        "色彩空间" },
    };

    [Theory]
    [MemberData(nameof(ColorSettings))]
    public void ColorSettings_HaveCorrectAttributes(string key, string expectedDesc)
    {
        var ok = SettingCatalog.TryGet(key, out var def);
        ok.Should().BeTrue();
        def!.Description.Should().Be(expectedDesc);
        def.Category.Should().Be("色彩");
    }

    [Fact]
    public void ColorGamut_HasExpectedEnumMap()
    {
        var ok = SettingCatalog.TryGet("color-gamut", out var def);
        ok.Should().BeTrue();
        def!.EnumMap.Should().HaveCount(2);
        def.EnumMap["srgb"].Should().Be("2");
        def.EnumMap["standard"].Should().Be("0");
        def.PrerequisiteSetting.Should().Be("hdr");
        def.PrerequisiteValueSet.Should().Contain("0");
    }

    // ── Category: HDR ──

    [Fact]
    public void Hdr_HasExpectedAttributes()
    {
        var ok = SettingCatalog.TryGet("hdr", out var def);
        ok.Should().BeTrue();
        def!.Description.Should().Be("HDR 模式");
        def.Category.Should().Be("HDR");
        def.Getter.Should().Be("GetHDR");
        def.EnumMap.Should().HaveCount(5);
        def.EnumMap["关闭"].Should().Be("0");
        def.EnumMap["DisplayHDR"].Should().Be("1");
        def.EnumMap["图片"].Should().Be("2");
        def.EnumMap["电影"].Should().Be("3");
        def.EnumMap["游戏"].Should().Be("4");
    }




}
