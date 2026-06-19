using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    /// <summary>
    /// Loads SDK metadata lookups (messages, message filters, plugin types) shared by step
    /// registration (forward: name → id) and adoption (reverse: id → name). Raw message and
    /// filter records are fetched once and cached per instance.
    /// </summary>
    internal class SdkMetadata(IOrganizationService svc)
    {
        private List<Entity>? _messages;
        private List<Entity>? _filters;
        private List<Entity>? _customApis;
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

        private IReadOnlyList<Entity> Messages =>
            _messages ??= svc.RetrieveMultiple(new QueryExpression("sdkmessage")
            {
                ColumnSet = new ColumnSet("sdkmessageid", "name")
            }).Entities.ToList();

        private IReadOnlyList<Entity> Filters =>
            _filters ??= svc.RetrieveMultiple(new QueryExpression("sdkmessagefilter")
            {
                ColumnSet = new ColumnSet("sdkmessagefilterid", "sdkmessageid",
                    "primaryobjecttypecode", "iscustomprocessingstepallowed")
            }).Entities.ToList();

        private IReadOnlyList<Entity> CustomApis =>
            _customApis ??= svc.RetrieveMultiple(new QueryExpression("customapi")
            {
                ColumnSet = new ColumnSet("customapiid", "uniquename", "plugintypeid", "sdkmessageid")
            }).Entities.ToList();

        /// <summary>Message name → id (case-insensitive).</summary>
        public Dictionary<string, Guid> MessageIdByName()
        {
            var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Messages)
            {
                var name = e.GetAttributeValue<string>("name");
                if (name is not null) map[name] = e.Id;
            }
            return map;
        }

        /// <summary>Message id → name.</summary>
        public Dictionary<Guid, string> MessageNameById()
        {
            var map = new Dictionary<Guid, string>();
            foreach (var e in Messages)
            {
                var name = e.GetAttributeValue<string>("name");
                if (name is not null) map[e.Id] = name;
            }
            return map;
        }

        /// <summary>(messageId, entity logical name) → filterId. First entry wins on duplicates.</summary>
        public Dictionary<(Guid, string), Guid> FilterIdByKey()
        {
            var map = new Dictionary<(Guid, string), Guid>();
            foreach (var e in Filters)
            {
                var msgRef = e.GetAttributeValue<EntityReference>("sdkmessageid");
                if (msgRef is null) continue;
                var entity = (e.GetAttributeValue<string>("primaryobjecttypecode") ?? string.Empty).ToLowerInvariant();
                map.TryAdd((msgRef.Id, entity), e.Id);
            }
            return map;
        }

        /// <summary>filterId → entity logical name (primaryobjecttypecode).</summary>
        public Dictionary<Guid, string> FilterEntityById()
        {
            var map = new Dictionary<Guid, string>();
            foreach (var e in Filters)
                map[e.Id] = e.GetAttributeValue<string>("primaryobjecttypecode") ?? string.Empty;
            return map;
        }

        /// <summary>plugintype typename → id for the given assembly (case-insensitive).</summary>
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

        /// <summary>plugintype ids referenced by a Custom API as its main-operation implementation.</summary>
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

        /// <summary>sdkmessage ids of the messages created for Custom APIs.</summary>
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

        /// <summary>plugintype id → typename for the given assembly.</summary>
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
