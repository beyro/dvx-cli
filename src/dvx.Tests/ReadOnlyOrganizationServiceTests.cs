using dvx.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class ReadOnlyOrganizationServiceTests
    {
        private readonly IOrganizationService _inner = Substitute.For<IOrganizationService>();

        private ReadOnlyOrganizationService Wrap() => new(_inner);

        [Fact]
        public void Create_DoesNotForward_ReturnsEmptyGuid()
        {
            Wrap().Create(new Entity("account")).ShouldBe(Guid.Empty);
            _inner.DidNotReceiveWithAnyArgs().Create(default!);
        }

        [Fact]
        public void Update_Delete_Associate_Disassociate_DoNotForward()
        {
            var svc = Wrap();

            svc.Update(new Entity("account"));
            svc.Delete("account", Guid.NewGuid());
            svc.Associate("account", Guid.NewGuid(), new Relationship("r"), new EntityReferenceCollection());
            svc.Disassociate("account", Guid.NewGuid(), new Relationship("r"), new EntityReferenceCollection());

            _inner.DidNotReceiveWithAnyArgs().Update(default!);
            _inner.DidNotReceiveWithAnyArgs().Delete(default!, default);
            _inner.DidNotReceiveWithAnyArgs().Associate(default!, default, default!, default!);
            _inner.DidNotReceiveWithAnyArgs().Disassociate(default!, default, default!, default!);
        }

        [Fact]
        public void Execute_DoesNotForward_EvenForMutatingRequest()
        {
            // PublishXml and AddSolutionComponent are issued via Execute; a read-only decorator
            // must not forward them, or dry-run could still write.
            var response = Wrap().Execute(new OrganizationRequest("PublishXml"));

            response.ShouldNotBeNull();
            _inner.DidNotReceiveWithAnyArgs().Execute(default!);
        }

        [Fact]
        public void Retrieve_And_RetrieveMultiple_AreForwarded()
        {
            var svc  = Wrap();
            var id   = Guid.NewGuid();
            var cols = new ColumnSet(true);
            var query = new QueryExpression("account");
            _inner.Retrieve("account", id, cols).Returns(new Entity("account", id));
            _inner.RetrieveMultiple(query).Returns(new EntityCollection());

            svc.Retrieve("account", id, cols);
            svc.RetrieveMultiple(query);

            _inner.Received(1).Retrieve("account", id, cols);
            _inner.Received(1).RetrieveMultiple(query);
        }
    }
}
