using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    /// <summary>
    /// Represents a composite key that combines a Message ID and an Entity Logical Name.
    /// This key is used for uniquely identifying SDK message filters associated with
    /// specific entities.
    /// </summary>
    internal readonly record struct MessageEntityKey
    {
        public Guid MessageId { get; }
        public string EntityLogicalName { get; }

        public MessageEntityKey(Guid messageId, string entityLogicalName)
        {
            MessageId = messageId;
            EntityLogicalName = entityLogicalName.ToLowerInvariant();
        }
    }

    /// <summary>
    /// Loads SDK metadata lookups (messages, message filters, plugin types) shared by step
    /// registration (forward: name → id) and adoption (reverse: id → name). Raw message and
    /// filter records are fetched once and cached per instance.
    /// </summary>
    internal class SdkMetadata(IOrganizationService svc)
    {
        private List<Entity>? _messages;
        private List<Entity>? _customApis;
        private List<Entity>? _customActions;
        private Guid? _systemUserId;

        public Guid SystemUserId()
        {
            if (_systemUserId.HasValue) return _systemUserId.Value;

            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid"),
                TopCount = 1,
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("fullname", ConditionOperator.Equal, "SYSTEM")
                    }
                }
            };

            var users = svc.RetrieveMultiple(query)?.Entities;
            _systemUserId = users != null && users.Count > 0 ? users[0].Id : Guid.Empty;
            return _systemUserId.Value;
        }

        /// <summary>
        /// Gets a collection of SDK messages cached within the instance.
        /// </summary>
        /// <remarks>
        /// SDK messages represent operations or actions that can be performed using the system,
        /// such as Create, Update, or Delete. The collection is retrieved from the CRM organization
        /// service and includes both the unique identifiers (GUIDs) and names of the SDK messages.
        /// </remarks>
        private IReadOnlyList<Entity> SdkMessages =>
            _messages ??= svc.RetrieveMultiple(new QueryExpression("sdkmessage")
            {
                ColumnSet = new ColumnSet("sdkmessageid", "name")
            }).Entities.ToList();

        /// <summary>
        /// Gets a collection of Custom API entities cached within the instance.
        /// </summary>
        /// <remarks>
        /// The Custom API entities represent custom-defined operations in the system.
        /// These entities include attributes such as unique identifiers, unique names,
        /// associated plugin types, and SDK messages. The data is retrieved from the
        /// CRM organization service and cached for efficient access.
        /// </remarks>
        private IReadOnlyList<Entity> CustomApis =>
            _customApis ??= svc.RetrieveMultiple(new QueryExpression("customapi")
            {
                ColumnSet = new ColumnSet("customapiid", "uniquename", "plugintypeid", "sdkmessageid")
            }).Entities.ToList();

        /// <summary>
        /// Gets the Custom Action (process) definitions cached within the instance.
        /// </summary>
        /// <remarks>
        /// Custom Actions are <c>workflow</c> records with category Action (3) and type Definition (1).
        /// Each registers an SDK message named after the action's <c>uniquename</c>.
        /// </remarks>
        private IReadOnlyList<Entity> CustomActions =>
            _customActions ??= svc.RetrieveMultiple(new QueryExpression("workflow")
            {
                ColumnSet = new ColumnSet("workflowid", "uniquename"),
                Criteria  = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("category", ConditionOperator.Equal, 3), // Action
                        new ConditionExpression("type",     ConditionOperator.Equal, 1), // Definition
                    }
                }
            }).Entities.ToList();

        /// <summary>
        /// Maps message names to their corresponding GUIDs.
        /// </summary>
        /// <returns>A dictionary where keys are sdk message names (case-insensitive) and values are their respective GUIDs.</returns>
        public Dictionary<string, Guid> MessageIdByName()
        {
            var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in SdkMessages)
            {
                var name = e.GetAttributeValue<string>("name");
                if (name is not null) map[name] = e.Id;
            }
            return map;
        }

        /// <summary>
        /// Maps message GUIDs to their corresponding names.
        /// </summary>
        /// <returns>A dictionary where keys are message GUIDs and values are their case-sensitive names.</returns>
        public Dictionary<Guid, string> MessageNameById()
        {
            var map = new Dictionary<Guid, string>();
            foreach (var e in SdkMessages)
            {
                var name = e.GetAttributeValue<string>("name");
                if (name is not null) map[e.Id] = name;
            }
            return map;
        }

        /// <summary>
        /// Retrieves a mapping of SDK message filters keyed by <see cref="MessageEntityKey"/>
        /// (the message ID and entity logical name), where the value is the SDK message filter ID.
        /// </summary>
        /// <param name="entityNames">A collection of entity logical names to filter SDK message filters by.</param>
        /// <returns>
        /// A dictionary where the key is a <see cref="MessageEntityKey"/> of the SDK message ID and entity
        /// logical name, and the value is the SDK message filter ID.
        /// </returns>
        public Dictionary<MessageEntityKey, Guid> SdkFilterIdsByEntityNames(IReadOnlyCollection<string> entityNames)
        {
            var map = new Dictionary<MessageEntityKey, Guid>();
            if (entityNames.Count == 0) return map;

            var query = new QueryExpression("sdkmessagefilter")
            {
                ColumnSet = new ColumnSet("sdkmessagefilterid", "sdkmessageid", "primaryobjecttypecode"),
                Criteria  = new FilterExpression()
            };
            query.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.In, entityNames.Cast<object>().ToArray());

            foreach (var e in svc.RetrieveMultiple(query).Entities)
            {
                var msgRef = e.GetAttributeValue<EntityReference>("sdkmessageid");
                if (msgRef is null) continue;
                var entity = e.GetAttributeValue<string>("primaryobjecttypecode") ?? string.Empty;
                map.TryAdd(new MessageEntityKey(msgRef.Id, entity), e.Id);
            }
            return map;
        }

        /// <summary>
        /// Retrieves a mapping between SDK message filter IDs and their associated primary object type codes.
        /// </summary>
        /// <param name="filterIds">A collection of GUIDs representing the SDK message filter IDs to query.</param>
        /// <returns>A dictionary where keys are the SDK message filter IDs and values are their corresponding primary object type codes.</returns>
        public Dictionary<Guid, string> FilterEntityById(IReadOnlyCollection<Guid> filterIds)
        {
            var map = new Dictionary<Guid, string>();
            if (filterIds.Count == 0) return map;

            var query = new QueryExpression("sdkmessagefilter")
            {
                ColumnSet = new ColumnSet("sdkmessagefilterid", "primaryobjecttypecode"),
                Criteria  = new FilterExpression()
            };
            query.Criteria.AddCondition("sdkmessagefilterid", ConditionOperator.In, filterIds.Cast<object>().ToArray());

            foreach (var e in svc.RetrieveMultiple(query).Entities)
                map[e.Id] = e.GetAttributeValue<string>("primaryobjecttypecode") ?? string.Empty;

            return map;
        }

        /// <summary>
        /// Retrieves a dictionary mapping plugin type names to their corresponding GUIDs within a specified plugin assembly.
        /// </summary>
        /// <param name="assemblyId">The unique identifier of the plugin assembly.</param>
        /// <returns>A dictionary where keys are plugin type names (case-insensitive) and values are their respective GUIDs.</returns>
        public Dictionary<string, Guid> PluginTypeIdByName(Guid assemblyId)
        {
            var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in PluginTypes(assemblyId))
            {
                var name = e.GetAttributeValue<string>("typename");
                if (name is not null) map[name] = e.Id;
            }

            return map;
        }

        /// <summary>
        /// Retrieves the unique identifiers of plugin types associated with custom APIs in the system.
        /// </summary>
        /// <returns>A set of GUIDs representing the plugin types linked to custom APIs.</returns>
        public HashSet<Guid> CustomApiPluginTypeIds()
        {
            var set = new HashSet<Guid>();
            foreach (var e in CustomApis)
            {
                var typeRef = e.GetAttributeValue<EntityReference>("plugintypeid");
                if (typeRef is not null) set.Add(typeRef.Id);
            }
            return set;
        }

        /// <summary>
        /// Retrieves the set of GUIDs corresponding to SDK messages associated with custom APIs.
        /// </summary>
        /// <returns>A hash set containing the unique identifiers for SDK messages linked to custom APIs.</returns>
        public HashSet<Guid> CustomApiMessageIds()
        {
            var set = new HashSet<Guid>();
            foreach (var e in CustomApis)
            {
                var msgRef = e.GetAttributeValue<EntityReference>("sdkmessageid");
                if (msgRef is not null) set.Add(msgRef.Id);
            }
            return set;
        }

        /// <summary>
        /// Retrieves the set of GUIDs for SDK messages owned by Custom Actions, resolved by matching
        /// each action's <c>uniquename</c> to an <c>sdkmessage</c> name.
        /// </summary>
        /// <returns>A hash set containing the unique identifiers for SDK messages linked to Custom Actions.</returns>
        public HashSet<Guid> CustomActionMessageIds()
        {
            var messageIdByName = MessageIdByName();
            var set = new HashSet<Guid>();
            foreach (var e in CustomActions)
            {
                var uniqueName = e.GetAttributeValue<string>("uniquename");
                if (uniqueName is not null && messageIdByName.TryGetValue(uniqueName, out var id))
                    set.Add(id);
            }
            return set;
        }

        /// <summary>
        /// Retrieves a dictionary mapping plugin type IDs to their respective type names for a given assembly.
        /// </summary>
        /// <param name="assemblyId">The unique identifier of the assembly for which plugin type records are being retrieved.</param>
        /// <returns>A dictionary where keys are plugin type GUIDs and values are their associated type names.</returns>
        public Dictionary<Guid, string> PluginTypeNameById(Guid assemblyId)
        {
            var map = new Dictionary<Guid, string>();
            foreach (var e in PluginTypes(assemblyId))
            {
                var name = e.GetAttributeValue<string>("typename");
                if (name is not null) map[e.Id] = name;
            }
            return map;
        }

        /// <summary>
        /// Retrieves a list of plugin types associated with a specific plugin assembly.
        /// </summary>
        /// <param name="assemblyId">The unique identifier of the plugin assembly.</param>
        /// <returns>A list of entities representing plugin types, including their IDs and type names.</returns>
        private List<Entity> PluginTypes(Guid assemblyId)
        {
            var query = new QueryExpression("plugintype")
            {
                ColumnSet = new ColumnSet("plugintypeid", "typename"),
                Criteria  = new FilterExpression()
            };
            query.Criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, assemblyId);
            return svc.RetrieveMultiple(query).Entities.ToList();
        }
    }
}
