// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Text.Json.Serialization;
using DeviceBridge.Common.Exceptions;
using Microsoft.OpenApi.Models;

namespace DeviceBridge.Models
{
    [JsonConverter(typeof(DeviceSubscriptionTypeConverter))]
    public class DeviceSubscriptionType
    {
        public static readonly OpenApiSchema Schema = new OpenApiSchema
        {
            Type = "string",
        };

        public static readonly DeviceSubscriptionType DesiredProperties = new DeviceSubscriptionType(DesiredPropertiesSubscriptionType);
        public static readonly DeviceSubscriptionType Methods = new DeviceSubscriptionType(MethodsSubscriptionType);
        public static readonly DeviceSubscriptionType C2DMessages = new DeviceSubscriptionType(C2DSubscriptionType);
        public static readonly DeviceSubscriptionType ConnectionStatus = new DeviceSubscriptionType(ConnectionStatusSubscriptionType);

        private const string DesiredPropertiesSubscriptionType = "DesiredProperties";
        private const string MethodsSubscriptionType = "Methods";
        private const string C2DSubscriptionType = "C2DMessages";
        private const string ConnectionStatusSubscriptionType = "ConnectionStatus";

        private string value;

        private DeviceSubscriptionType(string value)
        {
            switch (value)
            {
                case DesiredPropertiesSubscriptionType:
                case MethodsSubscriptionType:
                case C2DSubscriptionType:
                case ConnectionStatusSubscriptionType:
                    this.value = value;
                    break;
                default:
                    throw new UnknownDeviceSubscriptionTypeException(value);
            }
        }

        public static bool operator ==(DeviceSubscriptionType lhs, DeviceSubscriptionType rhs)
        {
            return (ReferenceEquals(lhs, null) && ReferenceEquals(rhs, null)) || (!ReferenceEquals(lhs, null) && lhs.Equals(rhs));
        }

        public static bool operator !=(DeviceSubscriptionType lhs, DeviceSubscriptionType rhs)
        {
            return !(lhs == rhs);
        }

        /// <summary>
        /// Returns the corresponding singleton for a give subscription type.
        /// </summary>
        /// <exception cref="UnknownDeviceSubscriptionTypeException">If the given value is not a valid subscription type.</exception>
        /// <param name="value">The string representation subscription type.</param>
        /// <returns>The corresponding singleton for the subscription type.</returns>
        public static DeviceSubscriptionType FromString(string value)
        {
            switch (value)
            {
                case DesiredPropertiesSubscriptionType:
                    return DesiredProperties;
                case MethodsSubscriptionType:
                    return Methods;
                case C2DSubscriptionType:
                    return C2DMessages;
                case ConnectionStatusSubscriptionType:
                    return ConnectionStatus;
                default:
                    throw new UnknownDeviceSubscriptionTypeException(value);
            }
        }

        /// <summary>
        /// A data subscription deals with events that depend on a device connection (properties, methods, C2D messages).
        /// A connection status subscription, in the other hand, is just a subscription to engine events that is always active and doesn't depend on a connection.
        /// </summary>
        /// <returns>Whether this is a data subscription or not.</returns>
        public bool IsDataSubscription()
        {
            return (this == DesiredProperties) || (this == Methods) || (this == C2DMessages);
        }

        public override string ToString()
        {
            return value;
        }

        public override bool Equals(object type)
        {
            if (ReferenceEquals(type, null))
            {
                return false;
            }

            return ReferenceEquals(this, type) || (value == (type as DeviceSubscriptionType).value);
        }

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }
    }
}