using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Xml.Linq;
using DbcParserLib.Model;

namespace DbcParserLib
{
    internal class ValuesTable
    {
        public IReadOnlyDictionary<int, string> ValueTableMap { get; set; }
        public string ValueTable { get; set; }
    }

    public class DbcBuilder : IDbcBuilder
    {
        private readonly ISet<Node> m_nodes = new HashSet<Node>(new NodeEqualityComparer());
        private readonly IDictionary<string, ValuesTable> m_namedTablesMap = new Dictionary<string, ValuesTable>();
        private readonly IDictionary<uint, Message> m_messages = new Dictionary<uint, Message>();
        private readonly IDictionary<uint, IDictionary<string, Signal>> m_signals = new Dictionary<uint, IDictionary<string, Signal>>();
        private readonly IDictionary<DbcObjectType, IDictionary<string, CustomProperty>> m_customProperties = new Dictionary<DbcObjectType, IDictionary<string, CustomProperty>>();

        private Message m_currentMessage;

        public void AddNode(Node node)
        {
            m_nodes.Add(node);
        }

        public void AddMessage(Message message)
        {
            m_messages[message.ID] = message;
            m_currentMessage = message;
            m_signals[message.ID] = new Dictionary<string, Signal>();
        }

        public void AddSignal(Signal signal)
        {
            if (m_currentMessage != null)
            {
                signal.ID = m_currentMessage.ID;
                m_signals[m_currentMessage.ID][signal.Name] = signal;
            }
        }

        public void AddCustomProperty(DbcObjectType objectType, CustomProperty customProperty)
        {
            m_customProperties[objectType][customProperty.Name] = customProperty;
        }

        public void AddCustomPropertyDefaultValue(string propertyName, string value)
        {
            foreach(var objectType in m_customProperties.Keys)
            {
                if (m_customProperties[objectType].TryGetValue(propertyName, out var customProperty))
                {
                    SetCustomPropertyDefaultValue(customProperty, value);
                }
            }
        }

        public void AddNodeCustomProperty(string propertyName, string nodeName, string value)
        {
            if(m_customProperties[DbcObjectType.Node].TryGetValue(propertyName, out var customProperty))
            {
                var node = m_nodes.FirstOrDefault(n => n.Name.Equals(nodeName));
                if (node != null)
                {
                    SetCustomPropertyValue(customProperty, value);
                    node.CustomProperties[propertyName] = customProperty;
                }
            }
        }

        public void AddMessageCustomProperty(string propertyName, uint messageId, string value)
        {
            if (m_customProperties[DbcObjectType.Message].TryGetValue(propertyName, out var customProperty))
            {
                if (m_messages.TryGetValue(messageId, out var message))
                {
                    SetCustomPropertyValue(customProperty, value);
                    message.CustomProperties[propertyName] = customProperty;
                }
                
            }
        }

        public void AddSignalCustomProperty(string propertyName, uint messageId, string signalName, string value)
        {
            if (m_customProperties[DbcObjectType.Message].TryGetValue(propertyName, out var customProperty))
            {
                if (TryGetValueMessageSignal(messageId, signalName, out var signal))
                {
                    SetCustomPropertyValue(customProperty, value);
                    signal.CustomProperties[propertyName] = customProperty;
                }
            }
        }

        public void AddSignalComment(uint messageId, string signalName, string comment)
        {
            if (TryGetValueMessageSignal(messageId, signalName, out var signal))
            {
                signal.Comment = comment;
            }
        }

        public void AddSignalInitialValue(uint messageId, string signalName, double initialValue)
        {
            IsExtID(ref messageId);
            if (TryGetValueMessageSignal(messageId, signalName, out var signal))
            {
                signal.InitialValue = initialValue * signal.Factor + signal.Offset;
            }
        }

        public void AddSignalValueType(uint messageId, string signalName, DbcValueType valueType)
        {
            if (TryGetValueMessageSignal(messageId, signalName, out var signal))
            {
                signal.ValueType = valueType;
            }
        }

        public void AddNodeComment(string nodeName, string comment)
        {
            var node = m_nodes.FirstOrDefault(n => n.Name.Equals(nodeName));
            if (node != null)
            {
                node.Comment = comment;
            }
        }

        public void AddMessageComment(uint messageId, string comment)
        {
            if (m_messages.TryGetValue(messageId, out var message))
            {
                message.Comment = comment;
            }
        }

        public void AddMessageCycleTime(uint messageId, int cycleTime)
        {
            IsExtID(ref messageId);
            if (m_messages.TryGetValue(messageId, out var message))
            {
                message.CycleTime = cycleTime;
            }
        }

        public void AddNamedValueTable(string name, IReadOnlyDictionary<int, string> dictValues, string stringValues)
        {
            m_namedTablesMap[name] = new ValuesTable()
            {
                ValueTableMap = dictValues,
                ValueTable = stringValues
            };
        }

        public void LinkTableValuesToSignal(uint messageId, string signalName, IReadOnlyDictionary<int, string> dictValues, string stringValues)
        {
            IsExtID(ref messageId);
            if (TryGetValueMessageSignal(messageId, signalName, out var signal))
            {
                signal.SetValueTable(dictValues, stringValues);
            }
        }

        public static bool IsExtID(ref uint id)
        {
            // For extended ID bit 31 is always 1
            if (id >= 0x80000000)
            {
                id -= 0x80000000;
                return true;
            }
            else
                return false;
        }

        public void LinkNamedTableToSignal(uint messageId, string signalName, string tableName)
        {
            if (m_namedTablesMap.TryGetValue(tableName, out var valuesTable))
            {
                LinkTableValuesToSignal(messageId, signalName, valuesTable.ValueTableMap, valuesTable.ValueTable);
            }
        }

        private bool TryGetValueMessageSignal(uint messageId, string signalName, out Signal signal)
        {
            if (m_signals.TryGetValue(messageId, out var signals) && signals.TryGetValue(signalName, out signal))
            {
                return true;
            }

            signal = null;
            return false;
        }

        private void SetCustomPropertyValue(CustomProperty customProperty, string value)
        {
            switch (customProperty.DataType)
            {
                case DbcDataType.Integer:
                    customProperty.IntegerCustomProperty.Value = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case DbcDataType.Hex:
                    customProperty.HexCustomProperty.Value = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case DbcDataType.Float:
                    customProperty.FloatCustomProperty.Value = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case DbcDataType.String:
                    customProperty.StringCustomProperty.Value = value;
                    break;
                case DbcDataType.Enum:
                    customProperty.EnumCustomProperty.Value = value.Split(',');
                    break;
            }
        }

        private void SetCustomPropertyDefaultValue(CustomProperty customProperty, string value)
        {
            switch (customProperty.DataType)
            {
                case DbcDataType.Integer:
                    customProperty.IntegerCustomProperty.Default = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case DbcDataType.Hex:
                    customProperty.HexCustomProperty.Default = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case DbcDataType.Float:
                    customProperty.FloatCustomProperty.Default = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case DbcDataType.String:
                    customProperty.StringCustomProperty.Default = value;
                    break;
                case DbcDataType.Enum:
                    customProperty.EnumCustomProperty.Default = value.Split(',');
                    break;
            }
        }

        private void SetCustomPropertyValueFromDefault(CustomProperty customProperty)
        {
            switch (customProperty.DataType)
            {
                case DbcDataType.Integer:
                    customProperty.IntegerCustomProperty.Value = customProperty.IntegerCustomProperty.Default;
                    break;
                case DbcDataType.Hex:
                    customProperty.HexCustomProperty.Value = customProperty.HexCustomProperty.Default;
                    break;
                case DbcDataType.Float:
                    customProperty.FloatCustomProperty.Value = customProperty.FloatCustomProperty.Default;
                    break;
                case DbcDataType.String:
                    customProperty.StringCustomProperty.Value = customProperty.StringCustomProperty.Default;
                    break;
                case DbcDataType.Enum:
                    customProperty.EnumCustomProperty.Value = customProperty.EnumCustomProperty.Default;
                    break;
            }
        }

        private void FillNodesNotSetCustomPropertyWithDefault()
        {
            var nodeCustomProperties = m_customProperties[DbcObjectType.Node];
            foreach (var customProperty in nodeCustomProperties)
            {
                foreach (var node in m_nodes)
                {
                    if (!node.CustomProperties.TryGetValue(customProperty.Key, out var property))
                    {
                        SetCustomPropertyValueFromDefault(property);
                        node.CustomProperties[property.Name] = property;
                    }
                }
            }
        }

        private void FillMesagesNotSetCustomPropertyWithDefault()
        {
            var messageCustomProperties = m_customProperties[DbcObjectType.Message];
            foreach (var customProperty in messageCustomProperties)
            {
                foreach (var message in m_messages.Values)
                {
                    FillSignalsNotSetCustomPropertyWithDefault(message.ID);
                    if (!message.CustomProperties.TryGetValue(customProperty.Key, out var property))
                    {
                        SetCustomPropertyValueFromDefault(property);
                        message.CustomProperties[property.Name] = property;
                    }
                }
            }
        }

        private void FillSignalsNotSetCustomPropertyWithDefault(uint messageId)
        {
            var signalCustomProperties = m_customProperties[DbcObjectType.Signal];
            foreach (var customProperty in signalCustomProperties)
            {
                foreach (var signal in m_signals[messageId].Values)
                {
                    if (!signal.CustomProperties.TryGetValue(customProperty.Key, out var property))
                    {
                        SetCustomPropertyValueFromDefault(property);
                        signal.CustomProperties[property.Name] = property;
                    }
                }
            }
        }

        public Dbc Build()
        {
            FillNodesNotSetCustomPropertyWithDefault();
            FillMesagesNotSetCustomPropertyWithDefault();

            foreach (var message in m_messages)
            {
                message.Value.Signals.Clear();
                message.Value.Signals.AddRange(m_signals[message.Key].Values);
            }

            return new Dbc(m_nodes.ToArray(), m_messages.Values.ToArray());
        }
    }

    internal class NodeEqualityComparer : IEqualityComparer<Node>
    {
        public bool Equals(Node b1, Node b2)
        {
            if (b2 == null && b1 == null)
                return true;
            else if (b1 == null || b2 == null)
                return false;
            else if (b1.Name == b2.Name)
                return true;
            else
                return false;
        }

        public int GetHashCode(Node bx)
        {
            return bx.Name.GetHashCode();
        }
    }
}