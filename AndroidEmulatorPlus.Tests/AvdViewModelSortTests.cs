using System.Reflection;
using AndroidEmulatorPlus.ViewModels;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class AvdViewModelSortTests
{
    /// <summary>
    /// AvdViewModel.SystemImageSortKey is private static; reflection-invoke to keep the
    /// API stable while still asserting the sort order put in place by B-19.
    /// </summary>
    private static (int Api, int VariantRank, string Raw) SortKey(string img)
    {
        var mi = typeof(AvdViewModel).GetMethod("SystemImageSortKey",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = mi.Invoke(null, new object?[] { img })!;
        // ValueTuple<int, int, string> reflected as a boxed tuple
        var t = (System.Runtime.CompilerServices.ITuple)result;
        return ((int)t[0]!, (int)t[1]!, (string)t[2]!);
    }

    [Fact]
    public void Api_36_sorts_after_Api_9()
    {
        var k9  = SortKey("system-images;android-9;google_apis;x86_64");
        var k36 = SortKey("system-images;android-36;google_apis_playstore;x86_64");
        Assert.True(k36.Api > k9.Api);
    }

    [Fact]
    public void Playstore_outranks_google_apis_outranks_default_at_same_api()
    {
        var k1 = SortKey("system-images;android-36;default;x86_64");
        var k2 = SortKey("system-images;android-36;google_apis;x86_64");
        var k3 = SortKey("system-images;android-36;google_apis_playstore;x86_64");
        Assert.True(k1.VariantRank < k2.VariantRank);
        Assert.True(k2.VariantRank < k3.VariantRank);
    }
}
