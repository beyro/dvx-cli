using dvx.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class SolutionPublisherResolverTests
    {
        private static IOrganizationService SvcReturning(EntityCollection result)
        {
            var svc = Substitute.For<IOrganizationService>();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "solution"))
               .Returns(result);
            return svc;
        }

        [Fact]
        public void GetCustomizationPrefix_ReturnsPublisherPrefix()
        {
            var sol = new Entity("solution", Guid.NewGuid());
            sol["publisher.customizationprefix"] =
                new AliasedValue("publisher", "customizationprefix", "contoso");
            var svc = SvcReturning(new EntityCollection(new List<Entity> { sol }));

            new SolutionPublisherResolver(svc).GetCustomizationPrefix("my_solution")
                .ShouldBe("contoso");
        }

        [Fact]
        public void GetCustomizationPrefix_SolutionNotFound_ThrowsContainingName()
        {
            var svc = SvcReturning(new EntityCollection());

            var ex = Should.Throw<InvalidOperationException>(() =>
                new SolutionPublisherResolver(svc).GetCustomizationPrefix("missing_solution"));

            ex.Message.ShouldContain("missing_solution");
        }

        [Fact]
        public void GetCustomizationPrefix_PublisherHasNoPrefix_Throws()
        {
            var sol = new Entity("solution", Guid.NewGuid()); // no aliased prefix attribute
            var svc = SvcReturning(new EntityCollection(new List<Entity> { sol }));

            Should.Throw<InvalidOperationException>(() =>
                new SolutionPublisherResolver(svc).GetCustomizationPrefix("my_solution"));
        }
    }
}
