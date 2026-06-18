using dvx.Config;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class PublisherPrefixResolutionTests
    {
        [Fact]
        public void NoSolution_UsesConfiguredPrefix_AndNeverHitsDataverse()
        {
            var lookupCalled = false;

            var (prefix, warning) = PublisherPrefixResolution.Resolve(
                "cfgpub", null, _ => { lookupCalled = true; return "x"; });

            prefix.ShouldBe("cfgpub");
            warning.ShouldBeNull();
            lookupCalled.ShouldBeFalse(); // no solution → no solution lookup
        }

        [Fact]
        public void NoSolution_NoConfiguredPrefix_Throws()
        {
            var ex = Should.Throw<InvalidOperationException>(() =>
                PublisherPrefixResolution.Resolve(null, null, _ => "x"));

            ex.Message.ShouldContain("publisher prefix");
        }

        [Fact]
        public void Solution_DerivesPrefixFromSolution_NoWarning()
        {
            string? queried = null;

            var (prefix, warning) = PublisherPrefixResolution.Resolve(
                null, "my_solution", s => { queried = s; return "solpub"; });

            prefix.ShouldBe("solpub");
            warning.ShouldBeNull();
            queried.ShouldBe("my_solution");
        }

        [Fact]
        public void Solution_AndConfiguredPrefix_SolutionWins_WithWarning()
        {
            var (prefix, warning) = PublisherPrefixResolution.Resolve(
                "cfgpub", "my_solution", _ => "solpub");

            prefix.ShouldBe("solpub");
            warning.ShouldNotBeNull();
            warning!.ShouldContain("ignored");
            warning.ShouldContain("solpub");
        }
    }
}
