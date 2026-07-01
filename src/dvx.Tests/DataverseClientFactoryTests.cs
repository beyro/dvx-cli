using dvx.Services;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class DataverseClientFactoryTests
    {
        [Fact]
        public void TokenCachePath_IsUnderLocalAppData_NotTheRepo()
        {
            var path = DataverseClientFactory.TokenCachePath();

            path.ShouldBe(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dvx", "msal_cache.data"));
            path.ShouldNotContain(Directory.GetCurrentDirectory());
        }
    }
}
