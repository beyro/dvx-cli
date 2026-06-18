using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    /// <summary>
    /// A decorator for <see cref="IOrganizationService"/> used for dry-run: it intercepts and ignores
    /// every mutation — Create, Update, Delete, Associate, Disassociate, and Execute (which carries write
    /// messages such as PublishXml / AddSolutionComponent). Only Retrieve and RetrieveMultiple pass through.
    /// </summary>
    public class ReadOnlyOrganizationService(IOrganizationService inner) : IOrganizationService
    {
        public Guid Create(Entity entity) => Guid.Empty;

        public void Update(Entity entity) { }

        public void Delete(string entityName, Guid id) { }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) 
            => inner.Retrieve(entityName, id, columnSet);

        public EntityCollection RetrieveMultiple(QueryBase query) 
            => inner.RetrieveMultiple(query);

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { }

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { }

        // Execute can carry mutating messages (PublishXml, AddSolutionComponent, CRUD-as-request),
        // so the read-only decorator must not forward it. Reads via Execute are not used on dry-run paths.
        public OrganizationResponse Execute(OrganizationRequest request) => new();
    }
}
